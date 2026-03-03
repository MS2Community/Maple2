using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Formulas;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools;
using Serilog;

namespace Maple2.Server.Game.Manager;

public sealed class ItemMergeManager {

    private readonly GameSession session;

    private readonly ILogger logger = Log.Logger.ForContext<ItemMergeManager>();

    private Item? upgradeItem;
    private Item? catalystItem;

    private ConstantsTable Constants => session.ServerTableMetadata.ConstantsTable;

    public ItemMergeManager(GameSession session) {
        this.session = session;
    }

    private void Clear() {
        upgradeItem = null;
        catalystItem = null;
    }

    public void Stage(long itemUid) {
        Clear();

        Item? item = session.Item.Inventory.Get(itemUid);
        if (item == null) {
            item = session.Item.Equips.Get(itemUid);
            if (item == null) {
                // TODO: Error
                return;
            }
        }
        int type = item.Type.Type;
        upgradeItem = item;

        List<long> catalystUids = [];
        foreach ((int id, Dictionary<int, ItemMergeTable.Entry> options) in session.ServerTableMetadata.ItemMergeTable.Entries) {
            IList<Item> catalysts = session.Item.Inventory.Find(id).ToList();
            if (!catalysts.Any()) {
                continue;
            }

            if (catalysts.FirstOrDefault()?.Metadata.Limit.Level < item.Metadata.Limit.Level) {
                continue;
            }

            if (!options.ContainsKey(type)) {
                continue;
            }

            foreach (Item catalyst in catalysts) {
                catalystUids.Add(catalyst.Uid);
            }
        }

        session.Send(ItemMergePacket.Stage(catalystUids));
    }

    public void SelectCrystal(long itemUid, long crystalUid) {
        if (upgradeItem?.Uid != itemUid) {
            logger.Error("Item Merge upgrade item UID mismatch: {UpgradeItemUid} != {ItemUid}", upgradeItem?.Uid, itemUid);
            return;
        }

        Item? catalyst = session.Item.Inventory.Get(crystalUid);
        if (catalyst == null) {
            return;
        }

        catalystItem = catalyst;

        if (!session.ServerTableMetadata.ItemMergeTable.Entries.TryGetValue(catalyst.Id, out Dictionary<int, ItemMergeTable.Entry>? options) ||
            (!options.TryGetValue(upgradeItem.Type.Type, out ItemMergeTable.Entry? mergeSlot))) {
            return;
        }

        session.Send(ItemMergePacket.Select(mergeSlot, ItemMerge.CostMultiplier(upgradeItem.Rarity)));

        if (!session.ScriptMetadata.TryGet(Constant.EmpowermentNpc, out ScriptMetadata? script) ||
            !script.States.TryGetValue(Constants.MergeSmithScriptID, out ScriptState? state)) {
            return;
        }

        CinematicEventScript[] eventScripts = state.Contents.First().Events;
        session.NpcScript = new NpcScriptManager(session, eventScripts);
    }

    public void Empower(long itemUid, long catalystUid) {
        if (upgradeItem?.Uid != itemUid) {
            logger.Error("Item Merge upgrade item UID mismatch: {UpgradeItemUid} != {ItemUid}", upgradeItem?.Uid, itemUid);
            return;
        }

        if (catalystItem?.Uid != catalystUid) {
            logger.Error("Item Merge catalyst item UID mismatch: {CatalystItemUid} != {CatalystUid}", catalystItem?.Uid, catalystUid);
            return;
        }

        if (!session.ServerTableMetadata.ItemMergeTable.Entries.TryGetValue(catalystItem.Id, out Dictionary<int, ItemMergeTable.Entry>? options) ||
            (!options.TryGetValue(upgradeItem.Type.Type, out ItemMergeTable.Entry? mergeSlot))) {
            return;
        }

        int multiplier = ItemMerge.CostMultiplier(upgradeItem.Rarity);
        long totalMesoCost = mergeSlot.MesoCost * multiplier;
        if (session.Currency.CanAddMeso(-totalMesoCost) != -totalMesoCost) {
            session.Send(ItemMergePacket.Error(ItemMergeError.s_item_merge_option_error_lack_meso));
            return;
        }

        List<IngredientInfo> ingredients = [];
        foreach (ItemComponent component in mergeSlot.Materials) {
            ingredients.Add(new IngredientInfo(component.Tag, component.Amount * multiplier));
        }
        if (!session.Item.Inventory.Consume(ingredients)) {
            session.Send(ItemMergePacket.Error(ItemMergeError.s_item_merge_option_error_lack_material));
            return;
        }

        if (!session.Item.Inventory.Consume(catalystItem.Uid, 1)) {
            session.Send(ItemMergePacket.Error(ItemMergeError.s_item_merge_option_error_invalid_merge_scroll));
            return;
        }

        session.Currency.Meso += -(mergeSlot.MesoCost * multiplier);

        int selectedIndex = Random.Shared.Next(mergeSlot.BasicOptions.Count + mergeSlot.SpecialOptions.Count);

        if (selectedIndex < mergeSlot.BasicOptions.Count) {
            (BasicAttribute attribute, ItemMergeTable.Option mergeOption) = mergeSlot.BasicOptions.ElementAt(selectedIndex);

            (int value, float rate) = Roll(mergeOption);
            upgradeItem.Stats![ItemStats.Type.Empowerment1] = new ItemStats.Option {
                Basic = {
                    [attribute] = new BasicOption(value, rate),
                },
            };
        } else {
            (SpecialAttribute attribute, ItemMergeTable.Option mergeOption) = mergeSlot.SpecialOptions.ElementAt(selectedIndex - mergeSlot.BasicOptions.Count);

            (int value, float rate) = Roll(mergeOption);
            upgradeItem.Stats![ItemStats.Type.Empowerment1] = new ItemStats.Option {
                Special = {
                    [attribute] = new SpecialOption(rate, value),
                },
            };
        }

        session.Send(ItemMergePacket.Empower(upgradeItem));
        session.NpcScript?.EmpowerEvent();
        session.ConditionUpdate(ConditionType.item_merge_success);

        return;

        (int, float) Roll(ItemMergeTable.Option mergeOption) {
            WeightedSet<(ItemMergeTable.Range<int>, ItemMergeTable.Range<int>)> weightedSet = new();

            for (int i = 0; i < mergeOption.Values.Length; i++) {
                weightedSet.Add((mergeOption.Values[i], mergeOption.Rates[i]), mergeOption.Weights[i]);
            }

            (ItemMergeTable.Range<int> valueRange, ItemMergeTable.Range<int> rateRange) = weightedSet.Get();
            int value = Random.Shared.Next(valueRange.Min, valueRange.Max);
            float rate = Random.Shared.Next(rateRange.Min, rateRange.Max);
            rate /= 1000;
            return (value, rate);
        }
    }
}
