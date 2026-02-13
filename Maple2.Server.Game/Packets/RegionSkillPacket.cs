using System.Numerics;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class RegionSkillPacket {
    private enum Command : byte {
        Add = 0,
        Remove = 1,
    }

    public static ByteWriter Add(FieldSkill fieldSkill) {
        var pWriter = Packet.Of(SendOp.RegionSkill);
        pWriter.Write<Command>(Command.Add);
        pWriter.WriteInt(fieldSkill.ObjectId);
        pWriter.WriteInt(fieldSkill.ObjectId);
        pWriter.WriteInt(fieldSkill.NextTick.Truncate32());
        pWriter.WriteByte((byte) fieldSkill.Points.Length);
        foreach (Vector3 point in fieldSkill.Points) {
            pWriter.Write<Vector3>(point);
        }

        pWriter.WriteInt(fieldSkill.Value.Id);
        pWriter.WriteShort(fieldSkill.Value.Level);
        pWriter.WriteFloat(fieldSkill.UseDirection ? fieldSkill.Rotation.Z : 0); // RotationH
        pWriter.WriteFloat(); // RotationV / 100

        return pWriter;
    }

    public static ByteWriter Remove(int objectId) {
        var pWriter = Packet.Of(SendOp.RegionSkill);
        pWriter.Write<Command>(Command.Remove);
        pWriter.WriteInt(objectId);

        return pWriter;
    }
}
