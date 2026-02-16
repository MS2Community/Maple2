using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Manager;

public sealed class ShopManager {
    #region EntryId
    private int idCounter;

    /// <summary>
    /// Generates an EntryId unique to this specific manager instance.
    /// </summary>
    /// <returns>Returns a local EntryId</returns>
    private int NextEntryId() => Interlocked.Increment(ref idCounter);
    #endregion
    private readonly GameSession session;
    private readonly IDictionary<int, CharacterShopData> accountShopData;
    private readonly IDictionary<int, CharacterShopData> characterShopData;
    private readonly Dictionary<int, Shop> instancedShops;
    private readonly Dictionary<int, IDictionary<int, CharacterShopItemData>> characterShopItemData;
    private readonly Dictionary<int, IDictionary<int, CharacterShopItemData>> accountShopItemData;
    private readonly Dictionary<int, BuyBackItem> buyBackItems;
    private Shop? activeShop;
    private int shopNpcId;

    private readonly ILogger logger = Log.Logger.ForContext<ShopManager>();

    public ShopManager(GameSession session) {
        this.session = session;
        instancedShops = new Dictionary<int, Shop>();
        buyBackItems = new Dictionary<int, BuyBackItem>();
        using GameStorage.Request db = session.GameStorage.Context();

        accountShopData = db.GetCharacterShopData(session.AccountId);
        characterShopData = db.GetCharacterShopData(session.CharacterId);

        accountShopItemData = new Dictionary<int, IDictionary<int, CharacterShopItemData>>();
        ICollection<CharacterShopItemData> accountShopItemList = db.GetCharacterShopItemData(session.AccountId);
        foreach (CharacterShopItemData itemData in accountShopItemList) {
            if (accountShopItemData.TryGetValue(itemData.ShopId, out IDictionary<int, CharacterShopItemData>? itemDictionary)) {
                if (itemDictionary.ContainsKey(itemData.ShopItemId)) {
                    continue;
                }
                itemDictionary[itemData.ShopItemId] = itemData;
                continue;
            }
            accountShopItemData[itemData.ShopId] = new Dictionary<int, CharacterShopItemData> {
                [itemData.ShopItemId] = itemData,
            };
        }

        characterShopItemData = new Dictionary<int, IDictionary<int, CharacterShopItemData>>();
        ICollection<CharacterShopItemData> characterShopItemList = db.GetCharacterShopItemData(session.CharacterId);
        foreach (CharacterShopItemData itemData in characterShopItemList) {
            if (characterShopItemData.TryGetValue(itemData.ShopId, out IDictionary<int, CharacterShopItemData>? itemDictionary)) {
                if (itemDictionary.ContainsKey(itemData.ShopItemId)) {
                    continue;
                }
                itemDictionary[itemData.ShopItemId] = itemData;
                continue;
            }
            characterShopItemData[itemData.ShopId] = new Dictionary<int, CharacterShopItemData> {
                [itemData.ShopItemId] = itemData,
            };
        }
    }

    public void ClearActiveShop() {
        activeShop = null;
        shopNpcId = 0;
    }

    private bool TryGetShopData(int shopId, [NotNullWhen(true)] out CharacterShopData? data) {
        return accountShopData.TryGetValue(shopId, out data) || characterShopData.TryGetValue(shopId, out data);
    }

    private bool TryRemoveShopData(int shopId) {
        if (accountShopData.Remove(shopId)) {
            return true;
        }
        return characterShopData.Remove(shopId);
    }

    private bool TryGetShopItemData(int shopId, [NotNullWhen(true)] out IDictionary<int, CharacterShopItemData>? data) {
        return accountShopItemData.TryGetValue(shopId, out data) || characterShopItemData.TryGetValue(shopId, out data);
    }

    /// <summary>
    /// Load shop.
    /// </summary>
    /// <param name="shopId">Shop ID</param>
    /// <param name="npcId">Npc ID. Optional parameter. This only is used for displaying the NPC's name on the shop window top bar.</param>
    public void Load(int shopId, int npcId = 0) {
        if (!session.ServerTableMetadata.ShopTable.Entries.TryGetValue(shopId, out ShopMetadata? metadata)) {
            logger.Warning("Shop {Id} not found", shopId);
            return;
        }

        if (metadata.EnableReset) {
            activeShop = GetInstancedShop(metadata);
        } else {
            activeShop = new Shop(metadata) {
                Items = GetShopItems(metadata),
            };
        }

        shopNpcId = npcId;

        session.Send(ShopPacket.Open(activeShop, shopNpcId));
        session.Send(ShopPacket.LoadItems(activeShop.Items.Values));
        if (!metadata.IsOnlySell) {
            session.Send(ShopPacket.BuyBackItemCount((short) buyBackItems.Count));
            if (buyBackItems.Count > 0) {
                session.Send(ShopPacket.LoadBuyBackItem(buyBackItems.Values.ToArray()));
            }
        }
    }

    private Shop GetInstancedShop(ShopMetadata metadata) {
        if (instancedShops.TryGetValue(metadata.Id, out Shop? instancedShop)) {
            if (instancedShop.RestockTime < DateTime.Now.ToEpochSeconds()) {
            }
            return instancedShop;
        }

        if (!TryGetShopData(metadata.Id, out CharacterShopData? data)) {
            long restockTime = GetRestockTime(metadata);
            return CreateInstancedShop(metadata, CreateShopData(metadata, restockTime), restockTime);
        }

        if (data.RestockTime < DateTime.Now.ToEpochSeconds()) {
            using GameStorage.Request db = session.GameStorage.Context();
            if (!TryRemoveShopData(data.ShopId)) {
                throw new InvalidOperationException($"Failed to remove shop data {metadata.Id} for character {session.CharacterId}");
            }
            db.DeleteCharacterShopData(metadata.RestockData.AccountWide ? session.AccountId : session.CharacterId, metadata.Id);

            long restockTime = GetRestockTime(metadata);
            return CreateInstancedShop(metadata, CreateShopData(metadata, restockTime), restockTime);
        }

        // Assemble shop
        instancedShop = new Shop(metadata) {
            RestockTime = data.RestockTime,
            Items = GetShopItems(metadata),
        };

        if (!TryGetShopItemData(instancedShop.Id, out IDictionary<int, CharacterShopItemData>? dataDictionary)) {
            dataDictionary = CreateShopItemData(instancedShop);
        }

        foreach ((int shopItemId, CharacterShopItemData itemData) in dataDictionary) {
            if (!instancedShop.Items.TryGetValue(shopItemId, out ShopItem? shopItem)) {
                continue;
            }

            instancedShop.Items[shopItemId] = new ShopItem(shopItem.Metadata) {
                Item = itemData.Item,
                StockPurchased = itemData.StockPurchased,
            };
        }

        instancedShops[instancedShop.Id] = instancedShop;
        return instancedShop;
    }

    private Shop CreateInstancedShop(ShopMetadata metadata, CharacterShopData shopData, long restockTime) {
        var shop = new Shop(metadata);
        // First delete any existing shop item data
        long ownerId = metadata.RestockData.AccountWide ? session.AccountId : session.CharacterId;
        using GameStorage.Request db = session.GameStorage.Context();

        if (TryGetShopItemData(metadata.Id, out IDictionary<int, CharacterShopItemData>? shopItemDatas)) {
            foreach ((int shopItemId, CharacterShopItemData itemData) in shopItemDatas) {
                db.DeleteCharacterShopItemData(ownerId, itemData.ShopId, shopItemId);
            }
        }

        // Create new shop data
        shop.Items = GetShopItems(metadata);
        shop.RestockTime = restockTime;
        shop.RestockCount = shopData.RestockCount;
        CreateShopItemData(shop);
        instancedShops[shop.Id] = shop;
        return shop;
    }

    private SortedDictionary<int, ShopItem> GetShopItems(ShopMetadata metadata) {
        var items = new SortedDictionary<int, ShopItem>();

        if (!session.ServerTableMetadata.ShopItemTable.Entries.TryGetValue(metadata.Id, out Dictionary<int, ShopItemMetadata>? shopItemMetadatas)) {
            return new SortedDictionary<int, ShopItem>();
        }

        if (metadata.EnableReset) {
            int minItemCount = metadata.RestockData.MinItemCount;
            if (shopItemMetadatas.Count < metadata.RestockData.MinItemCount) {
                minItemCount = shopItemMetadatas.Count;
            }

            int failSafe = 0;
            const int maxIterations = 10;

            while (items.Count < minItemCount && failSafe < maxIterations) {
                foreach (ShopItemMetadata shopItemMetadata in shopItemMetadatas.Values) {
                    if (items.Count >= metadata.RestockData.MaxItemCount) {
                        break;
                    }

                    if (items.ContainsKey(shopItemMetadata.Id)) {
                        continue;
                    }

                    if (Random.Shared.Next(10000) > shopItemMetadata.Probability) {
                        continue;
                    }

                    Item? item = session.Field?.ItemDrop.CreateItem(shopItemMetadata.ItemId, shopItemMetadata.Rarity, shopItemMetadata.SellUnit);
                    if (item == null) {
                        continue;
                    }

                    var shopItem = new ShopItem(shopItemMetadata) {
                        Item = item,
                    };
                    items.Add(shopItemMetadata.Id, shopItem);
                }
                failSafe++;
            }
            if (failSafe >= maxIterations) {
                logger.Warning("GetShopItems: Fail safe triggered after {MaxIterations} iterations for shop {ShopId}", maxIterations, metadata.Id);
            }
        } else {
            foreach (ShopItemMetadata shopItemMetadata in shopItemMetadatas.Values) {
                Item? item = session.Field?.ItemDrop.CreateItem(shopItemMetadata.ItemId, shopItemMetadata.Rarity, shopItemMetadata.SellUnit);
                if (item == null) {
                    continue;
                }

                var shopItem = new ShopItem(shopItemMetadata) {
                    Item = item,
                };
                items.Add(shopItemMetadata.Id, shopItem);
            }
        }

        return items;
    }

    private CharacterShopData CreateShopData(ShopMetadata metadata, long restockTime) {
        if (!TryGetShopData(metadata.Id, out CharacterShopData? data)) {
            data = new CharacterShopData {
                Interval = metadata.RestockData.ResetType,
                RestockTime = restockTime,
                ShopId = metadata.Id,
            };

            if (metadata.RestockData.AccountWide) {
                accountShopData[metadata.Id] = data;
            } else {
                characterShopData[metadata.Id] = data;
            }
        }

        // We do not save shops with default reset type to database
        if (metadata.RestockData.ResetType != ResetType.Default) {
            using GameStorage.Request db = session.GameStorage.Context();
            long ownerId = metadata.RestockData.AccountWide ? session.AccountId : session.CharacterId;
            data = db.CreateCharacterShopData(ownerId, data);
            if (data == null) {
                throw new InvalidOperationException($"Failed to create shop data {metadata.Id} for character {session.CharacterId}");
            }
        }

        return data;
    }

    private IDictionary<int, CharacterShopItemData> CreateShopItemData(Shop shop) {
        var itemData = new Dictionary<int, CharacterShopItemData>();
        using GameStorage.Request db = session.GameStorage.Context();
        long ownerId = shop.RestockData.AccountWide ? session.AccountId : session.CharacterId;
        foreach ((int id, ShopItem item) in shop.Items) {
            var data = new CharacterShopItemData {
                ShopId = shop.Id,
                ShopItemId = id,
                Item = item.Item,
            };
            if (shop.RestockData.ResetType != ResetType.Default) {
                data = db.CreateCharacterShopItemData(ownerId, data);
                if (data == null) {
                    continue;
                }
            }
            itemData[id] = data;
        }

        if (shop.RestockData.AccountWide) {
            accountShopItemData[shop.Id] = itemData;
            return accountShopItemData[shop.Id];
        }
        characterShopItemData[shop.Id] = itemData;
        return characterShopItemData[shop.Id];
    }

    public void InstantRestock() {
        if (activeShop?.RestockData.DisableInstantRestock != false) {
            return;
        }

        var shop = new Shop(activeShop.Metadata);

        int cost = activeShop.RestockData.Price;
        ShopCurrencyType currencyType = activeShop.RestockData.CurrencyType;
        if (activeShop.RestockData.EnablePriceMultiplier) {
            (ShopCurrencyType CurrencyType, int Cost) multiplierCost = Core.Formulas.Shop.ExcessRestockCost(activeShop.RestockData.CurrencyType, activeShop.RestockCount);
            cost = multiplierCost.Cost;
            currencyType = multiplierCost.CurrencyType;
        }
        if (!Pay(new ShopCost { Amount = cost, Type = currencyType }, activeShop.RestockData.Price)) {
            session.Send(ShopPacket.Error(ShopError.s_err_lack_shopitem)); // not neccessarily the right error.
            return;
        }

        if (!TryGetShopData(shop.Id, out CharacterShopData? data)) {
            data = CreateShopData(shop.Metadata, GetInstantRestockTime(shop.RestockData.ResetType));
        } else {
            data.RestockTime = GetInstantRestockTime(activeShop.RestockData.ResetType);
        }
        data.RestockCount++;
        activeShop = CreateInstancedShop(shop.Metadata, data, data.RestockTime);
        session.Send(ShopPacket.InstantRestock());
        session.Send(ShopPacket.Open(activeShop, shopNpcId));
        session.Send(ShopPacket.LoadItems(activeShop.Items.Values));
    }

    public void Refresh() {
        if (activeShop == null) {
            return;
        }

        if (!activeShop.Metadata.EnableReset) {
            return;
        }

        var shop = new Shop(activeShop.Metadata);
        if (!TryGetShopData(shop.Id, out CharacterShopData? data)) {
            data = CreateShopData(shop.Metadata, GetInstantRestockTime(shop.RestockData.ResetType));
        } else {
            data.RestockTime = GetInstantRestockTime(shop.RestockData.ResetType);
        }

        activeShop = CreateInstancedShop(shop.Metadata, data, data.RestockTime);
        session.Send(ShopPacket.Open(activeShop, shopNpcId));
        session.Send(ShopPacket.LoadItems(activeShop.Items.Values));
    }

    private void UpdateStockCount(int shopId, ShopItem shopItem, int quantity) {
        if (!TryGetShopItemData(shopId, out IDictionary<int, CharacterShopItemData>? data) ||
            !data.TryGetValue(shopItem.Id, out CharacterShopItemData? itemData)) {
            return;
        }
        itemData.StockPurchased += quantity;
        shopItem.StockPurchased += quantity;
    }

    private long GetRestockTime(ShopMetadata metadata) {
        if (metadata.RestockTime > 0) {
            return metadata.RestockTime;
        }
        // Follows a time schedule
        DateTime now = DateTime.Now;
        switch (metadata.RestockData.ResetType) {
            case ResetType.Default:
                return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddMinutes(1).ToEpochSeconds();
            case ResetType.Day:
                return new DateTime(now.Year, now.Month, now.Day, 0, 0, 0).AddDays(1).ToEpochSeconds();
            case ResetType.Week:
                return DateTime.Now.NextDayOfWeek(Constant.ResetDay).Date.ToEpochSeconds();
            case ResetType.Month:
                return new DateTime(now.Year, now.Month, 1, 0, 0, 0).AddMonths(1).ToEpochSeconds();
            case ResetType.Unlimited:
                return long.MaxValue;
            default:
                logger.Error("Unknown restock interval {Interval}", metadata.RestockData.ResetType);
                return long.MaxValue;
        }
    }

    private long GetInstantRestockTime(ResetType interval) {
        return interval switch {
            ResetType.Default => DateTime.Now.AddMinutes(1).ToEpochSeconds(),
            ResetType.Day => DateTime.Now.AddDays(1).ToEpochSeconds(),
            ResetType.Week => DateTime.Now.AddDays(7).ToEpochSeconds(),
            ResetType.Month => DateTime.Now.AddMonths(1).ToEpochSeconds(),
            ResetType.Unlimited => long.MaxValue,
            _ => long.MaxValue,
        };
    }


    public void Buy(int shopItemId, int quantity) {
        if (activeShop == null) {
            return;
        }

        if (!activeShop.Items.TryGetValue(shopItemId, out ShopItem? shopItem)) {
            session.Send(ShopPacket.Error(ShopError.s_err_invalid_item));
            return;
        }


        if (shopItem.Metadata.RestrictedBuyData != null) {
            RestrictedBuyData buyData = shopItem.Metadata.RestrictedBuyData;
            if (DateTime.Now.ToEpochSeconds() < buyData.StartTime || DateTime.Now.ToEpochSeconds() > buyData.EndTime) {
                session.Send(ShopPacket.Error(ShopError.s_err_invalid_item_cannot_buy_by_period));
                return;
            }

            if (buyData.TimeRanges.Count > 0) {
                int totalSeconds = (int) new TimeSpan(DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second).TotalSeconds;
                if (!buyData.TimeRanges.Any(time => time.StartTimeOfDay > totalSeconds || time.EndTimeOfDay < totalSeconds)) {
                    session.Send(ShopPacket.Error(ShopError.s_err_invalid_item_cannot_buy_by_period));
                    return;
                }
            }

            if (buyData.Days.Count > 0) {
                if (!buyData.Days.Contains(ToShopBuyDay(DateTime.Now.DayOfWeek))) {
                    session.Send(ShopPacket.Error(ShopError.s_err_invalid_item_cannot_buy_by_period));
                    return;
                }
            }
        }

        if (shopItem.StockCount != 0 && quantity > shopItem.StockCount - shopItem.StockPurchased) {
            session.Send(ShopPacket.Error(ShopError.s_err_lack_shopitem));
            return;
        }

        if (shopItem.Metadata.Requirements.Achievement.Id > 0 && !session.Achievement.HasAchievement(shopItem.Metadata.Requirements.Achievement.Id, shopItem.Metadata.Requirements.Achievement.Rank)) {
            return;
        }

        if (shopItem.Metadata.Requirements.GuildTrophy > 0 &&
            (session.Guild.Guild == null || session.Guild.Guild.AchievementInfo.Total < shopItem.Metadata.Requirements.GuildTrophy)) {
            session.Send(ShopPacket.Error(ShopError.s_err_lack_guild_trophy));
            return;
        }

        // TODO: Guild Merchant Type/Level, Championship, Alliance

        Item item = shopItem.Item.Clone();
        item.Amount = quantity;
        if (!session.Item.Inventory.CanAdd(item)) {
            session.Send(ShopPacket.Error(ShopError.s_err_inventory));
            return;
        }

        int price = shopItem.Metadata.Cost.SaleAmount > 0 ? shopItem.Metadata.Cost.SaleAmount * quantity : shopItem.Metadata.Cost.Amount * quantity;
        if (!Pay(shopItem.Metadata.Cost, price)) {
            return;
        }

        if (activeShop.Metadata.EnableReset && shopItem.StockCount > 0) {
            UpdateStockCount(activeShop.Id, shopItem, quantity);
            session.Send(ShopPacket.Update(shopItem.Id, shopItem.StockPurchased * shopItem.Metadata.SellUnit));
        }

        session.Item.Inventory.Add(item, true);
        session.Send(ShopPacket.Buy(shopItem, shopItem.Metadata.SellUnit * quantity, price));
    }

    public void PurchaseBuyBack(int id) {
        if (activeShop == null) {
            return;
        }

        if (!buyBackItems.TryGetValue(id, out BuyBackItem? buyBackItem)) {
            session.Send(ShopPacket.Error(ShopError.s_err_invalid_item));
            return;
        }

        if (!session.Item.Inventory.CanAdd(buyBackItem.Item)) {
            session.Send(ShopPacket.Error(ShopError.s_err_inventory));
            return;
        }

        if (!Pay(new ShopCost { Type = ShopCurrencyType.Meso, Amount = (int) buyBackItem.Price }, (int) buyBackItem.Price)) {
            return;
        }

        if (!buyBackItems.Remove(id)) {
            logger.Error("Failed to remove buyback item {Id}", id);
            return;
        }
        session.Item.Inventory.Add(buyBackItem.Item, true);
        session.Send(ShopPacket.RemoveBuyBackItem(id));
    }

    public void Sell(long itemUid, int quantity) {
        if (activeShop == null) {
            return;
        }

        if (activeShop.Metadata.IsOnlySell) {
            session.Send(ShopPacket.Error(ShopError.s_msg_cant_sell_to_only_sell_shop));
            return;
        }

        Item? item = session.Item.Inventory.Get(itemUid);
        if (item == null || !item.Metadata.Limit.ShopSell) {
            return;
        }

        if (!session.Item.Inventory.Remove(item.Uid, out item, quantity)) {
            logger.Error("Failed to remove item {Uid} from inventory", itemUid);
            return;
        }

        long sellPrice = Core.Formulas.Shop.SellPrice(item.Metadata, item.Type, item.Rarity);
        if (session.Currency.CanAddMeso(sellPrice) != sellPrice) {
            logger.Error("Could not add {sellPrice} meso(s) to player {CharacterId}", sellPrice, session.CharacterId);
            return;
        }

        session.Currency.Meso += sellPrice;

        if (buyBackItems.Count >= Constant.MaxBuyBackItems && !RemoveBuyBackItem()) {
            return;
        }

        int entryId = NextEntryId();
        buyBackItems[entryId] = new BuyBackItem {
            Id = entryId,
            Item = item,
            AddedTime = DateTime.Now.ToEpochSeconds(),
            Price = sellPrice,
        };

        session.Send(ShopPacket.LoadBuyBackItem(buyBackItems[entryId]));
    }

    private bool Pay(ShopCost cost, int price) {
        switch (cost.Type) {
            case ShopCurrencyType.Meso:
                if (session.Currency.CanAddMeso(-price) != -price) {
                    session.Send(ShopPacket.Error(ShopError.s_err_lack_meso));
                    return false;
                }
                session.Currency.Meso -= price;
                break;
            case ShopCurrencyType.Meret:
            case ShopCurrencyType.EventMeret:
                if (session.Currency.CanAddMeret(-price) != -price) {
                    session.Send(ShopPacket.Error(ShopError.s_err_lack_merat));
                    return false;
                }

                session.Currency.Meret -= price;
                break;
            case ShopCurrencyType.GameMeret:
                if (session.Currency.CanAddGameMeret(-price) != -price) {
                    session.Send(ShopPacket.Error(ShopError.s_err_lack_merat));
                    return false;
                }

                session.Currency.Meret -= price;
                break;
            case ShopCurrencyType.Item:
                var ingredient = new ItemComponent(cost.ItemId, -1, price, ItemTag.None);
                if (!session.Item.Inventory.ConsumeItemComponents([
                        ingredient,
                    ])) {
                    session.Send(ShopPacket.Error(ShopError.s_err_lack_payment_item, 0, cost.ItemId));
                    return false;
                }
                break;
            default:
                CurrencyType currencyType = cost.Type switch {
                    ShopCurrencyType.ValorToken => CurrencyType.ValorToken,
                    ShopCurrencyType.Treva => CurrencyType.Treva,
                    ShopCurrencyType.Rue => CurrencyType.Rue,
                    ShopCurrencyType.HaviFruit => CurrencyType.HaviFruit,
                    ShopCurrencyType.StarPoint => CurrencyType.StarPoint,
                    ShopCurrencyType.MenteeToken => CurrencyType.MenteeToken,
                    ShopCurrencyType.MentorToken => CurrencyType.MentorToken,
                    ShopCurrencyType.MesoToken => CurrencyType.MesoToken,
                    ShopCurrencyType.ReverseCoin => CurrencyType.ReverseCoin,
                    _ => CurrencyType.None,

                };
                if (currencyType == CurrencyType.None || session.Currency[currencyType] < price) {
                    return false;
                }

                session.Currency[currencyType] -= cost.Amount;
                break;
        }
        return true;
    }

    private bool RemoveBuyBackItem() {
        while (buyBackItems.Count >= Constant.MaxBuyBackItems) {
            BuyBackItem? item = buyBackItems.Values.MinBy(entry => entry.AddedTime);
            if (item == null) {
                logger.Error("Failed to remove buyback item");
                return false;
            }

            buyBackItems.Remove(item.Id);
            session.Item.Inventory.Discard(item.Item);
            session.Send(ShopPacket.RemoveBuyBackItem(item.Id));
        }
        return true;
    }

    public void DailyReset() {
        ResetShopData(ResetType.Day);
    }

    public void WeeklyReset() {
        ResetShopData(ResetType.Week);
    }

    private void ResetShopData(ResetType interval) {
        foreach (CharacterShopData data in accountShopData.Values) {
            if (data.Interval == interval) {
                data.RestockCount = 0;
            }
        }
        foreach (CharacterShopData data in characterShopData.Values) {
            if (data.Interval == interval) {
                data.RestockCount = 0;
            }
        }
    }

    public void Save(GameStorage.Request db) {
        db.SaveItems(0, buyBackItems.Values.Select(item => item.Item).ToArray());

        Dictionary<int, CharacterShopData> saveAccountShops = accountShopData.Values
            .Where(data => data.Interval != ResetType.Default)
            .ToDictionary(data => data.ShopId);

        Dictionary<int, CharacterShopData> saveCharacterShops = characterShopData.Values
            .Where(data => data.Interval != ResetType.Default)
            .ToDictionary(data => data.ShopId);

        db.SaveCharacterShopData(session.AccountId, saveAccountShops.Values.ToList());
        db.SaveCharacterShopData(session.CharacterId, saveCharacterShops.Values.ToList());

        db.SaveCharacterShopItemData(session.AccountId, accountShopItemData
            .Where(kvp => saveAccountShops.ContainsKey(kvp.Key))
            .SelectMany(kvp => kvp.Value.Values)
            .ToList());

        db.SaveCharacterShopItemData(session.CharacterId, characterShopItemData
            .Where(kvp => saveCharacterShops.ContainsKey(kvp.Key))
            .SelectMany(kvp => kvp.Value.Values)
            .ToList());
    }

    private ShopBuyDay ToShopBuyDay(DayOfWeek dayOfWeek) {
        return dayOfWeek switch {
            DayOfWeek.Monday => ShopBuyDay.Monday,
            DayOfWeek.Tuesday => ShopBuyDay.Tuesday,
            DayOfWeek.Wednesday => ShopBuyDay.Wednesday,
            DayOfWeek.Thursday => ShopBuyDay.Thursday,
            DayOfWeek.Friday => ShopBuyDay.Friday,
            DayOfWeek.Saturday => ShopBuyDay.Saturday,
            DayOfWeek.Sunday => ShopBuyDay.Sunday,
            _ => throw new InvalidDataException("Invalid day of week"),
        };
    }
}
