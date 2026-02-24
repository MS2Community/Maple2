using System.Text.Json.Serialization;

namespace Maple2.Model.Metadata;

public class MapEntity {
    public string XBlock { get; set; }
    public Guid Guid { get; set; }
    public string Name { get; set; }

    public required MapBlock Block { get; init; }

    public MapEntity(string xBlock, Guid guid, string name) {
        XBlock = xBlock;
        Guid = guid;
        Name = name;
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "!")]
[JsonDerivedType(typeof(Portal), 19716277)]
[JsonDerivedType(typeof(TaxiStation), 87397383)]
[JsonDerivedType(typeof(Liftable), 52914141)]
[JsonDerivedType(typeof(LiftableTargetBox), 832184036)]
[JsonDerivedType(typeof(Breakable), 1404063494)]
[JsonDerivedType(typeof(Ms2RegionSpawn), 478295409)]
[JsonDerivedType(typeof(ObjectWeapon), 1490986767)]
[JsonDerivedType(typeof(BreakableActor), 362799584)]
// [JsonDerivedType(typeof(Ms2InteractObject), 1928632421)]
[JsonDerivedType(typeof(Ms2InteractActor), 1650023023)]
[JsonDerivedType(typeof(Ms2InteractDisplay), 146802325)]
[JsonDerivedType(typeof(Ms2InteractMesh), 1638661275)]
// [JsonDerivedType(typeof(Ms2InteractWebActor), 114948310)]
// [JsonDerivedType(typeof(Ms2InteractWebMesh), 131882917)]
[JsonDerivedType(typeof(Ms2SimpleUiObject), 64070415)]
[JsonDerivedType(typeof(Ms2Telescope), 1660396588)]
// [JsonDerivedType(typeof(SpawnPoint), 446083964)]
[JsonDerivedType(typeof(SpawnPointPC), 476587788)]
[JsonDerivedType(typeof(SpawnPointNPC), 207007606)]
[JsonDerivedType(typeof(EventSpawnPointItem), 561252102)]
[JsonDerivedType(typeof(EventSpawnPointNPC), 2038856760)]
[JsonDerivedType(typeof(Ms2RegionSkill), 821242714)]
[JsonDerivedType(typeof(Ms2CubeSkill), 580235600)]
// [JsonDerivedType(typeof(Ms2TriggerObject), 244177309)]
[JsonDerivedType(typeof(Ms2TriggerActor), 1192557034)]
[JsonDerivedType(typeof(Ms2TriggerAgent), 1641615524)]
// [JsonDerivedType(typeof(Ms2TriggerBlock), 1887440090)]
[JsonDerivedType(typeof(Ms2TriggerBox), 1606545175)]
[JsonDerivedType(typeof(Ms2TriggerCamera), 1697877699)]
[JsonDerivedType(typeof(Ms2TriggerCube), 2031712866)]
[JsonDerivedType(typeof(Ms2TriggerEffect), 1728709847)]
[JsonDerivedType(typeof(Ms2TriggerLadder), 1182857305)]
[JsonDerivedType(typeof(Ms2TriggerMesh), 1957913511)]
[JsonDerivedType(typeof(Ms2TriggerPortal), 1960805826)]
[JsonDerivedType(typeof(Ms2TriggerRope), 177617088)]
[JsonDerivedType(typeof(Ms2TriggerSkill), 737806629)]
[JsonDerivedType(typeof(Ms2TriggerSound), 558345729)]
[JsonDerivedType(typeof(TriggerModel), 1436346081)]
[JsonDerivedType(typeof(Ms2Bounding), 1539875768)]
[JsonDerivedType(typeof(MS2PatrolData), 250722994)]
public abstract record MapBlock;
