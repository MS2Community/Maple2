using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class HomeDoctorHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestHomeDoctor;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        long time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (session.Player.Value.Character.DoctorCooldown + Constants.HomeDoctorCallCooltime > time) {
            return;
        }

        if (session.Config.DeathPenaltyEndTick < session.Field.FieldTick) {
            return;
        }

        int cost = Lua.CalcResolvePenaltyPrice((ushort) session.Player.Value.Character.Level, session.Config.DeathCount, 0);
        if (session.Currency.Meso < cost) {
            return;
        }

        session.Player.Value.Character.DoctorCooldown = time;
        session.Currency.Meso -= cost;
        session.Config.UpdateDeathPenalty(0);
        session.Send(HomeDoctor(time));
        session.ConditionUpdate(ConditionType.home_doctor);
        session.ConditionUpdate(ConditionType.resolve_panelty);
    }

    private static ByteWriter HomeDoctor(long time) {
        var pWriter = Packet.Of(SendOp.HomeDoctor);
        pWriter.WriteLong(time);

        return pWriter;
    }
}
