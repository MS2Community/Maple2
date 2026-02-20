---
name: mapleshark-sniff
description: Parse and analyze MapleShark2 .msb sniff files for MapleStory2 packet reverse engineering. Use when the user wants to understand packet structures, debug network traffic, or implement new packet handlers.
---

# MapleShark Sniff Analyzer

This skill helps analyze MapleShark2 sniff files (.msb) for MapleStory2 packet reverse engineering.

## Setup (first time only)

```bash
cd tools/mapleshark && npm install
```

## Before you start — ask these questions if not already answered

1. **Direction** — are we looking at packets the client sends, the server sends, or both?
   - `OUT` = client → server (RecvOp) — what the client is doing
   - `IN` = server → client (SendOp) — what the server is responding with
   - The same opcode number means a completely different thing in each direction.

2. **Server version** — GMS2 or KMS2? The file's locale byte is often `0` (Unknown) for older sniffs. If KMS2, pass `--locale kms2` to every command.

---

## Usage

### Parse an MSB file

```bash
node tools/mapleshark/parse-msb.js <path-to-msb-file>
```

The script outputs JSON with:
- `metadata`: File version, build number, locale, endpoints
- `packets`: Array of parsed packets with opcode names mapped to RecvOp/SendOp enums

### Example Output

```json
{
  "metadata": {
    "version": 8245,
    "build": 62000000,
    "locale": "Global",
    "localEndpoint": "127.0.0.1",
    "localPort": 20001,
    "remoteEndpoint": "127.0.0.1",
    "remotePort": 20001
  },
  "packets": [
    {
      "index": 0,
      "timestamp": "2024-01-15T10:30:00.000Z",
      "direction": "OUT",
      "opcode": "0x0011",
      "opcodeName": "UserChat",
      "opcodeEnum": "RecvOp.UserChat",
      "length": 38,
      "hexBytes": "00 00 00 00 0B 00 68 00 65 00 6C 00 6C 00 6F..."
    }
  ]
}
```

## Packet Format Reference

### Data Types

| Type | Size | Description |
|------|------|-------------|
| Byte | 1 | 2 hex digits (00-FF) |
| Bool | 1 | 0x00 = false, non-zero = true |
| Short | 2 | 16-bit signed integer |
| Int | 4 | 32-bit signed integer |
| Long | 8 | 64-bit signed integer |
| Float | 4 | IEEE 754 floating point |

### String Types

**Unicode String** (most common):
```
[2 bytes: length] [2 bytes per character]
Example: 06 00 63 00 72 00 65 00 61 00 74 00 65 00 = "create"
```

**ASCII String**:
```
[2 bytes: length] [1 byte per character]
```

### Endianness

All multi-byte values are **little-endian**. Example:
- `27 00` as short = 39 (not 9984)
- `38 00 00 00` as int = 56

## Packet Structure

### Basic Structure

```
[2 bytes: OPCODE] [payload...]
```

The opcode determines the packet type:
- **RecvOp (0x0001-0x00BE)**: Client → Server packets
- **SendOp (0x0000-0x0130)**: Server → Client packets

### Headers/Modes

Many packets use a **header byte** after the opcode to indicate sub-type:

```
[OPCODE] [HEADER: byte] [payload...]
```

Example: `UserEnv (0x00AA)` has multiple modes (0, 1, 8, etc.) with different structures.

### Loops

Packets often contain arrays with a count prefix:

```
[INT: count]
  [loop structure] (repeated `count` times)
```

### Branches

Conditional fields based on:
- Boolean values (true/false branches)
- Item existence (if UID found in inventory)
- Enum values

## Opcode Lookup

Opcode names are resolved automatically from the file's locale (GMS2 or KMS2) by the `maple2-packetlib-ts` library.

Key opcodes:
- `0x0011 UserChat` - Chat messages
- `0x0017 RequestItemInventory` - Inventory operations
- `0x0020 Skill` - Skill usage
- `0x00AA UserEnv` - Various user environment data
- `0x006B ResponseCube` - Housing/cube operations

## Finding Packet Handlers

When analyzing packets, search for handlers:

1. **RecvOp** → Client packets → Look in `Maple2.Server.Game/PacketHandlers/`
2. **SendOp** → Server packets → Look in `Maple2.Server.Game/Packets/`

Handler naming convention: `{PacketName}Handler.cs` or `{PacketName}Packet.cs`

## Packet Resolver

For unknown SendOp packets, use the in-game resolver:
```
/debug resolve <opcode>
```

Example: `/debug resolve 0x00AA`

The resolver:
1. Sends packet to client with zeros
2. Client reports errors with hints like `[hint=Decode4]`
3. Resolver adds the missing field
4. Structure saved to `./PacketStructures/`

**Note**: Only works for SendOp (server→client) packets.

## Resources

- [Understanding Packets Wiki](https://github.com/MS2Community/Maple2/wiki/Understanding-packets)
- [Packet Resolver Wiki](https://github.com/MS2Community/Maple2/wiki/Packet-Resolver)
- RecvOp enum: `Maple2.Server.Core/Constants/RecvOp.cs`
- SendOp enum: `Maple2.Server.Core/Constants/SendOp.cs`

## Tips for Analysis

1. **Start with known opcodes** - Cross-reference with existing handlers
2. **Look for strings** - Often reveal packet purpose (names, messages)
3. **Identify patterns** - Loops, headers, common field sequences
4. **Compare with similar packets** - Many share sub-structures
5. **Use the resolver** - For SendOp packets, let the client reveal structure
6. **Check database IDs** - Map IDs, item IDs, NPC IDs often appear as ints

## Converting Values for Packet Analysis

When searching for specific integer values in hex data, **always use Node.js for 100% accuracy**:

```bash
# Convert decimal to little-endian hex
node -e "const buf = Buffer.alloc(4); buf.writeUInt32LE(23000054); console.log(buf.toString('hex').toUpperCase())"
# Output: F6F35E01

# Convert little-endian hex back to decimal
node -e "const buf = Buffer.from('F6F35E01', 'hex'); console.log(buf.readUInt32LE(0))"
# Output: 23000054
```

Use in commands:
```bash
node tools/mapleshark/parse-msb.js <file.msb> --search-hex "F6 F3 5E 01"
```

**Don't calculate manually—Node.js eliminates conversion errors.**
