using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class NpcNoticePacket {
    private enum Command : byte {
        Announce = 0,
        TargetEffect = 1,
        Animation = 2, // play animation?
        SidePopup = 3,
    }

    public static ByteWriter Announce(string message, int duration) {
        var pWriter = Packet.Of(SendOp.NpcNotice);
        pWriter.Write<Command>(Command.Announce);
        pWriter.WriteUnicodeString(message);
        pWriter.WriteInt(duration);

        return pWriter;
    }

    public static ByteWriter TargetEffect(int targetId, string effect) {
        var pWriter = Packet.Of(SendOp.NpcNotice);
        pWriter.Write<Command>(Command.TargetEffect);
        pWriter.WriteInt(targetId);
        pWriter.WriteUnicodeString(effect);

        return pWriter;
    }

    public static ByteWriter Animation(int objectId, string sequence) {
        var pWriter = Packet.Of(SendOp.NpcNotice);
        pWriter.Write<Command>(Command.Animation);
        pWriter.WriteInt(objectId);
        pWriter.WriteUnicodeString(sequence);

        return pWriter;
    }

    public static ByteWriter SidePopup(NodePopupType type, int duration, string illustration, string voice, string script, string sound = "") {
        var pWriter = Packet.Of(SendOp.NpcNotice);
        pWriter.Write<Command>(Command.SidePopup);
        pWriter.Write<NodePopupType>(type);
        pWriter.WriteInt(duration);
        pWriter.WriteString();
        pWriter.WriteString(illustration);
        pWriter.WriteString(voice);
        pWriter.WriteString(sound); // sound?
        pWriter.WriteUnicodeString(script);

        return pWriter;
    }
}

