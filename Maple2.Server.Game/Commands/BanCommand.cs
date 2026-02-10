using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Session;
using Maple2.Tools;
using System.Net;

namespace Maple2.Server.Game.Commands;

public class BanCommand : GameCommand {
    private readonly GameSession session;

    public BanCommand(GameSession session) : base(AdminPermissions.Ban, "ban", "Ban a player by character name. Usage: ban <character> [-t type] [-d days] [reason...]") {
        this.session = session;

        var characterArg = new Argument<string>("character", description: "Target character name.");
        var typeOpt = new Option<string>([
            "--type",
            "-t",
        ], () => "account", "Ban type: account | ip | hw (default: account).");
        var durationOpt = new Option<string>([
            "--duration",
            "-d",
        ], () => string.Empty, "Duration in days (e.g. 1d, 7d, 30d). Omit for permanent");
        var reasonArg = new Argument<string[]>("reason", () => [], "Optional reason words.");

        AddArgument(characterArg);
        AddOption(typeOpt);
        AddOption(durationOpt);
        AddArgument(reasonArg);
        this.SetHandler<InvocationContext, string, string, string, string[]>(Handle, characterArg, typeOpt, durationOpt, reasonArg);
    }

    private void Handle(InvocationContext ctx, string characterName, string type, string durationToken, string[] reasonWords) {
        string banType = type.ToLowerInvariant();
        if (banType != "account" && banType != "ip" && banType != "hw") {
            ctx.Console.Out.WriteLine("Invalid --type. Allowed: account | ip | hw");
            return;
        }

        TimeSpan? duration = null;
        if (!string.IsNullOrWhiteSpace(durationToken)) {
            if (!DurationParser.TryParse(durationToken, out TimeSpan span, "d")) {
                ctx.Console.Out.WriteLine($"Invalid duration '{durationToken}'. Only day durations supported (e.g. 1d, 7d, 30d)");
                return;
            }
            duration = span;
        }

        string reason = reasonWords.Length == 0 ? "No ban reason given" : string.Join(' ', reasonWords);
        DateTime? expiryUtc = duration == null ? null : DateTime.UtcNow.Add(duration.Value);

        if (string.IsNullOrWhiteSpace(characterName)) {
            ctx.Console.Out.WriteLine("Character name cannot be empty.");
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        long characterId = db.GetCharacterId(characterName);
        Account? targetAccount = db.GetAccountByCharacterName(characterName);
        if (targetAccount is null || characterId == 0) {
            ctx.Console.Out.WriteLine($"Character '{characterName}' not found.");
            ctx.ExitCode = 1;
            return;
        }

        if ((targetAccount.AdminPermissions & (AdminPermissions.GameMaster | AdminPermissions.Admin)) != 0) {
            ctx.Console.Out.WriteLine($"Cannot {banType} ban GM/Admin account '{characterName}'.");
            return;
        }

        switch (banType) {
            case "account":
                HandleAccountBan(ctx, db, targetAccount, characterId, reason, duration, expiryUtc);
                break;
            case "ip":
                HandleIpBan(ctx, db, targetAccount, characterId, reason);
                break;
            case "hw":
                HandleHardwareBan(ctx, db, targetAccount, characterId, reason);
                break;
        }
    }

    private void HandleAccountBan(InvocationContext ctx, GameStorage.Request db, Account targetAccount, long characterId, string reason, TimeSpan? duration, DateTime? expiryUtc) {
        if (db.GetBanStatus(targetAccount.Id, null, null).IsBanned) {
            ctx.Console.Out.WriteLine($"Account for '{targetAccount.Username}' already banned.");
            return;
        }

        string details = $"banned_by_id={session.AccountId};banned_by_name={session.PlayerName};issued_at={DateTime.UtcNow:O};expires_at={(expiryUtc?.ToString("O") ?? "permanent")}";
        long banIdNew = db.CreateBan(targetAccount.Id, reason, duration, details);
        string durationLabel = expiryUtc == null ? "Permanently" : $"until {expiryUtc:yyyy-MM-dd HH:mm:ss} UTC";
        ctx.Console.Out.WriteLine($"Banned '{targetAccount.Username}' (Account {targetAccount.Id}) {durationLabel}. BanId={banIdNew} Reason='{reason}'.");

        DisconnectResponse? disconnectResponse = session.World.Disconnect(new DisconnectRequest {
            CharacterId = characterId,
            Force = true,
        });
        if (disconnectResponse is null || !disconnectResponse.Success) {
            ctx.Console.Out.WriteLine($"Failed to disconnect '{targetAccount.Username}'.");
        }
    }

    private void HandleIpBan(InvocationContext ctx, GameStorage.Request db, Account targetAccount, long characterId, string reason) {
        if (!session.FindSession(characterId, out GameSession? targetSession)) {
            ctx.Console.Out.WriteLine($"Player '{targetAccount.Username}' must be online for IP ban (no stored IP).");
            ctx.ExitCode = 1;
            return;
        }
        string ip = targetSession.ExtractIp();
        if (!IPAddress.TryParse(ip, out _)) {
            ctx.Console.Out.WriteLine($"Failed to parse IP from '{ip}'.");
            return;
        }
        if (db.GetBanStatus(null, ip, null).IsBanned) {
            ctx.Console.Out.WriteLine($"IP {ip} already banned.");
            return;
        }
        string details = $"banned_by_id={session.AccountId};banned_by_name={session.PlayerName};ip={ip};issued_at={DateTime.UtcNow:O};expires_at=permanent";
        long banId = db.CreateBan(targetAccount.Id, reason, null, details, ipAddress: ip);
        ctx.Console.Out.WriteLine($"IP banned '{targetAccount.Username}' (Account {targetAccount.Id}) permanently. IP={ip} BanId={banId} Reason='{reason}'.");

        DisconnectResponse? disconnectResponse = session.World.Disconnect(new DisconnectRequest {
            CharacterId = characterId,
            Force = true,
        });
        if (disconnectResponse is null || !disconnectResponse.Success) {
            ctx.Console.Out.WriteLine($"Failed to disconnect '{targetAccount.Username}'.");
        }
    }

    private void HandleHardwareBan(InvocationContext ctx, GameStorage.Request db, Account targetAccount, long characterId, string reason) {
        Guid? machineId = targetAccount.MachineId;
        if (machineId == null) {
            ctx.Console.Out.WriteLine("Target has no recorded machine id (cannot apply hardware ban).");
            ctx.ExitCode = 1;
            return;
        }
        if (db.GetBanStatus(null, null, machineId).IsBanned) {
            ctx.Console.Out.WriteLine("Hardware already banned.");
            return;
        }
        string details = $"banned_by_id={session.AccountId};banned_by_name={session.PlayerName};machine_id={machineId:N};issued_at={DateTime.UtcNow:O};expires_at=permanent";
        long banId = db.CreateBan(targetAccount.Id, reason, null, details, machineId: machineId);
        ctx.Console.Out.WriteLine($"Hardware banned '{targetAccount.Username}' (Account {targetAccount.Id}) permanently. MachineId={machineId} BanId={banId} Reason='{reason}'.");

        DisconnectResponse? disconnectResponse = session.World.Disconnect(new DisconnectRequest {
            CharacterId = characterId,
            Force = true,
        });
        if (disconnectResponse is null || !disconnectResponse.Success) {
            ctx.Console.Out.WriteLine($"Failed to disconnect '{targetAccount.Username}'.");
        }
    }
}

public class BanInfoCommand : GameCommand {
    private readonly GameSession session;
    public BanInfoCommand(GameSession session) : base(AdminPermissions.Ban, "baninfo", "Show all active bans for a character. Usage: baninfo <character>") {
        this.session = session;
        var characterArg = new Argument<string>("character", description: "Target character name.");
        AddArgument(characterArg);
        this.SetHandler<InvocationContext, string>(Handle, characterArg);
    }

    private void Handle(InvocationContext ctx, string characterName) {
        using GameStorage.Request db = session.GameStorage.Context();
        long characterId = db.GetCharacterId(characterName);
        Account? targetAccount = db.GetAccountByCharacterName(characterName);
        if (targetAccount is null || characterId == 0) {
            ctx.Console.Out.WriteLine($"Character '{characterName}' not found.");
            ctx.ExitCode = 1;
            return;
        }

        List<(long Id, string Reason, DateTime ExpiresAt, string? Details, string? IpAddress, Guid? MachineId)> bans = db.ListActiveBans(targetAccount.Id).OrderBy(b => b.ExpiresAt).ToList();
        if (bans.Count == 0) {
            ctx.Console.Out.WriteLine("No active bans found.");
            return;
        }
        foreach ((long Id, string Reason, DateTime ExpiresAt, string? Details, string? IpAddress, Guid? MachineId) ban in bans) {
            Dictionary<string, string> parsed = ParseDetails(ban.Details);
            string expires = ban.ExpiresAt.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
            string issuedBy = parsed.TryGetValue("banned_by_name", out string? byName) ? byName : "?";
            string hwStr = parsed.TryGetValue("machine_id", out string? hwVal) ? $", HW={hwVal}" : string.Empty;
            string ipStr = parsed.TryGetValue("ip", out string? ipVal) ? $", IP={ipVal}" : string.Empty;
            ctx.Console.Out.WriteLine($"ID={ban.Id} Reason='{ban.Reason}' Expires={expires} Issuer={issuedBy}{hwStr}{ipStr}");
        }
    }

    private static Dictionary<string, string> ParseDetails(string? details) {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(details)) return dict;
        foreach (string part in details.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string k = part[..eq];
            string v = part[(eq + 1)..];
            dict[k] = v;
        }
        return dict;
    }
}

public class UnbanCommand : GameCommand {
    private readonly GameSession session;
    public UnbanCommand(GameSession session) : base(AdminPermissions.Ban, "unban", "Remove bans by ID. Usage: unban <banId> [banId ...]") {
        this.session = session;
        var firstId = new Argument<long>("banId", description: "Ban ID to remove (see baninfo)");
        var moreIds = new Argument<long[]>("more", () => [], "Optional additional ban IDs.");
        AddArgument(firstId);
        AddArgument(moreIds);
        this.SetHandler<InvocationContext, long, long[]>(Handle, firstId, moreIds);
    }

    private void Handle(InvocationContext ctx, long first, long[] more) {
        var ids = new List<long> {
            first
        };
        ids.AddRange(more);
        using GameStorage.Request db = session.GameStorage.Context();
        int removed = db.RemoveBansByIds(ids);
        ctx.Console.Out.WriteLine(removed == 0 ? "No bans removed (IDs not found)." : $"Removed {removed} ban record(s).");
    }
}
