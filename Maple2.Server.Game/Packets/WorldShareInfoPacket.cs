using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class WorldShareInfoPacket {
    private enum Function : byte {
        BossAlive = 0,
    }

    public static ByteWriter BossAlive(int npcId, int mapId, short channel, long timestamp, bool alive) {
        var pWriter = Packet.Of(SendOp.WorldShareInfo);
        pWriter.Write<Function>(Function.BossAlive);
        pWriter.WriteInt(npcId);
        pWriter.WriteInt(mapId);
        pWriter.WriteShort(channel);
        pWriter.WriteLong(timestamp);
        pWriter.WriteBool(alive);
        return pWriter;
    }
}
