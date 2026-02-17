using System.CommandLine;
using System.CommandLine.Invocation;
using Maple2.Model.Enum;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Commands;

public class WeeklyResetCommand : GameCommand {
    private readonly GameSession session;

    public WeeklyResetCommand(GameSession session) : base(AdminPermissions.EventManagement, "weekly-reset", "Force weekly reset for this player.") {
        this.session = session;
        this.SetHandler<InvocationContext>(Handle);
    }

    private void Handle(InvocationContext ctx) {
        session.WeeklyReset();
    }
}
