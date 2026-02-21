using System.Collections.Concurrent;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Channel.Service;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Service;

public partial class ChannelService {
    private readonly ConcurrentDictionary<int, byte> monitoringBossFields = new();
    public override Task<TimeEventResponse> TimeEvent(TimeEventRequest request, ServerCallContext context) {
        switch (request.TimeEventCase) {
            case TimeEventRequest.TimeEventOneofCase.AnnounceGlobalPortal:
                return Task.FromResult(AnnounceGlobalPortal(request.AnnounceGlobalPortal));
            case TimeEventRequest.TimeEventOneofCase.CloseGlobalPortal:
                return Task.FromResult(CloseGlobalPortal(request.CloseGlobalPortal));
            case TimeEventRequest.TimeEventOneofCase.GetField:
                return Task.FromResult(GetField(request.GetField));
            case TimeEventRequest.TimeEventOneofCase.AnnounceFieldBoss:
                return Task.FromResult(AnnounceFieldBoss(request.AnnounceFieldBoss));
            case TimeEventRequest.TimeEventOneofCase.CloseFieldBoss:
                return Task.FromResult(CloseFieldBoss(request.CloseFieldBoss));
            case TimeEventRequest.TimeEventOneofCase.WarnFieldBoss:
                return Task.FromResult(WarnFieldBoss(request.WarnFieldBoss));
            default:
                return Task.FromResult(new TimeEventResponse());
        }
    }

    private TimeEventResponse AnnounceGlobalPortal(TimeEventRequest.Types.AnnounceGlobalPortal portal) {
        if (!serverTableMetadata.TimeEventTable.GlobalPortal.TryGetValue(portal.MetadataId, out GlobalPortalMetadata? metadata)) {
            return new TimeEventResponse();
        }
        foreach (GameSession session in server.GetSessions()) {
            if (session.Field?.Metadata.Property.Type is >= MapType.None and <= MapType.Telescope or >= MapType.Alikar and <= MapType.Shelter) {
                session.Send(GlobalPortalPacket.Announce(metadata, portal.EventId));
            }
        }
        return new TimeEventResponse();
    }

    private TimeEventResponse CloseGlobalPortal(TimeEventRequest.Types.CloseGlobalPortal portal) {
        foreach (GameSession session in server.GetSessions()) {
            session.Send(GlobalPortalPacket.Close(portal.EventId));
        }
        return new TimeEventResponse();
    }

    private TimeEventResponse AnnounceFieldBoss(TimeEventRequest.Types.AnnounceFieldBoss boss) {
        if (!serverTableMetadata.TimeEventTable.FieldBoss.TryGetValue(boss.MetadataId, out FieldBossMetadata? metadata)) {
            return new TimeEventResponse();
        }

        int npcId = metadata.NpcIds.Length > 0 ? metadata.NpcIds[0] : 0;
        int mapId = metadata.TargetMapIds.Length > 0 ? metadata.TargetMapIds[0] : 0;
        short channel = GameServer.GetChannel();
        long spawnTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (GameSession session in server.GetSessions()) {
            session.Send(WorldShareInfoPacket.BossAlive(npcId, mapId, channel, spawnTimestamp, true));
            session.Send(LegionBattlePacket.Update(boss.EventId, npcId, boss.NextSpawnTimestamp));
        }

        // Spawn the boss NPC in each target map
        foreach (int targetMapId in metadata.TargetMapIds) {
            FieldManager? field = server.GetField(targetMapId);
            field?.SpawnFieldBoss(metadata, boss.EventId);
        }

        return new TimeEventResponse();
    }

    private TimeEventResponse WarnFieldBoss(TimeEventRequest.Types.WarnFieldBoss boss) {
        if (!serverTableMetadata.TimeEventTable.FieldBoss.TryGetValue(boss.MetadataId, out FieldBossMetadata? metadata)) {
            return new TimeEventResponse();
        }

        int npcId = metadata.NpcIds.Length > 0 ? metadata.NpcIds[0] : 0;

        foreach (int targetMapId in metadata.TargetMapIds) {
            FieldManager? field = server.GetField(targetMapId);
            if (field == null) continue;
            FieldNpc? bossNpc = field.GetFieldBossNpc();
            if (bossNpc == null) continue; // Already dead on this channel
            field.Broadcast(NoticePacket.Message(new InterfaceText(StringCode.s_timeevent_boss_lifetimetext1, npcId.ToString())));
        }

        return new TimeEventResponse();
    }

    private TimeEventResponse CloseFieldBoss(TimeEventRequest.Types.CloseFieldBoss boss) {
        if (!serverTableMetadata.TimeEventTable.FieldBoss.TryGetValue(boss.MetadataId, out FieldBossMetadata? metadata)) {
            return new TimeEventResponse();
        }

        int npcId = metadata.NpcIds.Length > 0 ? metadata.NpcIds[0] : 0;

        foreach (int targetMapId in metadata.TargetMapIds) {
            FieldManager? field = server.GetField(targetMapId);
            if (field == null) continue;

            FieldNpc? bossNpc = field.GetFieldBossNpc();
            if (bossNpc == null) continue;

            long idleMs = bossNpc.LastDamageTick > 0
                ? Environment.TickCount64 - bossNpc.LastDamageTick
                : long.MaxValue;

            if (idleMs < (long) Constant.FieldBossIdleWarningThreshold.TotalMilliseconds) {
                if (monitoringBossFields.TryAdd(targetMapId, 0)) {
                    _ = MonitorExtendedBossLifetimeAsync(field, npcId, targetMapId);
                }
            } else {
                field.Broadcast(NoticePacket.Message(new InterfaceText(StringCode.s_timeevent_boss_lifetimetext2, npcId.ToString())));
                field.DespawnFieldBoss();
            }
        }

        return new TimeEventResponse();
    }

    private async Task MonitorExtendedBossLifetimeAsync(FieldManager field, int npcId, int targetMapId) {
        bool warningSent = false;
        try {
            while (true) {
                await Task.Delay(Constant.FieldBossMonitorInterval);

                FieldNpc? bossNpc = field.GetFieldBossNpc();
                if (bossNpc == null) return;

                long idleMs = bossNpc.LastDamageTick == 0
                    ? long.MaxValue
                    : Environment.TickCount64 - bossNpc.LastDamageTick;

                if (!warningSent && idleMs >= (long) Constant.FieldBossIdleWarningThreshold.TotalMilliseconds) {
                    field.Broadcast(NoticePacket.Message(new InterfaceText(StringCode.s_timeevent_boss_lifetimetext1, npcId.ToString())));
                    warningSent = true;
                }

                if (idleMs >= (long) Constant.FieldBossDespawnThreshold.TotalMilliseconds) {
                    field.Broadcast(NoticePacket.Message(new InterfaceText(StringCode.s_timeevent_boss_lifetimetext2, npcId.ToString())));
                    field.DespawnFieldBoss();
                    return;
                }
            }
        } catch (Exception ex) {
            logger.Error(ex, "Error monitoring field boss lifetime for NPC {NpcId}", npcId);
        } finally {
            monitoringBossFields.TryRemove(targetMapId, out _);
        }
    }

    private TimeEventResponse GetField(TimeEventRequest.Types.GetField field) {
        FieldManager? manager = server.GetField(field.MapId, field.RoomId);
        if (manager == null) {
            return new TimeEventResponse();
        }

        return new TimeEventResponse {
            Field = new FieldInfo {
                MapId = manager.MapId,
                RoomId = manager.RoomId,
                OwnerId = manager is HomeFieldManager homeManager ? homeManager.OwnerId : 0,
                PlayerIds = {
                    manager.Players.Values.Select(player => player.Value.Character.Id),
                },
            },
        };
    }
}
