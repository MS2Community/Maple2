#!/usr/bin/env node

const path = require('path');
const fs = require('fs');
const { MsbReader, resolveOpcodeName, resolveOpcodeNumber, getServerVersion } = require('maple2-packetlib-ts');

const LOCALE_MAP = {
    0: 'Unknown', 1: 'Korea', 2: 'KoreaTest', 3: 'Japan',
    4: 'China', 5: 'Tespia', 6: 'Taiwan', 7: 'SouthEastAsia',
    8: 'Global', 9: 'Europe', 10: 'Brazil',
};

function printHelp() {
    console.error([
        'Usage: parse-msb.js <file.msb> [options]',
        '',
        'Options:',
        '  --summary, -s              Opcode frequency table (no hex output)',
        '  --opcode, -o <op>          Filter by opcode (hex: 0x0020, name: Skill). Repeatable.',
        '  --direction, -d <IN|OUT>   Filter by direction (IN=server, OUT=client)',
        '  --limit, -l <n>            Max packets to output (default: 20, 0=unlimited)',
        '  --index, -i <n>            Get single packet by absolute index',
        '  --range, -r <n-m>          Get packets by index range (e.g. 100-200)',
        '  --search-hex <"XX XX XX">  Find packets whose payload contains byte pattern',
        '  --no-hex                   Omit hexBytes from output',
        '  --hex-limit <n>            Truncate hex output at n bytes (default: 64)',
        '  --locale <gms2|kms2|N>     Override locale for opcode resolution (useful when file locale is Unknown)',
        '',
        'Examples:',
        '  parse-msb.js capture.msb --summary',
        '  parse-msb.js capture.msb --opcode Skill --limit 5',
        '  parse-msb.js capture.msb --opcode 0x0020 --opcode 0x00AA --no-hex',
        '  parse-msb.js capture.msb --search-hex "E8 03 00 00" --direction IN',
        '  parse-msb.js capture.msb --index 42',
        '  parse-msb.js capture.msb --range 100-150 --no-hex',
    ].join('\n'));
}

function parseArgs(argv) {
    const args = {
        file: null,
        summary: false,
        opcodes: [],
        direction: null,
        limit: 20,
        noHex: false,
        hexLimit: 64,
        index: null,
        range: null,
        searchHex: null,
        locale: null,
    };

    let i = 0;
    while (i < argv.length) {
        const arg = argv[i];
        if (arg === '--help' || arg === '-h') {
            printHelp();
            process.exit(0);
        } else if (arg === '--summary' || arg === '-s') {
            args.summary = true;
        } else if ((arg === '--opcode' || arg === '-o') && i + 1 < argv.length) {
            args.opcodes.push(argv[++i]);
        } else if ((arg === '--direction' || arg === '-d') && i + 1 < argv.length) {
            args.direction = argv[++i].toUpperCase();
        } else if ((arg === '--limit' || arg === '-l') && i + 1 < argv.length) {
            args.limit = parseInt(argv[++i], 10);
        } else if (arg === '--no-hex') {
            args.noHex = true;
        } else if (arg === '--hex-limit' && i + 1 < argv.length) {
            args.hexLimit = parseInt(argv[++i], 10);
        } else if ((arg === '--index' || arg === '-i') && i + 1 < argv.length) {
            args.index = parseInt(argv[++i], 10);
        } else if ((arg === '--range' || arg === '-r') && i + 1 < argv.length) {
            const parts = argv[++i].split('-');
            args.range = { start: parseInt(parts[0], 10), end: parseInt(parts[1], 10) };
        } else if (arg === '--search-hex' && i + 1 < argv.length) {
            args.searchHex = argv[++i];
        } else if (arg === '--locale' && i + 1 < argv.length) {
            args.locale = argv[++i];
        } else if (!arg.startsWith('-')) {
            args.file = arg;
        }
        i++;
    }

    return args;
}

function resolveLocale(localeArg, fileLocale) {
    if (localeArg === null) return fileLocale;
    const lower = localeArg.toLowerCase();
    if (lower === 'kms2' || lower === 'korea' || lower === 'kr') return 1;
    if (lower === 'gms2' || lower === 'global' || lower === 'en') return 8;
    const num = parseInt(localeArg, 10);
    if (!isNaN(num)) return num;
    console.warn(`Warning: Unknown locale "${localeArg}", using file locale.`);
    return fileLocale;
}

function resolveOpcodeFilter(opcodeArgs, locale) {
    return opcodeArgs.map(raw => {
        const hexMatch = raw.match(/^(?:0x)?([0-9a-fA-F]+)$/);
        if (hexMatch) return parseInt(hexMatch[1], 16);

        const dec = parseInt(raw, 10);
        if (!isNaN(dec) && String(dec) === raw) return dec;

        const num = resolveOpcodeNumber(raw, locale);
        if (num === undefined) console.warn(`Warning: Could not resolve opcode "${raw}", ignoring.`);
        return num ?? null;
    }).filter(o => o !== null);
}

function parseSearchHex(hexStr) {
    const bytes = hexStr.trim().split(/\s+/).map(b => parseInt(b, 16));
    if (bytes.some(isNaN)) {
        console.error(`Error: Invalid hex pattern "${hexStr}"`);
        process.exit(1);
    }
    return new Uint8Array(bytes);
}

function opcodeHex(opcode) {
    return '0x' + opcode.toString(16).toUpperCase().padStart(4, '0');
}

function formatHex(packet, hexLimit) {
    const buffer = packet.getSegment(0, packet.length);
    const count = Math.min(buffer.length, hexLimit);
    const bytes = [];
    for (let i = 0; i < count; i++) {
        bytes.push(buffer[i].toString(16).toUpperCase().padStart(2, '0'));
    }
    let result = bytes.join(' ');
    if (buffer.length > hexLimit) {
        result += ` ... (+${buffer.length - hexLimit} bytes)`;
    }
    return result;
}

function formatPacket(packet, index, locale, args) {
    const name = resolveOpcodeName(packet.opcode, packet.outbound, locale) ?? 'Unknown';
    const enumPrefix = packet.outbound ? 'RecvOp' : 'SendOp';
    const entry = {
        index,
        timestamp: packet.timestamp.toISOString(),
        direction: packet.outbound ? 'OUT' : 'IN',
        opcode: opcodeHex(packet.opcode),
        opcodeName: name,
        opcodeEnum: `${enumPrefix}.${name}`,
        length: packet.length,
    };
    if (!args.noHex) {
        entry.hexBytes = formatHex(packet, args.hexLimit);
    }
    return entry;
}

function buildSummary(indexed, locale) {
    const counts = new Map();
    for (const { packet } of indexed) {
        const key = `${packet.outbound ? 1 : 0}_${packet.opcode}`;
        if (!counts.has(key)) {
            const name = resolveOpcodeName(packet.opcode, packet.outbound, locale) ?? 'Unknown';
            const enumPrefix = packet.outbound ? 'RecvOp' : 'SendOp';
            counts.set(key, {
                direction: packet.outbound ? 'OUT' : 'IN',
                opcode: opcodeHex(packet.opcode),
                opcodeName: name,
                opcodeEnum: `${enumPrefix}.${name}`,
                count: 0,
            });
        }
        counts.get(key).count++;
    }
    return [...counts.values()].sort((a, b) => b.count - a.count);
}

function applyFilters(allPackets, args, opcodeFilter, searchPattern) {
    let indexed = allPackets.map((packet, index) => ({ packet, index }));

    if (args.index !== null) {
        return indexed.filter(({ index }) => index === args.index);
    }
    if (args.range) {
        indexed = indexed.filter(({ index }) => index >= args.range.start && index <= args.range.end);
    }
    if (args.direction) {
        const outbound = args.direction === 'OUT';
        indexed = indexed.filter(({ packet }) => packet.outbound === outbound);
    }
    if (opcodeFilter.length > 0) {
        indexed = indexed.filter(({ packet }) => opcodeFilter.includes(packet.opcode));
    }
    if (searchPattern) {
        indexed = indexed.filter(({ packet }) => packet.search(searchPattern) !== -1);
    }

    return indexed;
}

async function main() {
    const argv = process.argv.slice(2);
    if (argv.length === 0) {
        printHelp();
        process.exit(1);
    }

    const args = parseArgs(argv);

    if (!args.file) {
        console.error('Error: No MSB file specified.');
        printHelp();
        process.exit(1);
    }

    const msbPath = path.resolve(args.file);
    if (!fs.existsSync(msbPath)) {
        console.error(`Error: File not found: ${msbPath}`);
        process.exit(1);
    }

    const searchPattern = args.searchHex ? parseSearchHex(args.searchHex) : null;

    try {
        const reader = new MsbReader(msbPath);
        const locale = resolveLocale(args.locale, reader.metadata?.Locale ?? 0);

        // Opcode filter is resolved after reading locale so name lookups use the right table
        const opcodeFilter = resolveOpcodeFilter(args.opcodes, locale);

        const allPackets = reader.readPackets();

        const metadata = {
            version: reader.version,
            build: reader.metadata?.Build || 0,
            locale: LOCALE_MAP[locale] ?? `Unknown (${locale})`,
            serverVersion: getServerVersion(locale),
            localEndpoint: `${reader.metadata?.LocalEndpoint}:${reader.metadata?.LocalPort}`,
            remoteEndpoint: `${reader.metadata?.RemoteEndpoint}:${reader.metadata?.RemotePort}`,
            totalPackets: allPackets.length,
        };

        const filtered = applyFilters(allPackets, args, opcodeFilter, searchPattern);

        if (args.summary) {
            const summary = buildSummary(filtered, locale);
            console.log(JSON.stringify({ metadata, matchedPackets: filtered.length, opcodes: summary }, null, 2));
            return;
        }

        const totalMatched = filtered.length;
        const output = args.limit > 0 ? filtered.slice(0, args.limit) : filtered;

        console.log(JSON.stringify({
            metadata,
            totalMatched,
            showing: output.length,
            packets: output.map(({ packet, index }) => formatPacket(packet, index, locale, args)),
        }, null, 2));

    } catch (error) {
        console.error(`Error parsing MSB file: ${error.message}`);
        process.exit(1);
    }
}

main();
