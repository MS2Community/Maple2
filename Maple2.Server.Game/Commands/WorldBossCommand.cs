using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.Text;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.World.Service;

namespace Maple2.Server.Game.Commands;

public class WorldBossCommand : GameCommand {
    public WorldBossCommand(GameSession session) : base(AdminPermissions.Debug, "world-boss", "World boss debugging.") {
        AddAlias("boss");
        AddCommand(new ListCommand(session));
        AddCommand(new SpawnCommand(session));
        AddCommand(new DespawnCommand(session));
        AddCommand(new WarpCommand(session));
        AddCommand(new NextCommand(session));
    }

    private class NextCommand : Command {
        private readonly GameSession session;

        public NextCommand(GameSession session) : base("next", "Show the next world bosses that will spawn.") {
            this.session = session;
            this.SetHandler<InvocationContext>(Handle);
        }

        private void Handle(InvocationContext ctx) {
            IReadOnlyDictionary<int, WorldBossMetadata> bosses = session.ServerTableMetadata.TimeEventTable.WorldBoss;
            if (bosses.Count == 0) {
                ctx.Console.Out.WriteLine("No world bosses found in metadata.");
                return;
            }

            // Query World for currently active bosses
            TimeEventResponse response = session.World.TimeEvent(new TimeEventRequest {
                GetActiveWorldBosses = new TimeEventRequest.Types.GetActiveWorldBosses(),
            });
            Dictionary<int, TimeEventResponse.Types.ActiveWorldBoss> activeBosses = response.ActiveWorldBosses.ToDictionary(b => b.MetadataId);

            // Find next spawns for dead bosses
            var nextSpawns = new List<(WorldBossMetadata Metadata, long NextSpawnTs)>();
            foreach ((int _, WorldBossMetadata metadata) in bosses.OrderBy(kv => kv.Key)) {
                if (activeBosses.ContainsKey(metadata.Id)) continue; // skip alive
                long nextTs = WorldBossUtil.ComputeNextSpawnTimestamp(metadata);
                if (nextTs > 0) {
                    nextSpawns.Add((metadata, nextTs));
                }
            }

            if (nextSpawns.Count == 0) {
                ctx.Console.Out.WriteLine("No upcoming world boss spawns.");
                ctx.ExitCode = 0;
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{"ID",-6} {"NpcId",-10} {"Maps",-25} Next Spawn (UTC)");
            sb.AppendLine(new string('-', 55));

            foreach ((WorldBossMetadata metadata, long nextTs) in nextSpawns.OrderBy(x => x.NextSpawnTs).Take(5)) {
                int npcId = metadata.NpcIds.Length > 0 ? metadata.NpcIds[0] : 0;
                string maps = string.Join(",", metadata.TargetMapIds);
                string nextSpawn = DateTimeOffset.FromUnixTimeSeconds(nextTs).UtcDateTime.ToString("HH:mm:ss");
                sb.AppendLine($"{metadata.Id,-6} {npcId,-10} {maps,-25} {nextSpawn}");
            }

            ctx.Console.Out.WriteLine(sb.ToString());
            ctx.ExitCode = 0;
        }
    }

    /// <summary>List all world bosses with their active status and next spawn time.</summary>
    private class ListCommand : Command {
        private readonly GameSession session;

        public ListCommand(GameSession session) : base("list", "List all world bosses and their status.") {
            this.session = session;
            this.SetHandler<InvocationContext>(Handle);
        }

        private void Handle(InvocationContext ctx) {
            IReadOnlyDictionary<int, WorldBossMetadata> bosses = session.ServerTableMetadata.TimeEventTable.WorldBoss;
            if (bosses.Count == 0) {
                ctx.Console.Out.WriteLine("No world bosses found in metadata.");
                return;
            }

            // Query World for currently active bosses
            TimeEventResponse response = session.World.TimeEvent(new TimeEventRequest {
                GetActiveWorldBosses = new TimeEventRequest.Types.GetActiveWorldBosses(),
            });
            Dictionary<int, TimeEventResponse.Types.ActiveWorldBoss> activeBosses = response.ActiveWorldBosses.ToDictionary(b => b.MetadataId);

            var sb = new StringBuilder();
            sb.AppendLine($"{"ID",-6} {"NpcId",-10} {"Status",-10} {"Maps",-25} Next Spawn (UTC)");
            sb.AppendLine(new string('-', 75));

            foreach ((int _, WorldBossMetadata metadata) in bosses.OrderBy(kv => kv.Key)) {
                int npcId = metadata.NpcIds.Length > 0 ? metadata.NpcIds[0] : 0;
                string maps = string.Join(",", metadata.TargetMapIds);

                string status;
                string nextSpawn;
                if (activeBosses.TryGetValue(metadata.Id, out TimeEventResponse.Types.ActiveWorldBoss? active)) {
                    status = "ALIVE";
                    long nextTs = active.NextSpawnTimestamp;
                    nextSpawn = nextTs > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(nextTs).UtcDateTime.ToString("HH:mm:ss")
                        : "—";
                } else {
                    status = "dead";
                    long nextTs = WorldBossUtil.ComputeNextSpawnTimestamp(metadata);
                    nextSpawn = nextTs > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(nextTs).UtcDateTime.ToString("HH:mm:ss")
                        : "expired";
                }

                sb.AppendLine($"{metadata.Id,-6} {npcId,-10} {status,-10} {maps,-25} {nextSpawn}");
            }

            ctx.Console.Out.WriteLine(sb.ToString());
            ctx.ExitCode = 0;
        }
    }

    /// <summary>Force spawn a specific field boss on the current map.</summary>
    private class SpawnCommand : Command {
        private readonly GameSession session;

        public SpawnCommand(GameSession session) : base("spawn", "Force spawn a field boss on the current map.") {
            this.session = session;

            var metadataId = new Argument<int>("id", "Metadata ID of the boss to spawn (from 'boss list').");
            AddArgument(metadataId);
            this.SetHandler<InvocationContext, int>(Handle, metadataId);
        }

        private void Handle(InvocationContext ctx, int metadataId) {
            if (session.Field == null) {
                ctx.Console.Error.WriteLine("No field loaded.");
                return;
            }

            if (!session.ServerTableMetadata.TimeEventTable.WorldBoss.TryGetValue(metadataId, out WorldBossMetadata? metadata)) {
                ctx.Console.Error.WriteLine($"Unknown boss metadata ID: {metadataId}. Use 'boss list' to see valid IDs.");
                return;
            }

            if (!metadata.TargetMapIds.Contains(session.Field.MapId)) {
                string validMaps = string.Join(", ", metadata.TargetMapIds);
                ctx.Console.Error.WriteLine($"Boss {metadataId} does not spawn on map {session.Field.MapId}. Valid maps: {validMaps}");
                return;
            }

            // Use eventId = 0 for debug spawns (no World coordination)
            if (session.Field.SpawnWorldBoss(metadata, 0) == null) {
                ctx.Console.Error.WriteLine($"Failed to spawn boss {metadataId} — spawn point not found or already active.");
                return;
            }

            ctx.Console.Out.WriteLine($"Spawned boss {metadataId} (NpcId: {(metadata.NpcIds.Length > 0 ? metadata.NpcIds[0] : 0)}) on map {session.Field.MapId}.");
            ctx.ExitCode = 0;
        }
    }

    /// <summary>Force despawn the active world boss on the current map.</summary>
    private class DespawnCommand : Command {
        private readonly GameSession session;

        public DespawnCommand(GameSession session) : base("despawn", "Force despawn the active world boss on the current map.") {
            this.session = session;
            this.SetHandler<InvocationContext>(Handle);
        }

        private void Handle(InvocationContext ctx) {
            if (session.Field == null) {
                ctx.Console.Error.WriteLine("No field loaded.");
                return;
            }

            session.Field.DespawnWorldBoss();
            ctx.Console.Out.WriteLine($"Despawned world boss on map {session.Field.MapId}.");
            ctx.ExitCode = 0;
        }
    }

    /// <summary>Warp to a boss's target map.</summary>
    private class WarpCommand : Command {
        private readonly GameSession session;

        public WarpCommand(GameSession session) : base("warp", "Warp to a boss's target map.") {
            this.session = session;

            var metadataId = new Argument<int>("id", "Metadata ID of the boss to warp to.");
            AddArgument(metadataId);
            this.SetHandler<InvocationContext, int>(Handle, metadataId);
        }

        private void Handle(InvocationContext ctx, int metadataId) {
            if (!session.ServerTableMetadata.TimeEventTable.WorldBoss.TryGetValue(metadataId, out WorldBossMetadata? metadata)) {
                ctx.Console.Error.WriteLine($"Unknown boss metadata ID: {metadataId}. Use 'boss list' to see valid IDs.");
                return;
            }

            int targetMap = metadata.TargetMapIds.Length > 0 ? metadata.TargetMapIds[0] : 0;
            if (targetMap == 0) {
                ctx.Console.Error.WriteLine($"Boss {metadataId} has no target map.");
                return;
            }

            bool success = session.PrepareField(targetMap);
            session.Send(success
                ? Packets.FieldEnterPacket.Request(session.Player)
                : Packets.FieldEnterPacket.Error(Maple2.Model.Error.MigrationError.s_move_err_default));

            if (success) {
                ctx.Console.Out.WriteLine($"Warping to map {targetMap} for boss {metadataId}.");
            } else {
                ctx.Console.Error.WriteLine($"Failed to warp to map {targetMap}.");
            }
            ctx.ExitCode = 0;
        }
    }
}
