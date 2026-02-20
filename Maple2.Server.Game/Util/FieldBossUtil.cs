using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

public static class FieldBossUtil {
    public static long ComputeNextSpawnTimestamp(FieldBossMetadata metadata) {
        if (metadata.EndTime < DateTime.Now || metadata.CycleTime == TimeSpan.Zero) {
            return 0;
        }
        DateTime next = metadata.StartTime;
        while (next < DateTime.Now) {
            next += metadata.CycleTime;
        }
        return next > metadata.EndTime ? 0 : new DateTimeOffset(next).ToUnixTimeSeconds();
    }
}
