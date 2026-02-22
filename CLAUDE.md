# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## AI Assistant Guidelines

- **DO NOT commit changes automatically.** Only commit when explicitly requested by the user.
- Present changes and let the user decide whether to commit them.

## Project Overview

This is a MapleStory2 server emulator written in C# targeting .NET 8.0+. It implements a distributed microservices architecture with multiple specialized servers handling different aspects of the game.

## Build and Development Commands

### Initial Setup

```powershell
# Windows - Run the interactive setup script
.\setup.bat
# This will:
# - Check for .NET 8.0+
# - Install dotnet-ef tool
# - Create .env from .env.example
# - Prompt for MapleStory2 client path
# - Download customized server files
# - Run Maple2.File.Ingest to import game data
```

### Building

```bash
# Build entire solution
dotnet build

# Build in Release mode
dotnet build -c Release

# Build specific project
dotnet build Maple2.Server.Game/Maple2.Server.Game.csproj
```

### Running Servers

Development mode (World, Login, Web servers only):

```bash
dev.bat  # Uses Windows Terminal if available
```

Full stack (all servers including Game):

```bash
start.bat
```

Individual servers:

```bash
cd Maple2.Server.World && dotnet run
cd Maple2.Server.Login && dotnet run
cd Maple2.Server.Game && dotnet run
cd Maple2.Server.Web && dotnet run
```

### Testing

```bash
# Run all tests
dotnet test

# Run tests in Release mode
dotnet test -c Release

# Run specific test project
dotnet test Maple2.Server.Tests/Maple2.Server.Tests.csproj
```

### Code Formatting

```bash
# Format whitespace (excluding migrations)
dotnet format whitespace --exclude 'Maple2.Server.World/Migrations/*.cs'

# Verify no changes needed
dotnet format whitespace --verify-no-changes --exclude 'Maple2.Server.World/Migrations/*.cs'
```

### Docker

```bash
# Start all services with Docker Compose
docker compose up

# Start specific service
docker compose up game-ch1

# Rebuild images
docker compose build
```

### Database Migrations

```bash
# Install EF Core tools (done by setup.bat)
dotnet tool install --global dotnet-ef

# Create new migration
dotnet ef migrations add <MigrationName> --project Maple2.Server.World

# Apply migrations
dotnet ef database update --project Maple2.Server.World
```

## Architecture

### Multi-Server Design

The project uses a distributed architecture with these server components:

- **Maple2.Server.World** - Central coordinator managing global state (guilds, parties, clubs, player info). Acts as service registry via gRPC.
- **Maple2.Server.Login** - Handles authentication, character selection, and server list.
- **Maple2.Server.Game** - Game channel servers running actual gameplay. Multiple instances per world (channels 0, 1, 2, etc.).
- **Maple2.Server.Web** - Web-based services and APIs.

Communication:

- **gRPC (HTTP/2)** for inter-server calls
- **Custom TCP protocol** with MapleCipher encryption for client connections
- World server coordinates state across all game channels

### Supporting Projects

- **Maple2.Server.Core** - Shared networking base classes, packet handling, and utilities
- **Maple2.Database** - Entity Framework-based data persistence layer
- **Maple2.Model** - Shared data models (entities, metadata, enums)
- **Maple2.File.Ingest** - Tools for importing game data from MapleStory2 client files
- **Maple2.Server.Tests** - NUnit test suite
- **Maple2.Server.DebugGame** - Debug/development version of game server
- **Maple2.TestClient** - Headless bot client for automated server testing (login, field entry, combat simulation). See [Maple2.TestClient/CLAUDE.md](Maple2.TestClient/CLAUDE.md)

### Networking Layer

**Session Architecture:**

```
Session (base class in Maple2.Server.Core)
├── LoginSession
├── GameSession (most complex, contains 20+ manager instances)
└── Uses System.IO.Pipelines for high-performance async I/O
```

**PacketRouter<T>:**

- Generic packet dispatcher mapping `RecvOp` (opcode) to handlers
- Handlers auto-registered via reflection (Autofac)
- Support for deferred handling (game loop synchronization)

**Packet Handler Pattern:**

```csharp
public abstract class PacketHandler<T> where T : Session {
    public abstract RecvOp OpCode { get; }
    public abstract void Handle(T session, IByteReader packet);
    public virtual bool TryHandleDeferred(T session, IByteReader reader);
}
```

All packet handlers are:

- Stateless singletons (registered with Autofac)
- Thread-safe (state lives in Session)
- Auto-discovered via assembly scanning

### Entity Model

**Field Entity Hierarchy:**

```
FieldEntity<T> - Base with position, rotation, transform
├── Actor<T> - Combat-capable entities
│   ├── FieldPlayer - Player character with GameSession reference
│   ├── FieldNpc - NPCs with AI, navigation, animations
│   └── FieldPet - Pet entities
└── FieldObject derivatives - Interactive objects, portals, etc.
```

**Data Model Layers:**

1. **Database Models** (`Maple2.Database/Model/`) - EF Core entities persisted to MySQL
2. **Game Models** (`Maple2.Model/Game/`) - Runtime representations, serializable with `IByteSerializable`
3. **Field Entities** (`Maple2.Server.Game/Model/Field/`) - Active instances in game world

**Metadata vs Runtime Data:**

- **Metadata**: Read-only game data loaded from XML/files at startup (ItemMetadataStorage, NpcMetadataStorage, MapMetadataStorage)
- **Runtime Data**: Mutable player/world state stored in database (managed by GameStorage)

### FieldManager - The Game Loop

`FieldManager` is the core of each map instance:

- One per map instance, runs dedicated thread with `UpdateLoop()`
- Manages all entities in the field (Players, NPCs, Items, Portals, Triggers)
- Contains: TriggerCollection, ItemDropManager, DotRecast pathfinding, spatial queries, EventQueue scheduler

### Session-Scoped Managers

Each `GameSession` has specialized managers:

- ItemManager - Inventory, equipment
- StatsManager - Character stats, combat calculations
- BuffManager - Status effects
- QuestManager - Quest progression
- GuildManager, PartyManager, ShopManager, HousingManager, CurrencyManager, etc.

### Trigger System

Dynamic scripting for map events:

- TriggerContext API for trigger scripts
- Compiled C# scripts loaded at runtime
- State machine pattern for dungeons/quests
- Event-driven with conditions and actions

## Key Patterns and Conventions

### Logging

Use Serilog for logging throughout the codebase:

```csharp
using Serilog;

public class MyClass {
    private readonly ILogger logger = Log.Logger.ForContext<MyClass>();

    public void MyMethod() {
        logger.Debug("Debug message with {Parameter}", value);
        logger.Information("Info message");
        logger.Warning("Warning message");
        logger.Error(exception, "Error message");
    }
}
```

For static contexts or one-off logging:

```csharp
using Serilog;

Log.Logger.ForContext<MyClass>().Debug("Message");
```

### Dependency Injection (Autofac)

Heavy use of property injection:

```csharp
public class GameSession {
    public required GameStorage GameStorage { get; init; }
    public required WorldClient World { get; init; }
    public required ItemMetadataStorage ItemMetadata { get; init; }
}
```

### Factory Pattern

`FieldManager.Factory` creates and manages field instances with pooling/reuse for dungeons.

### Storage Pattern

All data access through storage classes with unit-of-work:

```csharp
using GameStorage.Request db = gameStorage.Context() {
    Character character = db.GetCharacter(id);
    db.SaveCharacter(character);
}
```

### gRPC Service Communication

World server exposes gRPC services (defined in .proto files):

- Game servers call World.MigrateOut/MigrateIn for channel switching
- World manages global lookups (PlayerInfoLookup, GuildLookup, PartyLookup)
- Channel servers receive broadcasts via gRPC

## Working with Packets (Reverse Engineering)

**CRITICAL: This is a reverse engineering project.** Packet structures MUST match the client's exact expectations.

### Packet Development Rules

**DO NOT add arbitrary packet fields.** For example, if you notice meso (currency) isn't being sent in a packet, you CANNOT simply add `pWriter.WriteLong(meso)`. This will break the packet because the client expects a specific structure.

**Packet structure convention:**

```csharp
public static class SomePacket {
    public static ByteWriter Operation(...) {
        var pWriter = Packet.Of(SendOp.OPERATION);
        pWriter.Write<Type>(value);
        return pWriter;
    }
}
```

### PacketStructureResolver - Server→Client Packet Discovery

The `PacketStructureResolver` (`Maple2.Server.Game/Util/PacketStructureResolver.cs`) is a critical tool for discovering server-to-client packet structures:

**How it works:**

1. Send a packet to the client with partial structure
2. Client detects the error and reports what's wrong (offset, expected type)
3. Resolver automatically appends the correct field and retries
4. Process repeats until packet is complete
5. Packet structure is saved to `./PacketStructures/[OPCODE] - [NAME].txt`

**Usage:**

```
# In-game command, where 81 is the opcode (can be: 81, 0081, 0x81, 0x0081)
resolve 81
```

**Important notes:**

- Only works for **server→client** packets (SendOp)
- Does NOT work for client→server packets (those must be reverse engineered manually)
- Lines starting with `#`, `//`, or `ByteWriter` are ignored

**Editing and Testing:**

The saved packet files in `./PacketStructures/` can be manually edited for testing and prototyping:

1. Edit the generated `.txt` file to change values (e.g., change `pWriter.WriteInt(0)` to `pWriter.WriteInt(100)`)
2. Run `resolve [opcode]` again - it will send the packet with your edited values
3. The resolver picks up where it left off, allowing you to test different values quickly
4. This enables rapid iteration and prototyping of packet structures

**Future potential:** This resolver could be integrated with AI agents via MCP to assist with automated packet structure discovery.

## Configuration

Configuration is managed through:

- **.env file** - Primary configuration (database, server IPs, game data path)
- **appsettings.json** - Per-server ASP.NET Core settings

Key .env variables:

- `MS2_DATA_FOLDER` - Path to MapleStory2 client Data directory
- `DB_IP`, `DB_PORT`, `DB_USER`, `DB_PASSWORD` - MySQL connection
- `DATA_DB_NAME`, `GAME_DB_NAME` - Database names
- `GRPC_WORLD_IP`, `GRPC_WORLD_PORT` - World server gRPC endpoint
- `LANGUAGE` - Primary language (EN, KR, CN, JP, DE, PR)

## Important Implementation Notes

### Spawn System

- Maps define spawn points via metadata
- FieldManager spawns NPCs at initialization
- Supports respawning with timers
- Trigger-based spawning for events
- Recent work on spawn points without IDs (see commit 6906d4b2)

### Trigger and Portal Initialization

Order matters - triggers and portals must be initialized in correct sequence in FieldManager (see commit 303470ba)

### Thread Safety

- Session handlers are stateless singletons
- State lives in Session instances
- FieldManager runs on dedicated thread
- Use locks for concurrent database operations

### Event Scheduling

- EventQueue for scheduled tasks
- Scheduler in GameSession and FieldManager
- Used for time-based skill casts, buff expiration, NPC AI

## Project Structure

```
Maple2/
├── Maple2.Server.Core/       # Shared networking, packet handling
├── Maple2.Server.World/      # World server (gRPC coordinator)
├── Maple2.Server.Login/      # Login server
├── Maple2.Server.Game/       # Game channel server
├── Maple2.Server.Web/        # Web server
├── Maple2.Database/          # EF Core data layer
├── Maple2.Model/             # Shared data models
├── Maple2.File.Ingest/       # Game data import tool
├── Maple2.Server.Tests/      # Test suite
├── Maple2.TestClient/        # Headless bot client for automated ST
└── Maple2.Tools/             # Development tools
```

### MCP Database Access

Two MySQL MCP servers are configured in `.mcp.json`:
- **mysql-game**: `game-server` database - player data (quests, characters, items). Has write access.
- **mysql-data**: `maple-data` database - game metadata (quest definitions, NPC info, map data). Read-only.

## References

- Setup Guide: https://github.com/MS2Community/Maple2/wiki/Prerequisites
- Understanding Packets: https://github.com/MS2Community/Maple2/wiki/Understanding-packets
- Packet Resolver Guide: https://github.com/MS2Community/Maple2/wiki/Packet-Resolver
- Community Discord: https://discord.gg/r78CXkUmuj
