using System.Collections.Concurrent;
using Grpc.Core;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Channel.Service;
using Serilog;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.World.Containers;

public class WorldBossManager : IDisposable {
    public required ChannelClientLookup ChannelClients { get; init; }

    public readonly WorldBoss Boss;
    public readonly ConcurrentDictionary<int, byte> AliveChannels = new();

    public WorldBossManager(WorldBossMetadata metadata, int id, long endTick, long nextSpawnTimestamp) {
        Boss = new WorldBoss(metadata, id) {
            EndTick = endTick,
            NextSpawnTimestamp = nextSpawnTimestamp,
        };
    }

    public void RemoveChannel(int channel) => AliveChannels.TryRemove(channel, out _);

    public void Announce() {
        foreach ((int channelId, ChannelClient channelClient) in ChannelClients) {
            try {
                channelClient.TimeEvent(new TimeEventRequest {
                    AnnounceWorldBoss = new TimeEventRequest.Types.AnnounceWorldBoss {
                        MetadataId = Boss.MetadataId,
                        EventId = Boss.Id,
                        EndTick = Boss.EndTick,
                        NextSpawnTimestamp = Boss.NextSpawnTimestamp,
                    },
                });

                AliveChannels.TryAdd(channelId, 0);
            } catch (RpcException rpcException) {
                if (rpcException.StatusCode == StatusCode.Unavailable) {
                    Log.Warning("Channel {Channel} unavailable when announcing field boss {BossId}", channelId, Boss.MetadataId);
                    continue;
                }
                Log.Error(rpcException, "Error announcing field boss {BossId} to channel {Channel}", Boss.MetadataId, channelId);
            }
        }
    }

    public void WarnChannels() {
        foreach ((int channelId, ChannelClient channelClient) in ChannelClients) {
            try {
                channelClient.TimeEvent(new TimeEventRequest {
                    WarnWorldBoss = new TimeEventRequest.Types.WarnWorldBoss {
                        MetadataId = Boss.MetadataId,
                        EventId = Boss.Id,
                    },
                });
            } catch (RpcException rpcException) {
                if (rpcException.StatusCode == StatusCode.Unavailable) {
                    Log.Warning("Channel {Channel} unavailable when warning field boss {BossId}", channelId, Boss.MetadataId);
                    continue;
                }
                Log.Error(rpcException, "Error warning field boss {BossId} on channel {Channel}", Boss.MetadataId, channelId);
            }
        }
    }

    public void Dispose() {
        foreach ((int channelId, ChannelClient channelClient) in ChannelClients) {
            try {
                channelClient.TimeEvent(new TimeEventRequest {
                    CloseWorldBoss = new TimeEventRequest.Types.CloseWorldBoss {
                        MetadataId = Boss.MetadataId,
                        EventId = Boss.Id,
                    },
                });
            } catch (RpcException rpcException) {
                if (rpcException.StatusCode == StatusCode.Unavailable) {
                    Log.Warning("Channel {Channel} unavailable when closing field boss {BossId}", channelId, Boss.MetadataId);
                    continue;
                }
                Log.Error(rpcException, "Error closing field boss {BossId} on channel {Channel}", Boss.MetadataId, channelId);
            }
        }
    }
}
