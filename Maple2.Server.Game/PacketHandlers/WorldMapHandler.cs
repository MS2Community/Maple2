using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;

namespace Maple2.Server.Game.PacketHandlers;

public class WorldMapHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestWorldmap;

    private enum Command : byte {
        Load = 0,
        Population = 1,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();

        switch (command) {
            case Command.Load:
                HandleLoad(session, packet);
                return;
            case Command.Population:
                HandlePopulation(session, packet);
                return;
        }
    }

    private static void HandleLoad(GameSession session, IByteReader packet) {
        packet.ReadByte();

        // 102 = Victoria, 103 = Karkar, 105 = Kritias
        int mapCode = packet.ReadInt();

        TimeEventResponse bossResponse = session.World.TimeEvent(new TimeEventRequest {
            GetActiveWorldBosses = new TimeEventRequest.Types.GetActiveWorldBosses(),
        });

        var bossGroups = new List<ICollection<MapWorldBoss>>();
        foreach (TimeEventResponse.Types.ActiveWorldBoss active in bossResponse.ActiveWorldBosses) {
            if (!session.ServerTableMetadata.TimeEventTable.WorldBoss.TryGetValue(active.MetadataId, out WorldBossMetadata? metadata)) {
                continue;
            }
            int npcId = metadata.NpcIds.Length > 0 ? metadata.NpcIds[0] : 0;
            int mapId = metadata.TargetMapIds.Length > 0 ? metadata.TargetMapIds[0] : 0;
            bossGroups.Add(active.AliveChannels.Select(ch => new MapWorldBoss(npcId, mapId, (short) ch, active.SpawnTimestamp)).ToList());
        }

        session.Send(WorldMapPacket.Load(bossGroups, []));
    }

    private static void HandlePopulation(GameSession session, IByteReader packet) {
        // 102 = Victoria, 103 = Karkar, 105 = Kritias
        int mapCode = packet.ReadInt();
        session.Send(WorldMapPacket.Population(new List<MapPopulation>()));
    }
}
