using System.Collections.Concurrent;
using System.Numerics;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.TestClient.Network;
using Maple2.TestClient.Protocol;
using Serilog;

namespace Maple2.TestClient.Clients;

public record NpcInfo(int ObjectId, int NpcId, Vector3 Position);

/// <summary>
/// High-level client for interacting with the Game server.
/// Handles: connect → handshake → ResponseKey auth → enter field → stay alive (TimeSync + Heartbeat).
/// Also tracks field state (ObjectId, NPCs) and provides combat methods.
/// </summary>
public class GameClient : IDisposable {
    private static readonly ILogger Logger = Log.Logger.ForContext<GameClient>();

    private readonly MapleClient client = new();
    private long nextSkillUid = 1;

    public int MapId { get; private set; }
    public int ObjectId { get; private set; }
    public Vector3 Position { get; private set; }
    public ConcurrentDictionary<int, NpcInfo> FieldNpcs { get; } = new();

    /// <summary>
    /// Connect to the GameServer, authenticate, and enter the field.
    /// </summary>
    public async Task ConnectAsync(GameServerInfo serverInfo, long accountId, ulong token, Guid machineId) {
        string host = serverInfo.Address.ToString();
        ushort port = serverInfo.Port;

        await client.ConnectAsync(host, port);

        // Register persistent handlers before auth so we capture everything
        RegisterTimeSyncHandler();
        RegisterHeartbeatHandler();
        RegisterFieldAddUserHandler();
        RegisterFieldNpcHandlers();
        RegisterSkillDamageHandler();

        // Step 1: Send ResponseVersion, wait for RequestKey
        var requestKeyTask = client.WaitForPacketAsync(SendOp.RequestKey);
        client.Send(ClientPacket.ResponseVersion());
        Logger.Information("Sent ResponseVersion, waiting for RequestKey...");
        await requestKeyTask;
        Logger.Information("Received RequestKey");

        // Step 2: Send ResponseKey, wait for RequestFieldEnter
        var fieldEnterTask = client.WaitForPacketAsync(SendOp.RequestFieldEnter, TimeSpan.FromSeconds(30));
        client.Send(ClientPacket.ResponseKey(accountId, token, machineId));
        Logger.Information("Sent ResponseKey (AccountId={AccountId}), waiting for RequestFieldEnter...", accountId);
        byte[] fieldEnterRaw = await fieldEnterTask;
        var reader = new ByteReader(fieldEnterRaw, 0);
        reader.Read<SendOp>(); // skip opcode

        byte migrationError = reader.ReadByte();
        if (migrationError != 0) {
            throw new InvalidOperationException($"RequestFieldEnter failed: MigrationError={migrationError}");
        }

        int mapId = reader.ReadInt();
        MapId = mapId;

        // Parse position from RequestFieldEnter
        // After mapId: FieldType(byte) + InstanceType(byte) + InstanceId(int) + DungeonId(int) + Position(Vector3)
        reader.ReadByte(); // FieldType
        reader.ReadByte(); // InstanceType
        reader.ReadInt();  // InstanceId
        reader.ReadInt();  // DungeonId
        Position = reader.Read<Vector3>();
        Logger.Information("Received RequestFieldEnter, MapId={MapId}, Position={Position}", mapId, Position);

        // Step 3: Send ResponseFieldEnter to complete field entry
        client.Send(ClientPacket.ResponseFieldEnter());
        Logger.Information("Sent ResponseFieldEnter, entered field! MapId={MapId}", mapId);
    }

    /// <summary>
    /// Send a chat message or GM command.
    /// </summary>
    public void SendChat(string message) {
        client.Send(ClientPacket.Chat(message));
        Logger.Information("Sent chat: {Message}", message);
    }

    /// <summary>
    /// Spawn an NPC via GM command. Waits for FieldAddNpc confirmation.
    /// </summary>
    public async Task<NpcInfo?> SpawnNpcAsync(int npcId, TimeSpan? timeout = null) {
        timeout ??= TimeSpan.FromSeconds(5);
        var npcTask = client.WaitForPacketAsync(SendOp.FieldAddNpc, timeout);
        SendChat($"/npc {npcId}");
        try {
            byte[] raw = await npcTask;
            var r = new ByteReader(raw, 0);
            r.Read<SendOp>();
            int objectId = r.ReadInt();
            int id = r.ReadInt();
            var pos = r.Read<Vector3>();
            var info = new NpcInfo(objectId, id, pos);
            FieldNpcs[objectId] = info; // Manually add since one-shot waiter consumed the packet before persistent handler
            Logger.Information("NPC spawned: ObjectId={ObjectId}, NpcId={NpcId}, Position={Position}", objectId, id, pos);
            return info;
        } catch (TimeoutException) {
            Logger.Warning("SpawnNpc timeout — NPC {NpcId} may not have spawned (check admin permissions)", npcId);
            return null;
        }
    }

    /// <summary>
    /// Cast a skill (Skill.Use). Returns the skillUid for use in AttackTarget.
    /// Waits for SendOp.SkillUse broadcast to confirm server processed it.
    /// </summary>
    public async Task<long> CastSkillAsync(int skillId, short level = 1, TimeSpan? timeout = null) {
        timeout ??= TimeSpan.FromSeconds(5);
        long skillUid = Interlocked.Increment(ref nextSkillUid);
        int serverTick = Environment.TickCount;

        var skillUseTask = client.WaitForPacketAsync(SendOp.SkillUse, timeout);
        client.Send(ClientPacket.SkillUse(skillUid, serverTick, skillId, level, 0, Position, Vector3.UnitY, Vector3.Zero));
        Logger.Information("Sent Skill.Use: SkillId={SkillId}, Level={Level}, SkillUid={SkillUid}", skillId, level, skillUid);

        try {
            await skillUseTask;
            Logger.Information("Verified: Received SkillUse broadcast for SkillUid={SkillUid}", skillUid);
        } catch (TimeoutException) {
            Logger.Warning("Skill.Use verification timeout — server may not have processed SkillId={SkillId}", skillId);
        }

        return skillUid;
    }
    // PLACEHOLDER_GAMECLIENT_ATTACK

    /// <summary>
    /// Attack a target NPC (Skill.Attack.Target).
    /// Waits for SendOp.SkillDamage broadcast to confirm damage was applied.
    /// </summary>
    public async Task AttackTargetAsync(long skillUid, int targetObjectId, TimeSpan? timeout = null) {
        timeout ??= TimeSpan.FromSeconds(5);
        long targetUid = Interlocked.Increment(ref nextSkillUid);

        var damageTask = client.WaitForPacketAsync(SendOp.SkillDamage, timeout);
        client.Send(ClientPacket.SkillAttackTarget(skillUid, targetUid, Position, Vector3.UnitY, 0, 1, [targetObjectId]));
        Logger.Information("Sent Skill.Attack.Target: SkillUid={SkillUid}, TargetObjectId={TargetObjectId}", skillUid, targetObjectId);

        try {
            await damageTask;
            Logger.Information("Verified: Received SkillDamage broadcast for target ObjectId={TargetObjectId}", targetObjectId);
        } catch (TimeoutException) {
            Logger.Warning("Skill.Attack.Target verification timeout — damage may not have been applied to ObjectId={TargetObjectId}", targetObjectId);
        }
    }

    /// <summary>
    /// Stay connected by keeping the receive loop alive.
    /// </summary>
    public async Task StayAliveAsync(CancellationToken ct) {
        Logger.Information("Staying alive (Ctrl+C to exit)...");
        try {
            while (!ct.IsCancellationRequested) {
                await Task.Delay(1000, ct);
            }
        } catch (OperationCanceledException) {
            // Normal exit via cancellation
        }
        Logger.Information("StayAlive ended");
    }

    #region Persistent Handlers

    private void RegisterTimeSyncHandler() {
        client.On(SendOp.ResponseTimeSync, raw => {
            var r = new ByteReader(raw, 0);
            r.Read<SendOp>();
            byte command = r.ReadByte();
            if (command == 2) {
                client.Send(ClientPacket.RequestTimeSync(0));
            }
        });
    }
    // PLACEHOLDER_GAMECLIENT_HANDLERS

    private void RegisterHeartbeatHandler() {
        client.On(SendOp.RequestHeartbeat, raw => {
            var r = new ByteReader(raw, 0);
            r.Read<SendOp>();
            int serverTick = r.ReadInt();
            client.Send(ClientPacket.ResponseHeartbeat(serverTick, Environment.TickCount));
        });
    }

    private void RegisterFieldAddUserHandler() {
        bool first = true;
        client.On(SendOp.FieldAddUser, raw => {
            var r = new ByteReader(raw, 0);
            r.Read<SendOp>();
            int objectId = r.ReadInt();
            if (first) {
                ObjectId = objectId;
                first = false;
                Logger.Information("My ObjectId={ObjectId}", objectId);
            }
        });
    }

    private void RegisterFieldNpcHandlers() {
        client.On(SendOp.FieldAddNpc, raw => {
            var r = new ByteReader(raw, 0);
            r.Read<SendOp>();
            int objectId = r.ReadInt();
            int npcId = r.ReadInt();
            var pos = r.Read<Vector3>();
            FieldNpcs[objectId] = new NpcInfo(objectId, npcId, pos);
            Logger.Debug("FieldAddNpc: ObjectId={ObjectId}, NpcId={NpcId}", objectId, npcId);
        });

        client.On(SendOp.FieldRemoveNpc, raw => {
            var r = new ByteReader(raw, 0);
            r.Read<SendOp>();
            int objectId = r.ReadInt();
            FieldNpcs.TryRemove(objectId, out _);
            Logger.Debug("FieldRemoveNpc: ObjectId={ObjectId}", objectId);
        });
    }

    private void RegisterSkillDamageHandler() {
        client.On(SendOp.SkillDamage, raw => {
            var r = new ByteReader(raw, 0);
            r.Read<SendOp>();
            byte command = r.ReadByte();
            Logger.Debug("SkillDamage received: Command={Command}, Length={Length}", command, raw.Length);
        });
    }

    #endregion

    public void Dispose() {
        client.Dispose();
    }
}
