using System.Numerics;

namespace Maple2.Model.Metadata;

public abstract record SpawnPoint(int SpawnPointId, Vector3 Position, Vector3 Rotation, bool Visible) : MapBlock;

public record SpawnPointPC(
    int SpawnPointId,
    Vector3 Position,
    Vector3 Rotation,
    bool Visible,
    bool Enable
) : SpawnPoint(SpawnPointId, Position, Rotation, Visible);

public record SpawnPointNPCListEntry(int NpcId, int Count);

public record SpawnPointNPC(
    string EntityId,
    int SpawnPointId,
    Vector3 Position,
    Vector3 Rotation,
    bool Visible,
    bool SpawnOnFieldCreate,
    float SpawnRadius,
    IList<SpawnPointNPCListEntry> NpcList,
    int RegenCheckTime,
    string? PatrolData
) : SpawnPoint(SpawnPointId, Position, Rotation, Visible);

public record EventSpawnPointNPC(
    string EntityId,
    int SpawnPointId,
    Vector3 Position,
    Vector3 Rotation,
    bool Visible,
    bool SpawnOnFieldCreate,
    float SpawnRadius,
    int NpcCount,
    IList<SpawnPointNPCListEntry> NpcList,
    int RegenCheckTime,
    int LifeTime,
    string SpawnAnimation
) : SpawnPointNPC(EntityId, SpawnPointId, Position, Rotation, Visible, SpawnOnFieldCreate, SpawnRadius, NpcList, RegenCheckTime, String.Empty);

public record EventSpawnPointItem(
    int SpawnPointId,
    Vector3 Position,
    Vector3 Rotation,
    float Lifetime,
    int IndividualDropBoxId,
    int GlobalDropBoxId,
    int GlobalDropLevel,
    bool Visible
) : SpawnPoint(SpawnPointId, Position, Rotation, Visible);
