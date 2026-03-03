---
name: decode-packet
description: Decode MapleStory2 packet hex bytes interactively. Supports typed field parsing and little-endian conversions.
argument-hint: [mode] [args...]
allowed-tools: Bash(node -e *), AskUserQuestion
---

# MapleStory2 Packet Decoder

Arguments received: **$ARGUMENTS**

---

## Quick reference

| Mode | Shorthand example |
|------|-------------------|
| Parse hex as typed fields | `/decode-packet hex "A6 98 14 00 00 01 04" int byte byte byte` |
| Decimal → little-endian hex | `/decode-packet to-hex 23000054` |
| Little-endian hex → decimal | `/decode-packet from-hex "F6 F3 5E 01"` |

---

## Step 1 — Detect mode

Parse `$ARGUMENTS`:

- If it starts with `hex` → **Mode A**: typed field parse
- If it starts with `to-hex` → **Mode B**: decimal to little-endian hex
- If it starts with `from-hex` → **Mode C**: little-endian hex to decimal
- If empty or unrecognized → **ask the user** which mode they want using `AskUserQuestion`

---

## Mode A — Hex bytes → typed fields

Usage: `/decode-packet hex "<hex string>" <type> [type ...]`

**Supported types:** `byte`, `bool`, `short`, `int`, `uint`, `long`, `ulong`, `float`, `string`
- Sizes: byte/bool=1, short=2, int/uint/float=4, long/ulong=8, string=2+N*2 (UTF-16LE, length-prefixed short)
- All multi-byte types are **little-endian**

Extract the hex string (first quoted arg after `hex`) and the list of types, then run:

```bash
node -e "
const hex = '<HEX_STRING>';
const types = [<TYPES_ARRAY>];
const buf = Buffer.from(hex.replace(/\\s+/g, ''), 'hex');
let offset = 0;
for (const type of types) {
  let value, size;
  if (type === 'byte') { value = buf.readUInt8(offset); size = 1; }
  else if (type === 'bool') { value = buf.readUInt8(offset) !== 0 ? 'true' : 'false'; size = 1; }
  else if (type === 'short') { value = buf.readInt16LE(offset); size = 2; }
  else if (type === 'int') { value = buf.readInt32LE(offset); size = 4; }
  else if (type === 'uint') { value = buf.readUInt32LE(offset); size = 4; }
  else if (type === 'long') { value = buf.readBigInt64LE(offset).toString(); size = 8; }
  else if (type === 'ulong') { value = buf.readBigUInt64LE(offset).toString(); size = 8; }
  else if (type === 'float') { value = buf.readFloatLE(offset).toFixed(6); size = 4; }
  else if (type === 'string') {
    const len = buf.readUInt16LE(offset);
    value = '\"' + buf.slice(offset + 2, offset + 2 + len * 2).toString('utf16le') + '\"';
    size = 2 + len * 2;
  } else { console.error('Unknown type: ' + type); process.exit(1); }
  const hexSlice = buf.slice(offset, offset + size).toString('hex').toUpperCase().match(/../g).join(' ');
  console.log('[offset=' + offset + '] ' + type + ': ' + value + '  (' + hexSlice + ')');
  offset += size;
}
if (offset < buf.length) {
  const remaining = buf.slice(offset).toString('hex').toUpperCase().match(/../g).join(' ');
  console.log('\\n[' + (buf.length - offset) + ' bytes remaining]: ' + remaining);
}
"
```

**Example:** `/decode-packet hex "A6 98 14 00 00 01 04" int byte byte`
→ parses 4+1+1 bytes: ObjectId, Command, Count

---

## Mode B — Decimal → little-endian hex

Usage: `/decode-packet to-hex <number> [width]`

Width defaults to 4 bytes (Int). Use 8 for Long.

```bash
node -e "
const n = BigInt('<NUMBER>');
const width = <WIDTH>;  // 1, 2, 4, or 8
const buf = Buffer.alloc(width);
if (width === 1) buf.writeUInt8(Number(n));
else if (width === 2) buf.writeUInt16LE(Number(n));
else if (width === 4) buf.writeUInt32LE(Number(n));
else if (width === 8) buf.writeBigUInt64LE(n);
const spaced = buf.toString('hex').toUpperCase().match(/../g).join(' ');
console.log('Decimal : ' + n);
console.log('Hex (BE): 0x' + n.toString(16).toUpperCase().padStart(width * 2, '0'));
console.log('LE bytes: ' + spaced + '  ← use this with --search-hex');
"
```

---

## Mode C — Little-endian hex → decimal

Usage: `/decode-packet from-hex "<hex bytes>"` (auto-detects width from byte count)

```bash
node -e "
const hex = '<HEX_STRING>';
const buf = Buffer.from(hex.replace(/\\s+/g, ''), 'hex');
if (buf.length === 1) console.log('byte  : ' + buf.readUInt8(0));
else if (buf.length === 2) {
  console.log('short : ' + buf.readInt16LE(0));
  console.log('ushort: ' + buf.readUInt16LE(0));
} else if (buf.length === 4) {
  console.log('int   : ' + buf.readInt32LE(0));
  console.log('uint  : ' + buf.readUInt32LE(0));
  console.log('float : ' + buf.readFloatLE(0).toFixed(6));
} else if (buf.length === 8) {
  console.log('long  : ' + buf.readBigInt64LE(0).toString());
  console.log('ulong : ' + buf.readBigUInt64LE(0).toString());
} else {
  console.log('(raw ' + buf.length + ' bytes — showing all interpretations)');
  console.log('uint32 from offset 0: ' + buf.readUInt32LE(0));
}
"
```

---

## No arguments — ask the user

If `$ARGUMENTS` is empty or not one of the recognized modes, use `AskUserQuestion` to ask:

**"What would you like to decode?"** with options:
1. Parse hex bytes as typed fields (e.g. int, byte, short…)
2. Convert a decimal number → little-endian hex (for --search-hex)
3. Convert little-endian hex → decimal

Then ask a follow-up for the hex/number input once you know which mode they want.

---

## Notes

- **All values are little-endian** (same as MapleStory2 protocol)

---

## Data type cheat sheet

| C# type | Bytes | Node.js read method |
|---------|-------|---------------------|
| `byte`  | 1     | `readUInt8` |
| `bool`  | 1     | `readUInt8 !== 0` |
| `short` | 2     | `readInt16LE` |
| `int`   | 4     | `readInt32LE` |
| `uint`  | 4     | `readUInt32LE` |
| `long`  | 8     | `readBigInt64LE` |
| `float` | 4     | `readFloatLE` |
| `string`| 2+N*2 | `len=readUInt16LE`, then `toString('utf16le')` |
