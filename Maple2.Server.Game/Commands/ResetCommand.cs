using System.CommandLine;
using System.CommandLine.Invocation;
using Maple2.Model.Enum;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Commands;

public class ResetCommand : GameCommand {
    private readonly GameSession session;
    private readonly Argument<string> resetTypeArg;

    public ResetCommand(GameSession session) : base(AdminPermissions.EventManagement, "reset", "Force a reset for this player (daily/weekly/monthly).") {
        this.session = session;
        resetTypeArg = new Argument<string>("type", "Type of reset: daily, weekly, or monthly");
        AddArgument(resetTypeArg);
        this.SetHandler<InvocationContext>(Handle);
    }

    private void Handle(InvocationContext ctx) {
        string? resetType = ctx.ParseResult.GetValueForArgument(resetTypeArg)?.ToLower();
        switch (resetType) {
            case "daily":
                session.DailyReset();
                break;
            case "weekly":
                session.WeeklyReset();
                break;
            case "monthly":
                session.MonthlyReset();
                break;
            default:
                session.Send(NoticePacket.Message($"Unknown reset type: {resetType}. Use: daily, weekly, or monthly"));
                break;
        }
    }
}
