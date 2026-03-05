using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Serilog;

namespace Maple2.Server.Game.Manager;

public sealed class BlackMarketManager {
    private readonly GameSession session;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    private ConstantsTable Constants => session.ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    private readonly ILogger logger = Log.Logger.ForContext<BlackMarketManager>();

    public BlackMarketManager(GameSession session) {
        this.session = session;
    }

    public void LoadMyListings() {
        using GameStorage.Request db = session.GameStorage.Context();
        ICollection<BlackMarketListing> entries = db.GetBlackMarketListings(session.CharacterId).ToList();

        session.Send(BlackMarketPacket.MyListings(entries));
    }

    public void Add(long itemUid, long price, int quantity) {
        Item? item = session.Item.Inventory.Get(itemUid);
        if (item == null) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_register_not_exist_in_inven));
            return;
        }

        if (item.Amount < quantity) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_lack_sale_count));
            return;
        }

        if (item.IsExpired()) {
            return;
        }

        float depositPercent = Lua.CalcBlackMarketRegisterDepositPercent();
        long depositFee = Lua.CalcBlackMarketRegisterDeposit((long) (price * quantity * depositPercent));

        if (session.Currency.CanAddMeso(-depositFee) != -depositFee) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_err_lack_meso));
            return;
        }

        session.Currency.Meso -= depositFee;

        if (!session.Item.Inventory.Remove(itemUid, out item, quantity)) {
            return;
        }

        var listing = new BlackMarketListing(item) {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Deposit = depositFee,
            ExpiryTime = DateTime.Now.AddDays(Constants.BlackMarketSellEndDay).ToEpochSeconds(),
            Price = price,
            Quantity = quantity,
        };

        using GameStorage.Request db = session.GameStorage.Context();
        listing = db.CreateBlackMarketingListing(listing);
        if (listing == null) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_fail_register));
            CreateRefundErrorMail(item, depositFee);
            return;
        }

        // Also add to World
        BlackMarketResponse response = session.World.BlackMarket(new BlackMarketRequest {
            Add = new BlackMarketRequest.Types.Add {
                ListingId = listing.Id,
            },
        });

        var error = (BlackMarketError) response.Error;
        if (error != BlackMarketError.none) {
            session.Send(BlackMarketPacket.Error(error));
            CreateRefundErrorMail(item, depositFee);
            return;
        }

        session.Send(BlackMarketPacket.Add(listing));
    }

    public void Remove(long listingId) {
        using GameStorage.Request db = session.GameStorage.Context();

        BlackMarketListing? listing = db.GetBlackMarketListing(listingId);
        if (listing == null || listing.CharacterId != session.CharacterId) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_close));
            return;
        }

        BlackMarketResponse response = session.World.BlackMarket(new BlackMarketRequest {
            Remove = new BlackMarketRequest.Types.Remove {
                ListingId = listingId,
            },
        });

        var error = (BlackMarketError) response.Error;
        if (error != BlackMarketError.none) {
            session.Send(BlackMarketPacket.Error(error));
            return;
        }

        long deposit = listing.ExpiryTime < DateTime.Now.ToEpochSeconds() ? listing.Deposit : 0;

        var mail = new Mail {
            ReceiverId = session.CharacterId,
            Type = MailType.BlackMarketListingCancel,
            TitleArgs = [
                ("item", $"{listing.Item.Id}"),
            ],
            ContentArgs = [
                ("key", listing.ExpiryTime < DateTime.Now.ToEpochSeconds() ?
                    $"{StringCode.s_blackmarket_mail_to_cancel_expired}" :
                    $"{StringCode.s_blackmarket_mail_to_cancel_direct}"),
                ("item", $"{listing.Item.Id}"),
                ("str", $"{listing.Quantity}"),
                ("money", $"{listing.Price * listing.Quantity}"),
                ("money", $"{listing.Price}"),
                ("money", $"{deposit}"),
            ],
            Meso = deposit,
        };

        mail.SetTitle(StringCode.s_blackmarket_mail_to_cancel_title);
        mail.SetContent(StringCode.s_blackmarket_mail_to_cancel_content);
        mail.SetSenderName(StringCode.s_blackmarket_mail_to_sender);

        mail = db.CreateMail(mail);
        if (mail == null) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_close));
            logger.Error("Failed to create black market mail for {CharacterId}", session.CharacterId);
            return;
        }

        mail.Items.Add(listing.Item);
        db.SaveItems(mail.Id, listing.Item);
        session.Send(BlackMarketPacket.Remove(listingId));
        session.Mail.Notify(true);
    }

    public void Purchase(long listingId, int quantity) {
        using GameStorage.Request db = session.GameStorage.Context();
        BlackMarketListing? listing = db.GetBlackMarketListing(listingId);
        if (listing == null) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_err_lack_itemcount));
            return;
        }

        if (listing.ExpiryTime < DateTime.Now.ToEpochSeconds()) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_buy_expired));
            return;
        }

        if (listing.Quantity < quantity) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_purchase_count));
            return;
        }

        if (listing.Price * quantity > session.Currency.Meso) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_err_lack_meso));
            return;
        }

        session.Currency.Meso -= listing.Price * quantity;

        Mail? receivingMail = CreateBuyerMail(listing, quantity);
        if (receivingMail == null) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_close));
            session.Currency.Meso += listing.Price * quantity;
            logger.Error("Failed to create black market purchase mail for {CharacterId}", session.CharacterId);
            return;
        }

        listing.Quantity -= quantity;
        if (quantity == listing.Item.Amount) {
            db.SaveItems(receivingMail.Id, listing.Item);
            receivingMail.Items.Add(listing.Item);
            db.DeleteBlackMarketListing(listingId);
        } else {
            listing.Item.Amount -= quantity;
            db.SaveItems(listing.Id, listing.Item);
            Item? item = listing.Item.Clone();
            item.Amount = quantity;

            item = db.CreateItem(receivingMail.Id, item);
            if (item is null) {
                session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_close));
                session.Currency.Meso += listing.Price * quantity;
                logger.Error("Failed to create item for black market purchase mail for {CharacterId}", session.CharacterId);
                return;
            }
            receivingMail.Items.Add(item);
            db.SaveBlackMarketListing(listing);
        }

        BlackMarketResponse response = session.World.BlackMarket(new BlackMarketRequest {
            Purchase = new BlackMarketRequest.Types.Purchase {
                ListingId = listingId,
                SellerId = listing.CharacterId,
            },
        });

        var error = (BlackMarketError) response.Error;
        if (error != BlackMarketError.none) {
            session.Send(BlackMarketPacket.Error(error));
            session.Currency.Meso += listing.Price * quantity;

            return;
        }

        session.Mail.Notify(true);

        // Seller Mail
        Mail? sellerMail = CreateSellerMail(listing, quantity);
        if (sellerMail == null) {
            session.Send(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_close));
            logger.Error("Failed to create black market sale mail for {CharacterId}", listing.CharacterId);
            return;
        }

        try {
            session.World.MailNotification(new MailNotificationRequest {
                CharacterId = sellerMail.ReceiverId,
                MailId = sellerMail.Id,
            });
        } catch { /*ignored*/ }

        session.Send(BlackMarketPacket.Purchase(listingId, quantity));
    }

    private Mail? CreateBuyerMail(BlackMarketListing listing, int quantity) {
        var receivingMail = new Mail {
            ReceiverId = session.CharacterId,
            Type = MailType.BlackMarketSale,
            TitleArgs = [
                ("item", $"{listing.Item.Id}"),
            ],
            ContentArgs = [
                ("item", $"{listing.Item.Id}"),
                ("str", $"{quantity}"),
                ("money", $"{listing.Price * quantity}"),
                ("money", $"{listing.Price}"),
            ],
        };

        receivingMail.SetTitle(StringCode.s_blackmarket_mail_to_buyer_title);
        receivingMail.SetContent(StringCode.s_blackmarket_mail_to_buyer_content);
        receivingMail.SetSenderName(StringCode.s_blackmarket_mail_to_sender);

        using GameStorage.Request db = session.GameStorage.Context();
        return db.CreateMail(receivingMail);
    }

    private Mail? CreateSellerMail(BlackMarketListing listing, int quantity) {
        using GameStorage.Request db = session.GameStorage.Context();

        float costRate = Lua.CalcBlackMarketCostRate();
        long tax = (long) (costRate * (quantity * listing.Price));

        bool sellerIsPremium = false;
        long savings = tax;
        if (session.PlayerInfo.GetOrFetch(listing.CharacterId, out PlayerInfo? seller) && seller.PremiumTime > DateTime.Now.ToEpochSeconds()) {
            savings = (long) (tax * Constant.BlackMarketPremiumClubDiscount);
            sellerIsPremium = true;
        }
        long revenue = quantity * listing.Price - savings;
        List<(string, string)> contentArgs = [
            ("item", $"{listing.Item.Id}"),
            ("str", $"{quantity}"),
            ("money", $"{listing.Price * quantity}"),
            ("money", $"{listing.Price}"),
            ("money", $"{tax}"),
            ("str", $"{costRate * 100}" + "%"),
        ];

        if (listing.Quantity == 0) {
            contentArgs.Add(("money", $"{listing.Deposit}"));
            revenue += listing.Deposit;
        }
        contentArgs.Add(("money", $"{revenue}"));

        if (sellerIsPremium) {
            contentArgs.Add(("str", $"{Constant.BlackMarketPremiumClubDiscount * 100}" + "%"));
            contentArgs.Add(("money", $"{savings}"));
        }

        var mail = new Mail {
            ReceiverId = listing.CharacterId,
            Type = MailType.BlackMarketSale,
            TitleArgs = [
                ("item", $"{listing.Item.Id}"),
            ],
            ContentArgs = contentArgs,
            Meso = revenue,
        };

        if (listing.Quantity == 0) {
            mail.SetContent(sellerIsPremium ?
                StringCode.s_blackmarket_mail_to_vipseller_content_soldout :
                StringCode.s_blackmarket_mail_to_seller_content_soldout
            );
        } else {
            mail.SetContent(sellerIsPremium ?
                StringCode.s_blackmarket_mail_to_vipseller_content :
                StringCode.s_blackmarket_mail_to_seller_content);
        }
        mail.SetTitle(StringCode.s_blackmarket_mail_to_seller_title);
        mail.SetSenderName(StringCode.s_blackmarket_mail_to_sender);

        return db.CreateMail(mail);
    }

    private void CreateRefundErrorMail(Item item, long refund) {
        var mail = new Mail {
            ReceiverId = session.CharacterId,
            Type = MailType.BlackMarketFail,
            TitleArgs = [
                ("item", $"{item.Id}"),
            ],
            Meso = refund,
        };

        mail.SetTitle(StringCode.s_blackmarket_mail_to_fail_add_title);
        mail.SetContent(StringCode.s_blackmarket_mail_to_fail_add_content);
        mail.SetSenderName(StringCode.s_blackmarket_mail_to_sender);

        using GameStorage.Request db = session.GameStorage.Context();
        mail = db.CreateMail(mail);
        if (mail == null) {
            logger.Fatal("Failed to create failure black market mail for {CharacterId}", session.CharacterId);
            return;
        }
        mail.Items.Add(item);
        db.SaveItems(mail.Id, item);

        session.Mail.Notify(true);
    }

    public void Preview(int itemId, int rarity) {
        // We're not checking if the item is in the inventory or not as this is just for previewing. We'll check in Add.
        if (!session.ItemMetadata.TryGet(itemId, out ItemMetadata? metadata)) {
            return;
        }
        var type = new ItemType(metadata.Id);
        long sellPrice = Core.Formulas.Shop.SellPrice(metadata, type, rarity);

        session.Send(BlackMarketPacket.Preview(itemId, rarity, sellPrice));
    }
}
