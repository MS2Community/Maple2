using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Commands;

public class FieldCommand : GameCommand {
    private readonly GameSession session;
    private readonly MapMetadataStorage mapStorage;
    private readonly MapEntityStorage mapEntities;

    public FieldCommand(GameSession session, MapMetadataStorage mapStorage, MapEntityStorage mapEntities) : base(AdminPermissions.Debug, "field", "Field information.") {
        this.session = session;
        this.mapStorage = mapStorage;
        this.mapEntities = mapEntities;

        AddCommand(new EntityInfoCommand(session));
        AddCommand(new SpawnPointsCommand(session, mapStorage, mapEntities));

        this.SetHandler<InvocationContext>(Handle);
    }

    private void Handle(InvocationContext ctx) {
        if (session.Field == null) {
            ctx.Console.Error.WriteLine("No active field");
            ctx.ExitCode = 1;
            return;
        }

        ctx.Console.Out.WriteLine($"Map: {session.Field.MapId}, Channel: {session.Player.Value.Character.Channel} RoomId: {session.Field.RoomId}, \n"
                                  + $"XBlock:({session.Field.Metadata.XBlock})");
    }

    private class EntityInfoCommand : Command {
        private readonly GameSession session;

        public EntityInfoCommand(GameSession session) : base("entity", "Prints entity info.") {
            this.session = session;

            var objectId = new Argument<int>("id", "ObjectId of the entity.");

            AddArgument(objectId);
            this.SetHandler<InvocationContext, int>(Handle, objectId);
        }

        private void Handle(InvocationContext ctx, int objectId) {
            if (session.Field == null) {
                ctx.Console.Error.WriteLine("No active field");
                ctx.ExitCode = 1;
                return;
            }

            if (session.Field.Npcs.TryGetValue(objectId, out FieldNpc? npc)) {
                ctx.Console.Out.WriteLine($"Npc: {npc.Value.Metadata.Id} ({npc.Value.Metadata.Name})");
                ctx.Console.Out.WriteLine($"  Position: {npc.Position}");
                ctx.Console.Out.WriteLine($"  Rotation: {npc.Rotation}");
            }
            if (session.Field.Mobs.TryGetValue(objectId, out FieldNpc? mob)) {
                ctx.Console.Out.WriteLine($"Mob: {mob.Value.Metadata.Id} ({mob.Value.Metadata.Name})");
                ctx.Console.Out.WriteLine($"  Position: {mob.Position}");
                ctx.Console.Out.WriteLine($"  Rotation: {mob.Rotation}");
            }
        }
    }

    private class SpawnPointsCommand : Command {
        private readonly GameSession session;
        private readonly MapMetadataStorage mapStorage;
        private readonly MapEntityStorage mapEntities;

        public SpawnPointsCommand(GameSession session, MapMetadataStorage mapStorage, MapEntityStorage mapEntities) : base("spawnpoints", "Prints all spawn points in the map.") {
            this.session = session;
            this.mapStorage = mapStorage;
            this.mapEntities = mapEntities;

            this.SetHandler<InvocationContext>(Handle);
        }

        private void Handle(InvocationContext ctx) {
            if (session.Field == null) {
                ctx.Console.Error.WriteLine("No active field");
                ctx.ExitCode = 1;
                return;
            }

            if (!mapStorage.TryGet(session.Field.MapId, out MapMetadata? map)) {
                ctx.Console.Out.WriteLine($"Unknown map: {session.Field.MapId}");
                return;
            }

            MapEntityMetadata? mapEntityMetadata = mapEntities.Get(map.XBlock);
            if (mapEntityMetadata == null) {
                ctx.Console.Out.WriteLine($"No xblock data found for map: {session.Field.MapId}");
                return;
            }
            List<FieldPlayerSpawnPoint> playerSpawns = session.Field.GetPlayerSpawns();
            foreach (FieldPlayerSpawnPoint playerSpawn in playerSpawns) {
                ctx.Console.Out.WriteLine($"Id: {playerSpawn.Id}, Position: {playerSpawn.Position}. Enabled: {playerSpawn.Enable}");
            }
        }
    }
}
