using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Game.PacketHandlers;

public class SurvivalHandler : FieldPacketHandler {
    private static readonly ILogger SurvivalLogger = Log.Logger.ForContext<SurvivalHandler>();

    public override RecvOp OpCode => RecvOp.Survival;

    private enum Command : byte {
        JoinSolo = 0,
        WithdrawSolo = 1,
        Equip = 8,
        ClaimRewards = 35,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        byte rawCommand = packet.ReadByte();
        SurvivalLogger.Information("Survival command received cmd={Command}", rawCommand);
        Command command = (Command) rawCommand;
        switch (command) {
            case Command.Equip:
                HandleEquip(session, packet);
                return;
            case Command.ClaimRewards:
                HandleClaimRewards(session, packet);
                return;
            case Command.JoinSolo:
                session.Survival.TryActivateGoldPass();
                return;
            case Command.WithdrawSolo:
                return;
            default:
                session.Survival.TryActivateGoldPass();
                return;
        }
    }

    private static void HandleEquip(GameSession session, IByteReader packet) {
        MedalType slot = packet.Read<MedalType>();
        int medalId = packet.ReadInt();
        session.Survival.Equip(slot, medalId);
    }

    private static void HandleClaimRewards(GameSession session, IByteReader packet) {
        if (!session.Survival.TryClaimNextReward()) {
            SurvivalLogger.Information("Unhandled Survival claim: no claimable rewards available.");
        }
    }
}
