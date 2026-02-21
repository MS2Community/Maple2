using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using System.Numerics;

namespace Maple2.Server.Game.Packets;

public static class NpcControlPacket {
    public const short ANI_JUMP_A = -2;
    public const short ANI_JUMP_B = -3;

    public static ByteWriter Control(params FieldNpc[] npcs) {
        var pWriter = Packet.Of(SendOp.NpcControl);
        pWriter.WriteShort((short) npcs.Length);

        foreach (FieldNpc npc in npcs) {
            using var buffer = new PoolByteWriter();
            buffer.NpcBuffer(npc);
            pWriter.WriteShort((short) buffer.Length);
            pWriter.WriteBytes(buffer.ToArray());
        }

        return pWriter;
    }

    public static ByteWriter CorpseHit(FieldNpc npc) {
        var pWriter = Packet.Of(SendOp.NpcControl);
        pWriter.WriteShort(1);

        using var buffer = new PoolByteWriter();
        buffer.WriteInt(npc.ObjectId);
        buffer.WriteByte(0); // flags = 0
        buffer.Write<Vector3S>(npc.Position);
        buffer.WriteShort((short) (npc.Transform.RotationAnglesDegrees.Z * 10));
        buffer.Write<Vector3S>(default); // velocity = (0,0,0)
        buffer.WriteShort(100); // animation speed = 100%

        if (npc.Value.IsBoss) {
            buffer.WriteInt(0);
        }

        buffer.Write<ActorState>(ActorState.Hit);
        buffer.WriteShort(-1); // seqId = -1
        buffer.WriteShort(npc.SequenceCounter);

        // ActorState.Hit extra data
        buffer.WriteFloat(0); // UnknownF1
        buffer.WriteFloat(0); // UnknownF2
        buffer.WriteFloat(0); // UnknownF3
        buffer.WriteByte(0);  // UnknownB

        pWriter.WriteShort((short) buffer.Length);
        pWriter.WriteBytes(buffer.ToArray());
        return pWriter;
    }

    public static ByteWriter Dead(FieldNpc npc) {
        var pWriter = Packet.Of(SendOp.NpcControl);
        pWriter.WriteShort(1);

        using var buffer = new PoolByteWriter();
        buffer.WriteInt(npc.ObjectId);
        buffer.WriteByte(0); // flags = 0 (no HP bar, no additional effects)
        buffer.Write<Vector3S>(npc.Position);
        buffer.WriteShort((short) (npc.Transform.RotationAnglesDegrees.Z * 10));
        buffer.Write<Vector3S>(default); // velocity = (0,0,0)
        buffer.WriteShort(100); // animation speed = 100%

        if (npc.Value.IsBoss) {
            buffer.WriteInt(0); // target id = 0
        }

        buffer.Write<ActorState>(ActorState.None);
        buffer.WriteShort(-1); // seqId = -1 (freeze in death pose)
        buffer.WriteShort(npc.SequenceCounter);

        pWriter.WriteShort((short) buffer.Length);
        pWriter.WriteBytes(buffer.ToArray());
        return pWriter;
    }

    public static ByteWriter Talk(FieldNpc npc) {
        var pWriter = Packet.Of(SendOp.NpcControl);
        pWriter.WriteShort(1);

        using var buffer = new PoolByteWriter();
        buffer.NpcBuffer(npc, isTalk: true);
        pWriter.WriteShort((short) buffer.Length);
        pWriter.WriteBytes(buffer.ToArray());

        return pWriter;
    }

    private static void NpcBuffer(this PoolByteWriter buffer, FieldNpc npc, bool isTalk = false) {
        buffer.WriteInt(npc.ObjectId);
        // Flags bit-1 (AdditionalEffectRelated), bit-2 (UIHpBarRelated)
        buffer.WriteByte(2);

        buffer.Write<Vector3S>(npc.Position);
        buffer.WriteShort((short) (npc.Transform.RotationAnglesDegrees.Z * 10));
        buffer.Write<Vector3S>(npc.MovementState.Velocity);
        buffer.WriteShort((short) (npc.Animation.SequenceSpeed * 100));

        if (npc.Value.IsBoss) {
            buffer.WriteInt(npc.BattleState.TargetId); // ObjectId of Player being targeted?
        }

        short defaultSequenceId = npc.Animation.IdleSequenceId;

        if (isTalk) {
            buffer.Write<ActorState>(ActorState.Talk);
            buffer.WriteShort(-1);
        } else {
            buffer.Write<ActorState>(npc.MovementState.State);
            buffer.WriteShort(npc.Animation.PlayingSequence?.Id ?? defaultSequenceId);
        }
        buffer.WriteShort(npc.SequenceCounter);

        // Animation (-2 = Jump_A, -3 = Jump_B)
        bool isJumpSequence = (npc.Animation.PlayingSequence?.Id ?? -1) is ANI_JUMP_A or ANI_JUMP_B;

        if (isJumpSequence) {
            bool isAbsolute = false;
            buffer.WriteBool(isAbsolute);

            if (isAbsolute) {
                buffer.Write<Vector3>(new Vector3(0, 0, 0)); // start pos
                buffer.Write<Vector3>(new Vector3(0, 0, 0)); // end pos
                buffer.WriteFloat(0); // angle
                buffer.WriteFloat(0); // scale
            } else {
                buffer.Write<Vector3>(new Vector3(0, 0, 0)); // end offset
            }

            buffer.Write<ActorState>(npc.MovementState.State);
        }

        switch (npc.MovementState.State) {
            case ActorState.Hit:
                buffer.WriteFloat(0); //UnknownF1;
                buffer.WriteFloat(0); //UnknownF2;
                buffer.WriteFloat(0); //UnknownF3;
                buffer.WriteByte(0); //UnknownB;
                break;
            case ActorState.Spawn:
                buffer.WriteInt(npc.SpawnPointId);
                break;
        }

        // Set -1 to continue previous animation
        npc.SequenceId = -1;
    }
}
