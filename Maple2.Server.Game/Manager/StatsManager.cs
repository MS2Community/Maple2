using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Formulas;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Serilog;

namespace Maple2.Server.Game.Manager;

public class StatsManager {
    public IActor Actor { get; private set; }
    private readonly IReadOnlyDictionary<short, IReadOnlyDictionary<BasicAttribute, long>> levelStats;

    public Stats Values { get; init; }

    public StatsManager(IActor actor, UserStatTable userStatsMetadata) {
        Actor = actor;
        levelStats = new Dictionary<short, IReadOnlyDictionary<BasicAttribute, long>>();

        if (actor is not FieldPlayer player) {
            Values = new Stats(new Dictionary<BasicAttribute, long>(), 0);
            return;
        }
        if (!userStatsMetadata.JobStats.TryGetValue(player.Value.Character.Job.Code(), out IReadOnlyDictionary<short, IReadOnlyDictionary<BasicAttribute, long>>? stats)) {
            throw new Exception("Failed to initialize StatsManager, could not find metadata for job " + player.Value.Character.Job.Code() + ".");
        }

        levelStats = stats;
        if (!stats.TryGetValue(player.Value.Character.Level, out IReadOnlyDictionary<BasicAttribute, long>? metadata)) {
            throw new Exception("Failed to initialize StatsManager, could not find metadata for level " + player.Value.Character.Level + ".");
        }
        Values = new Stats(metadata, player.Value.Character.Job.Code());
    }

    public void SetBattleMountStats(IReadOnlyDictionary<BasicAttribute, long> stats) {
        Values.SetStaticStats(stats);
    }

    public StatsManager(IActor actor) {
        Actor = actor;
        levelStats = new Dictionary<short, IReadOnlyDictionary<BasicAttribute, long>>();

        if (actor is FieldNpc npc) {
            Values = new Stats(npc.Value.Metadata.Stat);
            return;
        }

        Values = new Stats(new Dictionary<BasicAttribute, long>(), 0);
    }

    /// <summary>
    /// Get the actor's bonus attack min and max.
    /// </summary>
    /// <param name="targetBonusAttackResistance">Target's total Bonus Attack resistance</param>
    /// <param name="targetMaxWeaponAttackResistance">Target's total Max Weapon Attack resistance</param>
    /// <returns>Tuple. Item1 = Min Damage. Item2 = Max Damage.</returns>
    public (double, double) GetBonusAttack(float targetBonusAttackResistance, float targetMaxWeaponAttackResistance) {
        switch (Actor) {
            case FieldPlayer player:
                double bonusAttack = Values[BasicAttribute.BonusAtk].Total + Constant.PetAttackMultiplier * Values[BasicAttribute.PetBonusAtk].Total;
                double bonusAttackResistance = 1 / (1 + targetBonusAttackResistance);
                double weaponAttackWeakness = 1 / (1 + targetMaxWeaponAttackResistance);
                double bonusAttackCoefficient = bonusAttackResistance * BonusAttackCoefficient(player);
                double minDamage = weaponAttackWeakness * Values[BasicAttribute.MinWeaponAtk].Total + bonusAttackCoefficient * bonusAttack;
                double maxDamage = weaponAttackWeakness * Values[BasicAttribute.MaxWeaponAtk].Total + bonusAttackCoefficient * bonusAttack;
                return (minDamage, maxDamage);
            case FieldNpc npc:
                return (1, 1);
        }
        return (1, 1);

        double BonusAttackCoefficient(FieldPlayer player) {
            int leftHandRarity = player.Session.Item.Equips.Get(EquipSlot.RH)?.Rarity ?? 0;
            int rightHandRarity = player.Session.Item.Equips.Get(EquipSlot.LH)?.Rarity ?? 0;
            return BonusAttack.Coefficient(rightHandRarity, leftHandRarity, player.Value.Character.Job.Code());
        }
    }

    /// <summary>
    /// Get actor's critical damage result. Takes into consideration the target's critical damage resistance.
    /// </summary>
    /// <param name="targetCriticalDamageResistance">Target's critical damage resistance</param>
    /// <param name="mode">Unknown. Used for Lua script. If 0, the cap of damage is 250%. If 14, it's 300%</param>
    /// <returns>Critical damage</returns>
    public float GetCriticalDamage(float targetCriticalDamageResistance, int mode = 0) {
        float criticalDamage = Actor switch {
            FieldPlayer or FieldNpc => Lua.CalcCritDamage(Values[BasicAttribute.CriticalDamage].Total, mode),
            _ => 0,
        };
        //TODO: Apply target's resistance. Need to figure out formula for this.
        return criticalDamage;
    }

    /// <summary>
    /// Gets the critical rate of the actor - uses a different formula for players and NPCs.
    /// </summary>
    /// <param name="targetCriticalEvasion"></param>
    /// <param name="skillId">Source skill ID</param>
    /// <returns>DamageType. If successful crit, returns DamageType.Critical. else returns DamageType.Normal</returns>
    public DamageType GetCriticalRate(long targetCriticalEvasion = 0, int skillId = 0) {
        double casterCriticalOverride = 0;
        float criticalChance = 0;
        switch (Actor) {
            case FieldPlayer player:
                criticalChance = Lua.CalcPlayerCritRate((int) player.Value.Character.Job.Code(), player.Stats.Values[BasicAttribute.Luck].Total, player.Stats.Values[BasicAttribute.CriticalRate].Total, targetCriticalEvasion, 0, 0);
                casterCriticalOverride = player.Buffs.TotalCompulsionRate(BuffCompulsionEventType.CritChanceOverride, skillId);
                break;
            case FieldNpc npc:
                criticalChance = Lua.CalcNpcCritRate(npc.Stats.Values[BasicAttribute.Luck].Total, npc.Stats.Values[BasicAttribute.CriticalRate].Total, targetCriticalEvasion);
                break;
            default:
                break;
        }

        return Random.Shared.NextDouble() < Math.Max(criticalChance, casterCriticalOverride) ? DamageType.Critical : DamageType.Normal;
    }

    public void Refresh() {
        if (Actor is not FieldPlayer player) {
            return;
        }

        // Do not refresh stats if player is mounted
        if (player.Session.Ride.RideType is RideOnType.Battle or RideOnType.Object) {
            return;
        }

        Character character = player.Value.Character;
        if (levelStats.TryGetValue(character.Level, out IReadOnlyDictionary<BasicAttribute, long>? metadata)) {
            Values.Reset(metadata, character.Job.Code());
        } else {
            Log.Logger.Error("Failed to refresh stats for {Job} level {Level}.", character.Job.Code(), character.Level);
            Values.Reset(character.Job.Code(), character.Level);
        }

        AddEquips(player);
        AddBuffs(player);
        Values.Total();
        StatConversion(player);
        Actor.Field.Broadcast(StatsPacket.Init(player));
        Actor.Field.Broadcast(StatsPacket.Update(player), player.Session);

        player.Session.PlayerInfo.SendUpdate(new PlayerUpdateRequest {
            AccountId = player.Session.AccountId,
            CharacterId = player.Session.CharacterId,
            Health = new HealthUpdate {
                CurrentHp = Values[BasicAttribute.Health].Current,
                TotalHp = Values[BasicAttribute.Health].Total,
            },
            Async = true,
        });
    }

    public void ResetActor(IActor actor) {
        Actor = actor;
    }

    private void AddEquips(FieldPlayer player) {
        Values.GearScore = 0;
        foreach (Item item in player.Session.Item.Equips.Gear.Values) {
            if (item.Stats != null) {
                AddItemStats(item.Stats);
            }

            // Add enhancement attributes to character attributes
            if (item.Enchant is not null) {
                foreach (KeyValuePair<BasicAttribute, BasicOption> kvp in item.Enchant.BasicOptions) {
                    Values[kvp.Key].AddTotal(kvp.Value);
                }
            }

            if (item.LimitBreak is not null) {
                foreach (KeyValuePair<BasicAttribute, BasicOption> kvp in item.LimitBreak.BasicOptions) {
                    Values[kvp.Key].AddTotal(kvp.Value);
                }
            }

            if (item.Socket != null) {
                for (int index = 0; index < item.Socket.UnlockSlots; index++) {
                    ItemGemstone? gem = item.Socket.Sockets[index];
                    if (gem != null && gem.Stats != null) {
                        AddItemStats(gem.Stats);
                    }
                }
            }

            player.Buffs.AddItemBuffs(item);

            if (item.Type.IsShield || item.Type.IsSpellbook) {
                // Shields and spellbooks do not contribute to gear score
                continue;
            }

            Log.Logger.Debug("Calculating Gearscore. Item ID: {id} - Gearscore: {gearscore} - Rarity: {rarity}, Enchant Level: {enchantLevel}, Limit Break Level: {limitBreakLevel}", item.Metadata.Id, item.Metadata.Property.GearScore, item.Rarity, item.Enchant?.Enchants ?? 0, item.LimitBreak?.Level ?? 0);
            (int gearScore, int enchantScore) = Lua.CalcItemLevel(item.Metadata.Property.GearScore, item.Rarity, item.Type.Type, item.Enchant?.Enchants ?? 0, item.LimitBreak?.Level ?? 0);
            int totalGearScore = gearScore + enchantScore;
            // Stars and daggers have half gear score
            if (item.Type.IsThrowingStar || item.Type.IsDagger) {
                totalGearScore /= 2;
            }
            // TODO: When dual-wielding weapons, if the difference between the two weapons GS is too great (we don't know the exact value yet), the GS should be penalized (we don't know by how much).
            Values.GearScore += totalGearScore;
        }

        Log.Logger.Debug("Final Gearscore for {name} ({id}): {gearscore}", player.Value.Character.Name, player.Value.Character.Id, Values.GearScore);
        player.Session.Dungeon.UpdateDungeonEnterLimit();
        player.Session.ConditionUpdate(ConditionType.item_gear_score, counter: Values.GearScore);
    }

    private void AddBuffs(FieldPlayer player) {
        foreach (Buff buff in player.Buffs.EnumerateBuffs()) {
            foreach ((BasicAttribute valueBasicAttribute, long value) in buff.Metadata.Status.Values) {
                Values[valueBasicAttribute].AddTotal(value);
            }
            foreach ((BasicAttribute rateBasicAttribute, float rate) in buff.Metadata.Status.Rates) {
                // ensure regen intervals do not drop below 0.1
                Values[rateBasicAttribute].AddRate(rate);
            }
            foreach ((SpecialAttribute valueSpecialAttribute, float value) in buff.Metadata.Status.SpecialValues) {
                Values[valueSpecialAttribute].AddTotal((long) value);
            }
            foreach ((SpecialAttribute rateSpecialAttribute, float rate) in buff.Metadata.Status.SpecialRates) {
                Values[rateSpecialAttribute].AddRate(rate);
            }
        }
    }

    private void StatConversion(FieldPlayer player) {
        foreach (Buff buff in player.Buffs.EnumerateBuffs()) {
            if (buff.Metadata.Status.Conversion != null) {
                float sum = Values[buff.Metadata.Status.Conversion.BaseAttribute].Total;
                sum *= buff.Metadata.Status.Conversion.Rate;
                Values[buff.Metadata.Status.Conversion.ResultAttribute].AddTotal((long) sum);
            }
        }
    }

    private void AddItemStats(ItemStats stats) {
        for (int type = 0; type < ItemStats.TYPE_COUNT; type++) {
            foreach ((BasicAttribute attribute, BasicOption option) in stats[(ItemStats.Type) type].Basic) {
                Values[attribute].AddTotal(option);
            }
            foreach ((SpecialAttribute attribute, SpecialOption option) in stats[(ItemStats.Type) type].Special) {
                Values[attribute].AddTotal(option);
            }
        }
    }
}
