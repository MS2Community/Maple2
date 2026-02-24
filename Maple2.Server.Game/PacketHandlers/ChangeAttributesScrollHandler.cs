using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class ChangeAttributesScrollHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.ChangeAttributesScroll;

    private enum Command : byte {
        Change = 1,
        Select = 3,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required TableMetadataStorage TableMetadata { private get; init; }
    public required ItemStatsCalculator ItemStatsCalc { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Change:
                HandleChange(session, packet);
                return;
            case Command.Select:
                HandleSelect(session, packet);
                return;
        }
    }

    private void HandleChange(GameSession session, IByteReader packet) {
        long scrollUid = packet.ReadLong();
        long itemUid = packet.ReadLong();
        packet.ReadLong();
        packet.ReadByte();

        bool useLock = packet.ReadBool();
        bool isSpecialAttribute = false;
        short attribute = -1;
        if (useLock) {
            isSpecialAttribute = packet.ReadBool();
            attribute = packet.ReadShort();
        }

        lock (session.Item) {
            Item? scroll = session.Item.Inventory.Get(scrollUid, InventoryType.Misc);
            if (scroll == null || scroll.Metadata.Function?.Type != ItemFunction.ItemRemakeScroll) {
                session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_scroll));
                return;
            }

            Item? item = session.Item.Inventory.Get(itemUid, InventoryType.Gear) ?? session.Item.Inventory.Get(itemUid, InventoryType.Pets);
            if (item == null) {
                session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target_data));
                return;
            }
            if (item.Stats == null) {
                session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target_stat));
                return;
            }

            ChangeAttributesScrollError error = IsCompatibleScroll(item, scroll, out ItemRemakeScrollMetadata? itemRemakeScrollMetadata);
            if (error is not ChangeAttributesScrollError.none || itemRemakeScrollMetadata is null) {
                session.Send(ChangeAttributesScrollPacket.Error(error));
                return;
            }

            Item? lockItem = null;
            if (useLock) {
                ItemStats.Option itemOption = item.Stats[ItemStats.Type.Random];
                if (isSpecialAttribute) {
                    if (!itemOption.Special.ContainsKey((SpecialAttribute) attribute)) {
                        session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_property));
                        return;
                    }
                } else {
                    if (!itemOption.Basic.ContainsKey((BasicAttribute) attribute)) {
                        session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_property));
                        return;
                    }
                }

                lockItem = ChangeAttributesHandler.GetLockConsumeItem(session, item);
                if (lockItem == null) {
                    session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_error_server_fail_lack_lock_consume_item));
                    return;
                }
            }

            // Clone the item so we can preview changes without modifying existing item.
            Item changeItem = item.Clone();
            if (changeItem.Stats == null) { // This should be impossible, but check again to make linter happy.
                session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target_stat));
                return;
            }

            if (changeItem.Metadata.Option == null) { // This should be impossible, but check again to make linter happy.
                session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_item));
                return;
            }

            if (itemRemakeScrollMetadata.RollAttribute) {
                // Randomize attributes.
                if (lockItem != null) {
                    // Add back the locked attribute.
                    if (isSpecialAttribute) {
                        if (!ItemStatsCalc.UpdateRandomOption(ref changeItem, new LockOption((SpecialAttribute) attribute, true))) {
                            session.Send(ChangeAttributesPacket.Error(ChangeAttributesError.s_itemremake_error_server_default));
                            return;
                        }
                    } else {
                        if (!ItemStatsCalc.UpdateRandomOption(ref changeItem, new LockOption((BasicAttribute) attribute, true))) {
                            session.Send(ChangeAttributesPacket.Error(ChangeAttributesError.s_itemremake_error_server_default));
                            return;
                        }
                    }
                } else {
                    if (!ItemStatsCalc.UpdateRandomOption(ref changeItem)) {
                        session.Send(ChangeAttributesPacket.Error(ChangeAttributesError.s_itemremake_error_server_default));
                        return;
                    }
                }
            } else {
                // Fixed attributes.
                if (lockItem != null) {
                    // Restore locked attribute values.
                    if (isSpecialAttribute) {
                        if (!ItemStatsCalc.UpdateFixedOption(ref changeItem, new LockOption((SpecialAttribute) attribute, true))) {
                            session.Send(ChangeAttributesPacket.Error(ChangeAttributesError.s_itemremake_error_server_default));
                            return;
                        }
                    } else {
                        if (!ItemStatsCalc.UpdateFixedOption(ref changeItem, new LockOption((BasicAttribute) attribute, true))) {
                            session.Send(ChangeAttributesPacket.Error(ChangeAttributesError.s_itemremake_error_server_default));
                            return;
                        }
                    }
                } else {
                    if (!ItemStatsCalc.UpdateFixedOption(ref changeItem)) {
                        session.Send(ChangeAttributesPacket.Error(ChangeAttributesError.s_itemremake_error_server_default));
                        return;
                    }
                }

            }

            // Try to consume both items.
            bool consumedScroll = session.Item.Inventory.Consume(scroll.Uid, 1);
            bool consumedLock = lockItem == null || session.Item.Inventory.Consume(lockItem.Uid, 1);

            if (!consumedScroll || !consumedLock) {
                // Rollback if either consumption failed.
                if (consumedScroll) {
                    Item newScroll = scroll.Clone();
                    newScroll.Amount = 1;
                    session.Item.Inventory.Add(newScroll); // Re-add scroll if only lock failed
                }
                if (lockItem != null && consumedLock) {
                    Item newLockItem = lockItem.Clone();
                    newLockItem.Amount = 1;
                    session.Item.Inventory.Add(newLockItem); // Re-add lock item if only scroll failed
                }
                session.Send(ChangeAttributesScrollPacket.Error(
                    consumedScroll
                        ? ChangeAttributesScrollError.s_itemremake_error_server_fail_lack_lock_consume_item
                        : ChangeAttributesScrollError.s_itemremake_scroll_error_server_fail_consume_scroll
                ));
                return;
            }

            if (item.Metadata.Limit.TransferType is TransferType.BindPet) {
                item.Transfer?.Bind(session.Player.Value.Character);
                session.Send(ItemInventoryPacket.UpdateItem(item));
            }

            session.ChangeAttributesItem = changeItem;
            session.Send(ChangeAttributesScrollPacket.PreviewItem(session.ChangeAttributesItem));
        }
    }

    private static void HandleSelect(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        if (session.ChangeAttributesItem == null || session.ChangeAttributesItem.Uid != itemUid) {
            session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_server_fail_apply_before_option));
            return;
        }

        lock (session.Item) {
            Item? item = session.Item.Inventory.Get(itemUid, InventoryType.Gear) ?? session.Item.Inventory.Get(itemUid, InventoryType.Pets);
            if (item == null) {
                session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target_data));
                return;
            }

            if (item.Stats == null || session.ChangeAttributesItem.Stats == null) {
                session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_server_fail_apply_before_option));
                return;
            }

            item.Stats[ItemStats.Type.Random] = session.ChangeAttributesItem.Stats[ItemStats.Type.Random];
            session.ChangeAttributesItem = null;
            session.Send(ChangeAttributesScrollPacket.SelectItem(item));
        }
    }

    private ChangeAttributesScrollError IsCompatibleScroll(Item item, Item scroll, out ItemRemakeScrollMetadata? metadata) {
        metadata = null;
        if (item.Rarity is < Constant.ChangeAttributesMinRarity or > Constant.ChangeAttributesMaxRarity) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_rank;
        }
        if (!item.Type.IsWeapon && item.Type is { IsArmor: false, IsAccessory: false, IsPet: false }) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_slot;
        }
        if (item.Metadata.Limit.Level < Constant.ChangeAttributesMinLevel) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_level;
        }

        // Validate scroll conditions
        if (!int.TryParse(scroll.Metadata.Function?.Parameters, out int remakeId)) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_scroll_data;
        }
        if (!TableMetadata.ItemRemakeScrollTable.Entries.TryGetValue(remakeId, out metadata)) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_scroll_data;
        }

        if (item.Metadata.Limit.Level < metadata.MinLevel || item.Metadata.Limit.Level > metadata.MaxLevel) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_item;
        }
        if (!metadata.Rarities.Contains(item.Rarity) || !metadata.ItemTypes.Contains(item.Type.Type) && !metadata.OnlyPet) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_item;
        }

        if (metadata.OnlyPet && !item.Type.IsPet) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_item;
        }

        return ChangeAttributesScrollError.none;
    }
}
