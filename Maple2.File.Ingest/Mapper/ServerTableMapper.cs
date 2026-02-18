using System.Globalization;
using System.Xml;
using Maple2.Database.Extensions;
using Maple2.File.Ingest.Utils;
using Maple2.File.IO;
using Maple2.File.Parser;
using Maple2.File.Parser.Enum;
using Maple2.File.Parser.Xml.Table.Server;
using Maple2.Model;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using DayOfWeek = System.DayOfWeek;
using ExpType = Maple2.Model.Enum.ExpType;
using Fish = Maple2.File.Parser.Xml.Table.Server.Fish;
using FishingSpot = Maple2.File.Parser.Xml.Table.Server.FishingSpot;
using GuildNpcType = Maple2.Model.Enum.GuildNpcType;
using IndividualItemDrop = Maple2.File.Parser.Xml.Table.Server.IndividualItemDrop;
using InstanceType = Maple2.Model.Enum.InstanceType;
using JobConditionTable = Maple2.Model.Metadata.JobConditionTable;
using MergeOption = Maple2.File.Parser.Xml.Table.Server.MergeOption;
using ScriptEventType = Maple2.Model.Enum.ScriptEventType;
using ScriptType = Maple2.Model.Enum.ScriptType;
using TimeEventType = Maple2.File.Parser.Enum.TimeEventType;

namespace Maple2.File.Ingest.Mapper;

public class ServerTableMapper : TypeMapper<ServerTableMetadata> {
    private readonly ServerTableParser parser;

    public ServerTableMapper(M2dReader xmlReader) {
        parser = new ServerTableParser(xmlReader);
    }

    protected override IEnumerable<ServerTableMetadata> Map() {
        yield return new ServerTableMetadata {
            Name = ServerTableNames.INSTANCE_FIELD,
            Table = ParseInstanceField(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.SCRIPT_CONDITION,
            Table = ParseScriptCondition(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.SCRIPT_FUNCTION,
            Table = ParseScriptFunction(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.SCRIPT_EVENT,
            Table = ParseScriptEventConditionTable(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.JOB_CONDITION,
            Table = ParseJobCondition(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.BONUS_GAME,
            Table = ParseBonusGameTable(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.GLOBAL_DROP_ITEM_BOX,
            Table = ParseGlobalItemDropTable(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.USER_STAT,
            Table = ParseUserStat(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.INDIVIDUAL_DROP_ITEM,
            Table = ParseIndividualItemDropTable(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.PRESTIGE_EXP,
            Table = ParsePrestigeExpTable(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.PRESTIGE_ID_EXP,
            Table = ParsePrestigeIdExpTable(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.TIME_EVENT,
            Table = ParseTimeEventTable(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.GAME_EVENT,
            Table = ParseGameEventTable(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.OX_QUIZ,
            Table = ParseOxQuizTable(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.ITEM_MERGE,
            Table = ParseItemMergeOptionTable(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.SHOP,
            Table = ParseShop(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.SHOP_ITEM,
            Table = ParseShopItems(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.BEAUTY_SHOP,
            Table = ParseBeautyShops(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.MERET_MARKET,
            Table = ParseMeretCustomShop(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.FISH,
            Table = ParseFish(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.COMBINE_SPAWN,
            Table = ParseCombineSpawn(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.ENCHANT_OPTION,
            Table = ParseEnchantOption(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.UNLIMITED_ENCHANT_OPTION,
            Table = ParseUnlimitedEnchantOption(),
        };

    }

    private InstanceFieldTable ParseInstanceField() {
        var results = new Dictionary<int, InstanceFieldMetadata>();
        foreach ((int instanceId, InstanceField instanceField) in parser.ParseInstanceField()) {
            foreach (int fieldId in instanceField.fieldIDs) {

                InstanceFieldMetadata instanceFieldMetadata = new(
                    MapId: fieldId,
                    Type: Enum.TryParse(instanceField.instanceType.ToString(), out InstanceType instanceType) ? instanceType : InstanceType.none,
                    InstanceId: instanceId,
                    BackupSourcePortal: instanceField.backupSourcePortal,
                    PoolCount: instanceField.poolCount,
                    SaveField: instanceField.isSaveField,
                    NpcStatFactorId: instanceField.npcStatFactorID,
                    MaxCount: instanceField.maxCount,
                    OpenType: instanceField.openType,
                    OpenValue: instanceField.openValue
                );

                results.Add(fieldId, instanceFieldMetadata);
            }
        }

        return new InstanceFieldTable(results);
    }

    private ScriptConditionTable ParseScriptCondition() {
        var results = new Dictionary<int, Dictionary<int, ScriptConditionMetadata>>();
        results = MergeNpcScriptConditions(results, parser.ParseNpcScriptCondition());
        results = MergeQuestScriptConditions(results, parser.ParseQuestScriptCondition());

        return new ScriptConditionTable(results);
    }

    private Dictionary<int, Dictionary<int, ScriptConditionMetadata>> MergeNpcScriptConditions(Dictionary<int, Dictionary<int, ScriptConditionMetadata>> results, IEnumerable<(int NpcId, IDictionary<int, NpcScriptCondition> ScriptConditions)> parser) {
        foreach ((int npcId, IDictionary<int, NpcScriptCondition> scripts) in parser) {
            var scriptConditions = new Dictionary<int, ScriptConditionMetadata>();
            foreach ((int scriptId, NpcScriptCondition scriptCondition) in scripts) {
                var questStarted = new Dictionary<int, bool>();
                foreach (string quest in scriptCondition.quest_start) {
                    KeyValuePair<int, bool> parsedQuest = ParseToIntKeyValuePair(quest);
                    questStarted.Add(parsedQuest.Key, parsedQuest.Value);
                }

                var questsCompleted = new Dictionary<int, bool>();
                foreach (string quest in scriptCondition.quest_complete) {
                    KeyValuePair<int, bool> parsedQuest = ParseToIntKeyValuePair(quest);
                    questsCompleted.Add(parsedQuest.Key, parsedQuest.Value);
                }

                var items = new List<KeyValuePair<ItemComponent, bool>>();
                for (int i = 0; i < scriptCondition.item.Length; i++) {
                    KeyValuePair<int, bool> parsedItem = ParseToIntKeyValuePair(scriptCondition.item[i]);
                    string itemCount = scriptCondition.itemCount.ElementAtOrDefault(i) ?? "1";
                    if (!int.TryParse(itemCount, out int itemAmount)) {
                        itemAmount = 1;
                    }
                    var item = new ItemComponent(parsedItem.Key, -1, itemAmount, ItemTag.None);
                    items.Add(new KeyValuePair<ItemComponent, bool>(item, parsedItem.Value));
                }

                scriptConditions.Add(scriptId, new ScriptConditionMetadata(
                    Id: npcId,
                    ScriptId: scriptId,
                    Type: ScriptType.Npc,
                    Maid: new ScriptConditionMetadata.MaidData(
                        Authority: scriptCondition.maid_auth,
                        Expired: scriptCondition.maid_expired != "!1",
                        ReadyToPay: scriptCondition.maid_ready_to_pay != "!1",
                        ClosenessRank: scriptCondition.maid_affinity_grade,
                        ClosenessTime: ParseToIntKeyValuePair(scriptCondition.maid_affinity_time),
                        MoodTime: ParseToIntKeyValuePair(scriptCondition.maid_mood_time),
                        DaysBeforeExpired: ParseToIntKeyValuePair(scriptCondition.maid_day_before_expired)
                    ),
                    Wedding: new ScriptConditionMetadata.WeddingData(
                        HasReservation: scriptCondition.weddingHallBooking < 0 ? null : scriptCondition.weddingHallBooking == 1,
                        MarriageDays: scriptCondition.marriageDate,
                        UserState: scriptCondition.weddingState < 0 ? null : (MaritalStatus) scriptCondition.weddingState,
                        HallState: ParseToStringKeyValuePair(scriptCondition.weddingHallState),
                        CoolingOff: scriptCondition.coolingOff),
                    JobCode: scriptCondition.job?.Select(job => (JobCode) job).ToList() ?? [],
                    QuestStarted: questStarted,
                    QuestCompleted: questsCompleted,
                    Items: items,
                    Buff: ParseToIntKeyValuePair(scriptCondition.buff),
                    Meso: ParseToIntKeyValuePair(scriptCondition.meso),
                    Level: ParseToIntKeyValuePair(scriptCondition.level),
                    AchieveCompleted: ParseToIntKeyValuePair(scriptCondition.achieve_complete),
                    DeathPenalty: scriptCondition.panelty == 1,
                    InGuild: scriptCondition.guild
                ));
            }
            results.Add(npcId, scriptConditions);
        }
        return results;
    }

    private Dictionary<int, Dictionary<int, ScriptConditionMetadata>> MergeQuestScriptConditions(Dictionary<int, Dictionary<int, ScriptConditionMetadata>> results, IEnumerable<(int NpcId, IDictionary<int, QuestScriptCondition> ScriptConditions)> parser) {
        foreach ((int questId, IDictionary<int, QuestScriptCondition> scripts) in parser) {
            var scriptConditions = new Dictionary<int, ScriptConditionMetadata>();
            foreach ((int scriptId, QuestScriptCondition scriptCondition) in scripts) {
                var questStarted = new Dictionary<int, bool>();
                foreach (string quest in scriptCondition.quest_start) {
                    KeyValuePair<int, bool> parsedQuest = ParseToIntKeyValuePair(quest);
                    questStarted.Add(parsedQuest.Key, parsedQuest.Value);
                }

                var questsCompleted = new Dictionary<int, bool>();
                foreach (string quest in scriptCondition.quest_complete) {
                    KeyValuePair<int, bool> parsedQuest = ParseToIntKeyValuePair(quest);
                    questsCompleted.Add(parsedQuest.Key, parsedQuest.Value);
                }

                var items = new List<KeyValuePair<ItemComponent, bool>>();
                for (int i = 0; i < scriptCondition.item.Length; i++) {
                    KeyValuePair<int, bool> parsedItem = ParseToIntKeyValuePair(scriptCondition.item[i]);
                    string itemCount = scriptCondition.itemCount.ElementAtOrDefault(i) ?? "1";
                    if (!int.TryParse(itemCount, out int itemAmount)) {
                        itemAmount = 1;
                    }
                    var item = new ItemComponent(parsedItem.Key, -1, itemAmount, ItemTag.None);
                    items.Add(new KeyValuePair<ItemComponent, bool>(item, parsedItem.Value));
                }

                scriptConditions.Add(scriptId, new ScriptConditionMetadata(
                    Id: questId,
                    ScriptId: scriptId,
                    Type: ScriptType.Quest,
                    Maid: new ScriptConditionMetadata.MaidData(
                        Authority: scriptCondition.maid_auth,
                        Expired: scriptCondition.maid_expired != "!1",
                        ReadyToPay: scriptCondition.maid_ready_to_pay != "!1",
                        ClosenessRank: scriptCondition.maid_affinity_grade,
                        ClosenessTime: ParseToIntKeyValuePair(scriptCondition.maid_affinity_time),
                        MoodTime: ParseToIntKeyValuePair(scriptCondition.maid_mood_time),
                        DaysBeforeExpired: ParseToIntKeyValuePair(scriptCondition.maid_day_before_expired)
                    ),
                    Wedding: new ScriptConditionMetadata.WeddingData(
                        HasReservation: scriptCondition.weddingHallBooking < 0 ? null : scriptCondition.weddingHallBooking == 1,
                        MarriageDays: scriptCondition.marriageDate,
                        UserState: scriptCondition.weddingState < 0 ? null : (MaritalStatus) scriptCondition.weddingState,
                        HallState: ParseToStringKeyValuePair(scriptCondition.weddingHallState),
                        CoolingOff: scriptCondition.coolingOff),
                    JobCode: scriptCondition.job?.Select(job => (JobCode) job).ToList() ?? [],
                    QuestStarted: questStarted,
                    QuestCompleted: questsCompleted,
                    Items: items,
                    Buff: ParseToIntKeyValuePair(scriptCondition.buff),
                    Meso: ParseToIntKeyValuePair(scriptCondition.meso),
                    Level: ParseToIntKeyValuePair(scriptCondition.level),
                    AchieveCompleted: ParseToIntKeyValuePair(scriptCondition.achieve_complete),
                    InGuild: scriptCondition.guild,
                    DeathPenalty: scriptCondition.panelty == 1
                ));
            }
            results.Add(questId, scriptConditions);
        }
        return results;
    }

    private static KeyValuePair<int, bool> ParseToIntKeyValuePair(string input) {
        bool value = !input.StartsWith("!");

        if (!value) {
            input = input.Replace("!", "");
        }

        if (!int.TryParse(input, out int key)) {
            key = 0;
        }
        return new KeyValuePair<int, bool>(key, value);
    }

    private static KeyValuePair<string, bool> ParseToStringKeyValuePair(string input) {
        bool value = !input.StartsWith("!");

        if (!value) {
            input = input.Substring(1);
        }
        return new KeyValuePair<string, bool>(input, value);
    }

    private ScriptFunctionTable ParseScriptFunction() {
        var results = new Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>>();
        results = MergeNpcScriptFunctions(results, parser.ParseNpcScriptFunction());
        results = MergeQuestScriptFunctions(results, parser.ParseQuestScriptFunction());

        return new ScriptFunctionTable(results);
    }

    private static Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>> MergeNpcScriptFunctions(Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>> results, IEnumerable<(int NpcId, IDictionary<int, NpcScriptFunction> ScriptFunctions)> parser) {
        foreach ((int npcId, IDictionary<int, NpcScriptFunction> scripts) in parser) {
            var scriptDict = new Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>(); // scriptIds, functionDict
            foreach ((int scriptId, NpcScriptFunction scriptFunction) in scripts) {
                var presentItems = new List<ItemComponent>();
                for (int i = 0; i < scriptFunction.presentItemID.Length; i++) {
                    short itemRarity = scriptFunction.presentItemRank.ElementAtOrDefault(i) != default(short) ? scriptFunction.presentItemRank.ElementAtOrDefault(i) : (short) -1;
                    int itemAmount = scriptFunction.presentItemAmount.ElementAtOrDefault(i) != default ? scriptFunction.presentItemAmount.ElementAtOrDefault(i) : 1;
                    presentItems.Add(new ItemComponent(scriptFunction.presentItemID[i], itemRarity, itemAmount, ItemTag.None));
                }

                var collectItems = new List<ItemComponent>();
                for (int i = 0; i < scriptFunction.collectItemID.Length; i++) {
                    int itemAmount = scriptFunction.collectItemAmount.ElementAtOrDefault(i) != default ? scriptFunction.collectItemAmount.ElementAtOrDefault(i) : 1;
                    collectItems.Add(new ItemComponent(scriptFunction.collectItemID[i], -1, itemAmount, ItemTag.None));
                }

                var metadata = new ScriptFunctionMetadata(
                    Id: npcId, // NpcId or QuestId
                    ScriptId: scriptId,
                    Type: ScriptType.Npc,
                    FunctionId: scriptFunction.functionID,
                    EndFunction: scriptFunction.endFunction,
                    PortalId: scriptFunction.portal,
                    UiName: scriptFunction.uiName,
                    UiArg: scriptFunction.uiArg,
                    UiArg2: scriptFunction.uiArg2,
                    MoveMapId: scriptFunction.moveFieldID,
                    MovePortalId: scriptFunction.moveFieldPortalID,
                    MoveMapMovie: scriptFunction.moveFieldMovie,
                    Emoticon: scriptFunction.emoticon,
                    PresentItems: presentItems,
                    CollectItems: collectItems,
                    SetTriggerValueTriggerId: scriptFunction.setTriggerValueTriggerID,
                    SetTriggerValueKey: scriptFunction.setTriggerValueKey,
                    SetTriggerValue: scriptFunction.setTriggerValue,
                    Divorce: scriptFunction.divorce,
                    PresentExp: scriptFunction.presentExp,
                    CollectMeso: scriptFunction.collectMeso,
                    MaidMoodIncrease: scriptFunction.maidMoodUp,
                    MaidClosenessIncrease: scriptFunction.maidAffinityUp,
                    MaidPay: scriptFunction.maidPay
                );
                if (!scriptDict.TryGetValue(scriptId, out Dictionary<int, ScriptFunctionMetadata>? functionDict)) {
                    functionDict = new Dictionary<int, ScriptFunctionMetadata> {
                        { scriptFunction.functionID, metadata },
                    };
                    scriptDict.Add(scriptId, functionDict);
                } else {
                    functionDict.Add(scriptFunction.functionID, metadata);
                }
            }
            results.Add(npcId, scriptDict);
        }
        return results;
    }

    private static Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>> MergeQuestScriptFunctions(Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>> results, IEnumerable<(int NpcId, IDictionary<int, QuestScriptFunction> ScriptFunctions)> parser) {
        foreach ((int questId, IDictionary<int, QuestScriptFunction> scripts) in parser) {
            var scriptDict = new Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>(); // scriptIds, functionDict
            foreach ((int scriptId, QuestScriptFunction scriptFunction) in scripts) {
                var presentItems = new List<ItemComponent>();
                for (int i = 0; i < scriptFunction.presentItemID.Length; i++) {
                    short itemRarity = scriptFunction.presentItemRank.ElementAtOrDefault(i) != default(short) ? scriptFunction.presentItemRank.ElementAtOrDefault(i) : (short) -1;
                    int itemAmount = scriptFunction.presentItemAmount.ElementAtOrDefault(i) != default ? scriptFunction.presentItemAmount.ElementAtOrDefault(i) : 1;
                    presentItems.Add(new ItemComponent(scriptFunction.presentItemID[i], itemRarity, itemAmount, ItemTag.None));
                }

                var collectItems = new List<ItemComponent>();
                for (int i = 0; i < scriptFunction.collectItemID.Length; i++) {
                    int itemAmount = scriptFunction.collectItemAmount.ElementAtOrDefault(i) != default ? scriptFunction.collectItemAmount.ElementAtOrDefault(i) : 1;
                    collectItems.Add(new ItemComponent(scriptFunction.collectItemID[i], -1, itemAmount, ItemTag.None));
                }

                var metadata = new ScriptFunctionMetadata(
                    Id: questId,
                    ScriptId: scriptId,
                    Type: ScriptType.Quest,
                    FunctionId: scriptFunction.functionID,
                    EndFunction: scriptFunction.endFunction,
                    PortalId: scriptFunction.portal,
                    UiName: scriptFunction.uiName,
                    UiArg: scriptFunction.uiArg,
                    UiArg2: scriptFunction.uiArg2,
                    MoveMapId: scriptFunction.moveFieldID,
                    MovePortalId: scriptFunction.moveFieldPortalID,
                    MoveMapMovie: scriptFunction.moveFieldMovie,
                    Emoticon: scriptFunction.emoticon,
                    PresentItems: presentItems,
                    CollectItems: collectItems,
                    SetTriggerValueTriggerId: scriptFunction.setTriggerValueTriggerID,
                    SetTriggerValueKey: scriptFunction.setTriggerValueKey,
                    SetTriggerValue: scriptFunction.setTriggerValue,
                    Divorce: scriptFunction.divorce,
                    PresentExp: scriptFunction.presentExp,
                    CollectMeso: scriptFunction.collectMeso,
                    MaidMoodIncrease: scriptFunction.maidMoodUp,
                    MaidClosenessIncrease: scriptFunction.maidAffinityUp,
                    MaidPay: scriptFunction.maidPay
                );
                if (!scriptDict.TryGetValue(scriptId, out Dictionary<int, ScriptFunctionMetadata>? functionDict)) {
                    functionDict = new Dictionary<int, ScriptFunctionMetadata> {
                        { scriptFunction.functionID, metadata },
                    };
                    scriptDict.Add(scriptId, functionDict);
                } else {
                    functionDict.Add(scriptFunction.functionID, metadata);
                }
            }
            results.Add(questId, scriptDict);
        }
        return results;
    }

    private JobConditionTable ParseJobCondition() {
        var results = new Dictionary<int, JobConditionMetadata>();
        foreach ((int npcId, Parser.Xml.Table.Server.JobConditionTable jobCondition) in parser.ParseJobConditionTable()) {
            DateTime date = DateTime.TryParseExact(jobCondition.date, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ? date : DateTime.MinValue;
            results.Add(npcId, new JobConditionMetadata(
                NpcId: npcId,
                ScriptId: jobCondition.scriptID,
                StartedQuestId: jobCondition.quest_start,
                CompletedQuestId: jobCondition.quest_complete,
                JobCode: (JobCode) jobCondition.job,
                MaidAuthority: jobCondition.maid_auth,
                MaidClosenessTime: jobCondition.maid_affinity_time,
                MaidCosenessRank: jobCondition.maid_affinity_grade,
                Date: date.ToEpochSeconds(),
                BuffId: jobCondition.buff,
                Mesos: jobCondition.meso,
                Level: jobCondition.level,
                Home: jobCondition.home,
                Roulette: jobCondition.roulette,
                Guild: jobCondition.guild,
                CompletedAchievement: jobCondition.achieve_complete,
                IsBirthday: jobCondition.birthday,
                ChangeToJobCode: (JobCode) jobCondition.jobCode,
                MapId: jobCondition.map,
                MoveMapId: jobCondition.moveFieldID,
                MovePortalId: jobCondition.movePortalID,
                DeathPenalty: jobCondition.panelty
            ));
        }

        return new JobConditionTable(results);
    }

    private BonusGameTable ParseBonusGameTable() {
        var bonusGames = new Dictionary<int, BonusGameTable.Game>();
        foreach ((int type, int id, BonusGame bonusGame) in parser.ParseBonusGame()) {
            List<BonusGameTable.Game.Slot> slots = [];
            foreach (BonusGame.Slot slot in bonusGame.slot) {
                slots.Add(new BonusGameTable.Game.Slot(
                    MinProp: slot.minProp,
                    MaxProp: slot.maxProp));
            }
            bonusGames.Add(id, new BonusGameTable.Game(
                Id: id,
                ConsumeItem: new ItemComponent(
                    ItemId: bonusGame.consumeItemID,
                    Rarity: 1,
                    Amount: bonusGame.consumeItemCount,
                    Tag: ItemTag.None),
                Slots: slots.ToArray()));
        }

        var drops = new Dictionary<int, BonusGameTable.Drop>();
        foreach ((int type, int id, BonusGameDrop gameDrop) in parser.ParseBonusGameDrop()) {
            List<BonusGameTable.Drop.Item> items = [];
            foreach (BonusGameDrop.Item item in gameDrop.item) {
                items.Add(new BonusGameTable.Drop.Item(
                    ItemComponent: new ItemComponent(
                        ItemId: item.id,
                        Rarity: item.rank,
                        Amount: item.count,
                        Tag: ItemTag.None),
                    Probability: item.prop,
                    Notice: item.notice));
            }
            drops.Add(id, new BonusGameTable.Drop(
                Id: id,
                Items: items.ToArray()));
        }

        return new BonusGameTable(bonusGames, drops);
    }

    private GlobalDropItemBoxTable ParseGlobalItemDropTable() {
        var dropGroups = new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>>();

        foreach ((int id, GlobalDropItemBox itemDrop) in parser.ParseGlobalDropItemBox()) {
            var groups = new List<GlobalDropItemBoxTable.Group>();
            foreach (GlobalDropItemBox.Group group in itemDrop.v) {
                List<GlobalDropItemBoxTable.Group.DropCount> dropCounts = [];
                for (int i = 0; i < group.dropCount.Length; i++) {
                    dropCounts.Add(new GlobalDropItemBoxTable.Group.DropCount(
                        Amount: group.dropCount[i],
                        Probability: group.dropCountProbability[i]));
                }
                groups.Add(new GlobalDropItemBoxTable.Group(
                    GroupId: group.dropGroupIDs,
                    MinLevel: group.minLevel,
                    MaxLevel: group.maxLevel,
                    DropCounts: dropCounts,
                    OwnerDrop: group.isOwnerDrop,
                    MapTypeCondition: (MapType) group.mapTypeCondition,
                    ContinentCondition: (Continent) group.continentCondition));
            }

            if (!dropGroups.TryGetValue(id, out Dictionary<int, IList<GlobalDropItemBoxTable.Group>>? groupDict)) {
                groupDict = new Dictionary<int, IList<GlobalDropItemBoxTable.Group>> {
                    { id, groups },
                };
                dropGroups.Add(id, groupDict);
            } else {
                groupDict.Add(id, groups);
            }
        }

        var dropItems = new Dictionary<int, IList<GlobalDropItemBoxTable.Item>>();
        foreach ((int id, GlobalDropItemSet itemBox) in parser.ParseGlobalDropItemSet()) {
            var items = new List<GlobalDropItemBoxTable.Item>();

            foreach (GlobalDropItemSet.Item item in itemBox.v) {
                int minCount = item.minCount <= 0 ? 1 : item.minCount;
                int maxCount = item.maxCount < item.minCount ? item.minCount : item.maxCount;
                items.Add(new GlobalDropItemBoxTable.Item(
                    Id: item.itemID,
                    MinLevel: item.minLevel,
                    MaxLevel: item.maxLevel,
                    DropCount: new GlobalDropItemBoxTable.Range<int>(minCount, maxCount),
                    Rarity: item.grade,
                    Weight: item.weight,
                    MapIds: item.mapDependency,
                    QuestConstraint: item.constraintsQuest));
            }

            dropItems.Add(id, items);
        }
        return new GlobalDropItemBoxTable(dropGroups, dropItems);
    }

    private UserStatTable ParseUserStat() {
        static IReadOnlyDictionary<BasicAttribute, long> UserStatMetadataMapper(UserStat userStat) {
            Dictionary<BasicAttribute, long> stats = new() {
                { BasicAttribute.Strength, (long) userStat.str },
                { BasicAttribute.Dexterity, (long) userStat.dex },
                { BasicAttribute.Intelligence, (long) userStat.@int },
                { BasicAttribute.Luck, (long) userStat.luk },
                { BasicAttribute.Health, (long) userStat.hp },
                { BasicAttribute.HpRegen, (long) userStat.hp_rgp },
                { BasicAttribute.HpRegenInterval, (long) userStat.hp_inv },
                { BasicAttribute.Spirit, (long) userStat.sp },
                { BasicAttribute.SpRegen, (long) userStat.sp_rgp },
                { BasicAttribute.SpRegenInterval, (long) userStat.sp_inv },
                { BasicAttribute.Stamina, (long) userStat.ep },
                { BasicAttribute.StaminaRegen, (long) userStat.ep_rgp },
                { BasicAttribute.StaminaRegenInterval, (long) userStat.ep_inv },
                { BasicAttribute.AttackSpeed, (long) userStat.asp },
                { BasicAttribute.MovementSpeed, (long) userStat.msp },
                { BasicAttribute.Accuracy, (long) userStat.atp },
                { BasicAttribute.Evasion, (long) userStat.evp },
                { BasicAttribute.CriticalRate, (long) userStat.cap },
                { BasicAttribute.CriticalDamage, (long) userStat.cad },
                { BasicAttribute.CriticalEvasion, (long) userStat.car },
                { BasicAttribute.Defense, (long) userStat.ndd },
                { BasicAttribute.PerfectGuard, (long) userStat.abp },
                { BasicAttribute.JumpHeight, (long) userStat.jmp },
                { BasicAttribute.PhysicalAtk, (long) userStat.pap },
                { BasicAttribute.MagicalAtk, (long) userStat.map },
                { BasicAttribute.PhysicalRes, (long) userStat.par },
                { BasicAttribute.MagicalRes, (long) userStat.mar },
                { BasicAttribute.MinWeaponAtk, (long) userStat.wapmin },
                { BasicAttribute.MaxWeaponAtk, (long) userStat.wapmax },
                { BasicAttribute.Damage, (long) userStat.dmg },
                { BasicAttribute.Piercing, (long) userStat.pen },
                { BasicAttribute.BonusAtk, (long) userStat.base_atk },
                { BasicAttribute.PetBonusAtk, (long) userStat.sp_value },
            };

            return stats;
        }

        return new UserStatTable(
            new Dictionary<JobCode, IReadOnlyDictionary<short, IReadOnlyDictionary<BasicAttribute, long>>> {
                { JobCode.Newbie, parser.ParseUserStat1().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.Knight, parser.ParseUserStat10().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.Berserker, parser.ParseUserStat20().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.Wizard, parser.ParseUserStat30().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.Priest, parser.ParseUserStat40().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.Archer, parser.ParseUserStat50().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.HeavyGunner, parser.ParseUserStat60().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.Thief, parser.ParseUserStat70().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.Assassin, parser.ParseUserStat80().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.RuneBlader, parser.ParseUserStat90().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.Striker, parser.ParseUserStat100().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.SoulBinder, parser.ParseUserStat110().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.GameMaster, parser.ParseUserStat999().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
            }
        );
    }

    private IndividualDropItemTable ParseIndividualItemDropTable() {
        var results = new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>>();

        foreach ((int id, IndividualItemDrop dropBox) in parser.ParseIndividualItemDrop()) {
            var entries = new Dictionary<int, IndividualDropItemTable.Entry>();

            foreach (IndividualItemDrop.Group group in dropBox.group) {
                List<IndividualDropItemTable.Item> items = [];
                foreach (IndividualItemDrop.Group.Item item in group.v) {
                    int minCount = item.minCount <= 0 ? 1 : item.minCount;
                    int maxCount = item.maxCount < item.minCount ? item.minCount : item.maxCount;
                    List<IndividualDropItemTable.Item.Rarity> rarities = item.gradeProbability
                        .Select((probability, i) => new IndividualDropItemTable.Item.Rarity(probability, item.grade[i]))
                        .ToList();

                    if (rarities.Count == 0) {
                        if (item.grade.Length > 0) {
                            foreach (short grade in item.grade) {
                                rarities.Add(new IndividualDropItemTable.Item.Rarity(100, grade));
                            }
                        } else if (item.uiItemRank != 0) {
                            rarities.Add(new IndividualDropItemTable.Item.Rarity(100, item.uiItemRank));
                        }
                    }
                    items.Add(new IndividualDropItemTable.Item(
                        Ids: [item.itemID, item.itemID2],
                        Announce: item.isAnnounce,
                        ProperJobWeight: item.properJobWeight,
                        ImproperJobWeight: item.imProperJobWeight,
                        Weight: item.weight,
                        DropCount: new IndividualDropItemTable.Range<int>(minCount, maxCount),
                        Rarities: rarities,
                        EnchantLevel: item.enchantLevel,
                        SocketDataId: item.socketDataID,
                        DeductTradeCount: item.tradableCountDeduction,
                        DeductRepackLimit: item.rePackingLimitCountDeduction,
                        Bind: item.isBindCharacter,
                        DisableBreak: item.disableBreak,
                        MapIds: item.mapDependency,
                        QuestId: item.constraintsQuest ? GetQuestId(dropBox.comment, item.reference1) : 0
                    ));
                }

                IList<IndividualDropItemTable.Entry.DropCount> dropCounts = group.dropCount.Zip(group.dropCountProbability, (count, probability) => new IndividualDropItemTable.Entry.DropCount(count, probability)).ToList();
                if (dropCounts.Count == 0) {
                    dropCounts.Add(new IndividualDropItemTable.Entry.DropCount(1, 100));
                }

                var entry = new IndividualDropItemTable.Entry(
                    GroupId: group.dropGroupID,
                    SmartDropRate: group.smartDropRate,
                    DropCounts: dropCounts,
                    MinLevel: group.dropGroupMinLevel,
                    ServerDrop: group.serverDrop,
                    SmartGender: group.isApplySmartGenderDrop,
                    Items: items
                );

                entries.Add(group.dropGroupID, entry);

            }

            results.Add(id, entries);
        }
        return new IndividualDropItemTable(results);

        int GetQuestId(string comment, string reference1) {
            if (reference1.Contains("Quest")) {
                string[] referenceArray = reference1.Split("/");
                int referenceQuestIndex = Array.IndexOf(referenceArray, "Quest");

                if (!int.TryParse(referenceArray[referenceQuestIndex - 2], out int questId) && comment.Contains("Quest")) {
                    string[] commentArray = comment.Split("/");
                    int commentQuestIndex = Array.IndexOf(commentArray, "Quest");
                    if (string.IsNullOrEmpty(commentArray[commentQuestIndex - 2])) {
                        return 0;
                    }
                    return !int.TryParse(commentArray[commentQuestIndex - 2], out questId) ? 0 : questId;
                }
            }
            return 0;
        }
    }

    private PrestigeExpTable ParsePrestigeExpTable() {
        var results = new Dictionary<ExpType, long>();

        foreach ((AdventureExpType type, AdventureExpTable table) in parser.ParseAdventureExp()) {
            ExpType expType = ToExpType(type);
            results.Add(expType, table.value);
        }

        return new PrestigeExpTable(results);
    }

    private PrestigeIdExpTable ParsePrestigeIdExpTable() {
        var results = new Dictionary<int, PrestigeIdExpTable.Entry>();
        foreach ((int id, AdventureIdExpTable table) in parser.ParseAdventureIdExp()) {
            results.Add(id, new PrestigeIdExpTable.Entry(
                Id: id,
                Value: table.value,
                Type: ToExpType(table.expType)));
        }

        return new PrestigeIdExpTable(results);
    }

    private static ExpType ToExpType(AdventureExpType type) {
        return type switch {
            AdventureExpType.Exp_MapCommon => ExpType.mapCommon,
            AdventureExpType.Exp_MapHidden => ExpType.mapHidden,
            AdventureExpType.Exp_TaxiStation => ExpType.taxi,
            AdventureExpType.Exp_Telescope => ExpType.telescope,
            AdventureExpType.Exp_RareChest => ExpType.rareChest,
            AdventureExpType.Exp_RareChestFirst => ExpType.rareChestFirst,
            AdventureExpType.Exp_NormalChest => ExpType.normalChest,
            AdventureExpType.Exp_DropItem => ExpType.dropItem,
            AdventureExpType.Exp_DungeonBoss => ExpType.dungeonBoss,
            AdventureExpType.Exp_MusicMasteryLv1 => ExpType.musicMastery1,
            AdventureExpType.Exp_MusicMasteryLv2 => ExpType.musicMastery2,
            AdventureExpType.Exp_MusicMasteryLv3 => ExpType.musicMastery3,
            AdventureExpType.Exp_MusicMasteryLv4 => ExpType.musicMastery4,
            AdventureExpType.Exp_Arcade => ExpType.arcade,
            AdventureExpType.Exp_Fishing => ExpType.fishing,
            AdventureExpType.Exp_Rest => ExpType.rest,
            AdventureExpType.Exp_Quest => ExpType.quest,
            AdventureExpType.Exp_PvpBloodMineRank1 => ExpType.bloodMineRank1,
            AdventureExpType.Exp_PvpBloodMineRank2 => ExpType.bloodMineRank2,
            AdventureExpType.Exp_PvpBloodMineRank3 => ExpType.bloodMineRank3,
            AdventureExpType.Exp_PvpBloodMineRankOther => ExpType.bloodMineRankOther,
            AdventureExpType.Exp_PvpRedDuelWin => ExpType.redDuelWin,
            AdventureExpType.Exp_PvpRedDuelLose => ExpType.redDuelLose,
            AdventureExpType.Exp_PvpBtiTeamWin => ExpType.btiTeamWin,
            AdventureExpType.Exp_PvpBtiTeamLose => ExpType.btiTeamLose,
            AdventureExpType.Exp_PvpRankDuelWin => ExpType.rankDuelWin,
            AdventureExpType.Exp_PvpRankDuelLose => ExpType.rankDuelLose,
            AdventureExpType.Exp_Gathering => ExpType.gathering,
            AdventureExpType.Exp_Manufacturing => ExpType.manufacturing,
            AdventureExpType.Exp_RandomDungeonBonus => ExpType.randomDungeonBonus,
            AdventureExpType.Exp_MiniGame => ExpType.miniGame,
            AdventureExpType.Exp_UserMiniGame => ExpType.userMiniGame,
            AdventureExpType.Exp_UserMiniGameExtra => ExpType.userMiniGameExtra,
            AdventureExpType.Exp_Mission => ExpType.mission,
            AdventureExpType.Exp_DungeonRelative => ExpType.dungeonRelative,
            AdventureExpType.Exp_GuildUserExp => ExpType.guildUserExp,
            AdventureExpType.Exp_DailyGuildQuest => ExpType.dailyGuildQuest,
            AdventureExpType.Exp_WeeklyGuildQuest => ExpType.weeklyGuildQuest,
            AdventureExpType.Exp_PetTaming => ExpType.petTaming,
            AdventureExpType.Exp_DailyMission => ExpType.dailymission,
            AdventureExpType.Exp_DailyMissionLevelUp => ExpType.dailymissionLevelUp,
            AdventureExpType.Exp_mapleSurvival => ExpType.mapleSurvival,
            AdventureExpType.Exp_DarkStream => ExpType.darkStream,
            AdventureExpType.Exp_DungeonClear => ExpType.dungeonClear,
            AdventureExpType.Exp_KillMonster => ExpType.monster,
            AdventureExpType.Exp_QuestETC => ExpType.questEtc,
            AdventureExpType.Exp_EpicQuest => ExpType.epicQuest,
            AdventureExpType.Exp_KillMonsterBoss => ExpType.monsterBoss,
            AdventureExpType.Exp_KillMonsterElite => ExpType.monsterElite,
            _ => ExpType.none,
        };
    }

    private TimeEventTable ParseTimeEventTable() {
        var results = new Dictionary<int, GlobalPortalMetadata>();
        foreach ((int id, TimeEventData data) in parser.ParseTimeEventData()) {
            // TODO: Handle other event types
            if (data.type == TimeEventType.GlobalEvent) {
                var entries = new GlobalPortalMetadata.Field[3]; // UI only supports 3 fields
                entries[0] = new GlobalPortalMetadata.Field(
                    Name: data.eventName1,
                    MapId: data.eventField1.Length == 0 ? 0 : data.eventField1[0],
                    PortalId: data.eventField1.Length == 0 ? 0 : data.eventField1[1]);
                entries[1] = new GlobalPortalMetadata.Field(
                    Name: data.eventName2,
                    MapId: data.eventField2.Length == 0 ? 0 : data.eventField2[0],
                    PortalId: data.eventField2.Length == 0 ? 0 : data.eventField2[1]);
                entries[2] = new GlobalPortalMetadata.Field(
                    Name: data.eventName3,
                    MapId: data.eventField3.Length == 0 ? 0 : data.eventField3[0],
                    PortalId: data.eventField3.Length == 0 ? 0 : data.eventField3[1]);
                int[] startTimeArray = ParseTimeToArray(data.startTime);
                int[] endTimeArray = ParseTimeToArray(data.endTime);
                int[] cycleArray = ParseTimeToArray(data.cycleTime);
                int[] randomArray = ParseTimeToArray(data.randomTime);
                int[] lifeArray = ParseTimeToArray(data.lifeTime);
                results.Add(id, new GlobalPortalMetadata(
                    Id: id,
                    Probability: data.prob,
                    StartTime: new DateTime(startTimeArray[0], startTimeArray[1], startTimeArray[2], startTimeArray[3], startTimeArray[4], startTimeArray[5]),
                    EndTime: new DateTime(endTimeArray[0], endTimeArray[1], endTimeArray[2], endTimeArray[3], endTimeArray[4], endTimeArray[5]),
                    CycleTime: new TimeSpan(cycleArray[2], cycleArray[3], cycleArray[4], cycleArray[5]),
                    RandomTime: new TimeSpan(randomArray[2], randomArray[3], randomArray[4], randomArray[5]),
                    LifeTime: new TimeSpan(lifeArray[2], lifeArray[3], lifeArray[4], lifeArray[5]),
                    PopupMessage: data.popupMessage,
                    SoundId: data.soundID,
                    Entries: entries));
            }
        }
        return new TimeEventTable(results);

        int[] ParseTimeToArray(string time) {
            string[] timeArray = time.Split('-');
            int[] timeInt = new int[timeArray.Length];
            for (int i = 0; i < timeArray.Length; i++) {
                timeInt[i] = int.Parse(timeArray[i]);
            }
            return timeInt;
        }
    }

    private GameEventTable ParseGameEventTable() {
        var results = new Dictionary<int, GameEventMetadata>();
        foreach ((int id, GameEvent data) in parser.ParseGameEvent()) {
            if (!Enum.TryParse(data.eventType, out GameEventType eventType)) {
                Console.WriteLine($"Unknown GameEventType: {data.eventType}");
            }

            GameEventData? eventData = ParseGameEventData(eventType, data.value1, data.value2, data.value3, data.value4);
            if (eventData == null) {
                continue;
            }

            DateTime startTime = DateTime.TryParseExact(data.eventStart, "yyyy-MM-dd-HH-mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out startTime) ? startTime : DateTime.MinValue;
            DateTime endTime = DateTime.TryParseExact(data.eventEnd, "yyyy-MM-dd-HH-mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out endTime) ? endTime : DateTime.MinValue;

            // Only add events that are not expired
            if (endTime < DateTime.UtcNow) {
                continue;
            }

            (TimeSpan partTimeStart, TimeSpan partTimeEnd) = ParsePartTime(data.partTime);
            results.Add(id, new GameEventMetadata(
                Id: id,
                Type: eventType,
                StartTime: startTime,
                EndTime: endTime,
                StartPartTime: partTimeStart,
                EndPartTime: partTimeEnd,
                ActiveDays: data.dayOfWeek.Length == 0 ? [] : data.dayOfWeek.Select(ParseDayOfWeek).ToArray(),
                Data: eventData,
                Value1: data.value1,
                Value2: data.value2,
                Value3: data.value3,
                Value4: data.value4));
        }
        return new GameEventTable(results);

        DayOfWeek ParseDayOfWeek(Maple2.File.Parser.Enum.DayOfWeek dayofWeek) {
            return dayofWeek switch {
                Maple2.File.Parser.Enum.DayOfWeek.sun => DayOfWeek.Sunday,
                Maple2.File.Parser.Enum.DayOfWeek.mon => DayOfWeek.Monday,
                Maple2.File.Parser.Enum.DayOfWeek.tue => DayOfWeek.Tuesday,
                Maple2.File.Parser.Enum.DayOfWeek.wed => DayOfWeek.Wednesday,
                Maple2.File.Parser.Enum.DayOfWeek.thu => DayOfWeek.Thursday,
                Maple2.File.Parser.Enum.DayOfWeek.fri => DayOfWeek.Friday,
                Maple2.File.Parser.Enum.DayOfWeek.sat => DayOfWeek.Saturday,
                _ => DayOfWeek.Sunday,
            };
        }
    }

    private (TimeSpan, TimeSpan) ParsePartTime(string partTimeString) {
        string[] partTimeStringArray = partTimeString.Split("-");
        if (partTimeStringArray.Length != 2) {
            return (TimeSpan.Zero, TimeSpan.Zero);
        }
        TimeSpan startTime = TimeSpan.Parse(partTimeStringArray[0]);
        TimeSpan endTime = TimeSpan.Parse(partTimeStringArray[1]);
        return (startTime, endTime);
    }

    private GameEventData? ParseGameEventData(GameEventType type, string value1, string value2, string value3, string value4) {
        var value1Xml = new XmlDocument();
        var value2Xml = new XmlDocument();
        var value3Xml = new XmlDocument();
        var value4Xml = new XmlDocument();

        switch (type) {
            case GameEventType.BlueMarble:
                if (!string.IsNullOrEmpty(value1)) {
                    value1Xml.LoadXml(value1);
                }

                value2Xml.LoadXml(value2);
                var rounds = new List<BlueMarble.Round>();
                var requiredItem = new ItemComponent(0, 0, 0, ItemTag.None);

                XmlNode? roundNode = value1Xml.FirstChild;
                if (roundNode != null) {
                    if (roundNode.Attributes?["consumeItemID"] != null) {
                        if (!int.TryParse(roundNode.Attributes?["consumeItemID"]?.Value, out int itemId)) {
                            itemId = 0;
                        }
                        if (!int.TryParse(roundNode.Attributes?["consumeItemCount"]?.Value, out int itemCount)) {
                            itemCount = 1;
                        }
                        requiredItem = new ItemComponent(itemId, -1, itemCount, ItemTag.None);
                    }

                    foreach (XmlNode vNode in roundNode.ChildNodes) {
                        if (!int.TryParse(vNode.Attributes?["round"]?.Value, out int round)) {
                            round = 0;
                        }

                        if (!int.TryParse(vNode.Attributes?["itemID"]?.Value, out int itemId)) {
                            itemId = 0;
                        }

                        if (!int.TryParse(vNode.Attributes?["itemCount"]?.Value, out int itemCount)) {
                            itemCount = 1;
                        }

                        rounds.Add(new BlueMarble.Round(
                            RoundCount: round,
                            Item: new ItemComponent(
                                ItemId: itemId,
                                Rarity: 1,
                                Amount: itemCount,
                                Tag: ItemTag.None)));
                    }
                }

                XmlNode? slotsNode = value2Xml.FirstChild;
                if (slotsNode == null) {
                    return null;
                }

                var slots = new List<BlueMarble.Slot>();
                foreach (XmlNode vNode in slotsNode.ChildNodes) {
                    if (!Enum.TryParse(vNode.Attributes?["type"]?.Value, true, out BlueMarbleSlotType slotType)) {
                        slotType = BlueMarbleSlotType.Item;
                    }

                    if (!int.TryParse(vNode.Attributes?["arg1"]?.Value, out int arg1)) {
                        arg1 = 0;
                    }

                    if (!int.TryParse(vNode.Attributes?["arg2"]?.Value, out int arg2)) {
                        arg2 = 0;
                    }

                    int moveAmount = 0;
                    if (slotType is BlueMarbleSlotType.Backward or BlueMarbleSlotType.Forward) {
                        moveAmount = arg1;
                    }

                    var blueMarbleSlotItem = new ItemComponent(0, 0, 0, ItemTag.None);
                    if (slotType is BlueMarbleSlotType.Item or BlueMarbleSlotType.Paradise) {
                        // TODO: Get rarity from item xmls
                        blueMarbleSlotItem = new ItemComponent(arg1, -1, arg2, ItemTag.None);
                    }

                    slots.Add(new BlueMarble.Slot(
                        Type: slotType,
                        MoveAmount: moveAmount,
                        Item: blueMarbleSlotItem));
                }
                return new BlueMarble(
                    RequiredItem: requiredItem,
                    Rounds: rounds.ToArray(),
                    Slots: slots.ToArray());
            case GameEventType.StringBoard:
                return new StringBoard(
                    Text: value4,
                    StringId: int.TryParse(value1, out int stringId) ? stringId : 0);
            case GameEventType.StringBoardLink:
                return new StringBoardLink(
                    Link: value1);
            case GameEventType.TrafficOptimizer:
                // values are hardcoded seeing as these are not shown in the table.
                return new TrafficOptimizer(
                    RideSyncInterval: 100,
                    UserSyncInterval: 100,
                    LinearMovementInterval: 100,
                    GuideObjectSyncInterval: 100);
            case GameEventType.LobbyMap:
                return new LobbyMap(
                    MapId: int.TryParse(value1, out int lobbyMapId) ? lobbyMapId : 0);
            case GameEventType.EventFieldPopup:
                return new EventFieldPopup(
                    MapId: int.TryParse(value1, out int fieldPopupMapId) ? fieldPopupMapId : 0);
            case GameEventType.SaleChat:
                return new SaleChat(
                    WorldChatDiscount: int.TryParse(value1, out int worldChatDiscount) ? worldChatDiscount : 0,
                    ChannelChatDiscount: int.TryParse(value2, out int channelChatDiscount) ? channelChatDiscount : 0);
            case GameEventType.AttendGift:
                value1Xml = new XmlDocument();
                value1Xml.LoadXml(value1);

                var rewards = new List<RewardItem>();
                if (value1Xml.FirstChild == null) {
                    return null;
                }
                foreach (XmlNode node in value1Xml.FirstChild.ChildNodes) {
                    if (!int.TryParse(node.Attributes?["itemID"]?.Value, out int itemId)) {
                        itemId = 0;
                    }
                    if (!int.TryParse(node.Attributes?["count"]?.Value, out int itemCount)) {
                        itemCount = 1;
                    }
                    if (!short.TryParse(node.Attributes?["grade"]?.Value, out short grade)) {
                        grade = -1;
                    }
                    rewards.Add(new RewardItem(itemId, grade, itemCount));
                }

                value2Xml = new XmlDocument();
                value2Xml.LoadXml(value2);
                if (value2Xml.FirstChild is not { Name: "ms2" }) {
                    return null;
                }

                XmlNode? stringNode = value2Xml.FirstChild.SelectSingleNode("string");
                XmlNode? configNode = value2Xml.FirstChild.SelectSingleNode("Config");
                if (stringNode == null || configNode == null) {
                    return null;
                }

                string name = stringNode.Attributes?["name"]?.Value ?? string.Empty;
                string mailTitle = stringNode.Attributes?["mailTitle"]?.Value ?? string.Empty;
                string mailContent = stringNode.Attributes?["mailContents"]?.Value ?? string.Empty;
                string link = stringNode.Attributes?["detailUrl"]?.Value ?? string.Empty;

                if (!int.TryParse(configNode.Attributes?["requirePlaySeconds"]?.Value, out int requiredPlaySeconds)) {
                    requiredPlaySeconds = 0;
                }

                AttendGift.Require? giftRequirement = null;
                if (!string.IsNullOrEmpty(value3)) {
                    value3Xml.LoadXml(value3);
                    if (value3Xml.FirstChild is { Name: "ms2" }) {
                        XmlNode? requirementNode = value3Xml.FirstChild.SelectSingleNode("require");
                        if (requirementNode != null) {
                            if (!Enum.TryParse(requirementNode.Attributes?["type"]?.Value, true, out AttendGiftRequirement requirement)) {
                                requirement = AttendGiftRequirement.None;
                            }

                            if (!int.TryParse(requirementNode.Attributes?["value1"]?.Value, out int requirementValue1)) {
                                requirementValue1 = 0;
                            }

                            if (!int.TryParse(requirementNode.Attributes?["value2"]?.Value, out int requirementValue2)) {
                                requirementValue2 = 0;
                            }

                            giftRequirement = new AttendGift.Require(
                                Type: requirement,
                                Value1: requirementValue1,
                                Value2: requirementValue2);
                        }
                    }
                }

                return new AttendGift(
                    Items: rewards.ToArray(),
                    Name: name,
                    MailTitle: mailTitle,
                    MailContent: mailContent,
                    Link: link,
                    RequiredPlaySeconds: requiredPlaySeconds,
                    Requirement: giftRequirement);
            case GameEventType.ReturnUser:
                var requiredTime = DateTimeOffset.MinValue;
                int daysInactive = 0;
                if (DateTime.TryParseExact(value1, "yyyy-MM-dd-HH-mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime returnUserTime)) {
                    requiredTime = new DateTimeOffset(returnUserTime);
                } else if (int.TryParse(value1, out daysInactive)) {
                }

                return new ReturnUser(
                    SeasonId: int.TryParse(value3, out int season) ? season : 0,
                    DateInactiveSince: requiredTime,
                    DaysInactive: daysInactive,
                    QuestIds: string.IsNullOrEmpty(value4) ? [] : value4.Split(',').Select(int.Parse).ToArray(),
                    RequiredLevel: int.TryParse(value1, out int levelRequirement) ? levelRequirement : 0,
                    RequiredUserValue: int.TryParse(value2, out int userValue) ? userValue : 0);
            case GameEventType.NewUser:
                var requiredNewUserTime = DateTimeOffset.MinValue;
                if (DateTime.TryParseExact(value1, "yyyy-MM-dd-HH-mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime newUserTime)) {
                    requiredNewUserTime = new DateTimeOffset(newUserTime);
                }

                return new NewUser(
                    SeasonId: int.TryParse(value2, out int newUserSeason) ? newUserSeason : 0,
                    DateCreatedBy: requiredNewUserTime);
            case GameEventType.ReturnUserCandidate:
                var unknownTime = DateTimeOffset.MinValue;
                if (DateTime.TryParseExact(value4, "yyyy-MM-dd-HH-mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime requiredDateTime)) {
                    unknownTime = new DateTimeOffset(requiredDateTime);
                }
                return new ReturnUserCandidate(
                    Season: int.TryParse(value3, out int returnUserCandidateSeason) ? returnUserCandidateSeason : 0,
                    SeasonId: int.TryParse(value2, out int returnUserCandidateSeasonId) ? returnUserCandidateSeasonId : 0,
                    MinLevel: int.TryParse(value1, out int returnUserCandidateMinLevel) ? returnUserCandidateMinLevel : 0,
                    UnknownDate: unknownTime);
            case GameEventType.ActiveUser:
                int[] value1Values = value1.Split(',').Select(int.Parse).ToArray();

                value2Xml.LoadXml(value2);
                int meret = 0;
                if (value2Xml.FirstChild is { Name: "money" }) {
                    foreach (XmlNode childNode in value2Xml.FirstChild.ChildNodes) {
                        if (childNode.Name == "v") {
                            string valueType = childNode.Attributes?["type"]?.Value ?? string.Empty;
                            if (valueType == "merat_e") {
                                string? amountStr = childNode.Attributes?["amount"]?.Value;
                                if (!int.TryParse(amountStr, out int meratEAmount)) {
                                    Console.WriteLine($"Failed to parse merat_e amount: {amountStr} for event type {type}");
                                } else {
                                    meret = meratEAmount;
                                }
                            }
                        }
                    }
                }
                return new ActiveUser(
                    MailId: value1Values[0],
                    MailDaysExpire: value1Values[1],
                    Meret: meret,
                    MinLevel: int.TryParse(value3, out int minLevel) ? minLevel : 0);
            case GameEventType.RPS:
                value1Xml = new XmlDocument();
                value1Xml.LoadXml(value1);
                if (value1Xml.FirstChild is not { Name: "ms2" }) {
                    return null;
                }

                XmlNode? rpseventNode = value1Xml.FirstChild.SelectSingleNode("rps_event");
                if (rpseventNode == null) {
                    return null;
                }

                int ticketId = 0;
                var rpsRewards = new List<Rps.RewardData>();
                foreach (XmlNode childNode in rpseventNode) {
                    if (childNode.Name == "gameTicket" && int.TryParse(childNode.Attributes?["itemID"]?.Value, out int itemId)) {
                        ticketId = itemId;
                    }

                    if (childNode.Name == "rewardData") {
                        if (!int.TryParse(childNode.Attributes?["condPlayCount"]?.Value, out int playCount)) {
                            playCount = 1;
                        }

                        // items
                        var rpsItems = new List<RewardItem>();
                        foreach (XmlNode itemNode in childNode.ChildNodes) {
                            foreach (XmlNode valueNode in itemNode.ChildNodes) {
                                if (!int.TryParse(valueNode.Attributes?["itemID"]?.Value, out int rpsRewardItemId)) {
                                    rpsRewardItemId = 0;
                                }

                                if (!short.TryParse(valueNode.Attributes?["grade"]?.Value, out short rpsRewardGrade)) {
                                    rpsRewardGrade = 1;
                                }

                                if (!int.TryParse(valueNode.Attributes?["count"]?.Value, out int rpsRewardCount)) {
                                    rpsRewardCount = 1;
                                }

                                rpsItems.Add(new RewardItem(rpsRewardItemId, rpsRewardGrade, rpsRewardCount));
                            }
                        }

                        rpsRewards.Add(new Rps.RewardData(
                            PlayCount: playCount,
                            Rewards: rpsItems.ToArray()));
                    }
                }

                return new Rps(
                    GameTicketId: ticketId,
                    Rewards: rpsRewards.ToArray(),
                    ActionsHtml: value2);
            case GameEventType.LoginNotice:
                return new LoginNotice();
            case GameEventType.FieldEffect:
                return new FieldEffect(
                    MapIds: value1.Split(',').Select(int.Parse).ToArray(),
                    Effect: value2);
            case GameEventType.DTReward:
                string[] itemStrings = value1.Split(';');

                List<DTReward.Entry> items = [];
                foreach (string itemString in itemStrings) {
                    int[] itemData = itemString.Split(',').Select(int.Parse).ToArray();
                    items.Add(new DTReward.Entry(
                        StartDuration: itemData[0],
                        EndDuration: itemData[1],
                        MailContentId: itemData[2],
                        Item: new RewardItem(
                            itemId: itemData[3],
                            amount: itemData[4],
                            rarity: (short) itemData[5])));
                }

                return new DTReward(
                    Entries: items.ToArray());
            case GameEventType.ConstructShowItem:
                return new ConstructShowItem(
                    CategoryId: int.TryParse(value1, out int categoryId) ? categoryId : 0,
                    CategoryName: value2,
                    ItemIds: value4.Split(',').Select(int.Parse).ToArray());
            case GameEventType.MassiveConstructionEvent:
                return new MassiveConstructionEvent(
                    MapIds: value1.Split(',').Select(int.Parse).ToArray());
            case GameEventType.UGCMapContractSale:
                return new UGCMapContractSale(
                    DiscountAmount: int.TryParse(value1, out int contractSaleAmount) ? contractSaleAmount : 0);
            case GameEventType.UGCMapExtensionSale:
                return new UGCMapExtensionSale(
                    DiscountAmount: int.TryParse(value1, out int extensionSaleAmount) ? extensionSaleAmount : 0);
            case GameEventType.Gallery:
                value1Xml.LoadXml(value1);
                if (value1Xml.FirstChild is not { Name: "cards" }) {
                    return null;
                }

                var questIds = new List<int>();
                foreach (XmlNode valueNode in value1Xml.FirstChild) {
                    if (!int.TryParse(valueNode.Attributes?["quest"]?.Value, out int questId)) {
                        continue;
                    }
                    questIds.Add(questId);
                }

                value2Xml.LoadXml(value2);
                if (value2Xml.FirstChild is not { Name: "items" }) {
                    return null;
                }

                var galleryRewards = new List<RewardItem>();
                foreach (XmlNode itemNode in value2Xml.FirstChild) {
                    if (!int.TryParse(itemNode.Attributes?["itemID"]?.Value, out int itemId)) {
                        continue;
                    }

                    if (!short.TryParse(itemNode.Attributes?["grade"]?.Value, out short grade)) {
                        grade = 1;
                    }

                    if (!int.TryParse(itemNode.Attributes?["count"]?.Value, out int count)) {
                        count = 1;
                    }

                    galleryRewards.Add(new RewardItem(itemId, grade, count));
                }
                return new Gallery(
                    QuestIds: questIds.ToArray(),
                    RewardItems: galleryRewards.ToArray(),
                    RevealDayLimit: int.TryParse(value3, out int revealDayLimit) ? revealDayLimit : 1,
                    Image: value4);
            case GameEventType.BingoEvent:
                value1Xml.LoadXml(value1);
                if (value1Xml.FirstChild is not { Name: "ms2" }) {
                    return null;
                }

                var numbers = new List<int[]>();
                foreach (XmlNode childNode in value1Xml.FirstChild.ChildNodes) {
                    if (childNode.Name == "number") {
                        int[] dayNumbers = childNode.Attributes?["value"]?.Value.Split(',').Select(int.Parse).ToArray() ?? [];
                        numbers.Add(dayNumbers);
                    }
                }


                value2Xml.LoadXml(value2);
                if (value2Xml.FirstChild is not { Name: "ms2" }) {
                    return null;
                }


                var bingoRewards = new List<BingoEvent.BingoReward>();
                foreach (XmlNode childNode in value2Xml.FirstChild.ChildNodes) {
                    if (childNode.Name == "reward") {
                        var bingoItems = new List<RewardItem>();
                        foreach (XmlNode itemNode in childNode.ChildNodes) {
                            if (!int.TryParse(itemNode.Attributes?["itemID"]?.Value, out int itemId)) {
                                continue;
                            }

                            if (!short.TryParse(itemNode.Attributes?["grade"]?.Value, out short grade)) {
                                grade = 1;
                            }

                            if (!int.TryParse(itemNode.Attributes?["count"]?.Value, out int count)) {
                                count = 1;
                            }

                            bingoItems.Add(new RewardItem(itemId, grade, count));
                        }
                        bingoRewards.Add(new BingoEvent.BingoReward(
                            Items: bingoItems.ToArray()));
                    }
                }

                int pencilItemId = int.TryParse(value3, out int pencilId) ? pencilId : 0;
                int pencilPlusItemId = int.TryParse(value4, out int plusPencilId) ? plusPencilId : 0;
                return new BingoEvent(
                    Numbers: numbers.ToArray(),
                    Rewards: bingoRewards.ToArray(),
                    PencilItemId: pencilItemId,
                    PencilPlusItemId: pencilPlusItemId);
            case GameEventType.TimeRunEvent:
                value1Xml.LoadXml(value1);
                if (value1Xml.FirstChild is not { Name: "ms" }) {
                    return null;
                }

                var quests = new List<TimeRunEvent.Quest>();
                foreach (XmlNode childNode in value1Xml.FirstChild.ChildNodes) {
                    if (childNode.Name == "quest") {
                        if (!int.TryParse(childNode.Attributes?["questID"]?.Value, out int questId)) {
                            continue;
                        }
                        if (!int.TryParse(childNode.Attributes?["distance"]?.Value, out int distance)) {
                            continue;
                        }
                        if (!int.TryParse(childNode.Attributes?["openingDay"]?.Value, out int openingDay)) {
                            continue;
                        }
                        quests.Add(new TimeRunEvent.Quest(
                            Id: questId,
                            Distance: distance,
                            OpeningDay: openingDay));
                    }
                }

                value2Xml.LoadXml(value2);
                if (value2Xml.FirstChild is not { Name: "ms" }) {
                    return null;
                }
                XmlNode? rewardNode = value2Xml.FirstChild.FirstChild;
                if (rewardNode == null) {
                    return null;
                }

                if (!int.TryParse(rewardNode.Attributes?["itemID"]?.Value, out int timerunEventItemId)) {
                    return null;
                }
                if (!int.TryParse(rewardNode.Attributes?["count"]?.Value, out int timerunEventItemCount)) {
                    return null;
                }
                if (!short.TryParse(rewardNode.Attributes?["grade"]?.Value, out short timerunEventItemGrade)) {
                    return null;
                }

                if (!int.TryParse(value3, out int startTimeRunEventItemId)) {
                    return null;
                }
                return new TimeRunEvent(
                    StartItemId: startTimeRunEventItemId,
                    Quests: quests.ToArray(),
                    StepRewards: new Dictionary<int, RewardItem>(), // No step rewards were added in the metadata.
                    FinalReward: new RewardItem(
                        itemId: timerunEventItemId,
                        amount: timerunEventItemCount,
                        rarity: timerunEventItemGrade));
            case GameEventType.MapleSurvivalOpenPeriod:
                return new MapleSurvivalOpenPeriod();
            case GameEventType.ShutdownMapleSurvival:
                return new ShutdownMapleSurvival();
            case GameEventType.SaleAutoPlayInstrument:
                if (!int.TryParse(value1, out int performanceDiscount) && string.IsNullOrEmpty(value2)) {
                    return null;
                }
                return new SaleAutoPlayInstrument(
                    Discount: performanceDiscount,
                    ContentType: value2);
            case GameEventType.SaleAutoFishing:
                if (!int.TryParse(value1, out int fishingDiscount) && string.IsNullOrEmpty(value2)) {
                    return null;
                }
                return new SaleAutoFishing(
                    Discount: fishingDiscount,
                    ContentType: value2);
            default:
                return null;
        }
    }

    private OxQuizTable ParseOxQuizTable() {
        var results = new Dictionary<int, OxQuizTable.Entry>();
        foreach ((int id, OxQuiz quiz) in parser.ParseOxQuiz()) {
            results.Add(id, new OxQuizTable.Entry(
                Id: quiz.quizID,
                CategoryId: quiz.categoryID,
                Category: quiz.categoryStr,
                Question: quiz.quizStr,
                Level: quiz.level,
                IsTrue: quiz.answer,
                Answer: quiz.answerStr));
        }
        return new OxQuizTable(results);
    }

    private ItemMergeTable ParseItemMergeOptionTable() {
        var results = new Dictionary<int, Dictionary<int, ItemMergeTable.Entry>>();
        foreach ((int id, MergeOption mergeOption) in parser.ParseItemMergeOption()) {
            var slots = new Dictionary<int, ItemMergeTable.Entry>();
            foreach (MergeOption.Slot slotEntry in mergeOption.slot) {
                var ingredients = new List<ItemComponent>();

                ItemComponent? ingredient1 = ParseItemMaterial(slotEntry.itemMaterial1);
                if (ingredient1 != null) {
                    ingredients.Add(ingredient1);
                }
                ItemComponent? ingredient2 = ParseItemMaterial(slotEntry.itemMaterial2);
                if (ingredient2 != null) {
                    ingredients.Add(ingredient2);
                }

                var basicOptions = new Dictionary<BasicAttribute, ItemMergeTable.Option>();
                var specialOptions = new Dictionary<SpecialAttribute, ItemMergeTable.Option>();

                foreach (MergeOption.Option mergeOptionEntry in slotEntry.option) {
                    if (mergeOptionEntry.optionName is "str" or "dex" or "int" or "luk" or "hp" or "hp_rgp" or "hp_inv" or "sp" or "sp_rgp" or "sp_inv" or "ep" or "ep_rgp" or "ep_inv" or "asp" or "msp" or "atp" or "evp" or
                        "cap" or "cad" or "car" or "ndd" or "abp" or "jmp" or "pap" or "map" or "par" or "mar" or "wapmin" or "wapmax" or "dmg" or "pen" or "rmsp" or "bap" or "bap_pet") {
                        var basicAttribute = mergeOptionEntry.optionName.ToBasicAttribute();
                        List<ItemMergeTable.Range<int>> values = [];
                        List<ItemMergeTable.Range<int>> rates = [];
                        List<int> weights = [];
                        int min = mergeOptionEntry.min;
                        if (basicAttribute is BasicAttribute.Piercing or BasicAttribute.PerfectGuard or
                            BasicAttribute.JumpHeight) {
                            // Looping by 10 because that's the max amount of values in the xml
                            for (int i = 0; i < 10; i++) {
                                (int value, int weight) = mergeOptionEntry[i];
                                if (value == 0) {
                                    continue;
                                }
                                rates.Add(new ItemMergeTable.Range<int>(min + 1, value));
                                values.Add(new ItemMergeTable.Range<int>(0, 0));
                                weights.Add(weight);
                                min = value;
                            }
                        } else {
                            for (int i = 0; i < 10; i++) {
                                (int value, int weight) = mergeOptionEntry[i];
                                if (value == 0) {
                                    continue;
                                }
                                values.Add(new ItemMergeTable.Range<int>(min + 1, value));
                                rates.Add(new ItemMergeTable.Range<int>(0, 0));
                                weights.Add(weight);
                                min = value;
                            }
                        }

                        basicOptions[basicAttribute] = new ItemMergeTable.Option(
                            Values: values.ToArray(),
                            Rates: rates.ToArray(),
                            Weights: weights.ToArray());
                    } else {
                        var specialAttribute = mergeOptionEntry.optionName.ToSpecialAttribute();
                        List<ItemMergeTable.Range<int>> values = [];
                        List<ItemMergeTable.Range<int>> rates = [];
                        List<int> weights = [];
                        int min = mergeOptionEntry.min;
                        if (specialAttribute is SpecialAttribute.HpOnKill or SpecialAttribute.ReduceCooldown or SpecialAttribute.ReduceKnockBack or SpecialAttribute.MassiveOxSpeed or SpecialAttribute.MassiveTrapMasterSpeed or
                            SpecialAttribute.MassiveFinalSurvivalSpeed or SpecialAttribute.MassiveCrazyRunnerSpeed or SpecialAttribute.MassiveShCrazyRunnerSpeed or SpecialAttribute.MassiveEscapeSpeed or SpecialAttribute.MassiveSpringBeachSpeed or
                            SpecialAttribute.MassiveDanceDanceSpeed or SpecialAttribute.DarkStreamEvp or SpecialAttribute.CompleteFieldMissionSpeed or SpecialAttribute.AdditionalEffect95000018 or SpecialAttribute.AdditionalEffect95000012 or
                            SpecialAttribute.AdditionalEffect95000014 or SpecialAttribute.AdditionalEffect95000020 or SpecialAttribute.AdditionalEffect95000021 or SpecialAttribute.AdditionalEffect95000022 or SpecialAttribute.AdditionalEffect95000023
                            or SpecialAttribute.AdditionalEffect95000024 or SpecialAttribute.AdditionalEffect95000025 or SpecialAttribute.AdditionalEffect95000026 or SpecialAttribute.AdditionalEffect95000027 or SpecialAttribute.AdditionalEffect95000028 or
                            SpecialAttribute.AdditionalEffect95000029 or SpecialAttribute.DashDistance or SpecialAttribute.SpiritOnKill or SpecialAttribute.StaminaOnKill or SpecialAttribute.PvpDamage or SpecialAttribute.ReducePvpDamage or SpecialAttribute.SkillLevelUpTier1
                            or SpecialAttribute.SkillLevelUpTier2 or SpecialAttribute.SkillLevelUpTier3 or SpecialAttribute.SkillLevelUpTier4 or SpecialAttribute.SkillLevelUpTier5 or SpecialAttribute.SkillLevelUpTier6 or SpecialAttribute.SkillLevelUpTier7 or SpecialAttribute.SkillLevelUpTier8
                            or SpecialAttribute.SkillLevelUpTier9 or SpecialAttribute.SkillLevelUpTier10 or SpecialAttribute.SkillLevelUpTier11 or SpecialAttribute.SkillLevelUpTier12 or SpecialAttribute.SkillLevelUpTier13 or SpecialAttribute.SkillLevelUpTier14 or SpecialAttribute.ChaosRaidAttackSpeed
                            or SpecialAttribute.ChaosRaidAccuracy or SpecialAttribute.ChaosRaidHp or SpecialAttribute.PetTrapReward) {
                            for (int i = 0; i < 10; i++) {
                                (int value, int weight) = mergeOptionEntry[i];
                                if (value == 0) {
                                    continue;
                                }
                                values.Add(new ItemMergeTable.Range<int>(min + 1, value));
                                rates.Add(new ItemMergeTable.Range<int>(0, 0));
                                weights.Add(weight);
                                min = value;
                            }
                        } else {
                            for (int i = 0; i < 10; i++) {
                                (int value, int weight) = mergeOptionEntry[i];
                                if (value == 0) {
                                    continue;
                                }
                                rates.Add(new ItemMergeTable.Range<int>(min + 1, value));
                                values.Add(new ItemMergeTable.Range<int>(0, 0));
                                weights.Add(weight);
                                min = value;
                            }
                        }

                        specialOptions[specialAttribute] = new ItemMergeTable.Option(
                            Values: values.ToArray(),
                            Rates: rates.ToArray(),
                            Weights: weights.ToArray());
                    }
                }
                var slot = new ItemMergeTable.Entry(
                    Slot: slotEntry.part,
                    MesoCost: slotEntry.consumeMeso,
                    Materials: ingredients.ToArray(),
                    BasicOptions: basicOptions,
                    SpecialOptions: specialOptions
                );

                slots.Add(slotEntry.part, slot);
            }
            results.Add(id, slots);
        }
        // Hardcoding values seeing as the missing ids here are utilizing table id 37000055
        for (int i = 37000056; i < 37000064; i++) {
            if (results.TryGetValue(37000055, out Dictionary<int, ItemMergeTable.Entry>? dictionary)) {
                results.Add(i, dictionary);
            }
        }
        return new ItemMergeTable(results);

        ItemComponent? ParseItemMaterial(string[] itemMaterial) {
            var tag = ItemTag.None;
            if (itemMaterial.Length > 0) {
                string[] item = itemMaterial[0].Split(':');
                if (item.Length == 2) {
                    tag = Enum.TryParse(item[1], out ItemTag itemTag) ? itemTag : ItemTag.None;
                }
                int itemId = int.TryParse(item[0], out int id) ? id : 0;
                int rarity = int.TryParse(itemMaterial[1], out int r) ? r : 1;
                int amount = int.TryParse(itemMaterial[2], out int a) ? a : 1;
                if (tag != ItemTag.None || itemId > 0) {
                    return new ItemComponent(itemId, rarity, amount, tag);
                }
            }
            return null;
        }
    }

    private ShopTable ParseShop() {
        var results = new Dictionary<int, ShopMetadata>();
        foreach ((int shopId, ShopGameInfo shopInfo) in parser.ParseShopGameInfo()) {
            var entry = new ShopMetadata(
                Id: shopInfo.shopID,
                CategoryId: shopInfo.categoryID,
                Name: shopInfo.iconName,
                FrameType: (ShopFrameType) shopInfo.uiFrameType,
                DisplayOnlyUsable: shopInfo.showOnlyUsableItem,
                HideStats: shopInfo.hideOptionInfo,
                DisplayProbability: shopInfo.showProbInfo,
                IsOnlySell: shopInfo.isOnlySell,
                OpenWallet: shopInfo.isOpenTokenPocket,
                DisplayNew: false, // this isn't present in the table
                DisableDisplayOrderSort: shopInfo.disableDisplayOrderSort,
                RestockTime: string.IsNullOrEmpty(shopInfo.resetFixedTime) ? 0 : DateTime.ParseExact(shopInfo.resetFixedTime, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture).ToEpochSeconds(),
                EnableReset: shopInfo.resetEnable,
                RestockData: new ShopRestockData(
                    ResetType: (ResetType) shopInfo.resetType,
                    CurrencyType: (ShopCurrencyType) shopInfo.resetPaymentType,
                    ExcessCurrencyType: (ShopCurrencyType) shopInfo.resetPaymentType, // not present in the table, using resetPaymentType for now
                    MinItemCount: shopInfo.resetListMin,
                    MaxItemCount: shopInfo.resetListMax,
                    Price: shopInfo.resetPrice,
                    EnablePriceMultiplier: false, // not present in the table
                    DisableInstantRestock: shopInfo.resetButtonHide,
                    AccountWide: shopInfo.resetByAccount)
            );
            results.Add(shopId, entry);
        }
        return new ShopTable(results);
    }

    private ShopItemTable ParseShopItems() {
        var results = new Dictionary<int, Dictionary<int, ShopItemMetadata>>();
        foreach ((int shopId, ShopGame data) in parser.ParseShopGame()) {
            var shopResults = new Dictionary<int, ShopItemMetadata>();
            foreach (ShopGame.Item item in data.item) {
                // Check if item exists in ItemMetadataById
                if (!ItemMapper.ItemMetadataById.TryGetValue(item.id, out ItemMetadata? itemMeta)) {
                    continue;
                }

                string[] achievementArray = string.IsNullOrEmpty(item.requireAchieve) ? [] : item.requireAchieve.Split(",");
                int achievementId = 0;
                int achievementRank = 0;
                if (achievementArray.Length == 2) {
                    if (!int.TryParse(achievementArray[0], out achievementId)) {
                        achievementId = 0;
                    }
                    if (!int.TryParse(achievementArray[1], out achievementRank)) {
                        achievementRank = 1;
                    }
                }

                byte championshipRank = 0;
                short championShipJoinCount = 0;
                if (item.requireChampionshipInfo.Length == 2) {
                    championshipRank = (byte) item.requireChampionshipInfo[0];
                    championShipJoinCount = (short) item.requireChampionshipInfo[1];
                }

                var npcType = GuildNpcType.Unknown;
                short guildNpcLevel = 0;
                if (item.requireGuildNpc.Length == 2) {
                    npcType = item.requireGuildNpc[0] switch {
                        "goods" => GuildNpcType.Goods,
                        "equip" => GuildNpcType.Equip,
                        "gemstone" => GuildNpcType.Gemstone,
                        "itemMerge" => GuildNpcType.ItemMerge,
                        "music" => GuildNpcType.Music,
                        "quest" => GuildNpcType.Quest,
                        _ => GuildNpcType.Unknown,
                    };
                    if (short.TryParse(item.requireGuildNpc[1], out short level)) {
                        guildNpcLevel = level;
                    }
                }

                RestrictedBuyData? restrictedBuyData = null;
                if (!string.IsNullOrEmpty(item.startDate) && !string.IsNullOrEmpty(item.endDate)) {
                    var buyTimeOfDays = new List<BuyTimeOfDay>();
                    foreach (string partTime in item.partTime) {
                        (TimeSpan startPartTime, TimeSpan endPartTime) = ParsePartTime(partTime);
                        buyTimeOfDays.Add(new BuyTimeOfDay(startPartTime.Seconds, endPartTime.Seconds));
                    }
                    restrictedBuyData = new RestrictedBuyData {
                        Days = item.dayOfWeek.Length == 0 ? [] : Array.ConvertAll(item.dayOfWeek, day => (ShopBuyDay) day),
                        TimeRanges = buyTimeOfDays,
                        StartTime = string.IsNullOrEmpty(item.startDate) ? 0 : DateTime.ParseExact(item.startDate, "yyyy-MM-dd-HH-mm", CultureInfo.InvariantCulture).ToEpochSeconds(),
                        EndTime = string.IsNullOrEmpty(item.endDate) ? 0 : DateTime.ParseExact(item.endDate, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture).ToEpochSeconds(),
                    };
                }

                var entry = new ShopItemMetadata(
                    Id: item.sn,
                    ShopId: shopId,
                    ItemId: item.id,
                    Rarity: (byte) item.grade,
                    Cost: new ShopCost {
                        Amount = (int) item.price,
                        ItemId = item.paymentItemID,
                        SaleAmount = 0, // ?
                        Type = (ShopCurrencyType) item.paymentType,
                    },
                    SellCount: item.sellCount,
                    Category: item.category,
                    Requirements: new ShopItemMetadata.Requirement(
                        GuildTrophy: item.requireGuildTrophy,
                        Achievement: new ShopItemMetadata.Achievement(
                            Id: achievementId,
                            Rank: achievementRank),
                        Championship: new ShopItemMetadata.Championship(
                            Rank: championshipRank,
                            JoinCount: championShipJoinCount),
                        GuildNpc: new ShopItemMetadata.GuildNpc(
                            Type: npcType,
                            Level: guildNpcLevel),
                        QuestAlliance: new ShopItemMetadata.QuestAlliance(
                            Type: item.requireAlliance switch {
                                "MapleUnion" => ReputationType.MapleAlliance,
                                "TriaRoyalGuard" => ReputationType.RoyalGuard,
                                "DarkWind" => ReputationType.DarkWind,
                                "GreenHood" => ReputationType.GreenHood,
                                "LumiKnight" => ReputationType.Lumiknight,
                                "MapleUnion_KritiasExped" => ReputationType.KritiasMapleAlliance,
                                "GreenHood_KritiasExped" => ReputationType.KritiasGreenHood,
                                "Lumiknight_KritiasExped" => ReputationType.KritiasLumiknight,
                                "Georg" => ReputationType.Humanitas,
                                _ => ReputationType.None,
                            },
                            Grade: item.requireAllianceGrade)),
                    RestrictedBuyData: restrictedBuyData,
                    SellUnit: (short) item.sellUnit,
                    Label: (ShopItemLabel) item.frameType,
                    IconTag: item.paymentIconTag,
                    WearForPreview: item.wearForPreview,
                    RandomOption: item.randomOption,
                    Probability: item.prob,
                    IsPremiumItem: item.premiumItem);
                shopResults.Add(item.sn, entry);
            }
            results.Add(shopId, shopResults);
        }
        return new ShopItemTable(results);
    }

    private BeautyShopTable ParseBeautyShops() {
        var results = new Dictionary<int, BeautyShopMetadata>();
        results = MergeBeautyShopData(results, parser.ParseShopBeauty());
        results = MergeBeautyShopData(results, parser.ParseShopBeautyCoupon());
        results = MergeBeautyShopData(results, parser.ParseShopBeautySpecialHair());
        return new BeautyShopTable(results);
    }

    private Dictionary<int, BeautyShopMetadata> MergeBeautyShopData(Dictionary<int, BeautyShopMetadata> entries, IEnumerable<(int, ShopBeauty)> beautyParser) {
        foreach ((int shopId, ShopBeauty shop) in beautyParser) {
            List<BeautyShopItemGroup> itemGroups = [];
            foreach (ShopBeauty.ItemGroup group in shop.itemGroup) {
                itemGroups.Add(new BeautyShopItemGroup(
                    StartTime: string.IsNullOrEmpty(group.saleStartTime) ? 0 : DateTime.ParseExact(group.saleStartTime, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture).ToEpochSeconds(),
                    Items: ParseBeautyShopItems(group.item).ToArray()));
            }

            entries.Add(shopId, new BeautyShopMetadata(
                Id: shop.shopID,
                Category: (BeautyShopCategory) shop.categoryID,
                SubType: shop.shopID switch {
                    // Hardcoding this because I'm not sure where this information is located in the xmls
                    500 => 16,
                    501 => 19,
                    504 => 17,
                    505 => 28,
                    506 => 18,
                    508 => 21,
                    509 => 0,
                    510 => 20,
                    _ => 0,
                },
                StyleCostMetadata: new BeautyShopCostMetadata(
                    CurrencyType: (ShopCurrencyType) shop.stylePaymentType,
                    Price: shop.stylePrice,
                    Icon: shop.stylePaymentIconTag,
                    PaymentItemId: shop.stylePaymentItemID),
                ColorCostMetadata: new BeautyShopCostMetadata(
                    CurrencyType: (ShopCurrencyType) shop.colorPaymentType,
                    Price: shop.colorPrice,
                    Icon: shop.colorPaymentIconTag,
                    PaymentItemId: shop.colorPaymentItemID),
                IsRandom: shop.random,
                IsByItem: shop.byItem,
                ReturnCouponId: shop.returnCouponID,
                CouponId: shop.displayCouponID,
                CouponTag: Enum.TryParse(shop.couponTag, out ItemTag tag) ? tag : ItemTag.None,
                Items: ParseBeautyShopItems(shop.item).ToArray(),
                ItemGroups: itemGroups.ToArray()));
        }
        return entries;

        IEnumerable<BeautyShopItem> ParseBeautyShopItems(IList<ShopBeauty.Item> items) {
            foreach (ShopBeauty.Item item in items) {
                yield return new BeautyShopItem(
                    Id: item.id,
                    Cost: new BeautyShopCostMetadata(
                        CurrencyType: (ShopCurrencyType) item.paymentType,
                        Price: item.price,
                        Icon: item.paymentIconTag,
                        PaymentItemId: item.paymentItemID),
                    Weight: item.weight,
                    AchievementId: item.achieveID,
                    AchievementRank: (byte) item.achieveGrade,
                    RequiredLevel: item.requireLevel,
                    SaleTag: (ShopItemLabel) item.saleTag);
            }
        }
    }

    private MeretMarketTable ParseMeretCustomShop() {
        var results = new Dictionary<int, MeretMarketItemMetadata>();
        foreach ((int id, ShopMeretCustom entry) in parser.ParseShopMeretCustom()) {
            foreach (ShopMeretCustom addQuantity in entry.additionalQuantity) {
                results.Add(addQuantity.id, ParseMarketItemMetadata(addQuantity, entry));
            }
            results.Add(id, ParseMarketItemMetadata(entry));
        }
        return new MeretMarketTable(results);

        MeretMarketItemMetadata ParseMarketItemMetadata(ShopMeretCustom item, ShopMeretCustom? parent = null) {
            string saleStartTime = string.IsNullOrEmpty(parent?.saleStartTime) ? item.saleStartTime : parent.saleStartTime;
            string saleEndTime = string.IsNullOrEmpty(parent?.saleEndTime) ? item.saleEndTime : parent.saleEndTime;
            string promoStartTime = string.IsNullOrEmpty(parent?.promoSaleStartTime) ? item.promoSaleStartTime : parent.promoSaleStartTime;
            string promoEndTime = string.IsNullOrEmpty(parent?.promoSaleEndTime) ? item.promoSaleEndTime : parent.promoSaleEndTime;
            int[] jobRequirement = parent?.jobRequire ?? item.jobRequire;
            return new MeretMarketItemMetadata(
                Id: item.id,
                ParentId: parent?.id ?? 0,
                TabId: parent?.tabID ?? item.tabID,
                Banner: item.banner,
                BannerTag: (MeretMarketBannerTag) item.bannerTag,
                ItemId: parent?.itemID ?? item.itemID,
                Rarity: (byte) (parent?.grade ?? item.grade),
                Quantity: item.quantity,
                BonusQuantity: item.bonusQuantity,
                DurationInDays: item.durationDay,
                SaleTag: (MeretMarketItemSaleTag) item.saleTag,
                CurrencyType: (MeretMarketCurrencyType) (parent?.paymentType ?? item.paymentType),
                Price: item.price,
                SalePrice: item.salePrice == 0 ? item.price : item.salePrice,
                SaleStartTime: string.IsNullOrEmpty(saleStartTime) ? 0 : DateTime.ParseExact(saleStartTime, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture).ToEpochSeconds(),
                SaleEndTime: string.IsNullOrEmpty(saleEndTime) ? 0 : DateTime.ParseExact(saleEndTime, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture).ToEpochSeconds(),
                JobRequirement: jobRequirement.Select(job => (JobCode) job).AsEnumerable().FilterFlags(),
                RestockUnavailable: parent?.noRestock ?? item.noRestock,
                RequireMinLevel: parent?.minLevel ?? item.minLevel,
                RequireMaxLevel: parent?.maxLevel ?? item.maxLevel,
                RequireAchievementId: parent?.achieveID ?? item.achieveID,
                RequireAchievementRank: parent?.achieveGrade ?? item.achieveGrade,
                PcCafe: parent?.pcCafe ?? item.pcCafe,
                Giftable: parent?.giftable ?? item.giftable,
                ShowSaleTime: item.showSaleTime,
                PromoName: item.promoName,
                PromoStartTime: string.IsNullOrEmpty(promoStartTime) ? 0 : DateTime.ParseExact(promoStartTime, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture).ToEpochSeconds(),
                PromoEndTime: string.IsNullOrEmpty(promoEndTime) ? 0 : DateTime.ParseExact(promoEndTime, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture).ToEpochSeconds());
        }
    }

    private FishTable ParseFish() {
        // Parse Fish
        var fishes = new Dictionary<int, FishTable.Fish>();
        foreach ((int id, Fish fish) in parser.ParseFish()) {
            if (!Enum.TryParse(fish.habitat, out LiquidType liquidType)) {
                liquidType = LiquidType.all;
            }

            int[] smallSize = fish.smallSize.Split("-").Select(int.Parse).ToArray();
            int[] bigSize = fish.bigSize.Split("-").Select(int.Parse).ToArray();
            fishes.Add(id, new FishTable.Fish(
                Id: id,
                FluidHabitat: liquidType,
                Mastery: fish.fishMastery,
                Level: fish.lv,
                Rarity: fish.rank,
                PointCount: fish.pointCount,
                MasteryExp: fish.masteryPoint,
                Exp: fish.exp,
                FishingTime: fish.fishingTime,
                CatchProbability: fish.catchProp,
                BaitProbability: fish.baitProp,
                SmallSize: new FishTable.Range<int>(smallSize[0], smallSize[1]),
                BigSize: new FishTable.Range<int>(bigSize[0], bigSize[1]),
                BaitEffectIds: fish.bait,
                IndividualDropBoxId: fish.individualDropBoxID,
                IgnoreSpotMastery: fish.ignoreSpotMastery));
        }

        // Parse Spots
        var spots = new Dictionary<int, FishTable.Spot>();
        foreach ((int mapId, FishingSpot spot) in parser.ParseFishingSpot()) {
            var liquidTypes = new List<LiquidType>();
            foreach (string liquidType in spot.liquidType) {
                if (Enum.TryParse(liquidType, out LiquidType type)) {
                    liquidTypes.Add(type);
                }
            }

            spots.Add(mapId, new FishTable.Spot(
                Id: mapId,
                MinMastery: spot.minMastery,
                MaxMastery: spot.maxMastery,
                LiquidTypes: liquidTypes,
                GlobalFishBoxId: spot.globalFishBoxID,
                IndividualFishBoxId: spot.individualFishBoxID,
                GlobalDropBoxId: spot.globalDropBoxId,
                IndividualDropBoxId: spot.individualDropBoxId,
                SpotLevel: spot.spotLevel,
                DropRank: spot.spotDropRank ? 1 : 0)); // TODO: Change this from a bool to int
        }

        // Parse Lure
        var lures = new Dictionary<int, FishTable.Lure>();
        foreach ((int id, FishLure lure) in parser.ParseFishLure()) {
            lures.Add(id, new FishTable.Lure(
                BuffId: lure.fishCode,
                BuffLevel: (short) lure.additionalEffectLevel,
                Catches: lure.catchRank.Select((t, i) => new FishTable.Lure.Catch(Rank: t, Probability: lure.catchProp[i])).ToArray(),
                Spawns: lure.spawnRank.Select((t, i) => new FishTable.Lure.Spawn(FishId: t, Rate: lure.spawnProp[i])).ToArray(),
                GlobalDropBoxId: lure.globalDropBoxID,
                GlobalDropRank: lure.globalDropRank,
                IndividualDropBoxId: lure.individualDropBoxID,
                IndividualDropRank: lure.individualDropRank));
        }

        // Global Fish Boxes
        Dictionary<int, FishTable.FishBox> globalBoxes = ParseFishBox(parser.ParseGlobalFishBox());
        Dictionary<int, FishTable.FishBox> individualBoxes = ParseFishBox(parser.ParseIndividualFishBox());

        return new FishTable(fishes, spots, lures, globalBoxes, individualBoxes);

        Dictionary<int, FishTable.FishBox> ParseFishBox(IEnumerable<(int, FishBox)> boxes) {
            var results = new Dictionary<int, FishTable.FishBox>();
            foreach ((int id, FishBox box) in boxes) {
                Dictionary<int, int> fishes = [];
                foreach (FishBox.Fish data in box.fish) {
                    fishes[data.fishCode] = data.weight;
                }

                results.Add(id, new FishTable.FishBox(
                    Id: id,
                    Probability: box.probability,
                    CubeRate: box.cubeRate,
                    Fishes: fishes));
            }
            return results;
        }
    }

    private CombineSpawnTable ParseCombineSpawn() {
        var groupDict = new Dictionary<int, Dictionary<int, SpawnGroupMetadata>>();
        var npcDict = new Dictionary<int, Dictionary<int, SpawnNpcMetadata>>();
        var objectDict = new Dictionary<int, Dictionary<int, SpawnInteractObjectMetadata>>();

        foreach ((int id, SpawnGroup spawnGroup) in parser.ParseSpawnGroup()) {
            var groupMetadata = new SpawnGroupMetadata(
                GroupId: spawnGroup.groupId,
                Type: Enum.TryParse(spawnGroup.groupType, out CombineSpawnGroupType type) ? type : CombineSpawnGroupType.none,
                TotalCount: spawnGroup.combineCount,
                ResetTick: spawnGroup.resetTick,
                MapId: spawnGroup.fieldId);

            if (!groupDict.TryGetValue(spawnGroup.fieldId, out Dictionary<int, SpawnGroupMetadata>? value)) {
                value = new Dictionary<int, SpawnGroupMetadata>();
                groupDict[spawnGroup.fieldId] = value;
            }

            value[spawnGroup.groupId] = groupMetadata;
        }

        foreach ((int id, SpawnNpc spawn) in parser.ParseSpawnNpc()) {
            var spawnMetadata = new SpawnNpcMetadata(
                CombineId: spawn.combineId,
                GroupId: spawn.groupId,
                Weight: spawn.weight,
                SpawnId: spawn.spawnId);

            if (!npcDict.TryGetValue(spawn.groupId, out Dictionary<int, SpawnNpcMetadata>? value)) {
                value = new Dictionary<int, SpawnNpcMetadata>();
                npcDict[spawn.groupId] = value;
            }

            value[spawn.combineId] = spawnMetadata;
        }

        foreach ((int id, SpawnInteractObject interactObject) in parser.ParseSpawnInteractObject()) {
            var objectMetadata = new SpawnInteractObjectMetadata(
                CombineId: interactObject.combineId,
                GroupId: interactObject.groupId,
                Weight: interactObject.weight,
                RegionSpawnId: interactObject.regionSpawnId,
                InteractId: interactObject.interactId,
                Model: interactObject.model,
                Asset: interactObject.asset,
                Normal: interactObject.normal,
                Reactable: interactObject.reactable,
                Scale: interactObject.scale,
                KeepAnimate: interactObject.isKeepAnimate);

            if (!objectDict.TryGetValue(interactObject.groupId, out Dictionary<int, SpawnInteractObjectMetadata>? value)) {
                value = new Dictionary<int, SpawnInteractObjectMetadata>();
                objectDict[interactObject.groupId] = value;
            }

            value[interactObject.combineId] = objectMetadata;
        }

        return new CombineSpawnTable(groupDict, npcDict, objectDict);
    }

    private EnchantOptionTable ParseEnchantOption() {
        var results = new Dictionary<int, EnchantOptionMetadata>();
        foreach ((int id, EnchantOption enchantOption) in parser.ParseEnchantOption()) {
            IList<BasicAttribute> basicAttributes = [];
            foreach (int option in enchantOption.option) {
                if (!Enum.TryParse(option.ToString(), out BasicAttribute basicAttribute)) {
                    Console.WriteLine($"Failed to parse basic attribute {option}");
                    continue;
                }
                basicAttributes.Add(basicAttribute);
            }
            results.Add(id, new EnchantOptionMetadata(
                Id: id,
                Slot: enchantOption.slot,
                EnchantLevel: enchantOption.grade,
                Rarity: (short) enchantOption.rank,
                Rate: enchantOption.rate,
                MinLevel: enchantOption.minLv,
                MaxLevel: enchantOption.maxLv,
                Attributes: basicAttributes.ToArray()));
        }
        return new EnchantOptionTable(results);
    }

    private ScriptEventConditionTable ParseScriptEventConditionTable() {
        var results = new Dictionary<ScriptEventType, Dictionary<int, ScriptEventConditionMetadata>>();

        foreach ((int eventId, ScriptEventCondition scriptEventCondition) in parser.ParseScriptEventCondition()) {
            ScriptEventType type = scriptEventCondition.type switch {
                Parser.Enum.ScriptEventType.enchant_fail => ScriptEventType.EnchantFail,
                Parser.Enum.ScriptEventType.enchant_item_select => ScriptEventType.EnchantSelect,
                Parser.Enum.ScriptEventType.enchant_complete => ScriptEventType.EnchantComplete,
                Parser.Enum.ScriptEventType.merge_select => ScriptEventType.EmpowerSelect,
                Parser.Enum.ScriptEventType.merge_try => ScriptEventType.EmpowerTry,
                Parser.Enum.ScriptEventType.merge_result => ScriptEventType.EmpowerResult,
                Parser.Enum.ScriptEventType.remake_fail => ScriptEventType.RerollFail,
                Parser.Enum.ScriptEventType.remake_item_select => ScriptEventType.RerollItemSelect,
                Parser.Enum.ScriptEventType.remake_option_select => ScriptEventType.RerollOptionSelect,
                Parser.Enum.ScriptEventType.remake_complete => ScriptEventType.RerollComplete,
                _ => ScriptEventType.EnchantFail,
            };

            List<int> enchantLevels = [];
            if (scriptEventCondition.enchantLevel.Contains('-')) {
                string[] enchantLevelSplit = scriptEventCondition.enchantLevel.Split('-');
                int startInt = int.TryParse(enchantLevelSplit[0], out int start) ? start : 0;
                int endInt = int.TryParse(enchantLevelSplit[1], out int end) ? end : 0;

                for (int i = startInt; i <= endInt; i++) {
                    enchantLevels.Add(i);
                }
            } else {
                if (!int.TryParse(scriptEventCondition.enchantLevel, out int enchantLevel)) {
                    if (scriptEventCondition.enchantLevel == "MAX") {
                        enchantLevels.Add(15);
                    }
                } else {
                    enchantLevels.Add(enchantLevel);
                }
            }

            if (!results.TryGetValue(type, out Dictionary<int, ScriptEventConditionMetadata>? value)) {
                value = new Dictionary<int, ScriptEventConditionMetadata>();
                results[type] = value;
            }

            value[scriptEventCondition.id] = new ScriptEventConditionMetadata(
                Id: scriptEventCondition.id,
                EventType: type,
                ErrorCode: (ItemEnchantError) scriptEventCondition.enchantError,
                Rarity: (short) scriptEventCondition.rank,
                EnchantLevel: enchantLevels.ToArray(),
                FailCount: scriptEventCondition.failCount,
                DamageType: (EnchantDamageType) scriptEventCondition.isDamaged,
                ResultType: (EnchantResult) scriptEventCondition.result
            );
        }
        return new ScriptEventConditionTable(results);
    }

    private UnlimitedEnchantOptionTable ParseUnlimitedEnchantOption() {
        var results = new Dictionary<int, Dictionary<int, UnlimitedEnchantOptionTable.Option>>();
        foreach ((int slot, IDictionary<int[], UnlimitedEnchantOption> enchantOptions) in parser.ParseUnlimitedEnchantOption()) {
            var levelDictionary = new Dictionary<int, UnlimitedEnchantOptionTable.Option>();
            foreach ((int[] optionLevel, UnlimitedEnchantOption enchantOption) in enchantOptions) {
                int minLevel = optionLevel[0];
                int maxLevel = optionLevel.Length > 1 ? optionLevel[1] : minLevel;

                for (int level = minLevel; level <= maxLevel; level++) {
                    Dictionary<BasicAttribute, int> values = [];
                    Dictionary<BasicAttribute, float> rates = [];
                    Dictionary<SpecialAttribute, int> specialValues = [];
                    Dictionary<SpecialAttribute, float> specialRates = [];
                    AddBasic(values, rates, (BasicAttribute) enchantOption.option1, enchantOption.value1, enchantOption.rate1);
                    AddBasic(values, rates, (BasicAttribute) enchantOption.option2, enchantOption.value2, enchantOption.rate2);
                    AddBasic(values, rates, (BasicAttribute) enchantOption.option3, enchantOption.value3, enchantOption.rate3);
                    AddBasic(values, rates, (BasicAttribute) enchantOption.option4, enchantOption.value4, enchantOption.rate4);
                    AddSpecial(specialValues, specialRates, (SpecialAttribute) enchantOption.sa_option1, enchantOption.sa_value1, enchantOption.sa_rate1);
                    AddSpecial(specialValues, specialRates, (SpecialAttribute) enchantOption.sa_option2, enchantOption.sa_value2, enchantOption.sa_rate2);

                    levelDictionary[level] = new UnlimitedEnchantOptionTable.Option(
                        Values: values,
                        Rates: rates,
                        SpecialValues: specialValues,
                        SpecialRates: specialRates);

                }
            }
            results[slot] = levelDictionary;
        }
        return new UnlimitedEnchantOptionTable(results);

        void AddBasic(Dictionary<BasicAttribute, int> values, Dictionary<BasicAttribute, float> rates, BasicAttribute attribute, int value, float rate) {
            if (value != 0) {
                values.Add(attribute, value);
            }
            if (rate != 0) {
                rates.Add(attribute, rate);
            }
        }

        void AddSpecial(Dictionary<SpecialAttribute, int> values, Dictionary<SpecialAttribute, float> rates, SpecialAttribute attribute, int value, float rate) {
            if (attribute == SpecialAttribute.None) {
                return;
            }
            if (value != 0) {
                values.Add(attribute, value);
            }
            if (rate != 0) {
                rates.Add(attribute, rate);
            }
        }
    }
}
