using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Maple2.Model.Common;
using Maple2.Tools.Collision;

namespace Maple2.Model.Metadata;

public class MapEntityMetadata {
    public required IReadOnlyDictionary<Guid, Breakable> Breakables { get; init; }
    public required IReadOnlyDictionary<Guid, Liftable> Liftables { get; init; }
    public required IReadOnlyDictionary<Vector3B, LiftableTargetBox> LiftableTargetBoxes { get; init; }
    public required IReadOnlyDictionary<Vector3B, ObjectWeapon> ObjectWeapons { get; init; }
    public required IReadOnlyDictionary<int, Portal> Portals { get; init; }
    public required IReadOnlyList<SpawnPointPC> PlayerSpawns { get; init; }
    public required IReadOnlyList<SpawnPointNPC> NpcSpawns { get; init; }
    public required IReadOnlyDictionary<int, Ms2RegionSpawn> RegionSpawns { get; init; }
    public required IReadOnlyList<Ms2RegionSkill> RegionSkills { get; init; }
    public required IReadOnlyList<Ms2CubeSkill> CubeSkills { get; init; }
    public required IReadOnlyDictionary<int, EventSpawnPointNPC> EventNpcSpawns { get; init; }
    public required IReadOnlyDictionary<int, EventSpawnPointItem> EventItemSpawns { get; init; }
    public TaxiStation? Taxi { get; init; }
    public Prism BoundingBox { get; init; }

    public required IReadOnlyDictionary<Guid, BreakableActor> BreakableActors { get; init; }

    public required IReadOnlyDictionary<Guid, InteractObject> Interacts { get; init; }
    public required IReadOnlyDictionary<int, TriggerModel> TriggerModels { get; init; }
    public required ITriggerStorage Trigger { get; init; }
    public required IReadOnlyList<MS2PatrolData> Patrols { get; init; }
}

public interface ITriggerStorage : IReadOnlyDictionary<int, Ms2Trigger> {
    public ImmutableArray<Ms2TriggerActor> Actors { get; }
    public ImmutableArray<Ms2TriggerBox> Boxes { get; }
    public ImmutableArray<Ms2TriggerCamera> Cameras { get; }
    public ImmutableArray<Ms2TriggerCube> Cubes { get; }
    public ImmutableArray<Ms2TriggerEffect> Effects { get; }
    public ImmutableArray<Ms2TriggerLadder> Ladders { get; }
    public ImmutableArray<Ms2TriggerMesh> Meshes { get; }
    public ImmutableArray<Ms2TriggerRope> Ropes { get; }
    public ImmutableArray<Ms2TriggerSkill> Skills { get; }
    public ImmutableArray<Ms2TriggerSound> Sounds { get; }
    public ImmutableArray<Ms2TriggerAgent> Agents { get; }

    public bool TryGet<T>(int key, [NotNullWhen(true)] out T? trigger) where T : Ms2Trigger;
}
