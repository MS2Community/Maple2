using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;
using Serilog;
using static Maple2.Model.Error.StorageInventoryError;

namespace Maple2.Server.Game.Manager.Items;

public sealed class StorageManager : IDisposable {
    private const int BATCH_SIZE = 10;

    private readonly GameSession session;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    private ConstantsTable Constants => session.ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    private readonly ItemCollection items;
    private long mesos;
    private short expand;

    public StorageManager(GameSession session) {
        this.session = session;

        using GameStorage.Request db = session.GameStorage.Context();
        (mesos, expand) = db.GetStorageInfo(session.AccountId);

        items = new ItemCollection((short) (Constant.BaseStorageCount + expand));

        foreach (Item item in db.GetStorage(session.AccountId)) {
            if (items.Add(item).Count == 0) {
                Log.Error("Failed to add storage item:{Uid}", item.Uid);
            }
        }
    }

    public void Dispose() {
        using GameStorage.Request db = session.GameStorage.Context();
        lock (session.Item) {
            db.SaveStorageInfo(session.AccountId, mesos, expand);
            db.SaveItems(session.AccountId, items.ToArray());

            session.Storage = null;
        }
    }

    public void Delete(long uid) {
        lock (session.Item) {
            Item? item = items.Get(uid);
            if (item == null) {
                return;
            }

            if (items.Remove(uid, out item)) {
                session.Item.Inventory.Discard(item);
                session.Send(StorageInventoryPacket.Remove(uid));
            }
        }
    }

    public void Load() {
        lock (session.Item) {
            session.Send(StorageInventoryPacket.Reset());
            session.Send(StorageInventoryPacket.SlotsExpanded(expand));
            session.Send(StorageInventoryPacket.UpdateMesos(mesos));
            session.Send(StorageInventoryPacket.SlotsUsed(items.Count));
            foreach (ImmutableList<Item> batch in items.Batch(BATCH_SIZE)) {
                session.Send(StorageInventoryPacket.Load(batch));
            }
        }
    }

    public void Deposit(long uid, short slot, int amount) {
        lock (session.Item) {
            Item? deposit = session.Item.Inventory.Get(uid);
            if (deposit == null || deposit.Amount < amount) {
                session.Send(StorageInventoryPacket.Error(s_item_err_invalid_count));
                return;
            }

            if (items.OpenSlots == 0) {
                int remaining = items.GetStackResult(deposit, amount);
                if (amount == remaining) {
                    session.Send(StorageInventoryPacket.Error(s_item_err_store_full));
                    return;
                }

                // Stack what we can and ignore the rest.
                amount -= remaining;
            }

            if (!session.Item.Inventory.Remove(uid, out deposit, amount)) {
                return;
            }

            deposit.Slot = slot;
            IList<(Item, int Added)> result = items.Add(deposit, true);

            foreach ((Item item, int _) in result) {
                session.Send(deposit.Uid == item.Uid
                    ? StorageInventoryPacket.Add(item)
                    : StorageInventoryPacket.Update(item.Uid, item.Amount));
            }
        }
    }

    public void Withdraw(long uid, short slot, int amount) {
        lock (session.Item) {
            Item? withdraw = items.Get(uid);
            if (withdraw == null || withdraw.Amount < amount) {
                session.Send(StorageInventoryPacket.Error(s_item_err_invalid_count));
                return;
            }

            if (withdraw.Binding != null && withdraw.Binding.CharacterId != session.CharacterId) {
                session.Send(StorageInventoryPacket.Error(s_item_err_binditem_store_out));
                return;
            }

            if (!RemoveInternal(uid, amount, out withdraw)) {
                return;
            }

            withdraw.Slot = slot;
            session.Item.Inventory.Add(withdraw, commit: true);
        }
    }

    public bool Move(long uid, short dstSlot) {
        lock (session.Item) {
            if (dstSlot < 0 || dstSlot >= items.Size) {
                return false;
            }

            if (items.Remove(uid, out Item? srcItem)) {
                short srcSlot = srcItem.Slot;
                if (items.RemoveSlot(dstSlot, out Item? removeDst)) {
                    items[srcSlot] = removeDst;
                }

                items[dstSlot] = srcItem;
                session.Send(StorageInventoryPacket.Move(removeDst?.Uid ?? 0, srcSlot, uid, dstSlot));
            }

            return true;
        }
    }

    // Deposit/Withdraw Mesos
    private bool CanDepositMeso(long amount) => 0 <= amount && mesos + amount <= Constant.MaxMeso;
    public void DepositMesos(long amount) {
        lock (session.Item) {
            if (!CanDepositMeso(amount)) {
                session.Send(StorageInventoryPacket.Error(s_store_err_deposit_invalid_money));
                return;
            }

            long negAmount = -amount;
            if (session.Currency.CanAddMeso(negAmount) != negAmount) {
                session.Send(StorageInventoryPacket.Error(s_store_err_deposit_invalid_money));
                return;
            }

            session.Currency.Meso -= amount;
            mesos += amount;
            session.Send(StorageInventoryPacket.UpdateMesos(mesos));
        }
    }

    private bool CanWithdrawMeso(long amount) => 0 <= amount && amount <= mesos;
    public void WithdrawMesos(long amount) {
        lock (session.Item) {
            if (!CanWithdrawMeso(amount)) {
                session.Send(StorageInventoryPacket.Error(s_store_err_deposit_invalid_money));
                return;
            }

            if (session.Currency.CanAddMeso(amount) != amount) {
                session.Send(StorageInventoryPacket.Error(s_store_err_deposit_invalid_money));
                return;
            }

            mesos -= amount;
            session.Send(StorageInventoryPacket.UpdateMesos(mesos));
            session.Currency.Meso += amount;
        }
    }

    public void Expand() {
        lock (session.Item) {
            short newSize = (short) (items.Size + Constant.InventoryExpandRowCount);
            if (newSize > Constants.StoreExpandMaxSlotCount) {
                session.Send(StorageInventoryPacket.Error(s_store_err_expand_max));
                return;
            }
            if (session.Currency.Meret < Constants.StoreExpandPrice1Row) {
                session.Send(StorageInventoryPacket.Error(s_cannot_charge_merat));
                return;
            }

            if (!items.Expand(newSize)) {
                session.Send(StorageInventoryPacket.Error(s_store_err_code));
                return;
            }

            session.Currency.Meret -= Constants.StoreExpandPrice1Row;
            expand += Constant.InventoryExpandRowCount;

            Load();
        }
    }

    public void Sort() {
        lock (session.Item) {
            items.Sort();

            session.Send(StorageInventoryPacket.Reset());
            foreach (ImmutableList<Item> batch in items.Batch(BATCH_SIZE)) {
                session.Send(StorageInventoryPacket.Reload(batch));
            }
        }
    }

    #region Internal (No Locks)
    private bool RemoveInternal(long uid, int amount, [NotNullWhen(true)] out Item? removed) {
        if (amount > 0) {
            Item? item = items.Get(uid);
            if (item == null || item.Amount < amount) {
                session.Send(StorageInventoryPacket.Error(s_item_err_invalid_count));
                removed = null;
                return false;
            }

            // Otherwise, we would just do a full remove.
            if (item.Amount > amount) {
                using GameStorage.Request db = session.GameStorage.Context();
                removed = db.SplitItem(0, item, amount);
                if (removed == null) {
                    return false;
                }
                item.Amount -= amount;

                session.Send(StorageInventoryPacket.Update(uid, item.Amount));
                return true;
            }
        }

        // Full remove of item
        if (items.Remove(uid, out removed)) {
            session.Send(StorageInventoryPacket.Remove(uid));
            return true;
        }

        return false;
    }
    #endregion
}
