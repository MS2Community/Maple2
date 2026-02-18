using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Commands;

public class ItemCommand : GameCommand {
    private const int MAX_RARITY = 6;
    private const int MAX_SOCKET = 5;

    private readonly GameSession session;
    private readonly ItemMetadataStorage itemStorage;

    public ItemCommand(GameSession session, ItemMetadataStorage itemStorage) : base(AdminPermissions.SpawnItem, "item", "Item spawning.") {
        this.session = session;
        this.itemStorage = itemStorage;

        var id = new Argument<int>("id", "Id of item to spawn.");
        var amount = new Option<int>(["--amount", "-a"], () => 1, "Amount of the item.");
        var rarity = new Option<int>(["--rarity", "-r"], () => 1, "Rarity of the item.");
        var drop = new Option<bool>(["--drop"], "Drop item instead of adding to inventory");
        var rollMax = new Option<bool>(["--roll-max"], () => false, "Max roll stats.");

        AddArgument(id);
        AddOption(amount);
        AddOption(rarity);
        AddOption(drop);
        AddOption(rollMax);
        this.SetHandler<InvocationContext, int, int, int, bool, bool>(Handle, id, amount, rarity, drop, rollMax);
    }

    private void Handle(InvocationContext ctx, int itemId, int amount, int rarity, bool drop, bool rollMax) {
        if (session.Field is null) return;
        try {
            rarity = Math.Clamp(rarity, 1, MAX_RARITY);

            Item? firstItem = session.Field.ItemDrop.CreateItem(itemId, rarity, rollMax: rollMax);
            if (firstItem == null) {
                ctx.Console.Error.WriteLine($"Invalid Item: {itemId}");
                return;
            }

            if (firstItem.IsCurrency()) {
                firstItem.Amount = amount;
                ctx.ExitCode = GiveItem(ctx, firstItem) ? 0 : 1;
                return;
            }

            bool isNonStackable = firstItem.Metadata.Property.SlotMax == 1;

            if (drop) {
                if (isNonStackable) {
                    // Apply hard cap only when dropping non-stackable items
                    amount = Math.Clamp(amount, 1, 100);

                    session.Field.DropItem(session.Player, firstItem);
                    for (int i = 1; i < amount; i++) {
                        Item? additionalItem = session.Field.ItemDrop.CreateItem(itemId, rarity, rollMax: rollMax);
                        if (additionalItem == null) {
                            ctx.Console.Error.WriteLine($"Failed to create additional item {i + 1}/{amount}");
                            continue;
                        }
                        session.Field.DropItem(session.Player, additionalItem);
                    }
                } else {
                    // Stackable items can be dropped as a single stack only up to SlotMax
                    firstItem.Amount = Math.Clamp(amount, 1, firstItem.Metadata.Property.SlotMax);
                    session.Field.DropItem(session.Player, firstItem);
                }
                ctx.ExitCode = 0;
                return;
            }

            if (isNonStackable) {
                int freeSlots = session.Item.Inventory.FreeSlots(firstItem.Inventory);
                if (freeSlots <= 0) {
                    session.Send(ItemInventoryPacket.Error(ItemInventoryError.s_err_inventory));
                    return;
                }
                if (!GiveItem(ctx, firstItem)) {
                    return;
                }

                amount = Math.Clamp(amount, 1, freeSlots);
                // don't need to recalculate free slots, since we are skipping one already
                for (int i = 1; i < amount; i++) {
                    Item? additionalItem = session.Field.ItemDrop.CreateItem(itemId, rarity, rollMax: rollMax);
                    if (additionalItem == null) {
                        ctx.Console.Error.WriteLine($"Failed to create additional item {i + 1}/{amount}");
                        continue;
                    }
                    if (!GiveItem(ctx, additionalItem)) {
                        return;
                    }
                }
            } else {
                firstItem.Amount = amount;
                if (!GiveItem(ctx, firstItem)) {
                    return;
                }
            }


            ctx.ExitCode = 0;
        } catch (SystemException ex) {
            ctx.Console.Error.WriteLine(ex.Message);
            ctx.ExitCode = 1;
        }
    }

    private bool GiveItem(InvocationContext ctx, Item item) {
        if (session.Item.Inventory.Add(item, true)) return true;
        session.Item.Inventory.Discard(item);
        ctx.Console.Error.WriteLine($"Failed to add item:{item.Id} to inventory");
        ctx.ExitCode = 1;
        return false;
    }
}
