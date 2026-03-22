using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Helpers;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Game.Util;

internal static class TriggerToolPacketProbe {
    internal const bool Enabled = true;

    private static readonly ILogger Logger = Log.Logger.ForContext(typeof(TriggerToolPacketProbe));
    private static readonly ConcurrentDictionary<long, ProbeState> States = new();
    private static readonly string ProbeDir = Path.Combine(AppContext.BaseDirectory, "TriggerToolProbe");
    private const string DefaultScript = "<ms2><state name=\"newState1\"></state></ms2>";

    private enum Stage {
        Catalog15,
        Open24,
        Script11,
        Done,
    }

    private sealed class ProbeState {
        public Stage Stage = Stage.Catalog15;
        public bool Pending;
        public bool HadError;
        public int CubeCoordKey;
        public string ScriptXml = DefaultScript;
    }

    internal static bool TryProbe(GameSession session, int cubeCoordKey, string scriptXml) {
        Directory.CreateDirectory(ProbeDir);

        ProbeState state = States.GetOrAdd(session.CharacterId, _ => new ProbeState());
        state.CubeCoordKey = cubeCoordKey;
        state.ScriptXml = string.IsNullOrWhiteSpace(scriptXml) ? DefaultScript : scriptXml;

        if (state.Pending && !state.HadError) {
            Logger.Information("[TriggerToolProbe] Stage {Stage} accepted by client, advancing.", state.Stage);
            state.Stage = Next(state.Stage);
            state.Pending = false;
        }

        if (state.Stage == Stage.Done) {
            Logger.Information("[TriggerToolProbe] All stages accepted. Probe files are in {Path}", ProbeDir);
            return false;
        }

        state.HadError = false;
        state.Pending = true;
        session.OnError = (_, debug) => OnError(session, state, debug);

        ByteWriter packet = BuildPacket(state);
        Logger.Information("[TriggerToolProbe] Sending stage {Stage}. Probe file: {File}", state.Stage, GetFilePath(state.Stage));
        session.Send(packet);
        return true;
    }

    private static void OnError(GameSession session, ProbeState state, string debug) {
        try {
            SockExceptionInfo info = ErrorParserHelper.Parse(debug);
            if (info.SendOp != SendOp.Trigger) {
                return;
            }

            state.HadError = true;
            string line = BuildLine(state.Stage, info.Hint, state);
            File.AppendAllText(GetFilePath(state.Stage), line + Environment.NewLine);
            Logger.Information("[TriggerToolProbe] Stage {Stage} append => {Line} (offset {Offset}, hint {Hint})", state.Stage, line, info.Offset, info.Hint);
        } catch (Exception ex) {
            Logger.Warning(ex, "[TriggerToolProbe] Failed to parse client error: {Debug}", debug);
        }
    }

    private static ByteWriter BuildPacket(ProbeState state) {
        ByteWriter pWriter = Packet.Of(SendOp.Trigger);
        pWriter.WriteByte(GetCommand(state.Stage));

        foreach (string line in ReadLines(state.Stage)) {
            ApplyLine(pWriter, line, state);
        }

        return pWriter;
    }

    private static IEnumerable<string> ReadLines(Stage stage) {
        string path = GetFilePath(stage);
        if (!File.Exists(path)) {
            yield break;
        }

        foreach (string line in File.ReadAllLines(path)) {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) {
                continue;
            }
            yield return line.Trim();
        }
    }

    private static void ApplyLine(IByteWriter pWriter, string line, ProbeState state) {
        string[] parts = line.Split('|', 2);
        string type = parts[0];
        string value = parts.Length > 1 ? parts[1] : string.Empty;
        value = value
            .Replace("{cubeCoordKey}", state.CubeCoordKey.ToString(CultureInfo.InvariantCulture))
            .Replace("{scriptXml}", state.ScriptXml);

        switch (type) {
            case "Int":
                pWriter.WriteInt(int.TryParse(value, out int i) ? i : 0);
                break;
            case "Short":
                pWriter.WriteShort(short.TryParse(value, out short s) ? s : (short) 0);
                break;
            case "Byte":
                pWriter.WriteByte(byte.TryParse(value, out byte b) ? b : (byte) 0);
                break;
            case "Long":
                pWriter.WriteLong(long.TryParse(value, out long l) ? l : 0L);
                break;
            case "String":
                pWriter.WriteString(value);
                break;
            case "UnicodeString":
                pWriter.WriteUnicodeString(value);
                break;
        }
    }

    private static string BuildLine(Stage stage, SockHint hint, ProbeState state) {
        return hint switch {
            SockHint.Decode1 => "Byte|0",
            SockHint.Decode2 => "Short|0",
            SockHint.Decode4 => DefaultInt(stage),
            SockHint.Decode8 => "Long|0",
            SockHint.Decodef => "Int|0",
            SockHint.DecodeStrA => DefaultString(stage),
            SockHint.DecodeStr => "UnicodeString|",
            _ => "Byte|0",
        };
    }

    private static string DefaultInt(Stage stage) {
        return stage switch {
            Stage.Open24 or Stage.Script11 => "Int|{cubeCoordKey}",
            _ => "Int|0",
        };
    }

    private static string DefaultString(Stage stage) {
        return stage == Stage.Script11 ? "String|{scriptXml}" : "String|";
    }

    private static byte GetCommand(Stage stage) {
        return stage switch {
            Stage.Catalog15 => 15,
            Stage.Open24 => 24,
            Stage.Script11 => 11,
            _ => 0,
        };
    }

    private static Stage Next(Stage stage) {
        return stage switch {
            Stage.Catalog15 => Stage.Open24,
            Stage.Open24 => Stage.Script11,
            Stage.Script11 => Stage.Done,
            _ => Stage.Done,
        };
    }

    private static string GetFilePath(Stage stage) {
        return Path.Combine(ProbeDir, $"Trigger_{GetCommand(stage):D2}.txt");
    }
}
