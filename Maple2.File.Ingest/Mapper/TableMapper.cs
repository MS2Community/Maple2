using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Maple2.Database.Extensions;
using Maple2.File.Ingest.Utils;
using Maple2.File.IO;
using Maple2.File.Parser;
using Maple2.File.Parser.Enum;
using Maple2.File.Parser.Xml;
using Maple2.File.Parser.Xml.Table;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Tools.Extensions;
using DayOfWeek = System.DayOfWeek;
using ChatSticker = Maple2.File.Parser.Xml.Table.ChatSticker;
using DungeonCooldownType = Maple2.Model.Enum.DungeonCooldownType;
using DungeonGroupType = Maple2.Model.Enum.DungeonGroupType;
using DungeonPlayType = Maple2.Model.Enum.DungeonPlayType;
using ExpType = Maple2.Model.Enum.ExpType;
using GuildBuff = Maple2.File.Parser.Xml.Table.GuildBuff;
using GuildNpc = Maple2.File.Parser.Xml.Table.GuildNpc;
using GuildNpcType = Maple2.Model.Enum.GuildNpcType;
using InteractObject = Maple2.File.Parser.Xml.Table.InteractObject;
using ItemOptionConstant = Maple2.Model.Metadata.ItemOptionConstant;
using ItemSocket = Maple2.File.Parser.Xml.Table.ItemSocket;
using JobTable = Maple2.Model.Metadata.JobTable;
using MagicPath = Maple2.Model.Metadata.MagicPath;
using MasteryType = Maple2.Model.Enum.MasteryType;
using MeretMarketCategory = Maple2.File.Parser.Xml.Table.MeretMarketCategory;
using WeddingHall = Maple2.File.Parser.Xml.Table.WeddingHall;
using WeddingPackage = Maple2.Model.Metadata.WeddingPackage;
using WeddingReward = Maple2.Model.Metadata.WeddingReward;

namespace Maple2.File.Ingest.Mapper;

public class TableMapper : TypeMapper<TableMetadata> {
    private readonly TableParser parser;
    private readonly ItemOptionParser optionParser;
    private readonly string language;

    public TableMapper(M2dReader xmlReader, string language) {
        parser = new TableParser(xmlReader, language);
        optionParser = new ItemOptionParser(xmlReader);
        this.language = language;
    }

    protected override IEnumerable<TableMetadata> Map() {
        yield return new TableMetadata { Name = TableNames.CHAT_EMOTICON, Table = ParseChatSticker() };
        yield return new TableMetadata { Name = TableNames.DEFAULT_ITEMS, Table = ParseDefaultItems() };
        yield return new TableMetadata { Name = TableNames.ITEM_BREAK_INGREDIENT, Table = ParseItemBreakIngredient() };
        yield return new TableMetadata { Name = TableNames.ITEM_GEMSTONE_UPGRADE, Table = ParseItemGemstoneUpgrade() };
        yield return new TableMetadata { Name = TableNames.ITEM_EXTRACTION, Table = ParseItemExtraction() };
        yield return new TableMetadata { Name = TableNames.JOB, Table = ParseJobTable() };
        yield return new TableMetadata { Name = TableNames.MAGIC_PATH, Table = ParseMagicPath() };
        yield return new TableMetadata { Name = TableNames.INSTRUMENT_CATEGORY_INFO, Table = ParseInstrument() };
        yield return new TableMetadata { Name = TableNames.INTERACT_OBJECT, Table = ParseInteractObject() };
        yield return new TableMetadata { Name = TableNames.ITEM_LAPENSHARD_UPGRADE, Table = ParseLapenshardUpgradeTable() };
        yield return new TableMetadata { Name = TableNames.ITEM_SOCKET, Table = ParseItemSocketTable() };
        yield return new TableMetadata { Name = TableNames.MASTERY_RECIPE, Table = ParseMasteryRecipe() };
        yield return new TableMetadata { Name = TableNames.MASTERY, Table = ParseMasteryReward() };
        yield return new TableMetadata { Name = TableNames.MASTERYT_DIFFERENTIAL_FACTOR, Table = ParseMasteryDifferentialFactor() };
        yield return new TableMetadata { Name = TableNames.GUILD, Table = ParseGuildTable() };
        yield return new TableMetadata { Name = TableNames.VIP, Table = ParsePremiumClubTable() };
        yield return new TableMetadata { Name = TableNames.INDIVIDUAL_ITEM_DROP, Table = ParseIndividualItemDropTable() };
        yield return new TableMetadata { Name = TableNames.COLOR_PALETTE, Table = ParseColorPaletteTable() };
        yield return new TableMetadata { Name = TableNames.MERET_MARKET_CATEGORY, Table = ParseMeretMarketCategoryTable() };
        yield return new TableMetadata { Name = TableNames.SHOP_BEAUTY_COUPON, Table = ParseShopBeautyCouponTable() };
        yield return new TableMetadata { Name = TableNames.SHOP_FURNISHING, Table = ParseFurnishingShopTable() };
        yield return new TableMetadata { Name = TableNames.GACHA_INFO, Table = ParseGachaInfoTable() };
        yield return new TableMetadata { Name = TableNames.NAME_TAG_SYMBOL, Table = ParseInsigniaTable() };
        yield return new TableMetadata { Name = TableNames.EXP, Table = ParseExpTable() };
        yield return new TableMetadata { Name = TableNames.COMMON_EXP, Table = ParseCommonExpTable() };
        yield return new TableMetadata { Name = TableNames.UGC_DESIGN, Table = ParseUgcDesignTable() };
        yield return new TableMetadata { Name = TableNames.LEARNING_QUEST, Table = ParseLearningQuestTable() };
        yield return new TableMetadata { Name = TableNames.BLACK_MARKET_TABLE, Table = ParseBlackMarketTable() };
        yield return new TableMetadata { Name = TableNames.CHANGE_JOB, Table = ParseChangeJobTable() };
        yield return new TableMetadata { Name = TableNames.CHAPTER_BOOK, Table = ParseChapterBookTable() };
        yield return new TableMetadata { Name = TableNames.FIELD_MISSION, Table = ParseFieldMissionTable() };
        yield return new TableMetadata { Name = TableNames.WORLD_MAP, Table = ParseWorldMapTable() };
        yield return new TableMetadata { Name = TableNames.SURVIVAL_SKIN_INFO, Table = ParseSurvivalSkinTable() };
        yield return new TableMetadata { Name = TableNames.BANNER, Table = ParseBanner() };
        yield return new TableMetadata { Name = TableNames.MASTERY_UGC_HOUSING, Table = ParseMasteryUgcHousingTable() };
        yield return new TableMetadata { Name = TableNames.UGC_HOUSING_POINT_REWARD, Table = ParseUgcHousingPointRewardTable() };
        yield return new TableMetadata { Name = TableNames.REWARD_CONTENT, Table = ParseRewardContentTable() };
        yield return new TableMetadata { Name = TableNames.SEASON_DATA, Table = ParseSeasonDataTable() };
        yield return new TableMetadata { Name = TableNames.SMART_PUSH, Table = ParseSmartPushTable() };
        yield return new TableMetadata { Name = TableNames.AUTO_ACTION, Table = ParseAutoActionTable() };

        // Marriage/Wedding
        yield return new TableMetadata { Name = TableNames.WEDDING, Table = ParseWeddingTable() };

        // Prestige
        yield return new TableMetadata { Name = TableNames.PRESTIGE_LEVEL_ABILITY, Table = ParsePrestigeLevelAbilityTable() };
        yield return new TableMetadata { Name = TableNames.PRESTIGE_LEVEL_REWARD, Table = ParsePrestigeLevelRewardTable() };
        yield return new TableMetadata { Name = TableNames.PRESTIGE_MISSION, Table = ParsePrestigeMissionTable() };

        yield return new TableMetadata { Name = TableNames.FISHING_ROD, Table = ParseFishingRod() };
        // Scroll
        yield return new TableMetadata { Name = TableNames.ENCHANT_SCROLL, Table = ParseEnchantScrollTable() };
        yield return new TableMetadata { Name = TableNames.ITEM_REMAKE_SCROLL, Table = ParseItemRemakeScrollTable() };
        yield return new TableMetadata { Name = TableNames.ITEM_REPACKING_SCROLL, Table = ParseItemRepackingScrollTable() };
        yield return new TableMetadata { Name = TableNames.ITEM_SOCKET_SCROLL, Table = ParseItemSocketScrollTable() };
        yield return new TableMetadata { Name = TableNames.ITEM_EXCHANGE_SCROLL, Table = ParseItemExchangeScrollTable() };
        // ItemOption
        yield return new TableMetadata { Name = TableNames.ITEM_OPTION_CONSTANT, Table = ParseItemOptionConstant() };
        yield return new TableMetadata { Name = TableNames.ITEM_OPTION_RANDOM, Table = ParseItemOptionRandom() };
        yield return new TableMetadata { Name = TableNames.ITEM_OPTION_STATIC, Table = ParseItemOptionStatic() };
        yield return new TableMetadata { Name = TableNames.ITEM_OPTION_PICK, Table = ParseItemOptionPick() };
        yield return new TableMetadata { Name = TableNames.ITEM_OPTION_VARIATION, Table = ParseItemVariation() };

        foreach ((string type, ItemEquipVariationTable table) in ParseItemEquipVariation()) {
            yield return new TableMetadata { Name = TableNames.ItemOptionVariationTableNames[type], Table = table };
        }
        // SetItemOption
        yield return new TableMetadata { Name = TableNames.SET_ITEM, Table = ParseSetItem() };

        //Dungeon
        yield return new TableMetadata { Name = TableNames.DUNGEON_ROOM, Table = ParseDungeonRoom() };
        yield return new TableMetadata { Name = TableNames.DUNGEON_RANK_REWARD, Table = ParseDungeonRankReward() };
        yield return new TableMetadata { Name = TableNames.DUNGEON_CONFIG, Table = ParseDungeonConfigTable() };
        yield return new TableMetadata { Name = TableNames.DUNGEON_MISSION, Table = ParseDungeonMissionTable() };
    }

    private ChatStickerTable ParseChatSticker() {
        var results = new Dictionary<int, ChatStickerMetadata>();
        foreach ((int id, ChatSticker sticker) in parser.ParseChatSticker()) {
            results[id] = new ChatStickerMetadata(
                Id: id,
                GroupId: sticker.group_id);
        }

        return new ChatStickerTable(results);
    }

    private DefaultItemsTable ParseDefaultItems() {
        var common = new Dictionary<EquipSlot, int[]>();
        var job = new Dictionary<JobCode, IReadOnlyDictionary<EquipSlot, int[]>>();

        foreach (IGrouping<int, (int JobCode, string Slot, IList<DefaultItems.Item> Items)> groups in parser.ParseDefaultItems().GroupBy(entry => entry.JobCode)) {
            var equips = new Dictionary<EquipSlot, int[]>();
            foreach ((_, string slot, IList<DefaultItems.Item> items) in groups) {
                var equipSlot = Enum.Parse<EquipSlot>(slot);

                List<int> itemIds = items.Select(item => item.id).ToList();
                Dictionary<EquipSlot, int[]> dict = groups.Key == 0 ? common : equips;
                if (dict.Remove(equipSlot, out int[]? existingIds)) {
                    itemIds.AddRange(existingIds);
                }
                dict.Add(equipSlot, itemIds.Distinct().Order().ToArray());
            }

            if (equips.Count > 0) {
                job.Add((JobCode) groups.Key, equips);
            }
        }

        return new DefaultItemsTable(common, job);
    }

    private ItemBreakTable ParseItemBreakIngredient() {
        var results = new Dictionary<int, IReadOnlyList<ItemBreakTable.Ingredient>>();
        foreach ((int itemId, ItemBreakIngredient item) in parser.ParseItemBreakIngredient()) {
            var ingredients = new List<ItemBreakTable.Ingredient>();
            if (item.IngredientItemID1 > 0 && item.IngredientCount1 > 0) {
                ingredients.Add(new ItemBreakTable.Ingredient(item.IngredientItemID1, item.IngredientCount1));
            }
            if (item.IngredientItemID2 > 0 && item.IngredientCount2 > 0) {
                ingredients.Add(new ItemBreakTable.Ingredient(item.IngredientItemID2, item.IngredientCount2));
            }
            if (item.IngredientItemID3 > 0 && item.IngredientCount3 > 0) {
                ingredients.Add(new ItemBreakTable.Ingredient(item.IngredientItemID3, item.IngredientCount3));
            }

            results.Add(itemId, ingredients);
        }

        return new ItemBreakTable(results);
    }

    private GemstoneUpgradeTable ParseItemGemstoneUpgrade() {
        var results = new Dictionary<int, GemstoneUpgradeTable.Entry>();
        foreach ((int itemId, ItemGemstoneUpgrade upgrade) in parser.ParseItemGemstoneUpgrade()) {
            var ingredients = new List<GemstoneUpgradeTable.Ingredient>();
            if (upgrade.IngredientCount1 > 0 && upgrade.IngredientItemID1?.Length > 1) {
                ingredients.Add(new GemstoneUpgradeTable.Ingredient(Enum.Parse<ItemTag>(upgrade.IngredientItemID1[1]), upgrade.IngredientCount1));
            }
            if (upgrade.IngredientCount2 > 0 && upgrade.IngredientItemID2?.Length > 1) {
                ingredients.Add(new GemstoneUpgradeTable.Ingredient(Enum.Parse<ItemTag>(upgrade.IngredientItemID2[1]), upgrade.IngredientCount2));
            }
            if (upgrade.IngredientCount3 > 0 && upgrade.IngredientItemID3?.Length > 1) {
                ingredients.Add(new GemstoneUpgradeTable.Ingredient(Enum.Parse<ItemTag>(upgrade.IngredientItemID3[1]), upgrade.IngredientCount3));
            }
            if (upgrade.IngredientCount4 > 0 && upgrade.IngredientItemID4?.Length > 1) {
                ingredients.Add(new GemstoneUpgradeTable.Ingredient(Enum.Parse<ItemTag>(upgrade.IngredientItemID4[1]), upgrade.IngredientCount4));
            }

            results.Add(itemId, new GemstoneUpgradeTable.Entry(upgrade.GemLevel, upgrade.NextItemID, ingredients));
        }

        return new GemstoneUpgradeTable(results);
    }

    private ItemExtractionTable ParseItemExtraction() {
        var results = new Dictionary<int, ItemExtractionTable.Entry>();
        foreach ((int targetItemId, ItemExtraction item) in parser.ParseItemExtraction()) {
            results.Add(targetItemId, new ItemExtractionTable.Entry(item.TargetItemID, item.TryCount, item.ScrollCount, item.ResultItemID));
        }

        return new ItemExtractionTable(results);
    }

    private JobTable ParseJobTable() {
        var results = new Dictionary<JobCode, JobTable.Entry>();
        foreach (Parser.Xml.Table.JobTable data in parser.ParseJobTable()) {
            var skills = new Dictionary<SkillRank, JobTable.Skill[]> {
                [SkillRank.Basic] = data.skills.skill
                    .Where(skill => skill.subJobCode <= data.code) // This is not actually correct, but works.
                    .Select(skill => new JobTable.Skill(skill.main, skill.sub, skill.maxLevel, skill.quickSlotPriority))
                    .ToArray(),
                [SkillRank.Awakening] = data.skills.skill
                    .Where(skill => skill.subJobCode > data.code) // This is not actually correct, but works.
                    .Select(skill => new JobTable.Skill(skill.main, skill.sub, skill.maxLevel, skill.quickSlotPriority))
                    .ToArray(),
            };

            results[(JobCode) data.code] = new JobTable.Entry(Tutorial: new JobTable.Tutorial(StartField: data.startField,
                    SkipField: data.tutorialSkipField.Length > 0 ? data.tutorialSkipField[0] : 0,
                    SkipItem: data.tutorialSkipItem,
                    OpenMaps: data.tutorialClearOpenMaps,
                    OpenTaxis: data.tutorialClearOpenTaxis,
                    StartItem: data.startInvenItem.item.Select(item => new JobTable.Item(item.itemID, item.grade, item.count)).ToArray(),
                    Reward: data.reward.item.Select(item => new JobTable.Item(item.itemID, item.grade, 1)).ToArray()),
                Skills: skills,
                BaseSkills: data.learn.SelectMany(learn => learn.skill)
                    .SelectMany(skill => skill.sub.Append(skill.id))
                    .OrderBy(id => id)
                    .ToArray());
        }

        return new JobTable(results);
    }

    private MagicPathTable ParseMagicPath() {
        var results = new Dictionary<long, IReadOnlyList<MagicPath>>();
        foreach ((long id, MagicType type) in parser.ParseMagicPath()) {
            // Dropping duplicates for now (60073021, 50000303, 5009, 5101)
            if (results.ContainsKey(id)) {
                continue;
            }

            List<MagicPath> moves = type.move.Select(move => new MagicPath(
                Align: move.align,
                AlignHeight: move.alignCubeHeight,
                Rotate: move.rotation,
                IgnoreAdjust: move.ignoreAdjustCubePosition,
                ExplosionByDestroy: move.explosionByDestroy,
                CatmullRom: move.catmullrom != 0,
                IgnorePhysXTestInitPosition: move.ignorePhysxTestInitPosition,
                IgnoreCancelAtSpawnTime: move.ignoreCancelAtSpawnTime,
                Direction: move.direction != default ? Vector3.Normalize(move.direction) : default,
                FireOffset: move.fireOffsetPosition,
                FireFixed: move.fireFixedPosition,
                ControlValue0: move.controlValue0,
                ControlValue1: move.controlValue1,
                ControlEndOffsetValue: move.controlEndOffsetValue,
                TraceTargetOffset: move.traceTargetOffsetPos,
                TraceTargetDuration: move.traceTargetDuration,
                Velocity: move.vel,
                Distance: move.distance,
                RotateZDegree: move.dirRotZDegree,
                LifeTime: move.lifeTime,
                DelayTime: move.delayTime,
                SpawnTime: move.spawnTime,
                DestroyTime: move.destroyTime,
                ControlRate: move.controlRate,
                LookAtType: move.lookAtType,
                PiercingAttackInterval: move.piercingAttackInterval,
                PiercingAttackMaxTargetCount: move.piercingAttackMaxTargetCount,
                NonTargetMoveDistance: move.nonTargetMoveDistance,
                MoveEndHoldDuration: move.moveEndHoldDuration
            )).ToList();
            results[id] = moves;
        }

        return new MagicPathTable(results);
    }

    private InstrumentTable ParseInstrument() {
        var categories = new Dictionary<int, (int MidiId, int PercussionId)>();
        foreach ((int _, InstrumentCategoryInfo info) in parser.ParseInstrumentCategoryInfo()) {
            categories[info.id] = (info.GMId, info.percussionId);
        }

        var results = new Dictionary<int, InstrumentMetadata>();
        foreach ((int id, InstrumentInfo info) in parser.ParseInstrumentInfo()) {
            if (!categories.ContainsKey(info.category)) {
                Console.WriteLine($"Instrument {id} does not have a matching category: {info.category}");
                continue;
            }

            (int midiId, int percussionId) = categories[info.category];
            results[id] = new InstrumentMetadata(
                Id: info.id,
                EquipId: info.equipItemId,
                ScoreCount: info.soloRelayScoreCount,
                Category: info.category,
                MidiId: midiId,
                PercussionId: percussionId);
        }

        return new InstrumentTable(results);
    }

    private InteractObjectTable ParseInteractObject() {
        var results = new Dictionary<int, InteractObjectMetadata>();
        results = MergeInteractObjectTable(results, parser.ParseInteractObjectMastery());
        results = MergeInteractObjectTable(results, parser.ParseInteractObject().Select(entry => (entry.Id, entry.Info)));
        return new InteractObjectTable(results);
    }
    private Dictionary<int, InteractObjectMetadata> MergeInteractObjectTable(Dictionary<int, InteractObjectMetadata> results, IEnumerable<(int Id, InteractObject Info)> parser) {
        foreach ((int id, InteractObject info) in parser) {
            var spawn = new InteractObjectMetadataSpawn[info.spawn.code.Length];
            for (int i = 0; i < spawn.Length; i++) {
                spawn[i] = new InteractObjectMetadataSpawn(
                    Id: info.spawn.code[i],
                    Radius: info.spawn.radius[i],
                    Count: info.spawn.count[i],
                    Probability: info.spawn.prop[i],
                    LifeTime: info.spawn.lifeTime[i]);
            }

            results[id] = new InteractObjectMetadata(
                Id: info.id,
                Type: (InteractType) info.type,
                Collection: info.collection,
                ReactCount: info.reactCount,
                TargetPortalId: info.portal.targetPortalId,
                GuildPosterId: info.guild.housePosterId,
                WeaponItemId: info.weapon.weaponItemId,
                Item: new InteractObjectMetadataItem(info.item.code, info.item.consume, info.item.rank, info.item.checkCount, info.gathering.receipeID),
                Time: new InteractObjectMetadataTime(info.time.resetTime, info.time.reactTime, info.time.hideTime),
                Drop: new InteractObjectMetadataDrop(info.drop.objectDropRank, info.drop.globalDropBoxId ?? [], info.drop.individualDropBoxId ?? [], info.drop.dropHeight, info.drop.dropDistance),
                AdditionalEffect: new InteractObjectMetadataEffect(
                    Condition: ParseConditional(info.conditionAdditionalEffect),
                    Invoke: ParseInvoke(info.additionalEffect),
                    ModifyCode: info.additionalEffect.modify.code,
                    ModifyTime: info.additionalEffect.modify.modifyTime),
                Spawn: spawn
            );
        }
        return results;

        InteractObjectMetadataEffect.ConditionEffect[] ParseConditional(InteractObject.ConditionAdditionalEffect additionalEffect) {
            if (additionalEffect.id.Length == 0 || additionalEffect.id[0] == 0) {
                return [];
            }

            return additionalEffect.id.Zip(additionalEffect.level, (effectId, level) =>
                new InteractObjectMetadataEffect.ConditionEffect(effectId, level)).ToArray();
        }

        InteractObjectMetadataEffect.InvokeEffect[] ParseInvoke(InteractObject.AdditionalEffect additionalEffect) {
            if (additionalEffect.invoke.code.Length == 0 || additionalEffect.invoke.code[0] == 0) {
                return [];
            }

            return additionalEffect.invoke.code
                .Zip(additionalEffect.invoke.level, (effectId, level) => new { skillId = effectId, level })
                .Zip(additionalEffect.invoke.prop, (effect, prop) =>
                    new InteractObjectMetadataEffect.InvokeEffect(effect.skillId, effect.level, prop))
                .ToArray();
        }
    }

    private ItemOptionConstantTable ParseItemOptionConstant() {
        var results = new Dictionary<int, IReadOnlyDictionary<int, ItemOptionConstant>>();
        foreach (ItemOptionConstantData entry in optionParser.ParseConstant()) {
            var statValues = new Dictionary<BasicAttribute, int>();
            var statRates = new Dictionary<BasicAttribute, float>();
            foreach (BasicAttribute attribute in Enum.GetValues<BasicAttribute>()) {
                int value = entry.StatValue((byte) attribute);
                if (value != default) {
                    statValues[attribute] = value;
                }
                float rate = entry.StatRate((byte) attribute);
                if (rate != default) {
                    statRates[attribute] = rate;
                }
            }

            var specialValues = new Dictionary<SpecialAttribute, int>();
            var specialRates = new Dictionary<SpecialAttribute, float>();
            foreach (SpecialAttribute attribute in Enum.GetValues<SpecialAttribute>()) {
                byte index = attribute.OptionIndex();
                if (index == byte.MaxValue) continue;

                SpecialAttribute fixAttribute = attribute.SgiTarget(entry.sgi_target);
                int value = entry.SpecialValue(index);
                if (value != default) {
                    specialValues[fixAttribute] = value;
                }
                float rate = entry.SpecialRate(index);
                if (rate != default) {
                    specialRates[fixAttribute] = rate;
                }
            }

            if (!results.ContainsKey(entry.code)) {
                results[entry.code] = new Dictionary<int, ItemOptionConstant>();
            }

            var option = new ItemOptionConstant(
                Values: statValues,
                Rates: statRates,
                SpecialValues: specialValues,
                SpecialRates: specialRates);
            (results[entry.code] as Dictionary<int, ItemOptionConstant>)!.Add(entry.grade, option);
        }

        return new ItemOptionConstantTable(results);
    }

    private ItemOptionRandomTable ParseItemOptionRandom() {
        return new ItemOptionRandomTable(optionParser.ParseRandom().ToDictionary());
    }

    private ItemOptionStaticTable ParseItemOptionStatic() {
        return new ItemOptionStaticTable(optionParser.ParseStatic().ToDictionary());
    }

    private ItemOptionPickTable ParseItemOptionPick() {
        var results = new Dictionary<int, IReadOnlyDictionary<int, ItemOptionPickTable.Option>>();
        foreach (ItemOptionPick entry in optionParser.ParsePick()) {
            var constantValue = new Dictionary<BasicAttribute, int>();
            for (int i = 0; i < entry.constant_value.Length; i += 2) {
                if (string.IsNullOrWhiteSpace(entry.constant_value[i])) continue;
                constantValue.Add(entry.constant_value[i].ToBasicAttribute(), int.Parse(entry.constant_value[i + 1]));
            }
            var constantRate = new Dictionary<BasicAttribute, int>();
            for (int i = 0; i < entry.constant_rate.Length; i += 2) {
                if (string.IsNullOrWhiteSpace(entry.constant_rate[i])) continue;
                constantRate.Add(entry.constant_rate[i].ToBasicAttribute(), int.Parse(entry.constant_rate[i + 1]));
            }
            var staticValue = new Dictionary<BasicAttribute, int>();
            for (int i = 0; i < entry.static_value.Length; i += 2) {
                if (string.IsNullOrWhiteSpace(entry.static_value[i])) continue;
                staticValue.Add(entry.static_value[i].ToBasicAttribute(), int.Parse(entry.static_value[i + 1]));
            }
            var staticRate = new Dictionary<BasicAttribute, int>();
            for (int i = 0; i < entry.static_rate.Length; i += 2) {
                if (string.IsNullOrWhiteSpace(entry.static_rate[i])) continue;
                staticRate.Add(entry.static_rate[i].ToBasicAttribute(), int.Parse(entry.static_rate[i + 1]));
            }
            var randomValue = new Dictionary<BasicAttribute, int>();
            for (int i = 0; i < entry.random_value.Length; i += 2) {
                if (string.IsNullOrWhiteSpace(entry.random_value[i])) continue;
                randomValue.Add(entry.random_value[i].ToBasicAttribute(), int.Parse(entry.random_value[i + 1]));
            }
            var randomRate = new Dictionary<BasicAttribute, int>();
            for (int i = 0; i < entry.random_rate.Length; i += 2) {
                if (string.IsNullOrWhiteSpace(entry.random_rate[i])) continue;
                randomRate.Add(entry.random_rate[i].ToBasicAttribute(), int.Parse(entry.random_rate[i + 1]));
            }

            if (!results.ContainsKey(entry.optionPickID)) {
                results[entry.optionPickID] = new Dictionary<int, ItemOptionPickTable.Option>();
            }

            var option = new ItemOptionPickTable.Option(constantValue, constantRate, staticValue, staticRate, randomValue, randomRate);
            (results[entry.optionPickID] as Dictionary<int, ItemOptionPickTable.Option>)!.Add(entry.itemGrade, option);
        }
        return new ItemOptionPickTable(results);
    }

    private ItemVariationTable ParseItemVariation() {
        var values = new Dictionary<BasicAttribute, List<ItemVariationTable.Range<int>>>();
        var rates = new Dictionary<BasicAttribute, List<ItemVariationTable.Range<float>>>();
        var specialValues = new Dictionary<SpecialAttribute, List<ItemVariationTable.Range<int>>>();
        var specialRates = new Dictionary<SpecialAttribute, List<ItemVariationTable.Range<float>>>();
        foreach (ItemOptionVariation.Option option in optionParser.ParseVariation()) {
            string name = option.OptionName;
            if (name.StartsWith("sid")) continue; // Don't know what stat this maps to.

            if (option.OptionValueVariation != 0) {
                var variation = new ItemVariationTable.Range<int>(
                    Min: option.OptionValueMin,
                    Max: option.OptionValueMax,
                    Variation: option.OptionValueVariation);
                try {
                    if (values.ContainsKey(name.ToBasicAttribute())) {
                        values[name.ToBasicAttribute()].Add(variation);
                    } else {
                        values.Add(name.ToBasicAttribute(), [variation]);
                    }
                } catch (ArgumentOutOfRangeException) {
                    if (specialValues.ContainsKey(name.ToSpecialAttribute())) {
                        specialValues[name.ToSpecialAttribute()].Add(variation);
                    } else {
                        specialValues.Add(name.ToSpecialAttribute(), [variation]);
                    }
                }
            } else if (option.OptionRateVariation != 0) {
                if (name.EndsWith("_rate")) {
                    name = name[..^"_rate".Length]; // sanitize suffix
                }

                var variation = new ItemVariationTable.Range<float>(
                    Min: option.OptionRateMin,
                    Max: option.OptionRateMax,
                    Variation: option.OptionRateVariation);
                try {
                    if (rates.ContainsKey(name.ToBasicAttribute())) {
                        rates[name.ToBasicAttribute()].Add(variation);
                    } else {
                        rates.Add(name.ToBasicAttribute(), [variation]);
                    }
                } catch (ArgumentOutOfRangeException) {
                    if (specialRates.ContainsKey(name.ToSpecialAttribute())) {
                        specialRates[name.ToSpecialAttribute()].Add(variation);
                    } else {
                        specialRates.Add(name.ToSpecialAttribute(), [variation]);
                    }
                }
            }
        }

        Dictionary<BasicAttribute, ItemVariationTable.Range<int>[]> valuesArray = values.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray());
        Dictionary<BasicAttribute, ItemVariationTable.Range<float>[]> ratesArray = rates.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray());
        Dictionary<SpecialAttribute, ItemVariationTable.Range<int>[]> specialValuesArray = specialValues.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray());
        Dictionary<SpecialAttribute, ItemVariationTable.Range<float>[]> specialRatesArray = specialRates.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray());

        return new ItemVariationTable(valuesArray, ratesArray, specialValuesArray, specialRatesArray);
    }

    private IEnumerable<(string Type, ItemEquipVariationTable Table)> ParseItemEquipVariation() {
        foreach ((string type, List<ItemOptionVariationEquip.Option> options) in optionParser.ParseVariationEquip()) {
            var values = new Dictionary<BasicAttribute, ItemEquipVariationTable.Set<int>[]>();
            var rates = new Dictionary<BasicAttribute, ItemEquipVariationTable.Set<float>[]>();
            var specialValues = new Dictionary<SpecialAttribute, ItemEquipVariationTable.Set<int>[]>();
            var specialRates = new Dictionary<SpecialAttribute, ItemEquipVariationTable.Set<float>[]>();
            foreach (ItemOptionVariationEquip.Option option in options) {
                string name = option.name.ToLower();
                if (name.EndsWith("value")) {
                    var entries = new ItemEquipVariationTable.Set<int>[18];
                    for (int i = 0; i < 18; i++) {
                        entries[i] = new ItemEquipVariationTable.Set<int>(
                            Value: (int) option[i],
                            Weight: 1); // TODO: Weight
                    }

                    name = name[..^"value".Length]; // Remove suffix
                    try {
                        values.Add(name.ToBasicAttribute(), entries);
                    } catch (ArgumentOutOfRangeException) {
                        specialValues.Add(name.ToSpecialAttribute(), entries);
                    }

                } else if (name.EndsWith("rate")) {
                    var entries = new ItemEquipVariationTable.Set<float>[18];
                    for (int i = 0; i < 18; i++) {
                        entries[i] = new ItemEquipVariationTable.Set<float>(
                            Value: option[i],
                            Weight: 1); // TODO: Weight
                    }

                    name = name[..^"rate".Length]; // Remove suffix
                    try {
                        rates.Add(name.ToBasicAttribute(), entries);
                    } catch (ArgumentOutOfRangeException) {
                        specialRates.Add(name.ToSpecialAttribute(), entries);
                    }
                } else {
                    throw new ArgumentException($"Invalid option name: {option.name}");
                }
            }

            yield return (type, new ItemEquipVariationTable(values, rates, specialValues, specialRates));
        }
    }

    private SetItemTable ParseSetItem() {
        var options = new Dictionary<int, SetBonusMetadata[]>();
        foreach ((int id, SetItemOption option) in parser.ParseSetItemOption()) {
            var parts = new List<SetBonusMetadata>();
            foreach (SetItemOption.Part part in option.part) {
                var values = new Dictionary<BasicAttribute, long>();
                var rates = new Dictionary<BasicAttribute, float>();
                var specialValues = new Dictionary<SpecialAttribute, float>();
                var specialRates = new Dictionary<SpecialAttribute, float>();

                foreach (BasicAttribute attribute in Enum.GetValues<BasicAttribute>()) {
                    values.AddIfNotDefault(attribute, part.StatValue((byte) attribute));
                    rates.AddIfNotDefault(attribute, part.StatRate((byte) attribute));
                }

                // Since 4 is already "Boss" we can ignore sgi_boss_target
                Debug.Assert(part.sgi_boss_target is 0 or 4);
                foreach (SpecialAttribute attribute in Enum.GetValues<SpecialAttribute>()) {
                    byte attributeOption = attribute.OptionIndex();

                    if (attributeOption != byte.MaxValue) {
                        SpecialAttribute fixAttribute = attribute.SgiTarget(part.sgi_target);
                        specialValues.AddIfNotDefault(fixAttribute, part.SpecialValue(attributeOption));
                        specialRates.AddIfNotDefault(fixAttribute, part.SpecialRate(attributeOption));
                    }
                }

                parts.Add(new SetBonusMetadata(
                    Count: part.count,
                    AdditionalEffects: part.additionalEffectID.Zip(part.additionalEffectLevel,
                        (skillId, level) => new SetBonusAdditionalEffect(skillId, level)).ToArray(),
                    Values: values,
                    Rates: rates,
                    SpecialValues: specialValues,
                    SpecialRates: specialRates));
            }

            options[id] = parts.ToArray();
        }

        var results = new Dictionary<int, SetItemTable.Entry>();
        foreach ((int id, string name, SetItemInfo info) in parser.ParseSetItemInfo()) {
            Debug.Assert(options.ContainsKey(info.optionID));

            results[id] = new SetItemTable.Entry(
                Info: new SetItemInfoMetadata(
                    Id: id,
                    Name: name,
                    ItemIds: info.itemIDs,
                    OptionId: info.optionID),
                Options: options[info.optionID]);
        }

        return new SetItemTable(results);
    }

    private LapenshardUpgradeTable ParseLapenshardUpgradeTable() {
        var results = new Dictionary<int, LapenshardUpgradeTable.Entry>();
        foreach ((int itemId, ItemLapenshardUpgrade upgrade) in parser.ParseItemLapenshardUpgrade()) {
            var ingredients = new List<LapenshardUpgradeTable.Ingredient>();
            if (upgrade.IngredientCount1 > 0 && upgrade.IngredientItemID1?.Length > 1) {
                ingredients.Add(new LapenshardUpgradeTable.Ingredient(Enum.Parse<ItemTag>(upgrade.IngredientItemID1[1]), upgrade.IngredientCount1));
            }
            if (upgrade.IngredientCount2 > 0 && upgrade.IngredientItemID2?.Length > 1) {
                ingredients.Add(new LapenshardUpgradeTable.Ingredient(Enum.Parse<ItemTag>(upgrade.IngredientItemID2[1]), upgrade.IngredientCount2));
            }
            if (upgrade.IngredientCount3 > 0 && upgrade.IngredientItemID3?.Length > 1) {
                ingredients.Add(new LapenshardUpgradeTable.Ingredient(Enum.Parse<ItemTag>(upgrade.IngredientItemID3[1]), upgrade.IngredientCount3));
            }

            results.Add(itemId, new LapenshardUpgradeTable.Entry(
                Level: upgrade.LapenLevel,
                GroupId: upgrade.LapenGroupID,
                NextItemId: upgrade.NextItemID,
                RequireCount: upgrade.GroupLapenshardMinCount,
                Ingredients: ingredients,
                Meso: upgrade.meso));
        }

        return new LapenshardUpgradeTable(results);
    }

    private ItemSocketTable ParseItemSocketTable() {
        var results = new Dictionary<int, IReadOnlyDictionary<int, ItemSocketMetadata>>();
        IEnumerable<IGrouping<int, ItemSocket>> groups = parser.ParseItemSocket()
            .Select(entry => entry.Socket)
            .GroupBy(entry => entry.id);
        foreach (IGrouping<int, ItemSocket> group in groups) {
            var idResults = new Dictionary<int, ItemSocketMetadata>();
            foreach (ItemSocket socket in group) {
                idResults.Add(socket.grade, new ItemSocketMetadata(
                    MaxCount: socket.maxCount,
                    OpenCount: socket.fixOpenCount));
            }
            results.Add(group.Key, idResults);
        }

        return new ItemSocketTable(results);
    }

    private MasteryRecipeTable ParseMasteryRecipe() {
        var results = new Dictionary<int, MasteryRecipeTable.Entry>();
        foreach ((long id, MasteryRecipe recipe) in parser.ParseMasteryRecipe()) {
            var requiredItems = new List<ItemComponent>();
            ItemComponent? requiredItem1 = ParseMasteryIngredient(recipe.requireItem1);
            if (requiredItem1 != null) requiredItems.Add(requiredItem1);
            ItemComponent? requiredItem2 = ParseMasteryIngredient(recipe.requireItem2);
            if (requiredItem2 != null) requiredItems.Add(requiredItem2);
            ItemComponent? requiredItem3 = ParseMasteryIngredient(recipe.requireItem3);
            if (requiredItem3 != null) requiredItems.Add(requiredItem3);
            ItemComponent? requiredItem4 = ParseMasteryIngredient(recipe.requireItem4);
            if (requiredItem4 != null) requiredItems.Add(requiredItem4);
            ItemComponent? requiredItem5 = ParseMasteryIngredient(recipe.requireItem5);
            if (requiredItem5 != null) requiredItems.Add(requiredItem5);

            var rewardItems = new List<ItemComponent>();
            ItemComponent? rewardItem1 = ParseMasteryIngredient(recipe.rewardItem1);
            if (rewardItem1 != null) rewardItems.Add(rewardItem1);
            ItemComponent? rewardItem2 = ParseMasteryIngredient(recipe.rewardItem2);
            if (rewardItem2 != null) rewardItems.Add(rewardItem2);
            ItemComponent? rewardItem3 = ParseMasteryIngredient(recipe.rewardItem3);
            if (rewardItem3 != null) rewardItems.Add(rewardItem3);
            ItemComponent? rewardItem4 = ParseMasteryIngredient(recipe.rewardItem4);
            if (rewardItem4 != null) rewardItems.Add(rewardItem4);
            ItemComponent? rewardItem5 = ParseMasteryIngredient(recipe.rewardItem5);
            if (rewardItem5 != null) rewardItems.Add(rewardItem5);

            var entry = new MasteryRecipeTable.Entry(
                Id: (int) id,
                Type: (MasteryType) recipe.masteryType,
                NoRewardExp: recipe.exceptRewardExp,
                RequiredMastery: recipe.requireMastery,
                RequiredMeso: recipe.requireMeso,
                RequiredQuests: recipe.requireQuest,
                RewardExp: recipe.rewardExp,
                RewardMastery: recipe.rewardMastery,
                HighRateLimitCount: recipe.highPropLimitCount,
                NormalRateLimitCount: recipe.normalPropLimitCount,
                RequiredItems: requiredItems,
                HabitatMapId: recipe.habitatMapId,
                RewardItems: rewardItems);

            results.Add((int) id, entry);
        }

        return new MasteryRecipeTable(results);
    }

    private static ItemComponent? ParseMasteryIngredient(IReadOnlyList<string> ingredientArray) {
        if (ingredientArray.Count == 0 || ingredientArray[0] == "0") {
            return null;
        }

        string[] idAndTag = ingredientArray[0].Split(":");
        int id = int.Parse(idAndTag[0]);
        string tag = idAndTag.Length > 1 ? idAndTag[1] : string.Empty;
        if (!short.TryParse(ingredientArray[1], out short rarity)) {
            rarity = 1;
        }
        if (!int.TryParse(ingredientArray[2], out int amount)) {
            amount = 1;
        }

        return new ItemComponent(
            ItemId: id,
            Rarity: rarity,
            Amount: amount,
            Tag: string.IsNullOrWhiteSpace(tag) ? ItemTag.None : Enum.Parse<ItemTag>(tag));
    }

    private static ItemComponent? ParseMasteryIngredient(IReadOnlyList<int> ingredientArray) {
        if (ingredientArray.Count == 0 || ingredientArray[0] == 0) {
            return null;
        }

        return new ItemComponent(
            ItemId: ingredientArray[0],
            Rarity: (short) ingredientArray[1],
            Amount: ingredientArray[2],
            Tag: ItemTag.None);
    }

    private MasteryRewardTable ParseMasteryReward() {
        var results = new Dictionary<MasteryType, IReadOnlyDictionary<int, MasteryRewardTable.Entry>>();
        foreach ((Parser.Enum.MasteryType type, MasteryReward reward) in parser.ParseMasteryReward()) {
            var masteryLevelDictionary = new Dictionary<int, MasteryRewardTable.Entry>();
            foreach (MasteryLevel level in reward.v) {
                masteryLevelDictionary.Add(level.grade, new MasteryRewardTable.Entry(
                    Value: level.value,
                    ItemId: level.rewardJobItemID,
                    ItemRarity: level.rewardJobItemRank,
                    ItemAmount: level.rewardJobItemCount));
            }
            results.Add((MasteryType) type, masteryLevelDictionary);
        }
        return new MasteryRewardTable(results);
    }

    private MasteryDifferentialFactorTable ParseMasteryDifferentialFactor() {
        var results = new Dictionary<int, MasteryDifferentialFactorTable.Entry>();
        foreach ((int id, MasteryDifferentialFactor factor) in parser.ParseMasteryDifferentialFactor()) {
            results.Add(id, new MasteryDifferentialFactorTable.Entry(
                Differential: factor.differential,
                Factor: factor.factor));
        }
        return new MasteryDifferentialFactorTable(results);
    }

    private GuildTable ParseGuildTable() {
        // Dictionary<short, int> expTable = parser.ParseGuildExp()
        //     .ToDictionary(entry => (short) entry.Id, entry => entry.Item.value);

        var guildBuffs = new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>>();
        foreach ((int id, IEnumerable<GuildBuff> buffs) in parser.ParseGuildBuff()) {
            var buffLevels = new Dictionary<short, GuildTable.Buff>();
            foreach (GuildBuff buff in buffs) {
                buffLevels[buff.level] = new GuildTable.Buff(
                    Id: buff.additionalEffectId,
                    Level: buff.additionalEffectLevel,
                    RequireLevel: buff.requireLevel,
                    Cost: buff.cost,
                    UpgradeCost: buff.upgradeCost,
                    Duration: buff.duration);
            }
            guildBuffs.Add(id, buffLevels);
        }

        var guildHouses = new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>>();
        foreach ((int rank, IEnumerable<GuildHouse> houses) in parser.ParseGuildHouse()) {
            var themes = new Dictionary<int, GuildTable.House>();
            foreach (GuildHouse house in houses) {
                themes.Add(house.theme, new GuildTable.House(
                    MapId: house.fieldID,
                    RequireLevel: house.upgradeReqGuildLevel,
                    UpgradeCost: house.upgradeCost,
                    ReThemeCost: house.rethemeCost,
                    Facilities: house.facility));
            }
            guildHouses.Add(rank, themes);
        }

        var guildNpcs = new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>>();
        foreach ((Parser.Enum.GuildNpcType type, IEnumerable<GuildNpc> npcs) in parser.ParseGuildNpc()) {
            var levels = new Dictionary<short, GuildTable.Npc>();
            foreach (GuildNpc npc in npcs) {
                levels.Add(npc.level, new GuildTable.Npc(
                    Type: (GuildNpcType) type,
                    Level: npc.level,
                    RequireGuildLevel: npc.requireGuildLevel,
                    RequireHouseLevel: npc.requireHouseLevel,
                    UpgradeCost: npc.upgradeCost));
            }
            guildNpcs.Add((GuildNpcType) type, levels);
        }

        var guildProperties = new SortedDictionary<short, GuildTable.Property>();
        foreach ((int level, GuildProperty property) in parser.ParseGuildProperty()) {
            var entry = new GuildTable.Property(
                Level: property.level,
                Experience: property.accumExp,
                Capacity: property.capacity,
                FundMax: property.fundMax,
                DonateMax: property.donationMax,
                CheckInExp: property.attendGuildExp,
                WinMiniGameExp: property.winMiniGameGuildExp,
                LoseMiniGameExp: property.loseMiniGameGuildExp,
                RaidExp: property.raidGuildExp,
                CheckInFund: property.attendGuildFund,
                WinMiniGameFund: property.winMiniGameGuildFund,
                LoseMiniGameFund: property.loseMiniGameGuildFund,
                RaidFund: property.raidGuildFund,
                CheckInPlayerExpRate: property.attendUserExpFactor,
                DonatePlayerExpRate: property.donationUserExpFactor,
                CheckInCoin: property.attendGuildCoin,
                DonateCoin: property.donateGuildCoin,
                WinMiniGameCoin: property.winMiniGameGuildCoin,
                LoseMiniGameCoin: property.loseMiniGameGuildCoin);
            guildProperties.Add((short) level, entry);
        }

        return new GuildTable(
            Buffs: guildBuffs,
            Houses: guildHouses,
            Npcs: guildNpcs,
            Properties: guildProperties);
    }

    private FishingRodTable ParseFishingRod() {
        var results = new Dictionary<int, FishingRodTable.Entry>();
        foreach ((int id, FishingRod rod) in parser.ParseFishingRod()) {
            var entry = new FishingRodTable.Entry(
                ItemId: rod.itemCode,
                MinMastery: rod.fishMasteryLimit,
                AddMastery: rod.addFishMastery,
                ReduceTime: rod.reduceFishingTime);
            results.Add(id, entry);
        }
        return new FishingRodTable(results);
    }

    private EnchantScrollTable ParseEnchantScrollTable() {
        var results = new Dictionary<int, EnchantScrollMetadata>();
        foreach ((int id, EnchantScroll scroll) in parser.ParseEnchantScroll()) {
            var metadata = new EnchantScrollMetadata(
                Type: (EnchantScrollType) scroll.scrollType,
                MinLevel: scroll.minLv,
                MaxLevel: scroll.maxLv,
                Enchants: scroll.grade,
                ItemTypes: scroll.slot,
                Rarities: scroll.rank);
            Array.Sort(metadata.Enchants); // Just in case
            results.Add(id, metadata);
        }

        return new EnchantScrollTable(results);
    }

    private ItemRemakeScrollTable ParseItemRemakeScrollTable() {
        var results = new Dictionary<int, ItemRemakeScrollMetadata>();
        foreach ((int id, ItemRemakeScroll scroll) in parser.ParseItemRemakeScroll()) {
            results.Add(id, new ItemRemakeScrollMetadata(
                MinLevel: scroll.minLv,
                MaxLevel: scroll.maxLv,
                ItemTypes: scroll.slot,
                Rarities: scroll.rank,
                RollAttribute: scroll.addOpKind == 1,
                RollValueType: (RollValueType) scroll.addOpValue,
                OnlyPet: scroll.onlyPet));
        }

        return new ItemRemakeScrollTable(results);
    }

    private ItemRepackingScrollTable ParseItemRepackingScrollTable() {
        var results = new Dictionary<int, ItemRepackingScrollMetadata>();
        foreach ((int id, ItemRepackingScroll scroll) in parser.ParseItemRepackingScroll()) {
            results.Add(id, new ItemRepackingScrollMetadata(
                MinLevel: scroll.minLv,
                MaxLevel: scroll.maxLv,
                ItemTypes: scroll.slot,
                Rarities: scroll.rank,
                IsPet: scroll.petType));
        }

        return new ItemRepackingScrollTable(results);
    }

    private ItemSocketScrollTable ParseItemSocketScrollTable() {
        // SELECT GROUP_CONCAT(Name), JSON_EXTRACT(`Function`, '$.Parameters') as param
        //     FROM item
        //     WHERE JSON_EXTRACT(`Function`, '$.Name')='ItemSocketScroll'
        // GROUP BY param;
        var socketCount = new Dictionary<int, byte> {
            {10000001, 1}, {10000002, 2}, {10000003, 3},
            {10000011, 1}, {10000012, 2},
            {10000013, 1}, {10000014, 2},
            {10000015, 1},
            {10000016, 1},
            {10000017, 1},
            {10000018, 1},
            {10000019, 1},
            {10000020, 1},
            {10000021, 1},
            {10000022, 1}, {10000023, 2},
            {10000024, 1}, {10000025, 2},
            {10000026, 1}, {10000027, 2},
            {10000028, 1}, {10000029, 2},
            {10000030, 1},
            {10000031, 1}, {10000032, 2},
            {10000033, 1}, {10000034, 2},
            {10000035, 1}, {10000036, 2},
        };

        var results = new Dictionary<int, ItemSocketScrollMetadata>();
        foreach ((int id, ItemSocketScroll scroll) in parser.ParseItemSocketScroll()) {
            results.Add(id, new ItemSocketScrollMetadata(
                MinLevel: scroll.minLv,
                MaxLevel: scroll.maxLv,
                ItemTypes: scroll.slot,
                Rarities: scroll.rank,
                SocketCount: socketCount[id],
                TradableCountDeduction: scroll.tradableCountDeduction));
        }

        return new ItemSocketScrollTable(results);
    }

    private ItemExchangeScrollTable ParseItemExchangeScrollTable() {
        var results = new Dictionary<int, ItemExchangeScrollMetadata>();
        foreach ((int id, ItemExchangeScroll scroll) in parser.ParseItemExchangeScroll()) {
            var requiredItems = new List<ItemComponent>();
            foreach (ItemExchangeScroll.Item item in scroll.require.item) {
                string[] idAndTag = item.id[0].Split(":");
                int requiredItemId = int.Parse(idAndTag[0]);
                string requiredItemTag = idAndTag.Length > 1 ? idAndTag[1] : string.Empty;
                if (!short.TryParse(item.id[1], out short rarity)) {
                    rarity = 1;
                }
                if (!int.TryParse(item.id[2], out int amount)) {
                    amount = 1;
                }
                requiredItems.Add(new ItemComponent(
                    ItemId: requiredItemId,
                    Tag: string.IsNullOrWhiteSpace(requiredItemTag) ? ItemTag.None : Enum.Parse<ItemTag>(requiredItemTag),
                    Rarity: rarity,
                    Amount: amount));
            }

            results.Add(id, new ItemExchangeScrollMetadata(
                RecipeScroll: new ItemComponent(
                    ItemId: scroll.receipe.id,
                    Rarity: (short) scroll.receipe.rank,
                    Amount: scroll.receipe.count,
                    Tag: ItemTag.None),
                RewardItem: new ItemComponent(
                    ItemId: scroll.exchange.id,
                    Rarity: (short) scroll.exchange.rank,
                    Amount: scroll.exchange.count,
                    Tag: ItemTag.None),
                TradeCountDeduction: scroll.tradableCountDeduction,
                RequiredMeso: scroll.require.meso,
                RequiredItems: requiredItems));
        }
        return new ItemExchangeScrollTable(results);
    }

    private PremiumClubTable ParsePremiumClubTable() {
        var premiumClubBuffs = new Dictionary<int, PremiumClubTable.Buff>();
        foreach ((int id, PremiumClubEffect buff) in parser.ParsePremiumClubEffect()) {
            premiumClubBuffs.Add(id, new PremiumClubTable.Buff(
                Id: buff.effectID,
                Level: buff.effectLevel));
        }

        var premiumClubItems = new Dictionary<int, PremiumClubTable.Item>();
        foreach ((int id, PremiumClubItem item) in parser.ParsePremiumClubItem()) {
            premiumClubItems.Add(id, new PremiumClubTable.Item(
                Id: item.itemID,
                Amount: item.itemCount,
                Rarity: item.itemRank,
                Period: 0));
        }

        var premiumClubPackages = new Dictionary<int, PremiumClubTable.Package>();
        foreach ((int id, PremiumClubPackage package) in parser.ParsePremiumClubPackage()) {
            DateTime startTime = DateTime.TryParseExact(package.salesStartDate, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out startTime) ? startTime : DateTime.MinValue;
            DateTime endTime = DateTime.TryParseExact(package.salesEndDate, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out endTime) ? endTime : DateTime.MinValue;
            var items = new List<PremiumClubTable.Item>();
            for (int item = 0; item < package.bonusItemID.Length; item++) {
                items.Add(new PremiumClubTable.Item(
                    Id: package.bonusItemID[item],
                    Amount: package.bonusItemCount[item],
                    Rarity: package.bonusItemRank[item],
                    Period: package.bonusItemPeriod[item]));
            }
            premiumClubPackages.Add(id, new PremiumClubTable.Package(
                Disabled: package.disable,
                StartDate: startTime.ToEpochSeconds(),
                EndDate: endTime.ToEpochSeconds(),
                Period: package.vipPeriod,
                Price: package.price < package.salePrice ? package.salePrice : package.price,
                BonusItems: items));
        }

        return new PremiumClubTable(premiumClubBuffs, premiumClubItems, premiumClubPackages);
    }

    private IndividualItemDropTable ParseIndividualItemDropTable() {
        var results = new Dictionary<int, Dictionary<byte, IList<IndividualItemDropTable.Entry>>>();
        results = MergeIndividualItemDropTable(results, parser.ParseIndividualItemDrop());
        results = MergeIndividualItemDropTable(results, parser.ParseIndividualItemDropCharge());
        results = MergeIndividualItemDropTable(results, parser.ParseIndividualItemDropEvent());
        results = MergeIndividualItemDropTable(results, parser.ParseIndividualItemDropGacha());
        results = MergeIndividualItemDropTable(results, parser.ParseIndividualItemDropPet());
        results = MergeIndividualItemDropTable(results, parser.ParseIndividualItemGearBox());
        results = MergeIndividualItemDropTable(results, parser.ParseIndividualItemDropEventNpc());
        results = MergeIndividualItemDropTable(results, parser.ParseIndividualItemDropNewGacha());
        results = MergeIndividualItemDropTable(results, parser.ParseIndividualItemDropQuestMob());
        results = MergeIndividualItemDropTable(results, parser.ParseIndividualItemDropQuestObj());

        return new IndividualItemDropTable(results);
    }

    private Dictionary<int, Dictionary<byte, IList<IndividualItemDropTable.Entry>>> MergeIndividualItemDropTable(Dictionary<int, Dictionary<byte, IList<IndividualItemDropTable.Entry>>> results, IEnumerable<(int Id, IDictionary<byte, List<IndividualItemDrop>>)> parser) {
        foreach ((int id, IDictionary<byte, List<IndividualItemDrop>> dict) in parser) {
            foreach ((byte dropGroup, List<IndividualItemDrop> drops) in dict) {
                foreach (IndividualItemDrop drop in drops) {
                    var itemIds = new List<int> {
                        drop.item,
                    };
                    if (drop.item2 > 0) {
                        itemIds.Add(drop.item2);
                    }

                    float minCount = drop.minCount;
                    float maxCount = drop.maxCount;
                    if (drop.item == 90000008) { // Experience Orb
                        minCount *= 10000;
                        maxCount *= 10000;
                    }

                    var entry = new IndividualItemDropTable.Entry(
                        ItemIds: itemIds.ToArray(),
                        SmartGender: drop.isApplySmartGenderDrop,
                        SmartDropRate: drop.smartDropRate,
                        Rarity: drop.PackageUIShowGrade,
                        EnchantLevel: drop.enchantLevel,
                        ReduceTradeCount: drop.tradableCountDeduction,
                        ReduceRepackLimit: drop.rePackingLimitCountDeduction,
                        Bind: drop.isBindCharacter,
                        MinCount: (int) minCount,
                        MaxCount: (int) maxCount);

                    if (!results.ContainsKey(id)) {
                        results.Add(id, new Dictionary<byte, IList<IndividualItemDropTable.Entry>> {
                            {drop.dropGroup, new List<IndividualItemDropTable.Entry> {
                                entry,
                            }},
                        });
                    } else if (!results[id].ContainsKey(dropGroup)) {
                        results[id].Add(drop.dropGroup, new List<IndividualItemDropTable.Entry>() {
                            entry,
                        });
                    } else {
                        results[id][dropGroup].Add(entry);
                    }
                }
            }
        }
        return results;
    }

    private ColorPaletteTable ParseColorPaletteTable() {
        var results = new Dictionary<int, IReadOnlyDictionary<int, ColorPaletteTable.Entry>>();
        foreach ((int id, ColorPalette palette) in parser.ParseColorPalette()) {
            foreach (ColorPalette.Color color in palette.color) {
                var entry = new ColorPaletteTable.Entry(
                    Primary: ParseColor(color.ch0),
                    Secondary: ParseColor(color.ch1),
                    Tertiary: ParseColor(color.ch2),
                    AchieveId: color.achieveID,
                    AchieveGrade: color.achieveGrade);
                if (!results.ContainsKey(id)) {
                    results.Add(id, new Dictionary<int, ColorPaletteTable.Entry> {
                        {color.colorSN, entry},
                    });
                } else {
                    (results[id] as Dictionary<int, ColorPaletteTable.Entry>)!.Add(color.colorSN, entry);
                }
            }
        }
        return new ColorPaletteTable(results);
    }

    private Color ParseColor(System.Drawing.Color color) {
        return new Color(color.B, color.G, color.R, color.A);
    }

    private MeretMarketCategoryTable ParseMeretMarketCategoryTable() {
        var results = new Dictionary<int, IReadOnlyDictionary<int, MeretMarketCategoryTable.Tab>>();
        foreach ((int id, MeretMarketCategory category) in parser.ParseMeretMarketCategory()) {
            foreach (MeretMarketCategory.Tab tab in category.tab) {
                var subTabIds = new List<int>();
                foreach (MeretMarketCategory.Tab subTab in tab.tab) {
                    var subTabEntry = new MeretMarketCategoryTable.Tab(
                        Categories: subTab.category,
                        SortGender: subTab.sortGender,
                        SortJob: subTab.sortJob,
                        SubTabIds: []);
                    subTabIds.Add(subTab.id);
                    if (!results.ContainsKey(id)) {
                        results.Add(id, new Dictionary<int, MeretMarketCategoryTable.Tab> {
                            {subTab.id, subTabEntry},
                        });
                    } else {
                        (results[id] as Dictionary<int, MeretMarketCategoryTable.Tab>)!.Add(subTab.id, subTabEntry);
                    }
                }
                var tabEntry = new MeretMarketCategoryTable.Tab(
                    Categories: tab.category,
                    SortGender: tab.sortGender,
                    SortJob: tab.sortJob,
                    SubTabIds: subTabIds.ToArray());

                if (!results.ContainsKey(id)) {
                    results.Add(id, new Dictionary<int, MeretMarketCategoryTable.Tab> {
                        {tab.id, tabEntry},
                    });
                } else {
                    (results[id] as Dictionary<int, MeretMarketCategoryTable.Tab>)!.Add(tab.id, tabEntry);
                }
            }
        }
        return new MeretMarketCategoryTable(results);
    }

    private ShopBeautyCouponTable ParseShopBeautyCouponTable() {
        var results = new Dictionary<int, IReadOnlyList<int>>();
        foreach ((int id, ShopBeautyCoupon coupon) in parser.ParseShopBeautyCoupon()) {
            results.Add(id, new List<int>(coupon.item.Select(item => item.id)));
        }

        return new ShopBeautyCouponTable(results);
    }
    private GachaInfoTable ParseGachaInfoTable() {
        var results = new Dictionary<int, GachaInfoTable.Entry>();
        foreach ((int randomBoxId, GachaInfo gachaInfo) in parser.ParseGachaInfo()) {
            results.Add(randomBoxId, new GachaInfoTable.Entry(
                RandomBoxGroup: gachaInfo.randomBoxGroup,
                DropBoxId: gachaInfo.individualDropBoxID,
                ShopId: gachaInfo.shopID,
                CoinItemId: gachaInfo.coinItemID,
                CoinItemAmount: gachaInfo.coinItemAmount));
        }

        return new GachaInfoTable(results);
    }

    private FurnishingShopTable ParseFurnishingShopTable() {
        var results = new Dictionary<int, FurnishingShopTable.Entry>();
        foreach ((int id, ShopFurnishing shop) in parser.ParseFurnishingShopUgcAll().Concat(parser.ParseFurnishingShopMaid())) {
            results.Add(id, new FurnishingShopTable.Entry(
                ItemId: shop!.id,
                Buyable: shop.ugcHousingBuy,
                FurnishingTokenType: (FurnishingCurrencyType) shop.ugcHousingMoneyType,
                Price: shop.ugcHousingDefaultPrice
            ));
        }

        return new FurnishingShopTable(results);
    }

    private InsigniaTable ParseInsigniaTable() {
        var results = new Dictionary<int, InsigniaTable.Entry>();
        foreach ((int id, NameTagSymbol symbol) in parser.ParseNameTagSymbol()) {
            results.Add(id, new InsigniaTable.Entry(
                Type: (InsigniaConditionType) symbol.conditionType,
                Code: symbol.code,
                BuffId: symbol.buffID,
                BuffLevel: symbol.buffLv));
        }

        return new InsigniaTable(results);
    }

    private ExpTable ParseExpTable() {
        var baseResults = new Dictionary<int, IReadOnlyDictionary<int, long>>();
        foreach ((int tableId, ExpBaseTable table) in parser.ParseExpBaseTable()) {
            foreach (ExpBaseTable.Base tableBase in table.@base) {
                if (!baseResults.ContainsKey(tableId)) {
                    baseResults.Add(tableId, new Dictionary<int, long>{
                            {tableBase.level, tableBase.exp},
                    });
                } else {
                    (baseResults[tableId] as Dictionary<int, long>)!.Add(tableBase.level, tableBase.exp);
                }
            }
        }

        var nextExpResults = new Dictionary<int, long>();
        foreach ((int level, NextExp entry) in parser.ParseNextExp()) {
            nextExpResults.Add(entry.level, entry.value);
        }
        return new ExpTable(baseResults, nextExpResults);
    }

    private CommonExpTable ParseCommonExpTable() {
        var results = new Dictionary<ExpType, CommonExpTable.Entry>();
        foreach ((CommonExpType type, CommonExp exp) in parser.ParseCommonExp()) {
            results.Add(ToExpType(type), new CommonExpTable.Entry(ExpTableId: exp.expTableID, Factor: exp.factor));
        }
        return new CommonExpTable(results);
    }

    private static ExpType ToExpType(CommonExpType commonExpType) {
        if (Enum.TryParse(commonExpType.ToString(), out ExpType expType)) {
            return expType;
        }
        return ExpType.none;
    }

    private UgcDesignTable ParseUgcDesignTable() {
        var results = new Dictionary<int, UgcDesignTable.Entry>();
        foreach ((int id, UgcDesign design) in parser.ParseUgcDesign()) {
            results.Add(id, new UgcDesignTable.Entry(
                ItemRarity: design.itemGrade,
                CurrencyType: (MeretMarketCurrencyType) design.priceType,
                CreatePrice: design.salePrice < design.price ? design.salePrice : design.price,
                MarketMinPrice: design.marketMinPrice,
                MarketMaxPrice: design.marketMaxPrice));
        }
        return new UgcDesignTable(results);
    }

    private LearningQuestTable ParseLearningQuestTable() {
        var results = new Dictionary<int, LearningQuestTable.Entry>();
        foreach ((int id, LearningQuest quest) in parser.ParseLearningQuest()) {
            results.Add(id, new LearningQuestTable.Entry(
                Category: quest.category,
                RequiredLevel: quest.reqLevel,
                QuestId: quest.reqQuest,
                RequiredMapId: quest.reqField,
                GoToMapId: quest.gotoField,
                GoToPortalId: quest.gotoPortal));
        }
        return new LearningQuestTable(results);
    }

    private PrestigeLevelAbilityTable ParsePrestigeLevelAbilityTable() {
        var results = new Dictionary<int, PrestigeLevelAbilityMetadata>();
        foreach ((int id, AdventureLevelAbility ability) in parser.ParseAdventureLevelAbility()) {
            results.Add(id, new PrestigeLevelAbilityMetadata(
                Id: id,
                RequiredLevel: ability.requireLevel,
                Interval: ability.interval,
                MaxCount: ability.maxCount,
                BuffId: ability.additionalEffectId,
                StartValue: ability.startValue,
                AddValue: ability.addValue));
        }
        return new PrestigeLevelAbilityTable(results);
    }

    private PrestigeLevelRewardTable ParsePrestigeLevelRewardTable() {
        var results = new Dictionary<int, PrestigeLevelRewardMetadata>();
        foreach ((int id, AdventureLevelReward reward) in parser.ParseAdventureLevelReward()) {
            results.Add(id, new PrestigeLevelRewardMetadata(
                Id: reward.id,
                Level: reward.level,
                Type: Enum.TryParse(reward.type, out PrestigeAwardType type) ? type : PrestigeAwardType.none,
                Rarity: reward.rank,
                Value: reward.value
                ));
        }
        return new PrestigeLevelRewardTable(results);
    }

    private PrestigeMissionTable ParsePrestigeMissionTable() {
        var results = new Dictionary<int, PrestigeMissionMetadata>();
        foreach ((int id, AdventureLevelMission mission) in parser.ParseAdventureLevelMission()) {
            results.Add(id, new PrestigeMissionMetadata(
                Id: mission.missionId,
                Count: mission.missionCount,
                Item: new ItemComponent(
                    ItemId: mission.itemId,
                    Rarity: mission.itemRank,
                    mission.itemCount,
                    Tag: ItemTag.None)));
        }
        return new PrestigeMissionTable(results);
    }

    private BlackMarketTable ParseBlackMarketTable() {
        var results = new Dictionary<int, string[]>();
        (int id, BlackMarketCategory blackMarket) category = parser.ParseBlackMarketCategory();
        foreach (BlackMarketCategory.BlackMarketTab item in category.blackMarket.tab) {
            ParseBlackMarketTab(item, results);
        }

        return new BlackMarketTable(results);
    }

    private void ParseBlackMarketTab(BlackMarketCategory.BlackMarketTab tab, Dictionary<int, string[]> results) {
        results.Add(tab.id, tab.category);
        if (tab.tab.Count > 0) {
            foreach (BlackMarketCategory.BlackMarketTab subTab in tab.tab) {
                ParseBlackMarketTab(subTab, results);
            }
        }
    }

    private ChangeJobTable ParseChangeJobTable() {
        var results = new Dictionary<Job, ChangeJobMetadata>();
        foreach ((int jobId, ChangeJob job) in parser.ParseChangeJob()) {
            results.Add((Job) jobId, new ChangeJobMetadata(
                Job: (Job) job.subJobCode,
                ChangeJob: (Job) job.changeSubJobCode,
                StartQuestId: job.startquestid,
                EndQuestId: job.endquestid
            ));
        }
        return new ChangeJobTable(results);
    }

    private ChapterBookTable ParseChapterBookTable() {
        var results = new Dictionary<int, ChapterBookTable.Entry>();
        foreach ((int id, ChapterBook book) in parser.ParseChapterBook()) {
            var items = new List<ItemComponent>();
            var skillpoints = new List<ChapterBookTable.Entry.SkillPoint>();
            int statPoints = 0;
            switch (book.rewardType1) {
                case QuestRewardType.skillPoint:
                    skillpoints.Add(ParseSkillPoint(book.rewardValue1));
                    break;
                case QuestRewardType.item:
                    items.Add(ParseItem(book.rewardValue1));
                    break;
                case QuestRewardType.statPoint:
                    statPoints += int.Parse(book.rewardValue1[0]);
                    break;
                default:
                    break;
            }

            switch (book.rewardType2) {
                case QuestRewardType.skillPoint:
                    skillpoints.Add(ParseSkillPoint(book.rewardValue2));
                    break;
                case QuestRewardType.item:
                    items.Add(ParseItem(book.rewardValue2));
                    break;
                case QuestRewardType.statPoint:
                    statPoints += int.Parse(book.rewardValue2[0]);
                    break;
                default:
                    break;
            }
            results.Add(id, new ChapterBookTable.Entry(
                Id: id,
                BeginQuestId: book.prologue,
                EndQuestId: book.epilogue,
                SkillPoints: skillpoints.ToArray(),
                StatPoints: statPoints,
                Items: items.ToArray()));
        }

        return new ChapterBookTable(results);

        ChapterBookTable.Entry.SkillPoint ParseSkillPoint(string[] rewardValue) {
            return new ChapterBookTable.Entry.SkillPoint(
                Amount: int.Parse(rewardValue[0]),
                Rank: short.Parse(rewardValue[1]));
        }

        ItemComponent ParseItem(string[] rewardValue) {
            return new ItemComponent(
                ItemId: int.Parse(rewardValue[0]),
                Amount: short.Parse(rewardValue[1]),
                Rarity: int.Parse(rewardValue[2]),
                Tag: ItemTag.None);
        }
    }

    private FieldMissionTable ParseFieldMissionTable() {
        var results = new Dictionary<int, FieldMissionTable.Entry>();
        foreach ((int id, FieldMission mission) in parser.ParseFieldMission()) {
            switch (mission.type) {
                case QuestRewardType.item:
                    results.Add(id, new FieldMissionTable.Entry(
                        MissionCount: mission.mission,
                        StatPoints: 0,
                        Item: new ItemComponent(
                            ItemId: mission.value[0],
                            Rarity: mission.value[1],
                            Amount: mission.value[2],
                            Tag: ItemTag.None)));
                    continue;
                case QuestRewardType.statPoint:
                    results.Add(id, new FieldMissionTable.Entry(
                        MissionCount: mission.mission,
                        StatPoints: mission.value[0],
                        Item: null));
                    continue;
            }
        }
        return new FieldMissionTable(results);
    }

    private WorldMapTable ParseWorldMapTable() {
        var mapList = new List<WorldMapTable.Map>();
        foreach ((string feature, var maps) in parser.ParseWorldMap()) {
            if (feature != "Kritias_2018_12") {
                continue;
            }
            foreach (var map in maps) {
                if (!map.@public) {
                    continue;
                }

                mapList.Add(new WorldMapTable.Map(map.code, map.x, map.y, map.z, map.size));
            }
        }

        if (mapList.Count == 0) {
            throw new InvalidOperationException("No maps ingested for WorldMapTable");
        }
        return new WorldMapTable(mapList);
    }

    private SurvivalSkinInfoTable ParseSurvivalSkinTable() {
        var results = new Dictionary<int, MedalType>();
        foreach ((int id, MapleSurvivalSkinInfo skin) in parser.ParseMapleSurvivalSkinInfo()) {
            MedalType type = skin.type switch {
                SurvivalSkinType.gliding => MedalType.Gliding,
                SurvivalSkinType.riding => MedalType.Riding,
                SurvivalSkinType.effectTail => MedalType.Tail,
                _ => throw new InvalidOperationException("Unknown SurvivalSkinType"),
            };
            results.Add(id, type);
        }

        return new SurvivalSkinInfoTable(results);
    }

    private BannerTable ParseBanner() {
        List<BannerTable.Entry> results = [];
        foreach ((int id, Banner banner) in parser.ParseBanner()) {
            results.Add(new BannerTable.Entry(
                Id: id,
                MapId: banner.field,
                Price: banner.price.ToList()
                ));
        }
        return new BannerTable(results);
    }

    private MasteryUgcHousingTable ParseMasteryUgcHousingTable() {
        var results = new Dictionary<int, MasteryUgcHousingTable.Entry>();
        foreach ((int id, MasteryUgcHousing ugcHousing) in parser.ParseMasteryUgcHousing()) {
            results.Add(id, new MasteryUgcHousingTable.Entry(
                Level: ugcHousing.grade,
                Exp: ugcHousing.value,
                RewardJobItemId: ugcHousing.rewardJobItemID));
        }
        return new MasteryUgcHousingTable(results);
    }

    private UgcHousingPointRewardTable ParseUgcHousingPointRewardTable() {
        var results = new Dictionary<int, UgcHousingPointRewardTable.Entry>();
        foreach ((int id, UgcHousingPointReward ugcHousing) in parser.ParseUgcHousingPointReward()) {
            results.Add(id, new UgcHousingPointRewardTable.Entry(
                DecorationScore: ugcHousing.housingPoint,
                IndividualDropBoxId: ugcHousing.individualDropBoxId));
        }
        return new UgcHousingPointRewardTable(results);
    }

    private WeddingTable ParseWeddingTable() {
        var rewardsResults = new Dictionary<MarriageExpType, WeddingReward>();
        foreach ((WeddingRewardType type, Parser.Xml.Table.WeddingReward reward) in parser.ParseWeddingReward()) {
            rewardsResults.Add((MarriageExpType) type, new WeddingReward(
                Type: (MarriageExpType) type,
                Amount: reward.rewardExp,
                Limit: (MarriageExpLimit) reward.rewardLimit));
        }

        var packageResults = new Dictionary<int, WeddingPackage>();
        foreach ((int id, Parser.Xml.Table.WeddingPackage package) in parser.ParseWeddingPackage()) {
            var hallDataDic = new Dictionary<int, WeddingPackage.HallData>();
            foreach (WeddingHall hall in package.weddingHall) {
                List<WeddingPackage.HallData.Item> hallItems = [];
                foreach (WeddingItem item in hall.weddingItem) {
                    hallItems.Add(new WeddingPackage.HallData.Item(
                        ItemId: item.itemID,
                        Amount: item.count,
                        Rarity: item.grade,
                        NightOnly: item.nightReward));
                }

                List<WeddingPackage.HallData.Item> completeHallItems = [];
                foreach (WeddingItem item in hall.weddingCompleteItem) {
                    completeHallItems.Add(new WeddingPackage.HallData.Item(
                        ItemId: item.itemID,
                        Amount: item.count,
                        Rarity: item.grade,
                        NightOnly: item.nightReward));
                }

                hallDataDic.Add(hall.id, new WeddingPackage.HallData(
                    Id: hall.id,
                    MapId: hall.fieldID,
                    NightMapId: hall.nightFieldID,
                    Tier: hall.grade,
                    MeretCost: hall.merat,
                    Items: hallItems,
                    CompleteItems: completeHallItems));
            }
            packageResults.Add(id, new WeddingPackage(
                Id: id,
                PlannerId: package.planner,
                Halls: hallDataDic));
        }
        return new WeddingTable(rewardsResults, packageResults);
    }

    private DungeonRoomTable ParseDungeonRoom() {
        var dungeons = new Dictionary<int, DungeonRoomMetadata>();
        foreach ((int id, DungeonRoom dungeon) in parser.ParseDungeonRoom()) {
            dungeons.Add(id, new DungeonRoomMetadata(
                Id: dungeon.dungeonRoomID,
                Level: dungeon.dungeonLevel,
                PlayType: (DungeonPlayType) dungeon.playType,
                GroupType: (DungeonGroupType) dungeon.groupType,
                CooldownType: (DungeonCooldownType) dungeon.cooldownType,
                CooldownValue: dungeon.cooldownType == Parser.Enum.DungeonCooldownType.dayOfWeeks ? dungeon.cooldownValue + 1 : dungeon.cooldownValue, // dayOfWeeks is 0-indexed
                DurationTick: dungeon.durationTick,
                LobbyFieldId: dungeon.lobbyFieldID,
                FieldIds: dungeon.fieldIDs,
                Reward: new DungeonRoomRewardMetadata(
                    AccountWide: dungeon.isAccountReward,
                    Count: dungeon.rewardCount,
                    SubRewardCount: dungeon.subRewardCount,
                    Exp: dungeon.rewardExp,
                    ExpRate: dungeon.rewardExpRate,
                    Meso: dungeon.rewardMeso,
                    LimitedDropBoxIds: dungeon.rewardLimitedDropBoxIds,
                    UnlimitedDropBoxIds: dungeon.rewardUnlimitedDropBoxIds,
                    UnionRewardId: dungeon.unionRewardID,
                    SeasonRankRewardId: dungeon.seasonRankRewardID,
                    ScoreBonusId: dungeon.scoreBonusId),
                Limit: new DungeonRoomLimitMetadata(
                    MinUserCount: dungeon.minUserCount,
                    MaxUserCount: dungeon.maxUserCount,
                    GearScore: dungeon.gearScore,
                    MinLevel: dungeon.limitPlayerLevel,
                    RequiredAchievementId: dungeon.limitAchieveID,
                    VipOnly: dungeon.limitVIP,
                    DayOfWeeks: dungeon.limitDayOfWeeks.Length == 0 ? [] : dungeon.limitDayOfWeeks.Select(ParseDayOfWeek).ToArray(),
                    ClearDungeonIds: dungeon.limitClearDungeon,
                    Buffs: dungeon.limitAdditionalEffects,
                    DisableMeretRevival: dungeon.limitMeratRevival,
                    EquippedRecommendedWeapon: dungeon.limitRecommendWeapon,
                    PartyOnly: dungeon.isPartyOnly,
                    ChangeMaxUsers: dungeon.isChangeMaxUser,
                    DisableMesoRevival: dungeon.limitMesoRevival,
                    MaxRevivalCount: dungeon.defaultRevivalLimitCount),
                PlayerCountFactorId: dungeon.playerCountFactorID,
                CustomMonsterLevel: dungeon.customMonsterLevel,
                HelperRequireClearCount: dungeon.dungeonHelperRequireClearCount,
                DisabledFindHelper: dungeon.isDisableFindHelper,
                RankTableId: dungeon.rankTableID,
                RoundId: dungeon.roundID,
                LeaveAfterCloseReward: dungeon.isLeaveAfterCloseReward,
                PartyMissions: dungeon.partyMissions,
                UserMissions: dungeon.userMissions,
                MoveToBackupField: dungeon.isMoveOutToBackupField
                ));
        }

        return new DungeonRoomTable(dungeons);

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

    private DungeonRankRewardTable ParseDungeonRankReward() {
        var results = new Dictionary<int, DungeonRankRewardTable.Entry>();
        foreach ((int id, DungeonRankReward reward) in parser.ParseDungeonRankReward()) {
            List<DungeonRankRewardTable.Entry.Item> rewards = [];
            foreach (DungeonRankRewardEntry item in reward.v) {
                rewards.Add(new DungeonRankRewardTable.Entry.Item(
                    Rank: item.rank,
                    ItemId: item.itemID,
                    SystemMailId: item.systemMailID));
            }

            results.Add(id, new DungeonRankRewardTable.Entry(
                Id: id,
                Items: rewards.ToArray()));
        }

        return new DungeonRankRewardTable(results);
    }

    private DungeonConfigTable ParseDungeonConfigTable() {
        var missionRankResults = new Dictionary<int, DungeonMissionRankMetadata>();
        foreach (DungeonConfig config in parser.ParseDungeonConfig()) {
            MissionRank missionRank = config.MissionRank.First();
            foreach (MissionRankGroup group in missionRank.group) {
                var scores = new List<DungeonMissionRankMetadata.Score>();
                for (int i = 0; i < group.rank.Count; i++) {
                    scores.Add(new DungeonMissionRankMetadata.Score(
                        Grade: (DungeonMissionRank) (i + 1), // Ranking start at C
                        Value: group.rank[i].score));
                }
                missionRankResults.Add(group.id, new DungeonMissionRankMetadata(
                    Id: group.id,
                    Description: group.desc,
                    MaxScore: group.maxScore,
                    Scores: scores.ToArray()));
            }
            break; // Break because there should only be one entry.
        }

        var unitedWeeklyResults = new Dictionary<int, int>();
        UnitedWeeklyReward reward = parser.ParseUnitedWeeklyReward().First();
        foreach (UnitedWeeklyRewardEntry item in reward.v) {
            unitedWeeklyResults.Add(item.rewardCount, item.rewardID);
        }

        return new DungeonConfigTable(unitedWeeklyResults, missionRankResults);
    }

    private DungeonMissionTable ParseDungeonMissionTable() {
        var results = new Dictionary<int, DungeonMissionMetadata>();
        foreach ((int id, DungeonMission mission) in parser.ParseDungeonMission()) {
            if (!Enum.TryParse(mission.type, out DungeonMissionType type)) {
                Console.WriteLine($"Unknown Mission type: {mission.type}");
                continue;
            }
            results.Add(id, new DungeonMissionMetadata(
                Id: id,
                Type: type,
                Value1: Array.ConvertAll(mission.value1, element => (long) element),
                Value2: mission.value2,
                MaxScore: (short) mission.maxScore,
                ApplyCount: (short) mission.applyCount,
                IsPenaltyType: mission.isPenaltyType));
        }
        return new DungeonMissionTable(results);
    }

    private RewardContentTable ParseRewardContentTable() {
        var baseResults = new Dictionary<int, RewardContentTable.Base>();
        foreach ((int id, RewardContent rewardContent) in parser.ParseRewardContent()) {
            baseResults.Add(id, new RewardContentTable.Base(
                Id: id,
                ExpTableId: rewardContent.expTableID,
                MesoTableId: rewardContent.mesoTableID,
                ExpFactor: rewardContent.expFactor,
                MesoFactor: rewardContent.mesoFactor,
                ItemTableId: rewardContent.itemTableID,
                PrestigeExpTableId: rewardContent.adventureExpTableID));
        }

        var itemResults = new Dictionary<int, RewardContentTable.Item>();
        foreach ((int id, RewardContentItem content) in parser.ParseRewardContentItem()) {
            List<RewardContentTable.Item.Data> itemData = [];
            foreach (RewardContentValue value in content.v) {
                List<RewardItem> items = [];
                foreach (RewardContentItemEntry item in value.item) {
                    items.Add(new RewardItem(item.itemID, (short) item.grade, item.count));
                }
                itemData.Add(new RewardContentTable.Item.Data(
                    MinLevel: value.minLevel,
                    MaxLevel: value.maxLevel,
                    RewardItems: items.ToArray()));
            }
            itemResults.Add(id, new RewardContentTable.Item(
                Id: id,
                ItemData: itemData.ToArray()));
        }

        var mesoStaticResults = new Dictionary<int, long>();
        foreach ((int id, RewardContentMesoStatic reward) in parser.ParseRewardContentMesoStatic()) {
            mesoStaticResults.Add(id, reward.v.FirstOrDefault()?.meso ?? 0);
        }

        var mesoResults = new Dictionary<int, Dictionary<int, long>>();
        foreach ((int id, RewardContentMeso reward) in parser.ParseRewardContentMeso()) {
            var entries = new Dictionary<int, long>();
            foreach (RewardContentMesoValue value in reward.v) {
                entries.Add(value.level, value.meso);
            }

            mesoResults.Add(id, entries);
        }

        var expStaticResults = new Dictionary<int, long>();
        foreach ((int id, RewardContentExpStatic reward) in parser.ParseRewardContentExpStatic()) {
            expStaticResults.Add(id, reward.@base.FirstOrDefault()?.exp ?? 0);
        }

        return new RewardContentTable(baseResults, itemResults, mesoStaticResults, mesoResults, expStaticResults);
    }

    private SeasonDataTable ParseSeasonDataTable() {
        return new SeasonDataTable(
            Arcade: ParseSeasonData(parser.ParseSeasonDataArcade()),
            Boss: ParseSeasonData(parser.ParseSeasonDataBossColosseum()),
            DarkDescent: ParseSeasonData(parser.ParseSeasonDataDarkStream()),
            GuildPvp: ParseSeasonData(parser.ParseSeasonDataGuildPvp()),
            Survival: ParseSeasonData(parser.ParseSeasonDataMapleSurvival()),
            SurvivalSquad: ParseSeasonData(parser.ParseSeasonDataMapleSurvivalSquad()),
            Pvp: ParseSeasonData(parser.ParseSeasonDataPvp()),
            UgcMapCommendation: ParseSeasonData(parser.ParseSeasonDataUgcMapCommendation()),
            WorldChampionship: ParseSeasonData(parser.ParseSeasonDataWorldChampion()));

        IReadOnlyDictionary<int, SeasonDataTable.Entry> ParseSeasonData(IEnumerable<(int, SeasonData)> seasonDataParser) {
            var results = new Dictionary<int, SeasonDataTable.Entry>();

            foreach ((int id, SeasonData seasonData) in seasonDataParser) {
                results.Add(id, new SeasonDataTable.Entry(
                    Id: seasonData.seasonID,
                    StartTime: DateTime.TryParseExact(seasonData.eventStart, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime startTime) ? startTime : DateTime.MinValue,
                    EndTime: DateTime.TryParseExact(seasonData.eventEnd, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime endTime) ? endTime : DateTime.MaxValue,
                    Grades: [
                        seasonData.grade1,
                        seasonData.grade2,
                        seasonData.grade3,
                        seasonData.grade4,
                        seasonData.grade5,
                        seasonData.grade6,
                        seasonData.grade7,
                    ]));
            }
            return results;
        }
    }

    private SmartPushTable ParseSmartPushTable() {
        var results = new Dictionary<int, SmartPushMetadata>();
        foreach ((int id, SmartPush smartPush) in parser.ParseSmartPush()) {
            var requiredItem = new IngredientInfo(ItemTag.None, 0);
            var requiredItemTag = ItemTag.None;
            if (smartPush.requireItem.Length != 0) {
                string[] requiredItemArray = smartPush.requireItem[0].Split(":");
                if (requiredItemArray.Length == 2) {
                    requiredItemTag = Enum.TryParse(requiredItemArray[1], out ItemTag tag) ? tag : ItemTag.None;
                }
                requiredItem = new IngredientInfo(
                    tag: requiredItemTag,
                    amount: int.Parse(smartPush.requireItem[2]));
            }

            results.Add(id, new SmartPushMetadata(
                Id: id,
                Content: smartPush.content,
                Type: Enum.TryParse(smartPush.actionType, out SmartPushType type) ? type : SmartPushType.none,
                Value: smartPush.actionValue,
                MeretCost: smartPush.requireMerat,
                RequiredItem: requiredItem));
        }
        return new SmartPushTable(results);
    }

    private AutoActionTable ParseAutoActionTable() {
        var results = new Dictionary<string, IReadOnlyDictionary<int, AutoActionMetaData>>();
        IEnumerable<IGrouping<string, AutoActionPricePackage>> groups = parser.ParseAutoActionPricePackage()
            .Select(entry => entry.Data)
            .GroupBy(entry => entry.content);
        foreach (IGrouping<string, AutoActionPricePackage> group in groups) {
            var packages = new Dictionary<int, AutoActionMetaData>();
            foreach (AutoActionPricePackage package in group) {
                packages.Add(package.id, new AutoActionMetaData(
                    Content: package.content,
                    Id: package.id,
                    Duration: package.duration,
                    MeretCost: package.merat,
                    MesoCost: package.meso));
            }
            results.Add(group.Key, packages);
        }
        return new AutoActionTable(results);
    }
}
