using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

public static class FieldBossUtil {
    public static long ComputeNextSpawnTimestamp(FieldBossMetadata metadata) {
        if (metadata.EndTime < DateTime.Now || metadata.CycleTime == TimeSpan.Zero) {
            return 0;
        }
        DateTime next = metadata.StartTime;
        if (next < DateTime.Now) {
            double elapsedMs = (DateTime.Now - metadata.StartTime).TotalMilliseconds;
            long cycles = (long) Math.Ceiling(elapsedMs / metadata.CycleTime.TotalMilliseconds);
            next = metadata.StartTime + TimeSpan.FromMilliseconds(cycles * metadata.CycleTime.TotalMilliseconds);
        }
        return next > metadata.EndTime ? 0 : new DateTimeOffset(next).ToUnixTimeSeconds();
    }
}
