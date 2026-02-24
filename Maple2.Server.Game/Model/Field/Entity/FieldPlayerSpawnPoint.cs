using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Game.Model;

public class FieldPlayerSpawnPoint : FieldEntity<SpawnPointPC> {
    public bool Enable;
    public int Id { get; init; }

    public FieldPlayerSpawnPoint(FieldManager field, int objectId, SpawnPointPC metadata, int id) : base(field, objectId, metadata) {
        Enable = metadata.Enable;
        Position = metadata.Position;
        Rotation = metadata.Rotation;
        Id = id;
    }
}
