using System.Net;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.TestClient.Network;
using Maple2.TestClient.Protocol;
using Serilog;

namespace Maple2.TestClient.Clients;

/// <summary>
/// Result of a login attempt.
/// </summary>
public record LoginResult(bool Success, long AccountId, List<CharacterInfo> Characters, byte ErrorCode = 0, string ErrorMessage = "");

/// <summary>
/// Basic character info parsed from CharacterList packet.
/// </summary>
public record CharacterInfo(long CharacterId, string Name);

/// <summary>
/// Game server connection info returned after character selection.
/// </summary>
public record GameServerInfo(IPAddress Address, ushort Port, ulong Token, int MapId);

/// <summary>
/// High-level client for interacting with the Login server.
/// Handles: connect → handshake → login/register → character list → character select.
/// </summary>
public class LoginClient : IDisposable {
    private static readonly ILogger Logger = Log.Logger.ForContext<LoginClient>();

    private readonly MapleClient client = new();
    public Guid MachineId { get; } = Guid.NewGuid();

    public long AccountId { get; private set; }

    /// <summary>
    /// Connect to the login server and complete the version handshake.
    /// </summary>
    public async Task ConnectAsync(string host = "127.0.0.1", int port = 20001) {
        await client.ConnectAsync(host, port);

        // Register waiter BEFORE sending to avoid race condition
        var requestLoginTask = client.WaitForPacketAsync(SendOp.RequestLogin);

        client.Send(ClientPacket.ResponseVersion());
        Logger.Information("Sent ResponseVersion, waiting for RequestLogin...");

        await requestLoginTask;
        Logger.Information("Received RequestLogin prompt");
    }

    /// <summary>
    /// Login with username/password. If AutoRegister is enabled on the server,
    /// a new account will be created automatically for unknown usernames.
    /// </summary>
    public async Task<LoginResult> LoginAsync(string username, string password) {
        // Collect CharacterList packets via persistent handler
        var characters = new List<CharacterInfo>();
        var charListDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.On(SendOp.CharacterList, raw => {
            var r = new ByteReader(raw, 0);
            r.Read<SendOp>(); // skip opcode
            byte command = r.ReadByte();
            switch (command) {
                case 0: // List - contains character entries
                    byte count = r.ReadByte();
                    if (count > 1) {
                        Logger.Warning("Multiple characters ({Count}) found; only first will be parsed correctly", count);
                    }
                    for (int i = 0; i < count; i++) {
                        long charId = ParseCharacterId(r);
                        characters.Add(new CharacterInfo(charId, ""));
                        if (i == 0 && count > 1) break; // avoid corrupted reads
                    }
                    break;
                case 4: // EndList
                    charListDone.TrySetResult();
                    break;
            }
        });

        // Register waiter BEFORE sending to avoid race condition
        var loginResultTask = client.WaitForPacketAsync(SendOp.LoginResult);

        client.Send(ClientPacket.Login(username, password, MachineId));
        Logger.Information("Sent login request for user: {Username}", username);

        byte[] raw = await loginResultTask;
        var reader = new ByteReader(raw, 0);
        reader.Read<SendOp>(); // skip opcode

        byte loginState = reader.ReadByte();
        int constVal = reader.ReadInt();
        string banReason = reader.ReadUnicodeString();
        long accountId = reader.ReadLong();

        if (loginState != 0) {
            Logger.Warning("Login failed: state={State}, reason={Reason}", loginState, banReason);
            return new LoginResult(false, accountId, [], loginState, banReason);
        }

        AccountId = accountId;
        Logger.Information("Login successful! AccountId={AccountId}", accountId);

        // Wait for character list to finish (StartList → List → EndList)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        cts.Token.Register(() => charListDone.TrySetCanceled());
        await charListDone.Task;

        Logger.Information("Received character list: {Count} character(s)", characters.Count);
        foreach (var c in characters) {
            Logger.Information("  CharacterId={CharacterId}", c.CharacterId);
        }

        return new LoginResult(true, accountId, characters);
    }

    /// <summary>
    /// Parse character ID from the CharacterList.List entry.
    /// The entry starts with WriteCharacter which begins with accountId(long) + characterId(long) + name(unicode).
    /// We skip accountId and read characterId, then skip the rest.
    /// Since the entry is complex and variable-length, we only extract the characterId.
    /// </summary>
    private static long ParseCharacterId(ByteReader reader) {
        // WriteCharacter format starts with:
        // accountId(long) + characterId(long) + name(unicodeString) + ...
        reader.ReadLong(); // accountId
        long characterId = reader.ReadLong();
        string name = reader.ReadUnicodeString();
        // We can't easily skip the rest of the variable-length entry,
        // so for now we just return what we have. This works for single-character accounts.
        return characterId;
    }

    /// <summary>
    /// Select a character and get the game server connection info.
    /// The characterId must be a valid character belonging to the logged-in account.
    /// </summary>
    public async Task<GameServerInfo> SelectCharacterAsync(long characterId) {
        // Register waiter BEFORE sending to avoid race condition
        var loginToGameTask = client.WaitForPacketAsync(SendOp.LoginToGame, TimeSpan.FromSeconds(10));

        client.Send(ClientPacket.SelectCharacter(characterId));
        Logger.Information("Sent character select for CharacterId={CharacterId}", characterId);

        byte[] raw = await loginToGameTask;
        var reader = new ByteReader(raw, 0);
        reader.Read<SendOp>(); // skip opcode

        // MigrationError is a byte enum
        byte error = reader.ReadByte();
        if (error != 0) {
            throw new InvalidOperationException($"Character select failed: MigrationError={error}");
        }

        // Success: ip(4 bytes) + port(ushort) + token(ulong) + mapId(int)
        byte[] ipBytes = reader.ReadBytes(4);
        var address = new IPAddress(ipBytes);
        ushort port = reader.Read<ushort>();
        ulong token = reader.Read<ulong>();
        int mapId = reader.ReadInt();

        var info = new GameServerInfo(address, port, token, mapId);
        Logger.Information("Character selected! GameServer={Address}:{Port}, MapId={MapId}", address, port, mapId);

        return info;
    }

    public void Dispose() {
        client.Dispose();
    }
}
