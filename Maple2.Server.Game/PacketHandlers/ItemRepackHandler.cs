using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class ItemRepackHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.ItemRepack;

    private enum Command : byte {
        Commit = 1,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Commit:
                HandleCommit(session, packet);
                return;
        }
    }

    private static void HandleCommit(GameSession session, IByteReader packet) {
        long scrollUid = packet.ReadLong();
        long targetItemUid = packet.ReadLong();

        lock (session.Item) {
            Item? scroll = session.Item.Inventory.Get(scrollUid);
            if (scroll == null) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_invalid_scroll));
                return;
            }

            Item? targetItem = session.Item.Inventory.Get(targetItemUid);
            if (targetItem == null || targetItem.Transfer == null || targetItem.Binding != null) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_invalid_target));
                return;
            }

            if (targetItem.Metadata.Property.RepackConsumeCount > scroll.Amount) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_server_fail_consume_scroll));
                return;
            }

            if (targetItem.Transfer.Flag.HasFlag(TransferFlag.LimitTrade) && targetItem.Transfer.RemainTrades > 0) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_invalid_target));
                return;
            }

            if (targetItem.Transfer.RepackageCount > targetItem.Metadata.Property.RepackCount) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_invalid_target));
                return;
            }

            if (!int.TryParse(scroll.Metadata.Function?.Parameters, out int scrollId) ||
                !targetItem.Metadata.Property.RepackScrollIds.Contains(scrollId) ||
                !session.TableMetadata.ItemRepackingScrollTable.Entries.TryGetValue(scrollId, out ItemRepackingScrollMetadata? scrollMetadata)) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_invalid_target));
                return;
            }

            if (!scrollMetadata.Rarities.Contains(targetItem.Rarity)) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_impossible_rank));
                return;
            }

            if (targetItem.Metadata.Limit.Level < scrollMetadata.MinLevel || targetItem.Metadata.Limit.Level > scrollMetadata.MaxLevel) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_impossible_level));
                return;
            }

            if (scrollMetadata.IsPet && targetItem.Metadata.Property.PetId == 0) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_invalid_target));
                return;
            }

            if (!scrollMetadata.ItemTypes.Contains(targetItem.Type.Type)) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_impossible_slot));
                return;
            }

            targetItem.Transfer.RemainTrades++;
            targetItem.Transfer.RepackageCount++;
            session.Send(ItemRepackPacket.Commit(targetItem));
        }
    }
}
