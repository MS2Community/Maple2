using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Sync;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util.Sync;
using Maple2.Tools.Extensions;
using Serilog;
using static Maple2.Model.Error.BuddyError;

namespace Maple2.Server.Game.Manager;

public class BuddyManager : IDisposable {
    private readonly GameSession session;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    private ConstantsTable Constants => session.ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    private readonly IDictionary<long, Buddy> buddies;
    private readonly IDictionary<long, Buddy> blocked;

    private readonly CancellationTokenSource tokenSource;
    private readonly ILogger logger = Log.Logger.ForContext<BuddyManager>();

    public BuddyManager(GameStorage.Request db, GameSession session) {
        this.session = session;
        IEnumerable<IGrouping<bool, BuddyEntry>> groups =
            db.ListBuddies(session.CharacterId).GroupBy(entry => entry.Type == BuddyType.Blocked);
        foreach (IGrouping<bool, BuddyEntry> group in groups) {
            Dictionary<long, Buddy> result = group
                .Select(entry => session.PlayerInfo.GetOrFetch(entry.BuddyId, out PlayerInfo? info) ? new Buddy(entry, info) : null)
                .WhereNotNull()
                .ToDictionary(entry => entry.Id, entry => entry);
            if (group.Key) {
                blocked = result;
            } else {
                buddies = result;
            }
        }

        // Set to empty dictionary if uninitialized by db.
        buddies ??= new Dictionary<long, Buddy>();
        blocked ??= new Dictionary<long, Buddy>();
        tokenSource = new CancellationTokenSource();

        foreach (Buddy buddy in buddies.Values) {
            BeginListen(buddy);
        }
    }

    public void Dispose() {
        tokenSource.Cancel();
        tokenSource.Dispose();

        foreach (Buddy buddy in buddies.Values) {
            buddy.Dispose();
        }
        foreach (Buddy buddy in blocked.Values) {
            buddy.Dispose();
        }
    }

    public bool IsBuddy(long characterId) {
        Buddy? buddy = buddies.Values.FirstOrDefault(entry => entry.Info.CharacterId == characterId);
        return buddy != null && buddy.Type == BuddyType.Default;
    }

    public bool IsBlocked(long characterId) {
        return blocked.TryGetValue(characterId, out Buddy? buddy);
    }

    public void Load() {
        session.Send(BuddyPacket.StartList());
        if (buddies.Count + blocked.Count > 0) {
            session.Send(BuddyPacket.Load(buddies.Values, blocked.Values));
        }
        session.Send(BuddyPacket.EndList(buddies.Count));
    }

    public void SendInvite(string name, string message) {
        if (name.Length > Constant.CharacterNameLengthMax || message.Length > Constant.BuddyMessageLengthMax) {
            session.Send(BuddyPacket.Invite(error: s_buddy_err_unknown));
            return;
        }
        if (name == session.PlayerName) {
            session.Send(BuddyPacket.Invite(error: s_buddy_err_my_id_ex));
            return;
        }
        if (buddies.Count >= Constants.MaxBuddyCount) {
            session.Send(BuddyPacket.Invite(error: s_buddy_err_max_buddy));
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        long receiverId = db.GetCharacterId(name);
        if (receiverId == default) {
            session.Send(BuddyPacket.Invite(error: s_buddy_err_miss_id));
            return;
        }

        BuddyError error = db.GetBuddyType(receiverId, session.CharacterId) switch {
            BuddyType.Default => s_buddy_err_already_buddy,
            BuddyType.InRequest => s_buddy_err_already_request_somebody,
            BuddyType.OutRequest => s_buddy_err_request_somebody,
            BuddyType.Blocked => s_buddy_refused_request_from_somebody,
            _ => ok,
        };
        if (error != ok) {
            session.Send(BuddyPacket.Invite(name, error: error));
            return;
        }

        try {
            db.BeginTransaction();
            if (db.CountBuddy(receiverId) >= Constants.MaxBuddyCount) {
                session.Send(BuddyPacket.Invite(name: name, error: s_buddy_err_target_full));
                return;
            }
            BuddyEntry? entry = db.CreateBuddy(session.CharacterId, receiverId, BuddyType.OutRequest, message);
            db.CreateBuddy(receiverId, session.CharacterId, BuddyType.InRequest, message); // Intentionally ignored result
            db.Commit();

            if (entry == null || !session.PlayerInfo.GetOrFetch(entry.BuddyId, out PlayerInfo? info)) {
                session.Send(BuddyPacket.Invite(error: s_buddy_err_miss_id));
                return;
            }

            var self = new Buddy(entry, info);
            buddies[self.Id] = self;

            session.Send(BuddyPacket.Invite(name, message));
            session.Send(BuddyPacket.Append(self));

            BuddyResponse _ = session.World.Buddy(new BuddyRequest {
                ReceiverId = receiverId,
                Invite = new BuddyRequest.Types.Invite { SenderId = session.CharacterId },
            });
            session.ConditionUpdate(ConditionType.buddy_request);
        } catch (SystemException ex) {
            logger.Warning(ex, "Invite failed");
            session.Send(BuddyPacket.Invite(error: s_buddy_err_unknown));
        }
    }

    public void ReceiveInvite(long senderId) {
        using GameStorage.Request db = session.GameStorage.Context();
        BuddyEntry? entry = db.GetBuddy(session.CharacterId, senderId);
        if (entry == null || !session.PlayerInfo.GetOrFetch(entry.BuddyId, out PlayerInfo? info)) {
            logger.Debug("Could not find sender:{SenderId} for ReceiveInvite", senderId);
            return;
        }

        var self = new Buddy(entry, info);
        // DB written by sender as single transaction.
        buddies[self.Id] = self;
        session.Send(BuddyPacket.Append(self));
    }

    public void SendAccept(long entryId) {
        if (!buddies.TryGetValue(entryId, out Buddy? self) || self.Type != BuddyType.InRequest) {
            logger.Debug("Could not find entry:{EntryId} to Accept", entryId);
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        long receiverId = self.Info.CharacterId;
        BuddyEntry? other = db.GetBuddy(receiverId, session.CharacterId);
        if (other == null) {
            logger.Warning("Accept without paired entry");
            return;
        }

        self.SetType(BuddyType.Default);
        other.Type = BuddyType.Default;

        try {
            if (!db.UpdateBuddy(self, other)) {
                logger.Error("Failed to update buddy");
                return;
            }

            session.World.Buddy(new BuddyRequest {
                ReceiverId = receiverId,
                Accept = new BuddyRequest.Types.Accept { EntryId = other.Id },
            });
            if (session.PlayerInfo.GetOrFetch(self.Info.CharacterId, out PlayerInfo? info)) {
                self.Info.Update(UpdateField.All, info);
            }
            BeginListen(self);

            session.Send(BuddyPacket.Accept(self));
            session.Send(BuddyPacket.UpdateInfo(self));
            if (self.Info.Online) {
                session.Send(BuddyPacket.NotifyOnline(self));
            }
        } catch (SystemException ex) {
            logger.Warning(ex, "Buddy Accept failed");
        }
    }

    public void ReceiveAccept(long entryId) {
        if (!buddies.TryGetValue(entryId, out Buddy? self)) {
            logger.Debug("Could not find entry:{EntryId} for ReceiveAccept", entryId);
            return;
        }

        // DB written by sender as single transaction.
        self.SetType(BuddyType.Default);
        if (session.PlayerInfo.GetOrFetch(self.Info.CharacterId, out PlayerInfo? info)) {
            self.Info.Update(UpdateField.All, info);
        }

        BeginListen(self);

        session.Send(BuddyPacket.UpdateInfo(self));
        session.Send(BuddyPacket.NotifyAccept(self));
        session.Send(BuddyPacket.NotifyOnline(self));
    }

    public void SendDecline(long entryId) {
        if (!buddies.TryGetValue(entryId, out Buddy? self) || self.Type != BuddyType.InRequest) {
            logger.Debug("Could not find entry:{EntryId} to Decline", entryId);
            return;
        }

        try {
            using GameStorage.Request db = session.GameStorage.Context();
            long receiverId = self.Info.CharacterId;
            BuddyEntry? other = db.GetBuddy(receiverId, session.CharacterId);
            if (other == null) {
                logger.Warning("Decline without paired entry");
                return;
            }

            if (!buddies.Remove(entryId) || !db.RemoveBuddy(self, other)) {
                return;
            }

            session.Send(BuddyPacket.Decline(self));
            BuddyResponse _ = session.World.Buddy(new BuddyRequest {
                ReceiverId = receiverId,
                Decline = new BuddyRequest.Types.Decline { EntryId = other.Id },
            });
        } catch (SystemException ex) {
            logger.Warning(ex, "Decline failed");
        }
    }

    public void ReceiveDecline(long entryId) {
        // DB written by sender as single transaction.
        if (!buddies.Remove(entryId, out Buddy? self)) {
            logger.Debug("Could not find entry:{EntryId} for ReceiveDecline", entryId);
            return;
        }

        session.Send(BuddyPacket.Remove(self));
    }

    public void SendBlock(long entryId, string name, string message) {
        if (name.Length > Constant.CharacterNameLengthMax || message.Length > Constant.BuddyMessageLengthMax) {
            session.Send(BuddyPacket.Block(error: s_buddy_err_unknown));
            return;
        }
        if (blocked.Count >= Constants.MaxBlockCount) {
            session.Send(BuddyPacket.Block(name: name, error: s_buddy_err_max_block));
            return;
        }
        if (entryId == 0) {
            // entryId is 0 when blocking by |name|.
            // We still search for entryId to convert the existing buddy entry.
            foreach (Buddy value in buddies.Values) {
                if (value.Info.Name == name) {
                    entryId = value.Id;
                }
            }
        } else {
            if (blocked.TryGetValue(entryId, out _)) {
                logger.Information("Could not find entry:{EntryId} to Block", entryId);
                return;
            }
        }

        try {
            using GameStorage.Request db = session.GameStorage.Context();
            long receiverId;
            if (buddies.Remove(entryId, out Buddy? self)) {
                EndListen(self);

                receiverId = self.Info.CharacterId;
                self.SetType(BuddyType.Blocked);
                self.Message = message;
                db.UpdateBuddy(self);

                blocked[self.Id] = self;
                session.Send(BuddyPacket.Block(self.Id, self.Info.Name, self.Message));
                session.Send(BuddyPacket.UpdateInfo(self));
            } else {
                receiverId = db.GetCharacterId(name);
                if (receiverId == default) {
                    session.Send(BuddyPacket.Block(error: s_buddy_err_miss_id));
                    return;
                }

                BuddyEntry? selfEntry = db.CreateBuddy(session.CharacterId, receiverId, BuddyType.Blocked, message);
                if (selfEntry == null || !session.PlayerInfo.GetOrFetch(selfEntry.BuddyId, out PlayerInfo? selfInfo)) {
                    session.Send(BuddyPacket.Block(error: s_buddy_err_unknown));
                    return;
                }

                self = new Buddy(selfEntry, selfInfo);
                blocked[self.Id] = self;
                session.Send(BuddyPacket.Block(self.Id, self.Info.Name, self.Message));
                session.Send(BuddyPacket.Append(self));
            }

            // Delete entry for existing buddy if it exists.
            BuddyEntry? other = db.GetBuddy(receiverId, session.CharacterId);
            if (other == null) {
                return;
            }

            if (other.Type != BuddyType.Blocked && db.RemoveBuddy(other)) {
                BuddyResponse _ = session.World.Buddy(new BuddyRequest {
                    ReceiverId = receiverId,
                    Block = new BuddyRequest.Types.Block { SenderId = session.CharacterId },
                });
            }
        } catch (SystemException ex) {
            logger.Warning(ex, "Block failed");
            session.Send(BuddyPacket.Block(error: s_buddy_err_unknown));
        }
    }

    public void ReceiveBlock(long senderId) {
        if (!TryGet(senderId, out Buddy? buddy)) {
            logger.Debug("Could not find sender:{SenderId} for ReceiveBlock", senderId);
            return;
        }

        if (buddies.Remove(buddy.Id)) {
            EndListen(buddy);

            session.Send(BuddyPacket.Remove(buddy));
        }
    }

    public void Unblock(long entryId) {
        if (!blocked.Remove(entryId, out Buddy? self)) {
            logger.Debug("Could not find entry:{EntryId} to Unblock", entryId);
            return;
        }

        try {
            using GameStorage.Request db = session.GameStorage.Context();
            if (!db.RemoveBuddy(self)) {
                return;
            }

            session.Send(BuddyPacket.Unblock(self));
            session.Send(BuddyPacket.Remove(self));
        } catch (SystemException ex) {
            logger.Warning(ex, "Unblock failed");
        }
    }

    public void SendDelete(long entryId) {
        if (!buddies.TryGetValue(entryId, out Buddy? self) || self.Type != BuddyType.Default) {
            logger.Debug("Could not find entry:{EntryId} to Delete", entryId);
            return;
        }

        try {
            using GameStorage.Request db = session.GameStorage.Context();
            long receiverId = self.Info.CharacterId;
            BuddyEntry? other = db.GetBuddy(receiverId, session.CharacterId);
            if (other == null) {
                logger.Warning("Delete without paired entry");
                return;
            }

            if (!db.RemoveBuddy(self, other)) {
                return;
            }

            if (!buddies.Remove(entryId, out self)) {
                return;
            }
            EndListen(self);
            session.Send(BuddyPacket.Remove(self));
            BuddyResponse _ = session.World.Buddy(new BuddyRequest {
                ReceiverId = receiverId,
                Delete = new BuddyRequest.Types.Delete { EntryId = other.Id },
            });
        } catch (SystemException ex) {
            logger.Warning(ex, "Delete failed");
        }
    }

    public void ReceiveDelete(long entryId) {
        // DB written by sender as single transaction.
        if (!buddies.Remove(entryId, out Buddy? self)) {
            logger.Debug("Could not find entry:{EntryId} for ReceiveDelete", entryId);
            return;
        }

        EndListen(self);
        session.Send(BuddyPacket.Remove(self));
    }

    public void UpdateBlock(long entryId, string name, string message) {
        if (name.Length > Constant.CharacterNameLengthMax || message.Length > Constant.BuddyMessageLengthMax) {
            session.Send(BuddyPacket.UpdateBlock(error: s_buddy_err_unknown));
            return;
        }
        if (!blocked.TryGetValue(entryId, out Buddy? self)) {
            session.Send(BuddyPacket.UpdateBlock(error: s_buddy_err_unknown));
            return;
        }

        try {
            using GameStorage.Request db = session.GameStorage.Context();
            self.Message = message;
            if (!db.UpdateBuddy(self)) {
                return;
            }

            session.Send(BuddyPacket.UpdateBlock(self.Id, self.Info.Name, self.Message));
        } catch (SystemException ex) {
            logger.Warning(ex, "UpdateBlock failed");
            session.Send(BuddyPacket.UpdateBlock(error: s_buddy_err_unknown));
        }
    }

    public void SendCancel(long entryId) {
        if (!buddies.TryGetValue(entryId, out Buddy? self) || self.Type != BuddyType.OutRequest) {
            logger.Debug("Could not find entry:{EntryId} to Cancel", entryId);
            return;
        }

        try {
            using GameStorage.Request db = session.GameStorage.Context();
            long receiverId = self.Info.CharacterId;
            BuddyEntry? other = db.GetBuddy(receiverId, session.CharacterId);
            if (other == null) {
                logger.Warning("Cancel without paired entry");
                return;
            }

            if (!db.RemoveBuddy(self, other)) {
                return;
            }

            session.Send(BuddyPacket.Cancel(self));
            BuddyResponse _ = session.World.Buddy(new BuddyRequest {
                ReceiverId = receiverId,
                Cancel = new BuddyRequest.Types.Cancel { EntryId = other.Id },
            });
        } catch (SystemException ex) {
            logger.Warning(ex, "Cancel failed");
        }
    }

    public void ReceiveCancel(long entryId) {
        // DB written by sender as single transaction.
        if (!buddies.Remove(entryId, out Buddy? self)) {
            logger.Debug("Could not find entry:{EntryId} for ReceiveCancel", entryId);
            return;
        }

        session.Send(BuddyPacket.Remove(self));
    }

    private bool TryGet(long buddyId, [NotNullWhen(true)] out Buddy? result) {
        foreach (Buddy buddy in buddies.Values) {
            if (buddyId == buddy.Info.CharacterId) {
                result = buddy;
                return true;
            }
        }
        foreach (Buddy buddy in blocked.Values) {
            if (buddyId == buddy.Info.CharacterId) {
                result = buddy;
                return true;
            }
        }

        result = null;
        return false;
    }

    #region PlayerInfo Events
    private void BeginListen(Buddy buddy) {
        if (buddy.Type != BuddyType.Default) {
            return;
        }
        // Clean up previous token if necessary
        if (buddy.TokenSource != null) {
            logger.Warning("BeginListen called on Buddy {Id} that was already listening", buddy.Id);
            EndListen(buddy);
        }

        buddy.TokenSource = CancellationTokenSource.CreateLinkedTokenSource(tokenSource.Token);
        CancellationToken token = buddy.TokenSource.Token;
        var listener = new PlayerInfoListener(UpdateField.Buddy, (type, info) => SyncUpdate(token, buddy.Id, type, info));
        session.PlayerInfo.Listen(buddy.Info.CharacterId, listener);
    }

    private void EndListen(Buddy buddy) {
        buddy.TokenSource?.Cancel();
        buddy.TokenSource?.Dispose();
        buddy.TokenSource = null;
    }

    private bool SyncUpdate(CancellationToken cancel, long id, UpdateField type, IPlayerInfo info) {
        if (cancel.IsCancellationRequested || !buddies.TryGetValue(id, out Buddy? buddy)) {
            return true;
        }

        bool wasOnline = buddy.Info.Online;
        buddy.Info.Update(type, info);

        session.Send(BuddyPacket.UpdateInfo(buddy));
        if (buddy.Info.Online != wasOnline) {
            session.Send(BuddyPacket.NotifyOnline(buddy));
        }
        return false;
    }
    #endregion
}
