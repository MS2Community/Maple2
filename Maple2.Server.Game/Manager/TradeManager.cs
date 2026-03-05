using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Network;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using static Maple2.Model.Error.TradeError;

namespace Maple2.Server.Game.Manager;

public class TradeManager : IDisposable {
    private enum TradeState {
        Completed = 0,
        Requested = 1,
        Acknowledged = 2,
        Started = 3,
        Finalized = 4,
    }

    private readonly Trader sender;
    private readonly Trader receiver;
    private TradeState state;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    private ConstantsTable Constants => sender.Session.ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    private readonly object mutex = new();

    public TradeManager(GameSession sender, GameSession receiver) {
        this.sender = new Trader(sender);
        this.receiver = new Trader(receiver);
        state = TradeState.Requested;

        // Send a trade request
        receiver.Send(TradePacket.Request(sender.Player));

        // End the trade if not accepted before |TradeRequestDuration|.
        string receiverName = receiver.Player.Value.Character.Name;
        Task.Factory.StartNew(() => {
            Thread.Sleep(TimeSpan.FromSeconds(Constants.TradeRequestDuration));
            lock (mutex) {
                if (state is not (TradeState.Requested or TradeState.Acknowledged)) {
                    return;
                }

                sender.Send(TradePacket.Error(s_trade_error_timeout, name: receiverName));
                Dispose();
            }
        });
    }

    public void Dispose() {
        sender.Session.Trade = null;
        receiver.Session.Trade = null;

        // Nothing to cancel if not started.
        if (state < TradeState.Started) {
            return;
        }

        lock (mutex) {
            EndTrade(false);
        }
    }

    // Called by |receiver| to acknowledge trade request.
    public void Acknowledge(GameSession caller) {
        if (state != TradeState.Requested || !IsReceiver(caller)) {
            return;
        }

        lock (mutex) {
            state = TradeState.Acknowledged;
        }
        sender.Session.Send(TradePacket.Acknowledge());
    }

    // Called by |receiver| to accept trade request.
    public void Accept(GameSession caller) {
        if (state != TradeState.Acknowledged || !IsReceiver(caller)) {
            return;
        }

        lock (mutex) {
            state = TradeState.Started;
        }
        sender.Session.Send(TradePacket.StartTrade(receiver.Session.CharacterId));
        receiver.Session.Send(TradePacket.StartTrade(sender.Session.CharacterId));
    }

    public void Decline(GameSession caller) {
        if (state != TradeState.Acknowledged || !IsReceiver(caller)) {
            return;
        }

        lock (mutex) {
            state = TradeState.Completed;
        }
        sender.Session.Send(TradePacket.Decline(receiver.Session.PlayerName));
        Dispose();
    }

    public void AddItem(GameSession caller, long itemUid, int amount, int tradeSlot) {
        if (state != TradeState.Started || !IsTrader(caller)) {
            return;
        }

        (Trader self, Trader other) = GetTraders(caller);
        if (self.Finalized) {
            caller.Send(TradePacket.Error(s_trade_error_latched));
            return;
        }

        lock (mutex)
            lock (caller.Item) {
                if (self.Items.OpenSlots <= 0) {
                    caller.Send(TradePacket.Error(s_trade_error_itemcount));
                }
                if (!caller.Item.Inventory.Remove(itemUid, out Item? item, amount)) {
                    return;
                }
                if (item.Transfer?.Flag.HasFlag(TransferFlag.LimitTrade) == true && item.Transfer.RemainTrades < 1) {
                    caller.Item.Inventory.Add(item);
                    return;
                }

                IList<(Item Item, int Added)> results = self.Items.Add(item, true);
                // Sanity check, this should never fail because we ensure an open slot.
                if (results.Sum(result => result.Added) != amount) {
                    // caller.Item.Inventory.Add(item); TODO: refund the item to inventory?
                    throw new InvalidOperationException("AddItem: Trade consistency error");
                }

                foreach ((Item Item, int) result in results) {
                    self.Session.Send(TradePacket.AddItem(true, result.Item));
                    other.Session.Send(TradePacket.AddItem(false, result.Item));
                }

                OnTradeModified();
            }
    }

    public void RemoveItem(GameSession caller, long itemUid, int tradeSlot) {
        if (state != TradeState.Started || !IsTrader(caller)) {
            return;
        }

        (Trader self, Trader other) = GetTraders(caller);
        if (self.Finalized) {
            caller.Send(TradePacket.Error(s_trade_error_latched));
            return;
        }

        lock (mutex)
            lock (caller.Item) {
                if (!self.Items.RemoveSlot((short) tradeSlot, out Item? item)) {
                    return;
                }

                self.Session.Send(TradePacket.RemoveItem(true, tradeSlot, itemUid));
                other.Session.Send(TradePacket.RemoveItem(false, tradeSlot, itemUid));
                caller.Item.Inventory.Add(item);

                OnTradeModified();
            }
    }

    public void SetMesos(GameSession caller, long amount) {
        // Can never set amount to a negative number!
        if (amount < 0 || state != TradeState.Started || !IsTrader(caller)) {
            return;
        }

        if (amount > Constants.TradeMaxMeso) {
            caller.Send(TradePacket.Error(s_trade_error_invalid_meso));
            return;
        }

        (Trader self, Trader other) = GetTraders(caller);
        if (self.Finalized) {
            caller.Send(TradePacket.Error(s_trade_error_latched));
            return;
        }

        lock (mutex) {
            long diff = amount - self.Mesos;
            if (self.Session.Currency.Meso < diff) {
                caller.Send(TradePacket.Error(s_trade_error_meso));
                return;
            }

            self.Session.Currency.Meso -= diff;
            self.Mesos = amount;

            self.Session.Send(TradePacket.SetMesos(true, amount));
            other.Session.Send(TradePacket.SetMesos(false, amount));

            OnTradeModified();
        }
    }

    public void Finalize(GameSession caller) {
        if (state != TradeState.Started || !IsTrader(caller)) {
            return;
        }

        (Trader self, Trader other) = GetTraders(caller);
        lock (mutex) {
            if (!self.Finalized) {
                self.Session.Send(TradePacket.Finalize(true));
                other.Session.Send(TradePacket.Finalize(false));
                self.Finalized = true;
            }

            if (self.Finalized && other.Finalized) {
                state = TradeState.Finalized;
            }
        }
    }

    public void Complete(GameSession caller) {
        if (state != TradeState.Finalized || !IsTrader(caller)) {
            return;
        }

        (Trader self, Trader other) = GetTraders(caller);
        lock (mutex) {
            if (!self.Completed) {
                self.Session.Send(TradePacket.Complete(true));
                other.Session.Send(TradePacket.Complete(false));
                self.Completed = true;
            }

            if (self.Completed && other.Completed) {
                // Swap sessions so items are returned to opposite players.
                (self.Session, other.Session) = (other.Session, self.Session);
                EndTrade(true);

                // Since this trade has ended, we can clear it from traders.
                sender.Session.Trade = null;
                receiver.Session.Trade = null;
            }
        }
    }

    // |mutex| Locking is done externally.
    private void EndTrade(bool success) {
        if (state == TradeState.Completed) {
            return;
        }

        lock (sender.Session.Item) {
            long fee = success ? (long) (Constants.TradeFeePercent / 100f * sender.Mesos) : 0;
            sender.Session.Currency.Meso += sender.Mesos - fee;
            foreach (Item item in sender.Items) {
                if (item.Transfer?.Flag.HasFlag(TransferFlag.LimitTrade) == true) {
                    item.Transfer.RemainTrades--;
                }
                sender.Session.Item.Inventory.Add(item);
            }

            // Clear just to be safe in case of multiple calls
            sender.Clear();
        }
        lock (receiver.Session.Item) {
            long fee = success ? (long) (Constants.TradeFeePercent / 100f * receiver.Mesos) : 0;
            receiver.Session.Currency.Meso += receiver.Mesos - fee;
            foreach (Item item in receiver.Items) {
                if (item.Transfer?.Flag.HasFlag(TransferFlag.LimitTrade) == true) {
                    item.Transfer.RemainTrades--;
                }
                receiver.Session.Item.Inventory.Add(item);
            }

            // Clear just to be safe in case of multiple calls
            receiver.Clear();
        }

        sender.Session.Send(TradePacket.EndTrade(success));
        receiver.Session.Send(TradePacket.EndTrade(success));
        state = TradeState.Completed;
    }

    // |mutex| Locking is done externally.
    private void OnTradeModified() {
        if (state != TradeState.Started) {
            throw new InvalidOperationException($"Trade was modified while in an invalid state: {state}");
        }

        if (sender.Finalized) {
            sender.Session.Send(TradePacket.UnFinalize(true));
            receiver.Session.Send(TradePacket.UnFinalize(false));
            sender.Finalized = false;
        }
        if (receiver.Finalized) {
            receiver.Session.Send(TradePacket.UnFinalize(true));
            sender.Session.Send(TradePacket.UnFinalize(false));
            receiver.Finalized = false;
        }
    }

    private (Trader self, Trader other) GetTraders(GameSession caller) {
        if (caller == sender.Session) return (sender, receiver);
        if (caller == receiver.Session) return (receiver, sender);
        throw new ArgumentException($"Invalid trader: {caller}");
    }

    private bool IsTrader(GameSession caller) => caller == sender.Session || caller == receiver.Session;
    private bool IsReceiver(GameSession caller) => caller == receiver.Session;

    private class Trader {
        public GameSession Session;
        public readonly ItemCollection Items;
        public long Mesos;

        public bool Finalized;
        public bool Completed;

        public Trader(GameSession session) {
            Session = session;
            Items = new ItemCollection(5);
        }

        public void Clear() {
            Mesos = 0;
            for (short i = 0; i < Items.Count; i++) {
                Items.RemoveSlot(i, out _);
            }
        }
    }
}
