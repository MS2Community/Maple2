namespace Maple2.Model.Metadata;

public record TimeEventTable(
    IReadOnlyDictionary<int, GlobalPortalMetadata> GlobalPortal,
    IReadOnlyDictionary<int, FieldBossMetadata> FieldBoss) : ServerTable;

public record FieldBossMetadata(
    int Id,
    int Probability,
    DateTime StartTime,
    DateTime EndTime,
    TimeSpan CycleTime,
    TimeSpan RandomTime,
    TimeSpan LifeTime,
    int[] TargetMapIds,
    int[] SpawnPointIds,
    int[] NpcIds,
    int Tag,
    bool Unique,
    bool IndividualChannelSpawn,
    float VariableCountByChannel,
    bool ScreenNotice,
    bool ChatNotice);

public record GlobalPortalMetadata(
    int Id,
    int Probability,
    DateTime StartTime,
    DateTime EndTime,
    TimeSpan CycleTime,
    TimeSpan RandomTime,
    TimeSpan LifeTime,
    string PopupMessage,
    string SoundId,
    GlobalPortalMetadata.Field[] Entries) {
    public record Field(
        string Name,
        int MapId,
        int PortalId);
}
