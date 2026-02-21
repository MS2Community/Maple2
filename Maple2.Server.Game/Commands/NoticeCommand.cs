using System.CommandLine;
using System.CommandLine.Invocation;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Commands;

public class NoticeCommand : GameCommand {
    private readonly GameSession session;

    public NoticeCommand(GameSession session) : base(AdminPermissions.Debug, "notice", "Send a notice packet with a StringCode for testing.") {
        this.session = session;

        var code = new Argument<int>("code", "StringCode integer value");
        var args = new Argument<string[]>("args", () => [], "Optional string arguments for the string code");
        var flag = new Option<int>(["--flag", "-f"], () => (int) (NoticePacket.Flags.Message | NoticePacket.Flags.Alert),
            "Notice flags (default: Message|Alert = 5)");

        AddArgument(code);
        AddArgument(args);
        AddOption(flag);

        this.SetHandler<InvocationContext, int, string[], int>(Handle, code, args, flag);
    }

    private void Handle(InvocationContext ctx, int code, string[] args, int flag) {
        if (!Enum.IsDefined(typeof(StringCode), code)) {
            ctx.Console.WriteLine($"Unknown StringCode: {code}");
            return;
        }

        var stringCode = (StringCode) code;
        session.Send(NoticePacket.Message(new InterfaceText(stringCode, args), (NoticePacket.Flags) flag));
        ctx.Console.WriteLine($"Sent StringCode {code} ({stringCode}) with {args.Length} arg(s), flags={flag}");
    }
}
