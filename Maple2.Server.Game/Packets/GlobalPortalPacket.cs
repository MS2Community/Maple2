using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Packets;

public static class GlobalPortalPacket {
    private enum Command : byte {
        Announce = 0,
        Close = 1,
    }

    public static ByteWriter Announce(GlobalPortalMetadata portal, int uid) {
        var pWriter = Packet.Of(SendOp.GlobalPortal);
        pWriter.Write<Command>(Command.Announce);
        pWriter.WriteInt(portal.Id);
        pWriter.WriteInt(uid);
        pWriter.WriteUnicodeString(portal.PopupMessage);
        pWriter.WriteUnicodeString(portal.SoundId);
        foreach (GlobalPortalMetadata.Field entry in portal.Entries) {
            pWriter.WriteUnicodeString(entry.Name);
        }

        return pWriter;
    }

    public static ByteWriter Close(int id) {
        var pWriter = Packet.Of(SendOp.GlobalPortal);
        pWriter.Write<Command>(Command.Close);
        pWriter.WriteInt(id);

        return pWriter;
    }
}

