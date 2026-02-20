using Maple2.Model.Metadata;

namespace Maple2.Model.Game;

public class FieldBoss {
    public int MetadataId => Metadata.Id;
    public int Id;
    public FieldBossMetadata Metadata;
    public long EndTick;
    public long SpawnTimestamp;
    public long NextSpawnTimestamp;

    public FieldBoss(FieldBossMetadata metadata, int id) {
        Metadata = metadata;
        Id = id;
    }
}
