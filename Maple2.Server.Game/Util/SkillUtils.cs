using System.Numerics;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Tools.Collision;

namespace Maple2.Server.Game.Util;

public static class SkillUtils {
    public static Prism GetPrism(this SkillMetadataRange range, in Vector3 position, float angle) {
        if (range.Type == SkillRegion.None) {
            return new Prism(IPolygon.Null, 0, 0);
        }

        float adjustedAngle = angle + range.RotateZDegree + 180;
        var origin = new Vector2(position.X + range.RangeOffset.X, position.Y + range.RangeOffset.Y);
        float boxWidth = range.Width + range.RangeAdd.X;
        IPolygon polygon = range.Type switch {
            // Box projects forward from caster (0 to distance), same as Frustum but with equal widths.
            SkillRegion.Box => new Trapezoid(origin, boxWidth, boxWidth, range.Distance + range.RangeAdd.Y, adjustedAngle),
            SkillRegion.Cylinder => new Circle(origin, range.Distance),
            SkillRegion.Frustum => new Trapezoid(origin, range.Width, range.EndWidth, range.Distance, adjustedAngle),
            SkillRegion.HoleCylinder => new HoleCircle(origin, range.Width, range.EndWidth),
            _ => throw new ArgumentOutOfRangeException($"Invalid range type: {range.Type}"),
        };

        return new Prism(polygon, position.Z + range.RangeOffset.Z, range.Height + range.RangeAdd.Z);
    }

    public static IEnumerable<T> Filter<T>(this Prism prism, IEnumerable<T> entities, int limit = 10) where T : IActor {
        HashSet<int> addedActorObjectIds = [];
        foreach (T entity in entities) {
            if (limit <= 0) {
                yield break;
            }

            if (addedActorObjectIds.Contains(entity.ObjectId)) {
                continue;
            }

            if (entity.IsDead) {
                continue;
            }

            IPrism shape = entity.Shape;
            if (prism.Intersects(shape)) {
                addedActorObjectIds.Add(entity.ObjectId);
                limit--;
                yield return entity;
            }
        }
    }

    public static IEnumerable<T> Filter<T>(this Prism[] prisms, IEnumerable<T> entities, int limit = 10, ICollection<IActor>? ignore = null) where T : IActor {
        HashSet<int> addedActorObjectIds = [];
        foreach (T entity in entities) {
            if (limit <= 0) {
                yield break;
            }

            if (addedActorObjectIds.Contains(entity.ObjectId)) {
                continue;
            }

            if (entity.IsDead) {
                continue;
            }
            if (ignore != null && ignore.Contains(entity)) {
                continue;
            }

            IPrism shape = entity.Shape;
            foreach (Prism prism in prisms) {
                if (prism.Intersects(shape)) {
                    limit--;
                    addedActorObjectIds.Add(entity.ObjectId);
                    yield return entity;
                }
            }
        }
    }

    public static bool Check(this BeginCondition condition, IActor caster, IActor owner, IActor target, EventConditionType eventType = EventConditionType.Activate, int eventSkillId = 0, int eventBuffId = 0) {
        if (caster is FieldPlayer player) {
            if (condition is not { Probability: 1 } && condition.Probability < Random.Shared.NextDouble()) {
                return false;
            }
            if (!condition.AllowDead && caster.IsDead) {
                return false;
            }
            if (condition.OnlyShadowWorld && caster.Field.Metadata.Property.Type != MapType.Shadow) {
                return false;
            }
            if (condition.OnlyFlyableMap && !caster.Field.Metadata.Property.CanFly) {
                return false;
            }
            if (!condition.AllowOnSurvival && (caster.Field.Metadata.Property.Type is MapType.SurvivalTeam or MapType.SurvivalSolo)) {
                return false;
            }
            if (player.Value.Character.Level < condition.Level) {
                return false;
            }
            if (player.Session.Currency.Meso < condition.Mesos) {
                return false;
            }
            if (condition.Gender != Gender.All && player.Value.Character.Gender != condition.Gender) {
                return false;
            }
            if (condition.JobCode.Length > 0 && !condition.JobCode.Contains(player.Value.Character.Job.Code())) {
                return false;
            }
            if (condition.Weapon?.Length > 0) {
                if (!condition.Weapon.Any(weapon => weapon.Check(player))) {
                    return false;
                }
            }
            foreach ((BasicAttribute stat, long value) in condition.Stat) {
                if (player.Stats.Values[stat].Total < value) {
                    return false;
                }
            }
            if (condition.DurationWithoutMoving > 0) {
                if (player.PositionTick.Duration < condition.DurationWithoutMoving) {
                    return false;
                }
            }
            if (condition.Maps.Length > 0 && !condition.Maps.Contains(caster.Field.MapId)) {
                return false;
            }
            if (condition.MapTypes.Length > 0 && !condition.MapTypes.Contains(caster.Field.Metadata.Property.Type)) {
                return false;
            }
            if (condition.Continents.Length > 0 && !condition.Continents.Contains(caster.Field.Metadata.Property.Continent)) {
                return false;
            }
            if (condition.DungeonGroupType.Length > 0 &&
                (caster.Field is not DungeonFieldManager dungeonFieldManager ||
                 !condition.DungeonGroupType.Contains(dungeonFieldManager.DungeonMetadata.GroupType))) {
                return false;
            }
            if (condition.ActiveSkill.Length > 0 && condition.ActiveSkill.All(id => owner.Animation.Current?.Skill?.Id != id)) {
                return false;
            }
            if (condition.OnlyOnBattleMount && player.Session.Ride.Ride?.Metadata.Basic.Type != RideOnType.Battle) {
                return false;
            }
            if (!condition.AllowOnBattleMount && player.Session.Ride.Ride?.Metadata.Basic.Type == RideOnType.Battle) {
                return false;
            }
        }

        return condition.Caster.Check(caster, eventType, eventSkillId, eventBuffId) && condition.Owner.Check(owner, eventType, eventSkillId, eventBuffId) && condition.Target.Check(target, eventType, eventSkillId, eventBuffId);
    }

    private static bool Check(this BeginConditionTarget? condition, IActor target, EventConditionType eventType = EventConditionType.Activate, int eventSkillId = 0, int eventBuffId = 0) {
        if (condition == null) {
            return true;
        }

        if (condition.Event.Type != eventType) {
            return false;
        }

        foreach ((int id, short level, bool owned, int count, CompareType compare) in condition.Buff) {
            List<Buff> buffs = target.Buffs.EnumerateBuffs(id);
            bool validBuff = false;
            foreach (Buff buff in buffs) {
                if (buff.Level < level) {
                    continue;
                }
                if (owned && buff.Owner.ObjectId == 0) {
                    continue;
                }

                if (count > 0) {
                    bool compareResult = compare switch {
                        CompareType.Equals => buff.Stacks == count,
                        CompareType.Less => buff.Stacks < count,
                        CompareType.LessEquals => buff.Stacks <= count,
                        CompareType.Greater => buff.Stacks > count,
                        CompareType.GreaterEquals => buff.Stacks >= count,
                        _ => true,
                    };

                    if (!compareResult) {
                        continue;
                    }
                }

                validBuff = true;
                break;
            }
            if (!validBuff) {
                return false;
            }
        }

        foreach ((BasicAttribute attribute, float value, CompareType compare, CompareStatValueType valueType) in condition.Stat) {
            float targetValue = valueType switch {
                CompareStatValueType.CurrentPercentage when target.Stats.Values[attribute].Total == 0 => 0,
                CompareStatValueType.CurrentPercentage => (float) target.Stats.Values[attribute].Current / target.Stats.Values[attribute].Total,
                CompareStatValueType.TotalValue => target.Stats.Values[attribute].Total,
                _ => 0,
            };

            bool compareResult = compare switch {
                CompareType.Equals => targetValue == value,
                CompareType.Less => targetValue < value,
                CompareType.LessEquals => targetValue <= value,
                CompareType.Greater => targetValue > value,
                CompareType.GreaterEquals => targetValue >= value,
                _ => true,
            };
            if (!compareResult) {
                return false;
            }
        }

        if (condition.HasNotBuffIds.Length > 0 && condition.HasNotBuffIds.Any(id => target.Buffs.HasBuff(id))) {
            return false;
        }

        // Verify if these conditions are only for player.
        if (target is FieldPlayer player) {
            if (condition.States.Length > 0 && !condition.States.Contains(player.State)) {
                return false;
            }

            if (condition.SubStates.Length > 0 && !condition.SubStates.Contains(player.SubState)) {
                return false;
            }

            if (condition.Masteries.Count > 0 && !condition.Masteries.All(mastery => player.Session.Mastery[mastery.Key] >= mastery.Value)) {
                return false;
            }
        } else if (target is FieldNpc npc) {
            if (condition.NpcIds.Length > 0 && !condition.NpcIds.Contains(npc.Value.Id)) {
                return false;
            }
        }

        if (condition.Event.BuffIds.Length > 0 && !condition.Event.BuffIds.Contains(eventBuffId)) {
            return false;
        }
        if (condition.Event.SkillIds.Length > 0 && !condition.Event.SkillIds.Contains(eventSkillId)) {
            return false;
        }

        return true;
    }

    private static bool Check(this BeginConditionWeapon weapon, FieldPlayer player) {
        return IsValid(weapon.LeftHand, EquipSlot.LH) && IsValid(weapon.RightHand, EquipSlot.RH);

        bool IsValid(ItemType itemType, EquipSlot slot) {
            if (itemType.Type == 0) return true;
            Item? handItem = player.Session.Item.Equips.Get(slot);
            return handItem != null && handItem.Type.Type == itemType.Type;
        }
    }
}
