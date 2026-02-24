using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Maple2.Server.Game.Manager;

public class ItemBoxManager {
    private readonly GameSession session;
    public int BoxCount { get; private set; }

    private readonly ILogger logger = Log.Logger.ForContext<ItemBoxManager>();

    public ItemBoxManager(GameSession session) {
        this.session = session;
        BoxCount = 0;
    }

    public void Reset() {
        BoxCount = 0;
    }

    public ItemBoxError Open(Item item, int count = 1, int index = -1) {
        if (string.IsNullOrEmpty(item.Metadata.Function?.Parameters)) {
            logger.Error("Item {ItemId} has no parameters for item box function", item.Id);
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }
        if (item.Type.IsTranscendenceCrystal) {
            Dictionary<string, string> parameters = XmlParseUtil.GetParameters(item.Metadata.Function.Parameters);
            if (parameters.Count < 4) {
                logger.Error("Invalid parameters for item box: {ItemId}", item.Id);
                return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
            }
            if (!parameters.TryGetValue("globalDropBoxId", out string? globalDropBoxIdStr) || !int.TryParse(globalDropBoxIdStr, out int globalDropBoxId)) {
                globalDropBoxId = 0;
            }

            if (!parameters.TryGetValue("individualDropBoxId", out string? individualDropBoxIdStr) || !int.TryParse(individualDropBoxIdStr, out int individualDropBoxId)) {
                individualDropBoxId = 0;
            }
            return OpenItemBox(item, globalDropBoxId, 0, 0, individualDropBoxId, 1, count);
        }
        int[] itemBoxParams = item.Metadata.Function.Parameters.Split(',').Select(int.Parse).ToArray();
        return item.Metadata.Function?.Type switch {
            ItemFunction.SelectItemBox => SelectItemBox(item, itemBoxParams[0], itemBoxParams[1], index, count),
            ItemFunction.OpenItemBox => OpenItemBox(item, itemBoxParams[0], itemBoxParams[1], itemBoxParams[2], itemBoxParams[3], itemBoxParams.Length == 5 ? itemBoxParams[4] : 1, count),
            ItemFunction.OpenItemBoxWithKey => OpenItemBoxWithKey(item, itemBoxParams[0], itemBoxParams[1], itemBoxParams[2], itemBoxParams[5], count),
            ItemFunction.OpenGachaBox => OpenGachaBox(item, itemBoxParams[0], count),
            _ => LogAndReturnError(item.Metadata.Function?.Type.ToString()),
        };
    }

    private ItemBoxError LogAndReturnError(string? type) {
        logger.Error("Invalid box type: {Type}", type);
        return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
    }

    public ItemBoxError OpenLulluBox(Item item, int count = 1, bool autoPay = false) {
        if (session.Field is null) return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;

        Dictionary<string, string> parameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);

        // Get common dropbox
        if (!int.TryParse(parameters["commonBoxId"], out int commonBoxId)) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        // Get uncommon dropbox
        if (!int.TryParse(parameters["unCommonBoxId"], out int unCommonBoxId)) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        var boxIngredient = new IngredientInfo(Enum.Parse<ItemTag>(parameters["boxItemTag"]), 1);
        var keyIngredient = new IngredientInfo(Enum.Parse<ItemTag>(parameters["keyItemTag"]), 1);
        var totalItems = new List<Item>();
        for (int startCount = 0; startCount < count; startCount++) {
            if (!session.Item.Inventory.Consume([keyIngredient])) {
                return ItemBoxError.s_err_cannot_open_multi_itembox_inventory;
            }

            if (autoPay) {
                if (!session.Item.Inventory.Consume([boxIngredient])) {
                    int.TryParse(parameters["boxPrice"], out int mesoCost);
                    mesoCost = Math.Max(mesoCost, 0);
                    if (session.Currency.CanAddMeso(-mesoCost) != -mesoCost) {
                        return ItemBoxError.s_err_cannot_open_multi_itembox_inventory;
                    }
                    session.Currency.Meso -= mesoCost;
                }
            } else {
                if (!session.Item.Inventory.Consume([boxIngredient])) {
                    return ItemBoxError.s_err_cannot_open_multi_itembox_inventory;
                }
            }

            // common dropbox items
            ICollection<Item> items = session.Field.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, unCommonBoxId);

            // uncommon dropbox items
            items = items.Concat(session.Field.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, commonBoxId)).ToList();
            if (items.Count == 0) {
                return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
            }

            foreach (Item newItem in items) {
                totalItems.Add(newItem);
                if (!session.Item.Inventory.Add(newItem, true)) {
                    session.Item.MailItem(newItem);
                }
            }

            BoxCount++;
        }

        session.Send(ItemScriptPacket.LulluBox(totalItems));
        return ItemBoxError.ok;
    }

    public ItemBoxError OpenLulluBoxSimple(Item item, int count = 1) {
        if (session.Field is null) return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        Dictionary<string, string> parameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);

        if (!int.TryParse(parameters["commonBoxId"], out int commonBoxId)) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        if (!session.ServerTableMetadata.IndividualDropItemTable.Entries.TryGetValue(commonBoxId, out IDictionary<int, IndividualDropItemTable.Entry>? entryDict)) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        if (entryDict.Count == 0) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        var keyItemTag = new IngredientInfo(Enum.Parse<ItemTag>(parameters["keyItemTag"]), 1);
        var totalItems = new List<Item>();
        for (int startCount = 0; startCount < count; startCount++) {
            if (!session.Item.Inventory.Consume([keyItemTag])) {
                return ItemBoxError.s_err_cannot_open_multi_itembox_inventory;
            }

            // common dropbox items
            ICollection<Item> items = session.Field.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, commonBoxId);
            if (items.Count == 0) {
                return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
            }

            foreach (Item newItem in items) {
                totalItems.Add(newItem);
                if (!session.Item.Inventory.Add(newItem, true)) {
                    session.Item.MailItem(newItem);
                }
            }

            BoxCount++;
        }

        session.Send(ItemScriptPacket.LulluBox(totalItems));
        return ItemBoxError.ok;
    }

    private ItemBoxError SelectItemBox(Item item, int groupId, int boxId, int index, int count = 1) {
        if (session.Field is null) return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        // check if box count is enough to open
        if (session.Item.Inventory.Find(item.Id).Sum(box => box.Amount) < 1) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        if (!session.ServerTableMetadata.IndividualDropItemTable.Entries.TryGetValue(boxId, out IDictionary<int, IndividualDropItemTable.Entry>? boxEntry)) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        if (boxEntry.Count == 0) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        var error = ItemBoxError.ok;
        var ingredient = new ItemComponent(item.Id, -1, 1, ItemTag.None);
        for (int startCount = 0; startCount < count; startCount++) {
            if (!session.Item.Inventory.ConsumeItemComponents([ingredient])) {
                return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
            }

            ICollection<Item> itemList = session.Field.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, boxId, index, groupId);
            if (itemList.Count == 0) {
                return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
            }
            foreach (Item newItem in itemList) {
                if (!session.Item.Inventory.Add(newItem, true)) {
                    session.Item.MailItem(newItem);
                    error = ItemBoxError.s_err_cannot_open_multi_itembox_inventory;
                }
            }
            BoxCount++;
            if (error != ItemBoxError.ok) {
                return error;
            }
        }
        return error;
    }

    private ItemBoxError OpenItemBox(Item item, int globalDropBoxId, int unknownId, int itemId, int individualDropBoxId, int itemRequiredAmount, int count = 1) {
        if (session.Field is null) return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        // unknownId is treated as a bool. if 1 = receive one item from each drop group, if 0 = receive all items from one drop group.
        // There are occasions where this number is higher than 1. No idea what it is as it's not a globaldropBoxId

        if (itemRequiredAmount > -1 && session.Item.Inventory.Find(item.Id).Sum(box => box.Amount) < itemRequiredAmount) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        // Verify table exists before consuming items
        if (!session.ServerTableMetadata.IndividualDropItemTable.Entries.TryGetValue(individualDropBoxId, out IDictionary<int, IndividualDropItemTable.Entry>? entryDic)) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        Dictionary<int, IList<GlobalDropItemBoxTable.Group>>? globalDropEntries = [];
        if (globalDropBoxId > 0 && !session.ServerTableMetadata.GlobalDropItemBoxTable.DropGroups.TryGetValue(globalDropBoxId, out globalDropEntries)) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        if (entryDic.Count == 0 && globalDropEntries.Count == 0) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        var error = ItemBoxError.ok;
        var ingredient = new ItemComponent(item.Id, item.Rarity, itemRequiredAmount, ItemTag.None);
        for (int startCount = 0; startCount < count; startCount++) {
            if (!session.Item.Inventory.ConsumeItemComponents([ingredient])) {
                return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
            }

            if (itemId > 0) {
                Item? newItem = session.Field.ItemDrop.CreateItem(itemId, item.Metadata.Option?.ConstantId ?? 1);
                if (newItem == null) {
                    continue;
                }
                if (!session.Item.Inventory.Add(newItem, true)) {
                    session.Item.MailItem(newItem);
                    error = ItemBoxError.s_err_cannot_open_multi_itembox_inventory;
                }
            }

            IEnumerable<Item> itemList = session.Field.ItemDrop.GetGlobalDropItems(globalDropBoxId);

            itemList = itemList.Concat(session.Field.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, individualDropBoxId));
            foreach (Item newItem in itemList) {
                if (!session.Item.Inventory.Add(newItem, true)) {
                    session.Item.MailItem(newItem);
                    error = ItemBoxError.s_err_cannot_open_multi_itembox_inventory;
                }
            }
            BoxCount++;
            if (error != ItemBoxError.ok) {
                return error;
            }
        }
        return error;
    }

    private ItemBoxError OpenItemBoxWithKey(Item item, int keyItemId, int keyAmountRequired, int itemId, int boxId, int count = 1) {
        if (session.Field is null) return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        if (keyItemId == 0) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        // check for keys
        if (session.Item.Inventory.Find(keyItemId).Sum(requiredItem => requiredItem.Amount) < keyAmountRequired) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        // Check if the boxId is in the drop table
        if (!session.ServerTableMetadata.IndividualDropItemTable.Entries.TryGetValue(boxId, out IDictionary<int, IndividualDropItemTable.Entry>? entryDict)) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        if (entryDict.Count == 0) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        IEnumerable<Item> items = session.Field.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, boxId);
        var ingredients = new List<ItemComponent> {
            new ItemComponent(item.Id, -item.Rarity, 1, ItemTag.None),
            new ItemComponent(keyItemId, -1, keyAmountRequired, ItemTag.None),
        };
        for (int startCount = 0; startCount < count; startCount++) {
            if (!session.Item.Inventory.ConsumeItemComponents(ingredients)) {
                return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
            }

            if (itemId > 0) {
                Item? newItem = session.Field?.ItemDrop.CreateItem(itemId, item.Metadata.Option?.ConstantId ?? 1);
                if (newItem == null) {
                    continue;
                }
                if (!session.Item.Inventory.Add(newItem, true)) {
                    session.Item.MailItem(newItem);
                }
            }

            foreach (Item newItem in items) {
                if (!session.Item.Inventory.Add(newItem, true)) {
                    session.Item.MailItem(newItem);
                }
            }
            BoxCount++;
        }
        return ItemBoxError.ok;
    }

    private ItemBoxError OpenGachaBox(Item item, int gachaInfoId, int count) {
        if (session.Field is null) return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        // Check if the gachaInfoId is in the drop table
        if (!session.TableMetadata.GachaInfoTable.Entries.TryGetValue(gachaInfoId, out GachaInfoTable.Entry? gachaEntry)) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        if (!session.ServerTableMetadata.IndividualDropItemTable.Entries.TryGetValue(gachaEntry.DropBoxId, out IDictionary<int, IndividualDropItemTable.Entry>? entryDict)) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        if (entryDict.Count == 0) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        var totalItems = new List<Item>();
        var ingredient = new ItemComponent(item.Id, item.Rarity, 1, ItemTag.None);
        for (int startCount = 0; startCount < count; startCount++) {
            if (!session.Item.Inventory.ConsumeItemComponents([ingredient])) {
                return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
            }

            IEnumerable<Item> items = session.Field.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, gachaEntry.DropBoxId);
            totalItems = totalItems.Concat(items).ToList();
            foreach (Item createdItem in items) {
                createdItem.GachaDismantleId = gachaInfoId;
                if (!session.Item.Inventory.Add(createdItem, true)) {
                    session.Item.MailItem(createdItem);
                }
            }
            BoxCount++;
        }

        session.Send(ItemScriptPacket.Gacha(totalItems));
        return ItemBoxError.ok;
    }
}
