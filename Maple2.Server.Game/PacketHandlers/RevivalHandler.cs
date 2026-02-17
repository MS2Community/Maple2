using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class RevivalHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Revival;

    private enum Command : byte {
        SafeRevive = 0,
        InstantRevive = 2,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.SafeRevive:
                HandleSafeRevive(session);
                return;
            case Command.InstantRevive:
                HandleInstantRevive(session, packet);
                return;
        }
    }

    /// <summary>
    /// Handles normal revival (from tombstone hits or manual revival)
    /// </summary>
    private void HandleSafeRevive(GameSession session) {
        if (session.Field is null) return;
        if (session.Field.Metadata.Property.NoRevivalHere) {
            return;
        }
        // Revive player - this will handle moving to spawn point
        if (session.Player.Revive()) {
            session.Player.Tombstone = null;
        }
    }

    /// <summary>
    /// Handles instant revival (using mesos or voucher)
    /// </summary>
    private void HandleInstantRevive(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        if (session.Field.Metadata.Property.NoRevivalHere) {
            return;
        }

        bool useVoucher = packet.ReadBool();

        if (useVoucher) {
            if (!session.Item.Inventory.Consume([new IngredientInfo(ItemTag.FreeReviveCoupon, 1)])) {
                // Send error packet?
                return;
            }
            session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert | NoticePacket.Flags.Message, StringCode.s_revival_use_coupon));
        } else {
            int totalMaxCount = Lua.GetMesoRevivalDailyMaxCount();
            if (session.Config.InstantReviveCount >= totalMaxCount) {
                return;
            }

            int cost = Lua.CalcRevivalPrice((ushort) session.Player.Value.Character.Level);
            if (session.Currency.Meso < cost) {
                // Client sends error
                return;
            }
            session.Currency.Meso -= cost;
        }

        session.Config.InstantReviveCount++;
        if (session.Player.Revive(true)) {
            session.Player.Tombstone = null;
        }
    }
}
