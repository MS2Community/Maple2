using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Maple2.File.IO;
using Maple2.File.Parser;
using Maple2.File.Parser.Xml.Skill;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.File.Ingest.Mapper;

public class SkillMapper : TypeMapper<StoredSkillMetadata> {
    private readonly SkillParser parser;

    public SkillMapper(M2dReader xmlReader, string language) {
        parser = new SkillParser(xmlReader, language);
    }

    protected override IEnumerable<StoredSkillMetadata> Map() {
        List<long> magicPaths = [];
        List<long> cubeMagicPaths = [];
        foreach ((int id, string name, SkillData data) in parser.Parse()) {
            if (data.basic == null) continue; // Old_JobChange_01
            Debug.Assert(data.basic.kinds.groupIDs.Length <= 1);


            // Note: 90000775 has cubeMagicPathID="2147483647" which should be cubeMagicPathID="9000073111"
            Dictionary<short, SkillMetadataLevel> levels = data.level.ToDictionary(
                level => level.value,
                level => new SkillMetadataLevel(
                    Condition: level.beginCondition.Convert(),
                    Change: level.changeSkill?.Convert(),
                    AutoTargeting: level.autoTargeting?.Convert(),
                    Consume: new SkillMetadataConsume(
                        Meso: level.consume.money,
                        UseItem: level.consume.useItem,
                        HpRate: level.consume.hpRate,
                        Stat: level.consume.stat.ToDictionary()),
                    Detect: new SkillMetadataDetect(
                        IncludeCaster: level.detectProperty?.includeCaster == 1,
                        Distance: level.detectProperty?.distance ?? 0),
                    Recovery: new SkillMetadataRecovery(
                        SpValue: level.recoveryProperty.spValue,
                        SpRate: level.recoveryProperty.spRate),
                    Skills: level.conditionSkill.Select(skill => skill.Convert()).ToArray(),
                    Motions: level.motion.Select(motion => new SkillMetadataMotion(
                        new SkillMetadataMotionProperty(
                            SequenceName: motion.motionProperty.sequenceName,
                            SequenceSpeed: motion.motionProperty.sequenceSpeed,
                            MoveDistance: motion.motionProperty.movedistance,
                            FaceTarget: motion.motionProperty.faceTarget),
                        Attacks: motion.attack.Select(attack => new SkillMetadataAttack(
                            Point: attack.point,
                            PointGroup: attack.pointGroupID,
                            TargetCount: attack.targetCount,
                            MagicPathId: attack.magicPathID,
                            CubeMagicPathId: attack.cubeMagicPathID == int.MaxValue ? 9000073111 : attack.cubeMagicPathID,
                            HitImmuneBreak: attack.hitImmuneBreak,
                            BrokenOffence: attack.brokenOffence,
                            CompulsionTypes: attack.compulsionType.Select(type => (CompulsionType) type).ToArray(),
                            Pet: attack.petTamingProperty != null ? new SkillMetadataPet(
                                TamingGroup: attack.petTamingProperty.tamingGroup,
                                TrapLevel: attack.petTamingProperty.trapLevel,
                                TamingPoint: attack.petTamingProperty.tamingPoint,
                                ForcedTaming: attack.petTamingProperty.forcedTaming
                            ) : null,
                            Range: Convert(attack.rangeProperty),
                            Arrow: new SkillMetadataArrow(
                                Overlap: attack.arrowProperty.overlap,
                                Explosion: attack.arrowProperty.explosion,
                                RayPhysXTest: attack.arrowProperty.rayPhysxTest,
                                NonTarget: (SkillTargetType) attack.arrowProperty.nonTarget,
                                BounceType: (BounceType) attack.arrowProperty.bounceType,
                                BounceCount: attack.arrowProperty.bounceCount,
                                BounceRadius: attack.arrowProperty.bounceRadius,
                                BounceOverlap: attack.arrowProperty.bounceType > 0 && attack.arrowProperty.bounceOverlap,
                                Collision: attack.arrowProperty.collision,
                                CollisionAdd: attack.arrowProperty.collisionAdd,
                                RayType: attack.arrowProperty.rayType),
                            Damage: new SkillMetadataDamage(
                                Count: attack.damageProperty.count,
                                Rate: attack.damageProperty.rate,
                                HitSpeed: attack.damageProperty.hitSpeedRate,
                                HitDelay: attack.damageProperty.hitPauseTime,
                                IsConstDamage: attack.damageProperty.isConstDamageValue != 0,
                                Value: attack.damageProperty.value,
                                DamageByTargetMaxHp: attack.damageProperty.damageByTargetMaxHP,
                                SuperArmorBreak: attack.damageProperty.superArmorBreak,
                                Push: attack.damageProperty.push > 0 ? new SkillMetadataPush(
                                    Type: attack.damageProperty.push,
                                    EaseType: attack.damageProperty.pushEaseType,
                                    ApplyField: attack.damageProperty.pushApplyField,
                                    Distance: attack.damageProperty.pushdistance,
                                    UpDistance: attack.damageProperty.pushUpDistance,
                                    Down: attack.damageProperty.pushDown,
                                    Fall: attack.damageProperty.pushFall,
                                    Duration: attack.damageProperty.pushduration,
                                    Probability: attack.damageProperty.pushprob,
                                    Priority: attack.damageProperty.pushPriority,
                                    PriorityHitImmune: attack.damageProperty.pushPriorityHitImmune
                                ) : null
                            ),
                            Skills: attack.conditionSkill.Where(skill => !skill.dependOnDamageCount).Select(skill => skill.Convert()).ToArray(),
                            SkillsOnDamage: attack.conditionSkill.Where(skill => skill.dependOnDamageCount).Select(skill => skill.Convert()).ToArray()
                        )).ToArray(),
                        AttackPoints: motion.CollectAttackPoints()
                    )).ToArray()
                ));

            yield return new StoredSkillMetadata(
                Id: id,
                Name: name,
                Property: new SkillMetadataProperty(
                    Type: (SkillType) data.basic.kinds.type,
                    SubType: (SkillSubType) data.basic.kinds.subType,
                    RangeType: (RangeType) data.basic.kinds.rangeType,
                    AttackType: (AttackType) data.basic.ui.attackType,
                    Element: (Element) data.basic.kinds.element,
                    State: string.IsNullOrEmpty(data.basic.kinds.state)
                        ? ActorState.None
                        : Enum.GetValues<ActorState>()
                            .FirstOrDefault(enumValue =>
                                enumValue.GetType()
                                    .GetField(enumValue.ToString())
                                    ?.GetCustomAttribute<DescriptionAttribute>()
                                    ?.Description == data.basic.kinds.state),
                    ContinueSkill: data.basic.kinds.continueSkill,
                    SpRecoverySkill: data.basic.kinds.spRecoverySkill,
                    ImmediateActive: data.basic.kinds.immediateActive,
                    UnrideOnHit: data.basic.kinds.unrideOnHit,
                    UnrideOnUse: data.basic.kinds.unrideOnUse,
                    ReleaseObjectWeapon: data.basic.kinds.releaseObjectWeapon,
                    Emotion: data.basic.kinds.emotion,
                    SkillGroup: data.basic.kinds.groupIDs.FirstOrDefault(),
                    MaxLevel: levels.Keys.Max()),
                State: new SkillMetadataState(
                    InBattle: data.basic.stateAttr.battle == 1,
                    SuperArmor: (SuperArmor) data.basic.stateAttr.superArmor,
                    UseInGameTime: data.basic.stateAttr.useInGameTime == 1,
                    IgnoreReduceCooldown: data.basic.stateAttr.ignoreReduceCooldown == 1,
                    CooldownGroupId: data.basic.stateAttr.cooldownGroupID,
                    RechargeMaxCount: data.basic.stateAttr.rechargeMaxCount),
                Levels: levels
            );
        }
    }

    private static SkillMetadataRange Convert(RegionSkill region) {
        return new SkillMetadataRange(
            Type: region.rangeType switch {
                "box" => SkillRegion.Box,
                "cylinder" => SkillRegion.Cylinder,
                "circle" => SkillRegion.Cylinder,
                "frustum" => SkillRegion.Frustum,
                "hole_cylinder" => SkillRegion.HoleCylinder,
                "1200" => SkillRegion.None, // skill/60/60012051.xml
                _ => SkillRegion.None,
            },
            Distance: region.distance,
            Height: region.height,
            Width: region.width,
            EndWidth: region.endWidth,
            RotateZDegree: region.rangeZRotateDegree,
            RangeAdd: region.rangeAdd,
            RangeOffset: region.rangeOffset,
            IncludeCaster: (IncludeCasterType) region.includeCaster,
            ApplyTarget: (ApplyTargetType) region.applyTarget,
            CastTarget: (SkillTargetType) region.castTarget
        );
    }
}
