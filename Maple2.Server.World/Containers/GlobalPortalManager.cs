using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Channel.Service;
using Serilog;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.World.Containers;

public class GlobalPortalManager : IDisposable {
    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    // ReSharper disable UnusedAutoPropertyAccessor.Global
    public required GameStorage GameStorage { get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata { get; init; }
    public required ChannelClientLookup ChannelClients { get; init; }
    // ReSharper restore All
    // ReSharper restore UnusedAutoPropertyAccessor.Global
    #endregion

    public readonly GlobalPortal Portal;
    public int Channel;

    public readonly int[] RoomIds;


    public GlobalPortalManager(GlobalPortalMetadata metadata, int id, long endTick) {
        Portal = new GlobalPortal(metadata, id) {
            EndTick = endTick,
        };
        RoomIds = new int[metadata.Entries.Length];
    }

    public void CreateFields() {
        if (!ChannelClients.TryGetClient(ChannelClients.FirstChannel(), out ChannelClient? client)) {
            return;
        }

        Channel = ChannelClients.FirstChannel();

        for (int i = 0; i < RoomIds.Length; i++) {
            try {
                TimeEventResponse? response = client.TimeEvent(new TimeEventRequest {
                    GetField = new TimeEventRequest.Types.GetField {
                        MapId = Portal.Metadata.Entries[i].MapId,
                        RoomId = 0,
                    },
                });

                if (response.Field == null) {
                    continue;
                }
                RoomIds[i] = response.Field.RoomId;
            } catch (RpcException rpcException) {
                // If the channel is not available, we cannot create the field.
                if (rpcException.StatusCode == StatusCode.Unavailable) {
                    Log.Error("Channel {Channel} is unavailable for creating global portal fields: {Message}", Channel, rpcException.Message);
                    return;
                }
                Log.Error(rpcException, "Error creating global portal field for channel {Channel}: {Message}", Channel, rpcException.Message);
            } catch (Exception e) {
                Log.Error(e, "Unexpected error creating global portal field for channel {Channel}", Channel);
            }
        }

        foreach ((int channelId, ChannelClient channelClient) in ChannelClients) {
            channelClient.TimeEvent(new TimeEventRequest {
                AnnounceGlobalPortal = new TimeEventRequest.Types.AnnounceGlobalPortal {
                    MetadataId = Portal.MetadataId,
                    EventId = Portal.Id,
                },
            });
        }
    }

    public void Join(int mapId, int index) {
        if (!ChannelClients.TryGetClient(Channel, out ChannelClient? client)) {
            return;
        }

        // Check if current field has reached capacity. If so, create a new field.
        GlobalPortalMetadata.Field globalPortalMetadata = Portal.Metadata.Entries[index];
        TimeEventResponse response = client.TimeEvent(new TimeEventRequest {
            GetField = new TimeEventRequest.Types.GetField {
                MapId = globalPortalMetadata.MapId,
                RoomId = RoomIds[index],
            },
        });

        if (!ServerTableMetadata.InstanceFieldTable.Entries.TryGetValue(mapId, out InstanceFieldMetadata? instanceFieldMetadata)) {
            return;
        }

        if (response.Field.PlayerIds.Count >= instanceFieldMetadata.MaxCount) {
            TimeEventResponse? createResponse = client.TimeEvent(new TimeEventRequest {
                GetField = new TimeEventRequest.Types.GetField {
                    MapId = mapId,
                    RoomId = 0,
                },
            });

            if (createResponse.Field == null) {
                return;
            }
            RoomIds[index] = createResponse.Field.RoomId;
        }
    }

    public void Dispose() {
        foreach ((int channelId, ChannelClient channelClient) in ChannelClients) {
            channelClient.TimeEvent(new TimeEventRequest {
                CloseGlobalPortal = new TimeEventRequest.Types.CloseGlobalPortal {
                    EventId = Portal.MetadataId,
                },
            });
        }
    }
}
