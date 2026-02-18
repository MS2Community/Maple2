using System.CommandLine;
using System.CommandLine.Invocation;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
// ReSharper disable HeuristicUnreachableCode
#pragma warning disable CS0162 // Unreachable code detected

namespace Maple2.Server.Game.Commands.HomeCommands;

public class RandomNumberCommand : GameCommand {
    private readonly GameSession session;

    public RandomNumberCommand(GameSession session) : base(AdminPermissions.None, "roll", "Roll a random number between 1 and 100") {
        this.session = session;
        IsHidden = Constant.HideHomeCommands;

        this.SetHandler<InvocationContext>(Handle);
    }

    private void Handle(InvocationContext context) {
        if (session.Field is null) return;
        Character character = session.Player.Value.Character;
        bool isHome = character.MapId is Constant.DefaultHomeMapId;
        int rng = Random.Shared.Next(1, 101);

        // TODO: check if message was sent in party chat or if player is in dungeon
        if (isHome) {
            session.Field.Broadcast(HomeActionPacket.Roll(character, rng));
            return;
        }

        if (!Constant.EnableRollEverywhere) {
            return;
        }

        var interfaceText = new InterfaceText(StringCode.s_ugcmap_fun_roll, character.Name, rng.ToString());
        session.Field.Broadcast(NoticePacket.Notice(NoticePacket.Flags.Message, interfaceText));
    }
}
