# Maple2.TestClient

Headless bot client for automated server testing. Simulates the real MapleStory2 client protocol to interact with the server without a game client.

## Purpose

- Automate system testing (ST) that previously required manual game client interaction
- Simulate login, character selection, field entry, and combat flows via code
- Enable future CI/automated regression testing and load testing

## Building & Running

```bash
# Build
dotnet build Maple2.TestClient/Maple2.TestClient.csproj

# Run (basic login + enter game)
dotnet run --project Maple2.TestClient -- [host] [port] [username] [password]

# Run with combat simulation
dotnet run --project Maple2.TestClient -- 127.0.0.1 20001 testbot testbot --skill 10000001

# Spawn NPC and attack it
dotnet run --project Maple2.TestClient -- 127.0.0.1 20001 testbot testbot --npc 21000001 --skill 10000001 --skill-level 1
```

Defaults: host=`127.0.0.1`, port=`20001`, username=`testbot`, password=`testbot`.

Requires a running server stack (World + Login + Game) and an existing character on the account (character creation is not implemented).

## Architecture

```
Maple2.TestClient/
├── Network/
│   └── MapleClient.cs        # Low-level TCP + MapleCipher encryption/decryption + packet dispatch
├── Clients/
│   ├── LoginClient.cs         # Login server flow (handshake → login → character list → select)
│   └── GameClient.cs          # Game server flow (auth → field entry → combat → stay alive)
├── Protocol/
│   └── ClientPacket.cs        # Client-to-server packet constructors (RecvOp packets)
└── Program.cs                 # Entry point with CLI arg parsing and full flow orchestration
```

### Dependencies

- `Maple2.Server.Core` — SendOp/RecvOp enums, MapleCipher (via Maple2.PacketLib NuGet), ByteWriter/ByteReader
- `Maple2.Model` — Game enums (Locale, ChatType, etc.)
- `Serilog.Sinks.Console` — Logging

## Protocol Flow

```
=== Login Phase ===
TCP Connect → LoginServer (port 20001)
Server → Handshake (RequestVersion + VERSION + RIV + SIV + BLOCK_IV + PatchType)
Client → ResponseVersion (version=12, unknown=47, locale=NA)
Server → RequestLogin
Client → ResponseLogin (command=CharacterList, username, password, machineId)
Server → LoginResult + CharacterList packets
Client → CharacterManagement (command=Select, characterId)
Server → LoginToGame (gameServerIP, port, token, mapId)

=== Game Phase ===
TCP Connect → GameServer (port from LoginToGame)
Server → Handshake
Client → ResponseVersion
Server → RequestKey
Client → ResponseKey (accountId, token, machineId)
Server → [initialization packets] → RequestFieldEnter
Client → ResponseFieldEnter (FIELD_KEY=0x1234)
Server → [field state packets] → player is in game

=== Stay Alive ===
Server → ResponseTimeSync (periodic) → Client → RequestTimeSync
Server → RequestHeartbeat → Client → ResponseHeartbeat
```

## Key Classes

### MapleClient (Network/MapleClient.cs)

Low-level network client handling:
- TCP connection and handshake parsing (6-byte header + 19-byte payload)
- MapleCipher IV initialization (server RIV = client send IV, server SIV = client recv IV)
- IV sync: feeds raw handshake bytes through `TryDecrypt` to align with server's cipher state
- Background receive thread with `SendOp`-based dispatch
- `WaitForPacketAsync(SendOp)` — one-shot async waiter (register BEFORE sending to avoid race conditions)
- `On(SendOp, handler)` — persistent packet handler registration

### LoginClient (Clients/LoginClient.cs)

High-level login flow:
- `ConnectAsync()` — TCP connect + handshake + version exchange
- `LoginAsync()` — send credentials, parse LoginResult, collect CharacterList (waits for EndList command=4)
- `SelectCharacterAsync()` — select character, return GameServerInfo (IP, port, token, mapId)

### GameClient (Clients/GameClient.cs)

High-level game flow:
- `ConnectAsync()` — auth via ResponseKey, wait for RequestFieldEnter, send ResponseFieldEnter
- `CastSkillAsync()` — send Skill.Use, wait for SkillUse broadcast confirmation
- `AttackTargetAsync()` — send Skill.Attack.Target, wait for SkillDamage broadcast confirmation
- `SpawnNpcAsync()` — send GM `/npc` command, wait for FieldAddNpc
- `StayAliveAsync()` — respond to TimeSync and Heartbeat until cancelled
- Tracks field state: ObjectId, MapId, Position, FieldNpcs dictionary

### ClientPacket (Protocol/ClientPacket.cs)

Static packet constructors for all client-to-server packets:
- `ResponseVersion`, `Login`, `SelectCharacter`, `ResponseKey`
- `ResponseFieldEnter`, `RequestTimeSync`, `ResponseHeartbeat`
- `Chat`, `SkillUse`, `SkillAttackTarget`

## Implementation Notes

- MapleCipher IV direction is inverted between client and server: server's RIV is the client's send IV
- After handshake, `recvCipher` must be synced by feeding the raw handshake through `TryDecrypt` once
- Always register `WaitForPacketAsync` BEFORE calling `Send` to avoid race conditions with the receive thread
- Character creation is not implemented — requires an existing character in the database
- ObjectId is 0 at field entry time; it arrives later via FieldAddUser broadcast
- The first FieldAddUser received is assumed to be the bot's own ObjectId

## Known Limitations

- Single character per account assumed for character list parsing
- No character creation support
- ObjectId not available until FieldAddUser arrives (after ConnectAsync returns)
- GM permissions required for `/npc` spawn command

## CLI Arguments

| Position/Flag | Description | Default |
|---|---|---|
| arg[0] | Login server host | `127.0.0.1` |
| arg[1] | Login server port | `20001` |
| arg[2] | Username | `testbot` |
| arg[3] | Password | `testbot` |
| `--skill <id>` | Skill ID to cast | (none) |
| `--skill-level <n>` | Skill level | `1` |
| `--npc <id>` | NPC ID to spawn via GM command | (none) |
