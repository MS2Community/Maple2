using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Maple2.Model.Metadata;

namespace Maple2.Server.World.Containers;

public class WorldBossLookup {
    private readonly ChannelClientLookup channelClients;
    private readonly ConcurrentDictionary<int, WorldBossManager> activeManagers = new();
    private int nextEventId = 1;

    public WorldBossLookup(ChannelClientLookup channelClients) {
        this.channelClients = channelClients;
    }

    public bool TryGet(int metadataId, [NotNullWhen(true)] out WorldBossManager? manager) {
        return activeManagers.TryGetValue(metadataId, out manager);
    }

    public IEnumerable<WorldBossManager> GetAll() => activeManagers.Values;

    public bool Create(WorldBossMetadata metadata, long endTick, long nextSpawnTimestamp, out int eventId) {
        int id = Interlocked.Increment(ref nextEventId);
        var manager = new WorldBossManager(metadata, id, endTick, nextSpawnTimestamp) {
            ChannelClients = channelClients,
        };

        if (!activeManagers.TryAdd(metadata.Id, manager)) {
            eventId = 0;
            return false;
        }

        eventId = id;
        return true;
    }

    public void RemoveChannel(int metadataId, short channel) {
        if (activeManagers.TryGetValue(metadataId, out WorldBossManager? manager)) {
            manager.RemoveChannel(channel);
        }
    }

    public void Dispose(int metadataId) {
        if (!activeManagers.TryRemove(metadataId, out WorldBossManager? manager)) {
            return;
        }
        manager.Dispose();
    }
}
