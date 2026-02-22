using System.Numerics;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Network;

namespace Maple2.TestClient.Protocol;

/// <summary>
/// Constructs client-to-server packets (RecvOp).
/// These mirror what the real game client sends.
/// </summary>
public static class ClientPacket {
    private static ByteWriter Of(RecvOp opcode, int size = 128) {
        var packet = new ByteWriter(size);
        packet.Write<RecvOp>(opcode);
        return packet;
    }

    /// <summary>
    /// Response to handshake. Server expects: version(uint) + unknown(short) + locale.
    /// See: ResponseVersionHandler.cs
    /// </summary>
    public static ByteWriter ResponseVersion() {
        var pWriter = Of(RecvOp.ResponseVersion);
        pWriter.Write<uint>(12); // VERSION
        pWriter.WriteShort(47);  // unknown constant
        pWriter.Write<Locale>(Locale.NA);
        return pWriter;
    }

    /// <summary>
    /// Login request. Server expects: command(byte) + username(unicode) + password(unicode) + short(1) + machineId(Guid).
    /// See: LoginHandler.cs:30-44
    /// </summary>
    public static ByteWriter Login(string username, string password, Guid machineId) {
        var pWriter = Of(RecvOp.ResponseLogin);
        pWriter.WriteByte(2); // Command.CharacterList
        pWriter.WriteUnicodeString(username);
        pWriter.WriteUnicodeString(password);
        pWriter.WriteShort(1);
        pWriter.Write<Guid>(machineId);
        return pWriter;
    }

    /// <summary>
    /// Select a character to enter the game.
    /// See: CharacterManagementHandler.cs:74-76
    /// </summary>
    public static ByteWriter SelectCharacter(long characterId) {
        var pWriter = Of(RecvOp.CharacterManagement);
        pWriter.WriteByte(0); // Command.Select
        pWriter.WriteLong(characterId);
        pWriter.WriteShort(1); // world/channel
        return pWriter;
    }

    /// <summary>
    /// Response key sent to GameServer after migration.
    /// See: ResponseKeyHandler.cs:25-27
    /// </summary>
    public static ByteWriter ResponseKey(long accountId, ulong token, Guid machineId) {
        var pWriter = Of(RecvOp.ResponseKey);
        pWriter.WriteLong(accountId);
        pWriter.Write<ulong>(token);
        pWriter.Write<Guid>(machineId);
        return pWriter;
    }

    /// <summary>
    /// Response to RequestFieldEnter after EnterServer completes.
    /// See: FieldEnterHandler.cs:13 — expects FIELD_KEY
    /// </summary>
    public static ByteWriter ResponseFieldEnter() {
        var pWriter = Of(RecvOp.ResponseFieldEnter);
        pWriter.WriteInt(Session.FIELD_KEY);
        return pWriter;
    }

    /// <summary>
    /// Time sync request sent in response to server's TimeSyncPacket.Request().
    /// See: TimeSyncHandler.cs:12 — expects key(int)
    /// </summary>
    public static ByteWriter RequestTimeSync(int key = 0) {
        var pWriter = Of(RecvOp.RequestTimeSync);
        pWriter.WriteInt(key);
        return pWriter;
    }

    /// <summary>
    /// Heartbeat response sent in response to server's RequestPacket.Heartbeat().
    /// See: ResponseHeartbeatHandler.cs:13-14 — expects serverTick(int) + clientTick(int)
    /// </summary>
    public static ByteWriter ResponseHeartbeat(int serverTick, int clientTick) {
        var pWriter = Of(RecvOp.ResponseHeartbeat);
        pWriter.WriteInt(serverTick);
        pWriter.WriteInt(clientTick);
        return pWriter;
    }

    /// <summary>
    /// Send a chat message or GM command (e.g. "/npc 21000001").
    /// See: UserChatHandler.cs:28-33
    /// </summary>
    public static ByteWriter Chat(string message) {
        var pWriter = Of(RecvOp.UserChat);
        pWriter.Write<ChatType>(ChatType.Normal);
        pWriter.WriteUnicodeString(message);
        pWriter.WriteUnicodeString(string.Empty); // recipient
        pWriter.WriteLong(0); // clubId
        return pWriter;
    }

    /// <summary>
    /// Cast a skill (Skill.Use). Server reads in SkillHandler.HandleUse.
    /// See: SkillHandler.cs:73-131
    /// </summary>
    public static ByteWriter SkillUse(long skillUid, int serverTick, int skillId, short level,
        byte motionPoint, Vector3 position, Vector3 direction, Vector3 rotation) {
        var pWriter = Of(RecvOp.Skill, 256);
        pWriter.WriteByte(0); // Command.Use
        pWriter.WriteLong(skillUid);
        pWriter.WriteInt(serverTick);
        pWriter.WriteInt(skillId);
        pWriter.WriteShort(level);
        pWriter.WriteByte(motionPoint);
        pWriter.Write<Vector3>(position);
        pWriter.Write<Vector3>(direction);
        pWriter.Write<Vector3>(rotation);
        pWriter.WriteFloat(0f); // rotate2Z
        pWriter.WriteInt(Environment.TickCount); // clientTick
        pWriter.WriteBool(false); // unknown
        pWriter.WriteLong(0); // itemUid
        pWriter.WriteBool(false); // isHold
        return pWriter;
    }

    /// <summary>
    /// Attack targets (Skill.Attack.Target). Server reads in SkillHandler.HandleTarget.
    /// See: SkillHandler.cs:188-260
    /// </summary>
    public static ByteWriter SkillAttackTarget(long skillUid, long targetUid,
        Vector3 impactPosition, Vector3 direction, byte attackPoint,
        byte targetCount, int[] targetObjectIds) {
        if (targetObjectIds.Length < targetCount) {
            throw new ArgumentException($"targetObjectIds length ({targetObjectIds.Length}) must be >= targetCount ({targetCount})");
        }
        var pWriter = Of(RecvOp.Skill, 256);
        pWriter.WriteByte(1); // Command.Attack
        pWriter.WriteByte(1); // SubCommand.Target
        pWriter.WriteLong(skillUid);
        pWriter.WriteLong(targetUid);
        pWriter.Write<Vector3>(impactPosition); // impactPos
        pWriter.Write<Vector3>(impactPosition); // impactPos2
        pWriter.Write<Vector3>(direction);
        pWriter.WriteByte(attackPoint);
        pWriter.WriteByte(targetCount);
        pWriter.WriteInt(0); // iterations
        for (int i = 0; i < targetCount; i++) {
            pWriter.WriteInt(targetObjectIds[i]);
            pWriter.WriteByte(0); // unknown
        }
        return pWriter;
    }
}
