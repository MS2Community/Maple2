using System.Collections.Concurrent;
using Grpc.Core;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Channel.Service;
using Serilog;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.World.Containers;

public class FieldBossManager : IDisposable {
    public required ChannelClientLookup ChannelClients { get; init; }

    public readonly FieldBoss Boss;
    public readonly ConcurrentDictionary<short, byte> AliveChannels = new();

    public FieldBossManager(FieldBossMetadata metadata, int id, long endTick, long nextSpawnTimestamp) {
        Boss = new FieldBoss(metadata, id) {
            EndTick = endTick,
            NextSpawnTimestamp = nextSpawnTimestamp,
        };
    }

    public void RemoveChannel(short channel) => AliveChannels.TryRemove(channel, out _);

    public void Announce() {
        foreach ((int channelId, ChannelClient channelClient) in ChannelClients) {
            try {
                channelClient.TimeEvent(new TimeEventRequest {
                    AnnounceFieldBoss = new TimeEventRequest.Types.AnnounceFieldBoss {
                        MetadataId = Boss.MetadataId,
                        EventId = Boss.Id,
                        EndTick = Boss.EndTick,
                        NextSpawnTimestamp = Boss.NextSpawnTimestamp,
                    },
                });

                AliveChannels.TryAdd((short) channelId, 0);
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
                    WarnFieldBoss = new TimeEventRequest.Types.WarnFieldBoss {
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
                    CloseFieldBoss = new TimeEventRequest.Types.CloseFieldBoss {
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
