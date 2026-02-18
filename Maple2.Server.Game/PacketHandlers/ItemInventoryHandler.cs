using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class ItemInventoryHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestItemInventory;

    private enum Command : byte {
        Move = 3,
        Drop = 4,
        DropAll = 5,
        Sort = 10,
        Expand = 11,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Move:
                HandleMove(session, packet);
                return;
            case Command.Drop:
                HandleDrop(session, packet);
                return;
            case Command.DropAll:
                HandleDropAll(session, packet);
                return;
            case Command.Sort:
                HandleSort(session, packet);
                return;
            case Command.Expand:
                HandleExpand(session, packet);
                return;
        }
    }

    private void HandleMove(GameSession session, IByteReader packet) {
        long uid = packet.ReadLong();
        short dstSlot = packet.ReadShort();

        session.Item.Inventory.Move(uid, dstSlot);
    }

    private void HandleDrop(GameSession session, IByteReader packet) {
        if (session.Field == null) {
            return;
        }

        long uid = packet.ReadLong();
        int amount = packet.ReadInt();
        DropItem(session, uid, amount);
    }

    private void HandleDropAll(GameSession session, IByteReader packet) {
        if (session.Field == null) {
            return;
        }

        long uid = packet.ReadLong();
        DropItem(session, uid);
    }

    private void HandleSort(GameSession session, IByteReader packet) {
        var type = packet.Read<InventoryType>();
        bool removeExpired = packet.ReadBool();

        session.Item.Inventory.Sort(type, removeExpired);
    }

    private void HandleExpand(GameSession session, IByteReader packet) {
        var type = packet.Read<InventoryType>();

        session.Item.Inventory.Expand(type);
    }

    /// <summary>
    /// Common util to handle item dropping from inventory.
    /// </summary>
    /// <param name="session"></param>
    /// <param name="uid">Uid of the item to drop</param>
    /// <param name="amount">The amount to drop. -1 drops the entire stack.</param>
    private void DropItem(GameSession session, long uid, int amount = -1) {
        if (session.Field is null) return;
        Item? drop = session.Item.Inventory.Get(uid);
        if (drop == null) {
            return;
        }

        if (drop.Metadata.Property.DisableDrop) {
            session.Send(NoticePacket.Notice(NoticePacket.Flags.MessageBox, StringCode.s_item_err_drop));
            return;
        }

        // If item is not splittable, force drop all.
        if (drop.Transfer?.Flag.HasFlag(TransferFlag.Split) != true) {
            amount = -1;
        }

        if (!session.Item.Inventory.Remove(uid, out drop, amount)) {
            return;
        }

        if (drop.Transfer == null || drop.IsExpired() || !drop.Transfer.Flag.HasFlag(TransferFlag.Trade) || !drop.Transfer.Flag.HasFlag(TransferFlag.Split) || drop.Transfer.Binding != null) {
            session.Item.Inventory.Discard(drop);
            return;
        }

        session.Field.DropItem(session.Player, drop);
    }
}
