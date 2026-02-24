using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class FollowNpcPacket {
    public static ByteWriter FollowNpc(int npcObjectId) {
        var writer = Packet.Of(SendOp.FollowNpc);
        writer.WriteInt(npcObjectId);

        return writer;
    }
}
