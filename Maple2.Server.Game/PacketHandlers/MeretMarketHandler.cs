using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.PacketHandlers;

public class MeretMarketHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.MeretMarket;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required TableMetadataStorage TableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    private enum Command : byte {
        LoadPersonalListings = 11,
        LoadSales = 12,
        ListItem = 13,
        RemoveListing = 14,
        UnlistItem = 15,
        RelistItem = 18,
        CollectProfit = 20,
        LoadDesigners = 22,
        AddDesigner = 23,
        RemoveDesigner = 24,
        LoadDesignerItems = 25,
        OpenShop = 27,
        BlueprintClear = 28,
        FindItem = 29,
        Purchase = 30,
        Featured = 101,
        OpenUgcShop = 102,
        Search = 104,
        BlueprintSearch = 105,
        LoadCart = 107, // Just a guess?
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.LoadPersonalListings:
                HandleLoadPersonalListings(session);
                return;
            case Command.LoadSales:
                HandleLoadSales(session);
                return;
            case Command.ListItem:
                HandleListItem(session, packet);
                return;
            case Command.RelistItem:
                HandleRelistItem(session, packet);
                return;
            case Command.CollectProfit:
                HandleCollectProfit(session, packet);
                return;
            case Command.LoadDesigners:
                HandleLoadDesigners(session, packet);
                return;
            case Command.AddDesigner:
                HandleAddDesigner(session, packet);
                return;
            case Command.RemoveDesigner:
                HandleRemoveDesigner(session, packet);
                return;
            case Command.LoadDesignerItems:
                HandleLoadDesignerItems(session, packet);
                return;
            case Command.RemoveListing:
                HandleRemoveListing(session, packet);
                return;
            case Command.UnlistItem:
                HandleUnlistItem(session, packet);
                return;
            case Command.OpenShop:
                HandleOpenShop(session, packet);
                return;
            case Command.Purchase:
                HandlePurchase(session, packet);
                return;
            case Command.Featured:
                HandleFeatured(session, packet);
                return;
            case Command.OpenUgcShop:
                HandleOpenUgcShop(session);
                return;
            case Command.FindItem:
                HandleFindItem(session, packet);
                return;
            case Command.Search:
                HandleSearch(session, packet);
                return;
            case Command.BlueprintClear:
            case Command.BlueprintSearch:
                HandleBlueprintSearch(session, packet);
                return;
        }
    }

    private void HandleLoadPersonalListings(GameSession session) {
        session.UgcMarket.Load();
    }

    private void HandleLoadSales(GameSession session) {
        session.UgcMarket.RefreshSales();
    }

    private void HandleListItem(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        long price = packet.ReadLong();
        bool promote = packet.ReadBool();
        string[] tags = packet.ReadUnicodeString().Split(",");
        string description = packet.ReadUnicodeString();
        long listingFee = packet.ReadLong();

        Item? item = GetUgcItem(itemUid);
        if (item?.Template == null || item.Template.Id == 0) {
            return;
        }

        if (!TableMetadata.MeretMarketCategoryTable.Entries.TryGetValue((int) MeretMarketSection.Ugc, out IReadOnlyDictionary<int, MeretMarketCategoryTable.Tab>? tab)) {
            // Invalid UGC Item
            return;
        }

        int tabId = tab.FirstOrDefault(kv => kv.Value.Categories.Contains(item.Metadata.Property.Category)).Key;

        StringCode stringCode = Pay(session, listingFee, MeretMarketCurrencyType.Meret);
        if (stringCode != StringCode.s_empty_string) {
            session.Send(NoticePacket.MessageBox(stringCode));
            return;
        }

        var marketItem = new UgcMarketItem(item.Metadata) {
            SellerAccountId = session.AccountId,
            SellerCharacterId = session.CharacterId,
            SellerCharacterName = session.PlayerName,
            Description = description,
            Tags = tags,
            Look = item.Template,
            Blueprint = item.Blueprint ?? new ItemBlueprint(),
            Status = UgcMarketListingStatus.Active,
            PromotionEndTime = promote ? DateTime.Now.AddHours(Constant.UGCShopAdHour).ToEpochSeconds() : 0,
            ListingEndTime = DateTime.Now.AddDays(Constant.UGCShopSaleDay).ToEpochSeconds(),
            CreationTime = DateTime.Now.ToEpochSeconds(),
            Price = Math.Clamp(price, Constant.UGCShopSellMinPrice, Constant.UGCShopSellMaxPrice),
            TabId = tabId,
        };

        using GameStorage.Request db = session.GameStorage.Context();
        marketItem = db.CreateUgcMarketItem(marketItem);
        if (marketItem == null) {
            Logger.Error("Failed to create UGC market item: {ItemUid}", itemUid);
            return;
        }

        session.UgcMarket.ListItem(marketItem);
        return;

        Item? GetUgcItem(long uid) {
            Item? result = session.Item.Inventory.Get(uid);
            if (result != null) {
                return result;
            }

            result = session.Item.GetOutfit(uid);
            return result ?? session.Item.Furnishing.GetCube(uid);
        }
    }

    private void HandleRelistItem(GameSession session, IByteReader packet) {
        long ugcMarketItemId = packet.ReadLong();
        long price = packet.ReadLong();
        bool promote = packet.ReadBool();
        string[] tags = packet.ReadUnicodeString().Split(",");
        string description = packet.ReadUnicodeString();
        long listingFee = packet.ReadLong();

        if (!session.UgcMarket.TryGetItem(ugcMarketItemId, out UgcMarketItem? item) || item.ListingEndTime > DateTime.Now.ToEpochSeconds()) {
            return;
        }

        StringCode stringCode = Pay(session, listingFee, MeretMarketCurrencyType.Meret);
        if (stringCode != StringCode.s_empty_string) {
            session.Send(NoticePacket.MessageBox(stringCode));
            return;
        }

        item.Price = price;
        item.PromotionEndTime = promote ? DateTime.Now.AddHours(Constant.UGCShopAdHour).ToEpochSeconds() : 0;
        item.ListingEndTime = DateTime.Now.AddDays(Constant.UGCShopSaleDay).ToEpochSeconds();
        item.Status = UgcMarketListingStatus.Active;
        item.Description = description;
        item.Tags = tags;

        using GameStorage.Request db = session.GameStorage.Context();
        if (!db.SaveUgcMarketItem(item)) {
            return;
        }

        session.Send(MeretMarketPacket.RelistItem(item));
    }

    private void HandleCollectProfit(GameSession session, IByteReader packet) {
        long saleId = packet.ReadLong();
        session.UgcMarket.CollectProfit(saleId);
    }

    private void HandleLoadDesigners(GameSession session, IByteReader packet) {
        session.UgcMarket.LoadDesigners();
    }

    private void HandleAddDesigner(GameSession session, IByteReader packet) {
        long characterId = packet.ReadLong();
        session.UgcMarket.AddDesigner(characterId);
    }

    private void HandleRemoveDesigner(GameSession session, IByteReader packet) {
        long characterId = packet.ReadLong();
        session.UgcMarket.RemoveDesigner(characterId);
    }

    private void HandleLoadDesignerItems(GameSession session, IByteReader packet) {
        long characterId = packet.ReadLong();
        session.UgcMarket.LoadDesignerItems(characterId);
    }

    private void HandleRemoveListing(GameSession session, IByteReader packet) {
        packet.ReadInt(); // 0
        long ugcMarketItemId = packet.ReadLong();
        packet.ReadLong(); // duplicate id read?

        session.UgcMarket.RemoveListing(ugcMarketItemId);
    }

    private void HandleUnlistItem(GameSession session, IByteReader packet) {
        packet.ReadInt(); // 0
        long ugcMarketItemId = packet.ReadLong();
        packet.ReadLong(); // duplicate id read?

        session.UgcMarket.UnlistItem(ugcMarketItemId);
    }

    private void HandleOpenShop(GameSession session, IByteReader packet) {
        MeretMarketSearch meretMarketSearch = packet.ReadClass<MeretMarketSearch>();

        ICollection<MarketItem> entries = GetItems(session, meretMarketSearch, MeretMarketSection.All).ToList();
        int totalItems = entries.Count;
        entries = TakeLimit(entries, meretMarketSearch.StartPage, meretMarketSearch.ItemsPerPage);

        session.Send(MeretMarketPacket.LoadItems(entries, totalItems, meretMarketSearch.StartPage));
    }

    private void HandlePurchase(GameSession session, IByteReader packet) {
        byte quantity = packet.ReadByte();
        int premiumMarketId = packet.ReadInt();
        long ugcItemId = packet.ReadLong();
        packet.ReadInt();
        int childMarketItemId = packet.ReadInt();
        long unk1 = packet.ReadLong();
        int itemIndex = packet.ReadInt();
        int totalQuantity = packet.ReadInt();
        int unk2 = packet.ReadInt();
        bool gift = packet.ReadBool();
        string playerName = packet.ReadUnicodeString();
        string message = packet.ReadUnicodeString();
        long price = packet.ReadLong(); // price? (but price is auto-determined below)

        if (gift) {
            // TODO: Implement gifting. This wasn't enabled in GMS2
            Logger.Warning("Gifting in Meret Market is not implemented");
            return;
        }

        //PlayerInfo? giftedPlayer = GetGiftedPlayerInfo(session, playerName);
        //if (gift && giftedPlayer == null) {
        // The meret market packet to send to player if the player doesn't exist is FailPurchase (31) but it somehow doesn't
        // send the correct message to the player.
        //    return;
        //}

        Item? item = ugcItemId > 0 ? PurchaseUgcItem(session, ugcItemId) : PurchasePremiumItem(session, premiumMarketId, childMarketItemId);
        if (item == null) {
            return;
        }

        if (!session.Item.Inventory.Add(item, true)) {
            session.Item.MailItem(item);
        }


        session.Send(MeretMarketPacket.Purchase(totalQuantity, itemIndex, price, premiumMarketId, ugcItemId));
        return;

        PlayerInfo? GetGiftedPlayerInfo(GameSession session, string name) {
            if (string.IsNullOrWhiteSpace(name)) {
                return null;
            }
            using GameStorage.Request db = session.GameStorage.Context();
            long characterId = db.GetCharacterId(name);
            session.PlayerInfo.GetOrFetch(characterId, out PlayerInfo? receiverInfo);
            return receiverInfo;
        }
    }

    private Item? PurchasePremiumItem(GameSession session, int premiumMarketId, int childMarketItemId) {
        PremiumMarketItem? entry = session.GetPremiumMarketItem(premiumMarketId, childMarketItemId);
        if (entry == null) {
            return null;
        }

        // TODO: Find meret market error packets
        if ((entry.Metadata.SaleStartTime != 0 && entry.Metadata.SaleStartTime > DateTime.Now.ToEpochSeconds()) ||
            (entry.Metadata.SaleEndTime != 0 && entry.Metadata.SaleEndTime < DateTime.Now.ToEpochSeconds())) {
            return null;
        }

        if ((entry.Metadata.RequireMinLevel > 0 && entry.Metadata.RequireMinLevel > session.Player.Value.Character.Level) ||
            (entry.Metadata.RequireMaxLevel > 0 && entry.Metadata.RequireMaxLevel < session.Player.Value.Character.Level)) {
            return null;
        }

        // If JobRequirement is None, no job is eligible.
        if ((entry.Metadata.JobRequirement & session.Player.Value.Character.Job.Code().FilterFlag()) == JobFilterFlag.None) {
            return null;
        }

        if (entry.Metadata.RequireAchievementId > 0 && session.Achievement.HasAchievement(entry.Metadata.RequireAchievementId, entry.Metadata.RequireAchievementRank)) {
            return null;
        }

        long price = entry.Metadata.SalePrice > 0 ? entry.Metadata.SalePrice : entry.Price;
        StringCode payResult = Pay(session, price, entry.Metadata.CurrencyType);
        if (payResult != StringCode.s_empty_string) {
            session.Send(NoticePacket.MessageBox(payResult));
            return null;
        }

        Item? item = session.Field?.ItemDrop.CreateItem(entry.ItemMetadata.Id, entry.Metadata.Rarity, entry.Metadata.Quantity + entry.Metadata.BonusQuantity);
        if (item == null) {
            Logger.Fatal("Failed to create item {ItemId}, {Rarity}, {Quantity}", entry.ItemMetadata.Id, entry.Metadata.Rarity, entry.Metadata.Quantity + entry.Metadata.BonusQuantity);
            throw new InvalidOperationException($"Fatal: Failed to create item {entry.ItemMetadata.Id}, {entry.Metadata.Rarity}, {entry.Metadata.Quantity + entry.Metadata.BonusQuantity}");
        }

        if (entry.Metadata.DurationInDays > 0) {
            item.ExpiryTime = DateTime.Now.AddDays(entry.Metadata.DurationInDays).ToEpochSeconds();
        }

        using GameStorage.Request db = session.GameStorage.Context();
        if (!db.CreateSoldMeretMarketItem(entry, session.CharacterId)) {
            Logger.Fatal("Failed to create Sold Meret Market Entry for {itemId}", entry.Id);
        }

        return item;
    }

    private Item? PurchaseUgcItem(GameSession session, long id) {
        using GameStorage.Request db = session.GameStorage.Context();
        UgcMarketItem? ugcItem = db.GetUgcMarketItem(id);
        if (ugcItem == null) {
            return null;
        }

        if (!TableMetadata.UgcDesignTable.Entries.TryGetValue(ugcItem.ItemMetadata.Id, out UgcDesignTable.Entry? designMetadata)) {
            return null;
        }

        StringCode stringCode = Pay(session, ugcItem.Price, MeretMarketCurrencyType.Meret);
        if (stringCode != StringCode.s_empty_string) {
            session.Send(NoticePacket.MessageBox(stringCode));
            return null;
        }

        ugcItem.SalesCount++;
        db.SaveUgcMarketItem(ugcItem);

        var soldEntry = new SoldUgcMarketItem {
            AccountId = ugcItem.SellerAccountId,
            Price = ugcItem.Price,
            Name = ugcItem.Look.Name,
            Profit = (long) (ugcItem.Price - Constant.UGCShopProfitFee * ugcItem.Price),
        };

        soldEntry = db.CreateSoldUgcMarketItem(soldEntry);
        if (soldEntry == null) {
            Logger.Fatal("Unable to create Sold Ugc Market Entry for {itemId}", ugcItem.Id);
            return null;
        }

        Item? item = session.Field?.ItemDrop.CreateItem(ugcItem.ItemMetadata.Id, designMetadata.ItemRarity);
        if (item == null) {
            Logger.Fatal("Unable to create Item for {itemId}", ugcItem.ItemMetadata.Id);
            return null;
        }
        item.Template = ugcItem.Look;
        item.Blueprint = ugcItem.Blueprint;
        return item;
    }

    private static void HandleFeatured(GameSession session, IByteReader packet) {
        byte section = packet.ReadByte();
        byte tabId = packet.ReadByte();

        IList<MarketItem> entries = ToMarketSection(section) switch {
            MeretMarketSection.All => session.GetPremiumMarketItems(tabId).Cast<MarketItem>().ToList(),
            MeretMarketSection.Premium => session.GetPremiumMarketItems(tabId).Cast<MarketItem>().ToList(),
            MeretMarketSection.Ugc => [],
            MeretMarketSection.RedMeret => [],
            _ => [],
        };

        // Featured page needs a multiple of 2 slots. Add to the entries total if odd number.
        byte marketSlots = (byte) (entries.Count % 2 == 0 ? entries.Count : (byte) (entries.Count + 1));
        session.Send(MeretMarketPacket.FeaturedPremium(section, tabId, marketSlots, entries));
    }

    private void HandleOpenUgcShop(GameSession session) {
        using GameStorage.Request db = session.GameStorage.Context();

        ICollection<UgcMarketItem> newItems = db.GetUgcMarketNewItems();
        ICollection<UgcMarketItem> promoItems = db.GetUgcMarketPromotedItems();
        session.Send(MeretMarketPacket.FeaturedUgc(promoItems, newItems));
    }

    private void HandleFindItem(GameSession session, IByteReader packet) {
        bool premium = packet.ReadBool();
        if (premium) {
            int premiumId = packet.ReadInt();
            MarketItem? marketItem = session.GetPremiumMarketItem(premiumId);
            if (marketItem is null) {
                return;
            }
            session.Send(MeretMarketPacket.LoadItems([marketItem], 1, 1));
            return;
        }

        long ugcId = packet.ReadLong();
        using GameStorage.Request db = session.GameStorage.Context();

        UgcMarketItem? ugcMarketItem = db.GetUgcMarketItem(ugcId);
        if (ugcMarketItem is null) {
            return;
        }
        session.Send(MeretMarketPacket.LoadBlueprints([ugcMarketItem], 1, 1, 1));

    }

    private void HandleSearch(GameSession session, IByteReader packet) {
        MeretMarketSearch meretMarketSearch = packet.ReadClass<MeretMarketSearch>();

        MeretMarketSection section = ToMarketSection(packet.ReadByte());

        ICollection<MarketItem> entries = GetItems(session, meretMarketSearch, section).ToList();
        int totalItems = entries.Count;
        entries = TakeLimit(entries, meretMarketSearch.StartPage, meretMarketSearch.ItemsPerPage);

        session.Send(MeretMarketPacket.LoadItems(entries, totalItems, meretMarketSearch.StartPage));
    }

    private void HandleBlueprintSearch(GameSession session, IByteReader packet) {
        MeretMarketSearch meretMarketSearch = packet.ReadClass<MeretMarketSearch>();

        ICollection<MarketItem> entries = GetItems(session, meretMarketSearch, MeretMarketSection.Ugc).ToList();
        int totalItems = entries.Count;
        entries = TakeLimit(entries, meretMarketSearch.StartPage, meretMarketSearch.ItemsPerPage);

        session.Send(MeretMarketPacket.LoadBlueprints(entries, totalItems, meretMarketSearch.ItemsPerPage, meretMarketSearch.StartPage));
    }

    private static MeretMarketSection ToMarketSection(byte section) {
        return section switch {
            0 => MeretMarketSection.All,
            1 => MeretMarketSection.Premium,
            2 => MeretMarketSection.RedMeret,
            3 => MeretMarketSection.Ugc,
            _ => MeretMarketSection.All,
        };
    }

    #region Helpers
    private IEnumerable<MarketItem> GetItems(GameSession session, MeretMarketSearch meretMarketSearch, MeretMarketSection section) {
        int[] tabIds = [];
        bool sortGender = false;
        bool sortJob = false;
        if (meretMarketSearch.TabId is not 1) {
            if (!GetTab(section, meretMarketSearch.TabId, out MeretMarketCategoryTable.Tab? tab)) {
                return new List<MarketItem>();
            }
            // get any sub tabs
            tabIds = new[] {
                meretMarketSearch.TabId,
            }.Concat(tab.SubTabIds).ToArray();
            sortGender = tab.SortGender;
            sortJob = tab.SortJob;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        IEnumerable<MarketItem> items;
        switch (section) {
            case MeretMarketSection.All:
                items = db.GetUgcMarketItems(tabIds);
                items = items.Concat(session.GetPremiumMarketItems(tabIds));
                items = Filter(items, meretMarketSearch.Gender, meretMarketSearch.Job, meretMarketSearch.SearchString);
                return Sort(items, meretMarketSearch.SortBy, sortJob, sortGender);
            case MeretMarketSection.Premium:
            case MeretMarketSection.RedMeret:
                items = session.GetPremiumMarketItems(tabIds);
                items = Filter(items, meretMarketSearch.Gender, meretMarketSearch.Job, meretMarketSearch.SearchString);
                return Sort(items, meretMarketSearch.SortBy, sortJob, sortGender);
            case MeretMarketSection.Ugc:
                items = db.GetUgcMarketItems(tabIds);
                items = Filter(items, meretMarketSearch.Gender, meretMarketSearch.Job, meretMarketSearch.SearchString);
                return Sort(items, meretMarketSearch.SortBy, sortJob, sortGender);
            default:
                Logger.Warning("Unimplemented Market section {section}", section);
                return new List<MarketItem>();
        }
    }

    private bool GetTab(MeretMarketSection section, int tabId, [NotNullWhen(true)] out MeretMarketCategoryTable.Tab? tab) {
        if (section == MeretMarketSection.All) {
            foreach ((int sectionId, IReadOnlyDictionary<int, MeretMarketCategoryTable.Tab> dictionary) in TableMetadata.MeretMarketCategoryTable.Entries) {
                if (dictionary.TryGetValue(tabId, out tab)) {
                    return true;
                }
            }
        } else {
            return TableMetadata.MeretMarketCategoryTable.Entries.TryGetValue((int) section, tabId, out tab);
        }

        tab = null;
        return false;
    }

    private static StringCode Pay(GameSession session, long price, MeretMarketCurrencyType currencyType) {
        switch (currencyType) {
            case MeretMarketCurrencyType.Meso:
                if (session.Currency.CanAddMeso(-price) != -price) {
                    return StringCode.s_err_lack_meso;
                }
                session.Currency.Meso -= price;
                return StringCode.s_empty_string;
            case MeretMarketCurrencyType.Meret:
                if (session.Currency.CanAddMeret(-price) != -price) {
                    return StringCode.s_err_lack_merat;
                }
                session.Currency.Meret -= price;
                return StringCode.s_empty_string;
            case MeretMarketCurrencyType.RedMeret:
                if (session.Currency.CanAddGameMeret(-price) != -price) {
                    return StringCode.s_err_lack_merat_red;
                }
                session.Currency.GameMeret -= price;
                return StringCode.s_empty_string;
            default:
                return StringCode.s_err_lack_money;
        }
    }

    private static IEnumerable<MarketItem> Filter(IEnumerable<MarketItem> items, GenderFilterFlag gender, JobFilterFlag job, string searchString) {
        if (!string.IsNullOrWhiteSpace(searchString)) {
            items = FilterByString(items, searchString);
        }

        // GenderFilterFlag.None means no genders are eligible.
        if (gender != GenderFilterFlag.All) {
            items = items.Where(entry => (gender & entry.ItemMetadata.Limit.Gender.FilterFlag()) != GenderFilterFlag.None);
        }

        // JobFilterFlag.None means no jobs are eligible.
        return items.Where(entry => entry.ItemMetadata.Limit.JobRecommends.Length == 0 ||
                                    entry.ItemMetadata.Limit.JobRecommends.Contains(JobCode.None) ||
                                    (job & entry.ItemMetadata.Limit.JobRecommends.FilterFlags()) != JobFilterFlag.None);
    }

    private static IEnumerable<MarketItem> FilterByString(IEnumerable<MarketItem> items, string searchString) {
        foreach (MarketItem item in items) {
            if (item is UgcMarketItem ugc) {
                if (ugc.Look.Name.Contains(searchString, StringComparison.OrdinalIgnoreCase)) {
                    yield return ugc;
                    continue;
                }

                if (ugc.Description.Contains(searchString, StringComparison.OrdinalIgnoreCase)) {
                    yield return ugc;
                    continue;
                }

                foreach (string tag in ugc.Tags) {
                    if (tag.Contains(searchString, StringComparison.OrdinalIgnoreCase)) {
                        yield return ugc;
                    }
                }
                continue;
            }

            if (item.ItemMetadata.Name != null && item.ItemMetadata.Name.Contains(searchString, StringComparison.OrdinalIgnoreCase)) {
                yield return item;
            }
        }
    }

    private static IEnumerable<MarketItem> Sort(IEnumerable<MarketItem> entries, MeretMarketSort sort, bool sortJob, bool sortGender) {
        if (sortGender) {
            entries = entries.OrderBy(item => item.ItemMetadata.Limit.Gender);
        }
        if (sortJob) {
            entries = entries.OrderBy(item => item.ItemMetadata.Limit.JobLimits);
        }

        switch (sort) {
            case MeretMarketSort.MostRecent:
                return entries.OrderByDescending(entry => entry.CreationTime);
            case MeretMarketSort.PriceHighest:
                return entries.OrderByDescending(entry => entry.Price);
            case MeretMarketSort.PriceLowest:
                return entries.OrderBy(entry => entry.Price);
            // TODO: Implement most popular?
            // Unsure how most popular is different than top seller.
            case MeretMarketSort.MostPopularPremium:
            case MeretMarketSort.MostPopularUgc:
            case MeretMarketSort.TopSeller:
                return entries.OrderByDescending(entry => entry.SalesCount);
            case MeretMarketSort.None:
            default:
                return entries;
        }
    }

    /// <summary>
    /// Limits the amount of market items returned to the client.
    /// </summary>
    /// <returns>Limited Market Items. 5 * itemsPerPage</returns>
    private static ICollection<MarketItem> TakeLimit(IEnumerable<MarketItem> entries, int startPage, int itemsPerPage) {
        const int numPages = 5;
        int offset = startPage * itemsPerPage - itemsPerPage;
        return entries.Skip(offset).Take(numPages * itemsPerPage + Math.Min(0, offset)).ToList();
    }
    #endregion
}
