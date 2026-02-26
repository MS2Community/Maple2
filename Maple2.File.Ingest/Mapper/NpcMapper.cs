using System.Diagnostics;
using System.Numerics;
using M2dXmlGenerator;
using Maple2.File.IO;
using Maple2.File.Parser;
using Maple2.File.Parser.Xml.Npc;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.File.Ingest.Mapper;

public class NpcMapper : TypeMapper<NpcMetadata> {
    private readonly NpcParser parser;

    public static Dictionary<int, NpcMetadata> NpcMetadataById { get; } = new();

    public NpcMapper(M2dReader xmlReader, string language) {
        parser = new NpcParser(xmlReader, language);
    }

    protected override IEnumerable<NpcMetadata> Map() {
        foreach ((int id, string name, NpcData data, List<EffectDummy> _) in parser.Parse()) {
            Debug.Assert(data.collision.shape == "box" || string.IsNullOrWhiteSpace(data.collision.shape));

            var metadata = new NpcMetadata(Id: id,
                Name: name,
                AiPath: data.aiInfo.path,
                Model: new NpcMetadataModel(
                    Name: data.model.kfm,
                    Scale: data.model.scale,
                    AniSpeed: data.model.anispeed
                ),
                Distance: new NpcMetadataDistance(
                    data.distance.avoid,
                    data.distance.sight,
                    data.distance.sightHeightUP,
                    data.distance.sightHeightDown,
                    data.distance.customLastSightRadius,
                    data.distance.customLastSightHeightUp,
                    data.distance.customLastSightHeightDown
                ),
                Skill: new NpcMetadataSkill(
                    Entries: data.skill.ids.Select((skillId, i) =>
                        new NpcMetadataSkill.Entry(skillId, data.skill.levels[i])).ToArray(),
                    Cooldown: data.skill.coolDown
                ),
                Stat: new NpcMetadataStat(Stats: MapStats(data.stat),
                    ScaleStatRate: [
                        data.stat.scaleStatRate_1,
                        data.stat.scaleStatRate_2,
                        data.stat.scaleStatRate_3,
                        data.stat.scaleStatRate_4,
                    ],
                    ScaleBaseTap: [
                        data.stat.scaleBaseTap_1,
                        data.stat.scaleBaseTap_2,
                        data.stat.scaleBaseTap_3,
                        data.stat.scaleBaseTap_4,
                    ],
                    ScaleBaseDef: [
                        data.stat.scaleBaseDef_1,
                        data.stat.scaleBaseDef_2,
                        data.stat.scaleBaseDef_3,
                        data.stat.scaleBaseDef_4,
                    ],
                    ScaleBaseSpaRate: [
                        data.stat.scaleBaseSpaRate_1,
                        data.stat.scaleBaseSpaRate_2,
                        data.stat.scaleBaseSpaRate_3,
                        data.stat.scaleBaseSpaRate_4,
                    ]),
                Basic: new NpcMetadataBasic(Friendly: data.basic.friendly,
                    AttackGroup: data.basic.npcAttackGroup,
                    DefenseGroup: data.basic.npcDefenseGroup,
                    Kind: data.basic.kind,
                    ShopId: data.basic.shopId,
                    HitImmune: data.basic.hitImmune,
                    AbnormalImmune: data.basic.abnormalImmune,
                    Level: data.basic.level,
                    Class: data.basic.@class,
                    RotationDisabled: data.basic.rotationDisabled,
                    MaxSpawnCount: data.basic.maxSpawnCount,
                    GroupSpawnCount: data.basic.groupSpawnCount,
                    RareDegree: data.basic.rareDegree,
                    MainTags: data.basic.mainTags,
                    SubTags: data.basic.subTags,
                    Difficulty: data.basic.difficulty,
                    CustomExp: data.exp.customExp),
                Property: new NpcMetadataProperty(
                    Buffs: data.additionalEffect.codes.Select((buffId, i) => new NpcMetadataBuff(buffId, data.additionalEffect.levels[i])).ToArray(),
                    Capsule: new NpcMetadataCapsule(data.capsule.radius, data.capsule.height),
                    Collision: data.collision.shape == "box" ? new NpcMetadataCollision(
                        Dimensions: new Vector3(data.collision.width, data.collision.depth, data.collision.height),
                        Offset: new Vector3(data.collision.widthOffset, data.collision.depthOffset, data.collision.heightOffset)
                    ) : null),
                DropInfo: new NpcMetadataDropInfo(
                    DropDistanceBase: data.dropiteminfo.dropDistanceBase,
                    DropDistanceRandom: data.dropiteminfo.dropDistanceRandom,
                    GlobalDropBoxIds: data.dropiteminfo.globalDropBoxId,
                    DeadGlobalDropBoxIds: data.dropiteminfo.globalDeadDropBoxId,
                    IndividualDropBoxIds: data.dropiteminfo.individualDropBoxId,
                    GlobalHitDropBoxIds: data.dropiteminfo.globalHitDropBoxId,
                    IndividualHitDropBoxIds: data.dropiteminfo.globalHitDropBoxId),
                Action: new NpcMetadataAction(
                    RotateSpeed: data.speed.rotation,
                    WalkSpeed: data.speed.walk,
                    RunSpeed: data.speed.run,
                    Actions: data.normal.action.Zip(data.normal.prob, (action, prob) => new NpcAction(action, prob)).ToArray(),
                    MoveArea: data.normal.movearea,
                    MaidExpired: data.normal.maidExpired),
                Dead: new NpcMetadataDead(
                    Time: data.dead.time,
                    Revival: data.dead.revival,
                    Count: data.dead.count,
                    LifeTime: data.dead.lifeTime,
                    ExtendRoomTime: data.dead.extendRoomTime),
                LookAtTarget: new NpcMetadataLookAtTarget(
                    data.lookattarget.targetdummy,
                    data.lookattarget.lookAtMyPCWhenTalking == 1,
                    data.lookattarget.useTalkMotion == 1)
            );

            NpcMetadataById[id] = metadata;

            yield return metadata;
        }
    }

    private static IReadOnlyDictionary<BasicAttribute, long> MapStats(Stat stat) {
        Dictionary<BasicAttribute, long> stats = stat.ToDictionary();

        if (FeatureLocaleFilter.FeatureEnabled("HiddenStatAdd01")) {
            stats[BasicAttribute.Health] = stats.GetValueOrDefault(BasicAttribute.Health) + stat.hiddenhpadd;
            stats[BasicAttribute.Defense] = stats.GetValueOrDefault(BasicAttribute.Defense) + stat.hiddennddadd;
        }
        if (FeatureLocaleFilter.FeatureEnabled("HiddenStatAdd02")) {
            stats[BasicAttribute.PhysicalAtk] = stats.GetValueOrDefault(BasicAttribute.PhysicalAtk) + stat.hiddenwapadd;
            stats[BasicAttribute.MagicalAtk] = stats.GetValueOrDefault(BasicAttribute.MagicalAtk) + stat.hiddenwapadd;
        }
        if (FeatureLocaleFilter.FeatureEnabled("HiddenStatAdd03")) {
            stats[BasicAttribute.Health] = stats.GetValueOrDefault(BasicAttribute.Health) + stat.hiddenhpadd03;
            stats[BasicAttribute.Defense] = stats.GetValueOrDefault(BasicAttribute.Defense) + stat.hiddennddadd03;
            stats[BasicAttribute.PhysicalAtk] = stats.GetValueOrDefault(BasicAttribute.PhysicalAtk) + stat.hiddenwapadd03;
            stats[BasicAttribute.MagicalAtk] = stats.GetValueOrDefault(BasicAttribute.MagicalAtk) + stat.hiddenwapadd03;
        }
        if (FeatureLocaleFilter.FeatureEnabled("HiddenStatAdd04")) {
            stats[BasicAttribute.Health] = stats.GetValueOrDefault(BasicAttribute.Health) + stat.hiddenhpadd04;
            stats[BasicAttribute.Defense] = stats.GetValueOrDefault(BasicAttribute.Defense) + stat.hiddennddadd04;
            stats[BasicAttribute.PhysicalAtk] = stats.GetValueOrDefault(BasicAttribute.PhysicalAtk) + stat.hiddenwapadd04;
            stats[BasicAttribute.MagicalAtk] = stats.GetValueOrDefault(BasicAttribute.MagicalAtk) + stat.hiddenwapadd04;
        }

        return stats;
    }
}
