using Maple2.TestClient.Clients;
using Maple2.Tools;
using Serilog;

// Load .env for configuration (DB, server IPs, etc.)
DotEnv.Load();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

// Parse positional args
string host = args.Length > 0 ? args[0] : "127.0.0.1";
int port = args.Length > 1 ? int.Parse(args[1]) : 20001;
string username = args.Length > 2 ? args[2] : "testbot";
string password = args.Length > 3 ? args[3] : "testbot";

// Parse optional named args
int? npcId = null;
int skillId = 0;
short skillLevel = 1;
for (int i = 0; i < args.Length; i++) {
    switch (args[i]) {
        case "--npc" when i + 1 < args.Length:
            npcId = int.Parse(args[++i]);
            break;
        case "--skill" when i + 1 < args.Length:
            skillId = int.Parse(args[++i]);
            break;
        case "--skill-level" when i + 1 < args.Length:
            skillLevel = short.Parse(args[++i]);
            break;
    }
}

Log.Information("=== Maple2 TestClient ===");
Log.Information("Target: {Host}:{Port}, User: {Username}", host, port, username);
if (skillId != 0) Log.Information("Combat: SkillId={SkillId}, Level={Level}, NpcId={NpcId}", skillId, skillLevel, npcId?.ToString() ?? "none");

try {
    long accountId;
    GameServerInfo gameServer;
    Guid machineId;

    // Steps 1-3: Login and select character
    using (var loginClient = new LoginClient()) {
        Log.Information("--- Step 1: Connecting to Login Server ---");
        await loginClient.ConnectAsync(host, port);

        // Step 2: Login (auto-registers if AutoRegister=true)
        Log.Information("--- Step 2: Logging in ---");
        LoginResult result = await loginClient.LoginAsync(username, password);
        if (!result.Success) {
            Log.Error("Login failed: code={Code}, message={Message}", result.ErrorCode, result.ErrorMessage);
            return;
        }
        Log.Information("Logged in as AccountId={AccountId}", result.AccountId);

        // Step 3: Select character
        if (result.Characters.Count == 0) {
            Log.Information("No characters found. Create a character first using the game client.");
            return;
        }

        long characterId = result.Characters[0].CharacterId;
        Log.Information("--- Step 3: Selecting character {CharacterId} ---", characterId);
        gameServer = await loginClient.SelectCharacterAsync(characterId);
        Log.Information("Game server: {Address}:{Port}, Token={Token}, MapId={MapId}",
            gameServer.Address, gameServer.Port, gameServer.Token, gameServer.MapId);

        accountId = result.AccountId;
        machineId = loginClient.MachineId;
    } // LoginClient disposed here before GameServer connection

    // Step 4: Connect to GameServer and enter field
    Log.Information("--- Step 4: Connecting to Game Server ---");
    using var gameClient = new GameClient();
    await gameClient.ConnectAsync(gameServer, accountId, gameServer.Token, machineId);
    Log.Information("In game! MapId={MapId}, ObjectId={ObjectId}, Position={Position}", gameClient.MapId, gameClient.ObjectId, gameClient.Position);

    // Step 5 (optional): Spawn NPC via GM command
    NpcInfo? spawnedNpc = null;
    if (npcId.HasValue) {
        Log.Information("--- Step 5: Spawning NPC {NpcId} ---", npcId.Value);
        // Wait a moment for field initialization to complete
        await Task.Delay(500);
        spawnedNpc = await gameClient.SpawnNpcAsync(npcId.Value);
    }

    // Step 6: Cast skill
    if (skillId != 0) {
        Log.Information("--- Step 6: Casting skill {SkillId} (level {Level}) ---", skillId, skillLevel);
        await Task.Delay(300);
        long skillUid = await gameClient.CastSkillAsync(skillId, skillLevel);

        // Step 7 (optional): Attack target NPC
        int? targetObjectId = spawnedNpc?.ObjectId ?? gameClient.FieldNpcs.Values.FirstOrDefault()?.ObjectId;
        if (targetObjectId.HasValue) {
            Log.Information("--- Step 7: Attacking target ObjectId={TargetObjectId} ---", targetObjectId.Value);
            await Task.Delay(300);
            await gameClient.AttackTargetAsync(skillUid, targetObjectId.Value);
        } else {
            Log.Information("No target NPC available, skipping attack");
        }

        // Step 8: Summary
        Log.Information("--- Step 8: Combat simulation complete ---");
        Log.Information("Field NPCs tracked: {Count}", gameClient.FieldNpcs.Count);
    }

    // Stay alive until Ctrl+C
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => {
        e.Cancel = true;
        cts.Cancel();
    };
    await gameClient.StayAliveAsync(cts.Token);

    Log.Information("=== TestClient finished ===");
} catch (Exception ex) {
    Log.Error(ex, "TestClient error");
}
