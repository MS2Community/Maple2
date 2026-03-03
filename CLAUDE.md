# CLAUDE.md

MapleStory2 server emulator in C# (.NET 8.0+). Distributed architecture with World (gRPC coordinator), Login, Game (channel servers), and Web servers.

- **DO NOT commit changes automatically.** Only commit when explicitly requested.
- **Code formatting**: `dotnet format whitespace --exclude 'Maple2.Server.World/Migrations/*.cs'`
- **Database migrations**: `dotnet ef migrations add <Name> --project Maple2.Server.World`

## Working with Packets (Reverse Engineering)

**CRITICAL: This is a reverse engineering project.** Packet structures MUST match the client's exact expectations.

**DO NOT add arbitrary packet fields.** For example, if you notice meso (currency) isn't being sent in a packet, you CANNOT simply add `pWriter.WriteLong(meso)`. This will break the packet because the client expects a specific structure.

**Never use generic type writes like `pWriter.Write<Type>(value)`.** Always use explicit methods (`WriteInt`, `WriteLong`, `WriteByte`, etc.). Generic writes infer the wire format from the C# type, so changing a model field from `int` to `byte` would silently change the packet format and break the client.

**Use `WriteClass<T>`/`ReadClass<T>` for serializing objects.** Classes that implement `IByteSerializable`/`IByteDeserializable` can be written/read with `pWriter.WriteClass<MyType>(obj)` and `packet.ReadClass<MyType>()`. This keeps packet code clean by delegating field-level serialization to the model itself, and allows nested composition (a class can `WriteClass` its child objects).

**Packet structure convention:**

```csharp
public static class SomePacket {
    public static ByteWriter Operation(...) {
        var pWriter = Packet.Of(SendOp.OPERATION);
        pWriter.WriteInt(value);
        pWriter.WriteClass<SomeModel>(model); // delegates to model.WriteTo()
        return pWriter;
    }
}
```

### PacketStructureResolver - Server→Client Packet Discovery

The `PacketStructureResolver` (`Maple2.Server.Game/Util/PacketStructureResolver.cs`) discovers server-to-client packet structures by sending partial packets and using the client's error reports to build the correct structure iteratively. Results are saved to `./PacketStructures/[OPCODE] - [NAME].txt`.

- In-game command: `resolve 81` (accepts: 81, 0081, 0x81, 0x0081)
- Only works for **server→client** packets (SendOp), NOT client→server
- Saved files can be edited and re-sent with `resolve [opcode]` for rapid prototyping

## Key Patterns

### Data Access (Storage Pattern)

All data access through storage classes with unit-of-work:

```csharp
using GameStorage.Request db = gameStorage.Context() {
    Character character = db.GetCharacter(id);
    db.SaveCharacter(character);
}
```

### Metadata vs Runtime Data

- **Metadata**: Read-only game data loaded from XML at startup (`ItemMetadataStorage`, `NpcMetadataStorage`, `MapMetadataStorage`)
- **Runtime Data**: Mutable player/world state stored in database (managed by `GameStorage`)

### Thread Safety

- Packet handlers are stateless singletons — all state lives in Session instances
- FieldManager runs on a dedicated thread per map instance
- Use locks for concurrent database operations

### Packet Handlers

```csharp
public abstract class PacketHandler<T> where T : Session {
    public abstract RecvOp OpCode { get; }
    public abstract void Handle(T session, IByteReader packet);
}
```

Handlers are auto-discovered via assembly scanning and registered as singletons with Autofac.

### Trigger and Portal Initialization

Order matters — triggers and portals must be initialized in correct sequence in FieldManager.

### MCP Database Access

Two MySQL MCP servers are configured in `.mcp.json`:
- **mysql-game**: `game-server` database - player data (quests, characters, items). Has write access.
- **mysql-data**: `maple-data` database - game metadata (quest definitions, NPC info, map data). Read-only.

### Claude Code Skills

- **`/mapleshark-sniff <path.msb>`** — Parse MapleShark2 sniff files (.msb) to analyze captured packet traffic. Provides opcode summaries, hex inspection, value searching across files, and codebase cross-referencing.
- **`/decode-packet <mode> <args>`** — Decode packet hex bytes interactively. Modes: `hex` (parse typed fields), `to-hex`/`from-hex` (endian conversions).
