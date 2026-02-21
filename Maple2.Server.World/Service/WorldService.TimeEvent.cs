using Grpc.Core;
using Maple2.Model.Metadata;
using Maple2.Server.World.Containers;

namespace Maple2.Server.World.Service;

public partial class WorldService {
    public override Task<TimeEventResponse> TimeEvent(TimeEventRequest request, ServerCallContext context) {
        switch (request.TimeEventCase) {
            case TimeEventRequest.TimeEventOneofCase.JoinGlobalPortal:
                return Task.FromResult(JoinGlobalPortal(request.JoinGlobalPortal));
            case TimeEventRequest.TimeEventOneofCase.GetGlobalPortal:
                return Task.FromResult(GetGlobalPortal(request.GetGlobalPortal));
            case TimeEventRequest.TimeEventOneofCase.GetActiveWorldBosses:
                return Task.FromResult(GetActiveWorldBosses());
            case TimeEventRequest.TimeEventOneofCase.WorldBossKilled:
                return Task.FromResult(OnWorldBossKilled(request.WorldBossKilled));
            default:
                return Task.FromResult(new TimeEventResponse());
        }
    }

    private TimeEventResponse OnWorldBossKilled(TimeEventRequest.Types.WorldBossKilled kill) {
        worldBossLookup.RemoveChannel(kill.MetadataId, (short) kill.Channel);
        return new TimeEventResponse();
    }

    private TimeEventResponse GetActiveWorldBosses() {
        var response = new TimeEventResponse();
        foreach (WorldBossManager manager in worldBossLookup.GetAll()) {
            var entry = new TimeEventResponse.Types.ActiveWorldBoss {
                MetadataId = manager.Boss.MetadataId,
                EventId = manager.Boss.Id,
                SpawnTimestamp = manager.Boss.SpawnTimestamp,
                NextSpawnTimestamp = manager.Boss.NextSpawnTimestamp,
            };
            entry.AliveChannels.AddRange(manager.AliveChannels.Keys);
            response.ActiveWorldBosses.Add(entry);
        }
        return response;
    }

    private TimeEventResponse JoinGlobalPortal(TimeEventRequest.Types.JoinGlobalPortal portal) {
        if (!globalPortalLookup.TryGet(out GlobalPortalManager? manager) || manager.Portal.MetadataId != portal.EventId) {
            return new TimeEventResponse();
        }

        GlobalPortalMetadata.Field fieldMetadata = manager.Portal.Metadata.Entries[portal.Index];

        if (fieldMetadata.MapId == 0) {
            return new TimeEventResponse();
        }

        manager.Join(fieldMetadata.MapId, portal.Index);
        int roomId = manager.RoomIds[portal.Index];
        return new TimeEventResponse {
            GlobalPortalInfo = new GlobalPortalInfo {
                Channel = manager.Channel,
                RoomId = roomId,
                MapId = fieldMetadata.MapId,
                PortalId = fieldMetadata.PortalId,
            },
        };
    }

    private TimeEventResponse GetGlobalPortal(TimeEventRequest.Types.GetGlobalPortal portal) {
        if (!globalPortalLookup.TryGet(out GlobalPortalManager? manager)) {
            return new TimeEventResponse();
        }

        return new TimeEventResponse {
            GlobalPortalInfo = new GlobalPortalInfo {
                MetadataId = manager.Portal.MetadataId,
                EventId = manager.Portal.Id,
            },
        };
    }
}
