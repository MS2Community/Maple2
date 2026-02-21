---
name: mapleshark-sniff
description: Parse and analyze MapleShark2 .msb sniff files for MapleStory2 packet reverse engineering. Use when the user wants to understand packet structures, debug network traffic, or implement new packet handlers.
---

# MapleShark Sniff Analyzer

## Setup (first time only)

```bash
cd tools/mapleshark && npm install
```

The MSB file to analyze: **$ARGUMENTS**

## Before you start — ask these questions if not already answered

1. **Direction** — are we looking at packets the client sends, the server sends, or both?
   - `OUT` = client → server (RecvOp) — what the client is doing
   - `IN` = server → client (SendOp) — what the server is responding with
   - Knowing this upfront avoids wasting queries on the wrong half of the traffic, and the same opcode number means a completely different thing in each direction.

2. **Server version** — GMS2 or KMS2? The file's locale byte is often `0` (Unknown) for older sniffs, so the opcode table won't auto-detect. If the user is on KMS2, pass `--locale kms2` to every command.

---

## Workflow — always follow this order

**Never fetch all packets at once.** Use targeted commands and build up context incrementally.

### Step 1 — Get an overview (always start here)

```bash
node tools/mapleshark/parse-msb.js $ARGUMENTS --summary
# If KMS2: add --locale kms2
node tools/mapleshark/parse-msb.js $ARGUMENTS --summary --locale kms2
```

This returns an opcode frequency table with no hex data — cheap on tokens. Use it to understand what's in the file and decide where to focus.

### Step 2 — Drill into specific opcodes

```bash
# By name
node tools/mapleshark/parse-msb.js $ARGUMENTS --opcode Skill --limit 5

# By hex opcode
node tools/mapleshark/parse-msb.js $ARGUMENTS --opcode 0x0020 --limit 10

# Multiple opcodes at once
node tools/mapleshark/parse-msb.js $ARGUMENTS --opcode Skill --opcode UserEnv --limit 5

# Only server→client packets
node tools/mapleshark/parse-msb.js $ARGUMENTS --opcode 0x00AA --direction IN --limit 5
```

### Step 3 — Inspect a specific packet in full

```bash
# By index (from step 2 output)
node tools/mapleshark/parse-msb.js $ARGUMENTS --index 42 --hex-limit 512

# A range of consecutive packets
node tools/mapleshark/parse-msb.js $ARGUMENTS --range 40-50 --no-hex
```

### Step 4 — Track a known value across the sniff

```bash
# Find all packets referencing a specific integer (e.g. objectId, itemId)
# Value must be little-endian — e.g. 1000 decimal = E8 03 00 00
node tools/mapleshark/parse-msb.js $ARGUMENTS --search-hex "E8 03 00 00" --summary
```

### Step 5 — Find which sniff files contain a value (folder search)

Use this when you don't know which file to look at. Pass a folder instead of a file — the same filters apply.

```bash
# Find all sniffs containing a specific hex value
node tools/mapleshark/parse-msb.js <folder-path> --search-hex "F6 F3 5E 01" --no-hex

# Find all sniffs containing a specific opcode
node tools/mapleshark/parse-msb.js <folder-path> --opcode FieldAddNpc --no-hex --limit 3
```

Results are grouped by file. Use `filesWithMatches` to narrow down which files to drill into with Steps 2–4.

---

## Codebase cross-reference

- **OUT packets (client→server)** → `Maple2.Server.Game/PacketHandlers/{Name}Handler.cs`
- **IN packets (server→client)** → `Maple2.Server.Game/Packets/{Name}Packet.cs`
- **Enums**: `Maple2.Server.Core/Constants/RecvOp.cs` (OUT) and `SendOp.cs` (IN)

When you identify an opcode, search the codebase:
```bash
# Find the handler or packet builder
# e.g. for Skill (RecvOp): look for SkillHandler.cs
# e.g. for a SendOp: look for the matching Packet.cs
```

---

## Packet format reference

### Direction convention
- **OUT** = client → server (`outbound: true`) → maps to `RecvOp`
- **IN** = server → client (`outbound: false`) → maps to `SendOp`

### Data types (little-endian)

| Type   | Size | Notes |
|--------|------|-------|
| Byte   | 1    | |
| Bool   | 1    | 0x00 = false |
| Short  | 2    | |
| Int    | 4    | `27 00 00 00` = 39 |
| Long   | 8    | |
| Float  | 4    | IEEE 754 |
| String | 2+n  | `[len: short][chars: 2 bytes each]` for Unicode |

### Common patterns

**Mode byte** — many packets have a sub-type byte after the opcode:
```
[OPCODE 2b] [MODE 1b] [payload...]
```

**Arrays** — always length-prefixed:
```
[count: int] [element] × count
```

### Converting values for `--search-hex`

To search for a 32-bit int in little-endian, **always use Node.js for 100% accuracy**:

```bash
# Convert decimal to little-endian hex
node -e "const buf = Buffer.alloc(4); buf.writeUInt32LE(23000054); console.log(buf.toString('hex').toUpperCase())"
# Output: F6F35E01

# Convert little-endian hex back to decimal
node -e "const buf = Buffer.from('F6F35E01', 'hex'); console.log(buf.readUInt32LE(0))"
# Output: 23000054
```

Then use in command:
```bash
node tools/mapleshark/parse-msb.js $ARGUMENTS --search-hex "F6 F3 5E 01"
```

Common conversions (for reference):
- `1000` decimal → `E8 03 00 00`
- `256` decimal → `00 01 00 00`

**Don't calculate manually—Node.js eliminates conversion errors.**

---

## Packet Resolver (for unknown SendOp)

For server→client packets that are missing or broken, use the in-game resolver:
```
resolve <opcode>
```
The client will report what field is expected next. Results saved to `./PacketStructures/`.

---

## Resources

- [Understanding Packets Wiki](https://github.com/MS2Community/Maple2/wiki/Understanding-packets)
- [Packet Resolver Wiki](https://github.com/MS2Community/Maple2/wiki/Packet-Resolver)
