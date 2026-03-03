using Maple2.Model.Metadata;

namespace Maple2.Model.Game;

public class WorldBoss {
    public int MetadataId => Metadata.Id;
    public int Id;
    public WorldBossMetadata Metadata;
    public long EndTick;
    public long SpawnTimestamp;
    public long NextSpawnTimestamp;

    public WorldBoss(WorldBossMetadata metadata, int id) {
        Metadata = metadata;
        Id = id;
    }
}
