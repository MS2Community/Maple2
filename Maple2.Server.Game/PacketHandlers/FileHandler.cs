using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Game.PacketHandlers;

public class FileHashHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.FileHash;

    public override void Handle(GameSession session, IByteReader packet) {
        packet.ReadInt();
        string filename = packet.ReadString();
        string md5 = packet.ReadString();

        Log.Logger.Debug("Hash for {filename}: {md5}", filename, md5);
    }
}
