using Maple2.Model;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Session;
using Maple2.Tools;

namespace Maple2.Server.Game.Manager.Items;

public class ItemDropManager {
    private readonly FieldManager field;

    public ItemDropManager(FieldManager field) {
        this.field = field;
    }

    // level is the NPC's level. It gates both group-level and item-level min/max level checks within
    // globalDropItemBox/globalDropItemSet (e.g. meso amounts in group 103 scale with NPC level).
    public ICollection<Item> GetGlobalDropItems(int globalDropBoxId, int level = 0) {
        if (!field.ServerTableMetadata.GlobalDropItemBoxTable.DropGroups.TryGetValue(globalDropBoxId, out Dictionary<int, IList<GlobalDropItemBoxTable.Group>>? dropGroup)) {
            return new List<Item>();
        }

        ICollection<Item> results = new List<Item>();

        foreach ((int groupId, IList<GlobalDropItemBoxTable.Group> list) in dropGroup) {
            foreach (GlobalDropItemBoxTable.Group group in list) {
                if (!field.ServerTableMetadata.GlobalDropItemBoxTable.Items.TryGetValue(group.GroupId, out IList<GlobalDropItemBoxTable.Item>? items)) {
                    continue;
                }

                // Check if NPC level meets the group's level requirements.
                if (group.MinLevel > level || (group.MaxLevel > 0 && group.MaxLevel < level)) {
                    continue;
                }

                // Check map and continent conditions.
                if (group.MapTypeCondition != 0 && group.MapTypeCondition != field.Metadata.Property.Type) {
                    continue;
                }

                if (group.ContinentCondition != 0 && group.ContinentCondition != field.Metadata.Property.Continent) {
                    continue;
                }

                // Implement OwnerDrop???

                var itemsAmount = new WeightedSet<GlobalDropItemBoxTable.Group.DropCount>();
                foreach (GlobalDropItemBoxTable.Group.DropCount dropCount in group.DropCounts) {
                    itemsAmount.Add(dropCount, dropCount.Probability);
                }

                int amount = itemsAmount.Get().Amount;
                if (amount == 0) {
                    continue;
                }

                var weightedItems = new WeightedSet<GlobalDropItemBoxTable.Item>();

                // Randomize list in order to get true random items when pulled from weightedItems.
                foreach (GlobalDropItemBoxTable.Item itemEntry in items.OrderBy(_ => Random.Shared.Next())) {
                    if (itemEntry.MinLevel > level || (itemEntry.MaxLevel > 0 && itemEntry.MaxLevel < level)) {
                        continue;
                    }

                    if (itemEntry.MapIds.Length > 0 && !itemEntry.MapIds.Contains(field.Metadata.Id)) {
                        continue;
                    }

                    // TODO: find quest ID and see if player has it in progress. if not, skip item. Currently just skipping.
                    if (itemEntry.QuestConstraint) {
                        continue;
                    }
                    weightedItems.Add(itemEntry, itemEntry.Weight);
                }

                if (weightedItems.Count == 0) {
                    continue;
                }

                for (int i = 0; i < amount; i++) {
                    GlobalDropItemBoxTable.Item selectedItem = weightedItems.Get();

                    int itemAmount = Random.Shared.Next(selectedItem.DropCount.Min, selectedItem.DropCount.Max + 1);
                    Item? createdItem = CreateItem(selectedItem.Id, selectedItem.Rarity, itemAmount);
                    if (createdItem == null) {
                        continue;
                    }

                    results.Add(createdItem);
                }
            }
        }

        return results;
    }

    // level gates which drop groups are eligible. For mob drops this is the player's character level
    // (MinLevel in individualDropItemTable is a player requirement, not NPC level — e.g. Shinjo's Feather
    // requires player level 30+). For other callers (e.g. housing rewards) it may be a different score.
    public ICollection<Item> GetIndividualDropItems(GameSession session, int level, int individualDropBoxId, int index = -1, int dropGroupId = -1) {
        if (!field.ServerTableMetadata.IndividualDropItemTable.Entries.TryGetValue(individualDropBoxId, out IDictionary<int, IndividualDropItemTable.Entry>? entryDict)) {
            return new List<Item>();
        }

        if (index >= 0 && dropGroupId > 0) {
            if (entryDict.TryGetValue(dropGroupId, out IndividualDropItemTable.Entry? entry)) {
                return GetAllGroups(session, level, [entry], index).ToList();
            }
        }
        return GetAllGroups(session, level, entryDict.Values.ToList()).ToList();
    }

    public ICollection<Item> GetIndividualDropItems(int individualDropBoxId, int rarity = -1) {
        if (!field.ServerTableMetadata.IndividualDropItemTable.Entries.TryGetValue(individualDropBoxId, out IDictionary<int, IndividualDropItemTable.Entry>? entryDict)) {
            return new List<Item>();
        }

        ICollection<Item> items = new List<Item>();
        foreach (IndividualDropItemTable.Entry group in entryDict.Values) {
            foreach (IndividualDropItemTable.Item itemEntry in group.Items) {
                if (itemEntry.MapIds.Length > 0 && !itemEntry.MapIds.Contains(field.Metadata.Id)) {
                    continue;
                }
                items = items.Concat(CreateIndividualDropBoxItems(itemEntry, rarity: rarity)).ToList();
            }
        }

        return items;
    }

    private IEnumerable<Item> GetSelectedIndividualDropBoxItem(GameSession session, IEnumerable<IndividualDropItemTable.Item> itemEntries, int index) {
        IndividualDropItemTable.Item? selectedItem = itemEntries.ElementAtOrDefault(index);
        if (selectedItem == null) {
            return new List<Item>();
        }

        // assuming selectedBoxes skip requirements otherwise they wouldn't have seen the item in the item selection.
        return CreateIndividualDropBoxItems(selectedItem, session.Player.Value.Character).ToList();
    }

    private IEnumerable<Item> GetAllGroups(GameSession session, int level, List<IndividualDropItemTable.Entry> entry, int index = -1) {
        var items = new List<Item>();
        foreach (IndividualDropItemTable.Entry group in entry) {
            if (group.MinLevel > level) {
                continue;
            }

            ICollection<IndividualDropItemTable.Item> itemEntries = group.Items;
            if (group.SmartGender) {
                itemEntries = GetGenderedEntries(itemEntries, session.Player.Value.Character.Gender).ToList();
            }

            var itemsAmount = new WeightedSet<IndividualDropItemTable.Entry.DropCount>();
            foreach (IndividualDropItemTable.Entry.DropCount dropCount in group.DropCounts) {
                itemsAmount.Add(dropCount, dropCount.Probability);
            }

            int amount = itemsAmount.Get().Count;
            if (amount == 0) {
                continue;
            }

            if (index >= 0) {
                items = items.Concat(GetSelectedIndividualDropBoxItem(session, itemEntries, index)).ToList();
                continue;
            }

            // some boxes have a smart drop rate, but the items dont have a weight. If that's the case, just get the item for the job. if it doesn't exist, skip.
            if (group.SmartDropRate > 0 && itemEntries.All(x => x.Weight == 0)) {
                IndividualDropItemTable.Item? jobRecommendedItems = itemEntries.FirstOrDefault(x => x.Ids.Length > 0 && IsItemJobRecommended(x.Ids.FirstOrDefault(), session.Player.Value.Character.Job.Code()));
                if (jobRecommendedItems is not null) {
                    items = items.Concat(CreateIndividualDropBoxItems(jobRecommendedItems, session.Player.Value.Character)).ToList();
                    continue;
                }
                continue;
            }

            var weightedItems = new WeightedSet<IndividualDropItemTable.Item>();
            foreach (IndividualDropItemTable.Item itemEntry in itemEntries.OrderBy(_ => Random.Shared.Next())) {
                if (itemEntry.QuestId > 0) {
                    if (!session.Quest.TryGetQuest(itemEntry.QuestId, out Quest? quest) || quest.State != QuestState.Started) {
                        continue;
                    }
                }

                if (itemEntry.MapIds.Length > 0 && !itemEntry.MapIds.Contains(field.Metadata.Id)) {
                    continue;
                }

                int weight;
                if (group.SmartDropRate > 0) {
                    weight = GetWeightByJob(itemEntry.Ids.FirstOrDefault(), session.Player.Value.Character.Job.Code(), itemEntry.Weight, itemEntry.ProperJobWeight, itemEntry.ImproperJobWeight);
                } else {
                    weight = itemEntry.Weight;
                }

                weightedItems.Add(itemEntry, weight);
            }

            if (weightedItems.Count == 0) {
                continue;
            }

            for (int i = 0; i < amount; i++) {
                IndividualDropItemTable.Item selectedItem = weightedItems.Get();
                items = items.Concat(CreateIndividualDropBoxItems(selectedItem, session.Player.Value.Character)).ToList();
            }
        }

        return items;
    }

    private IEnumerable<Item> CreateIndividualDropBoxItems(IndividualDropItemTable.Item selectedItem, Character? character = null, int rarity = -1) {
        int itemAmount = Random.Shared.Next(selectedItem.DropCount.Min, selectedItem.DropCount.Max + 1);

        if (rarity <= 0) {
            var raritySet = new WeightedSet<IndividualDropItemTable.Item.Rarity>();
            foreach (IndividualDropItemTable.Item.Rarity rarityEntry in selectedItem.Rarities) {
                raritySet.Add(rarityEntry, rarityEntry.Probability);
            }

            rarity = raritySet.Count > 0 ? raritySet.Get().Grade : -1;
        }

        foreach (int itemId in selectedItem.Ids) {
            Item? createdItem = CreateItem(itemId, rarity, itemAmount);
            if (createdItem == null) {
                continue;
            }

            if (createdItem.Transfer?.RemainTrades > 0 && selectedItem.DeductTradeCount) {
                createdItem.Transfer.RemainTrades--;
            }

            if (selectedItem.DeductRepackLimit && createdItem.Transfer != null) {
                createdItem.Transfer.RepackageCount++;
            }

            if (selectedItem.Bind && character != null) {
                createdItem.Transfer?.Bind(character);
            }

            if (selectedItem.EnchantLevel > 0) {
                createdItem.Enchant = ItemEnchantManager.GetEnchant(field.ServerTableMetadata.EnchantOptionTable, createdItem, selectedItem.EnchantLevel);
            }

            // TODO: SockDataId, DisableBreak, Announce

            yield return createdItem;
        }
    }


    private IEnumerable<IndividualDropItemTable.Item> GetGenderedEntries(ICollection<IndividualDropItemTable.Item> itemEntries, Gender gender) {
        foreach (IndividualDropItemTable.Item itemEntry in itemEntries) {
            if (!field.ItemMetadata.TryGet(itemEntry.Ids.FirstOrDefault(), out ItemMetadata? itemMetadata)) {
                continue;
            }

            if (itemMetadata.Limit.Gender != Gender.All && itemMetadata.Limit.Gender != gender) {
                continue;
            }

            yield return itemEntry;
        }
    }

    private int GetWeightByJob(int itemId, JobCode job, int weight, int jobWeight, int improperJobWeight) {
        if (!field.ItemMetadata.TryGet(itemId, out ItemMetadata? itemMetadata)) {
            return weight;
        }

        if (itemMetadata.Limit.JobRecommends.Length == 0) {
            return weight;
        }

        if (!itemMetadata.Limit.JobRecommends.Contains(job) && !itemMetadata.Limit.JobRecommends.Contains(JobCode.None)) {
            return improperJobWeight;
        }

        return jobWeight;
    }

    private bool IsItemJobRecommended(int itemId, JobCode job) {
        if (!field.ItemMetadata.TryGet(itemId, out ItemMetadata? itemMetadata)) {
            return false;
        }

        if (itemMetadata.Limit.JobRecommends.Length == 0) {
            return true;
        }

        return itemMetadata.Limit.JobRecommends.Contains(job) || itemMetadata.Limit.JobRecommends.Contains(JobCode.None);
    }

    public Item? CreateItem(int itemId, int rarity = -1, int amount = 1, bool rollMax = false) {
        if (!field.ItemMetadata.TryGet(itemId, out ItemMetadata? itemMetadata)) {
            return null;
        }

        if (rarity <= 0) {
            if (itemMetadata.Option != null && itemMetadata.Option.ConstantId is < 7 and > 0) {
                rarity = itemMetadata.Option.ConstantId;
            } else {
                rarity = 1;
            }
        }

        var item = new Item(itemMetadata, rarity, amount);
        item.Stats = field.ItemStatsCalc.GetStats(item, rollMax);
        item.Socket = field.ItemStatsCalc.GetSockets(item);

        if (item.Appearance != null) {
            item.Appearance.Color = GetColor(item.Metadata.Customize);
        }

        return item;
    }

    private EquipColor GetColor(ItemMetadataCustomize metadata) {
        // Item has no color
        if (metadata.ColorPalette == 0 ||
            !field.TableMetadata.ColorPaletteTable.Entries.TryGetValue(metadata.ColorPalette, out IReadOnlyDictionary<int, ColorPaletteTable.Entry>? palette)) {
            return default;
        }

        // Item has random color
        if (metadata.DefaultColorIndex < 0) {
            // random entry from palette
            int index = Random.Shared.Next(palette.Count);
            ColorPaletteTable.Entry randomEntry = palette.Values.ElementAt(index);
            return new EquipColor(randomEntry.Primary, randomEntry.Secondary, randomEntry.Tertiary, metadata.ColorPalette, index);
        }

        // Item has specified color
        if (palette.TryGetValue(metadata.DefaultColorIndex, out ColorPaletteTable.Entry? entry)) {
            return new EquipColor(entry.Primary, entry.Secondary, entry.Tertiary, metadata.ColorPalette, metadata.DefaultColorIndex);
        }

        return default;
    }
}
