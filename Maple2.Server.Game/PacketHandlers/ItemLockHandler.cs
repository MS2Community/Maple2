using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class ItemLockHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestItemLock;

    private enum Command : byte {
        Reset = 0,
        Stage = 1,
        Unstage = 2,
        Commit = 3,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Reset:
                HandleReset(session);
                return;
            case Command.Stage:
                HandleStage(session, packet);
                return;
            case Command.Unstage:
                HandleUnstage(session, packet);
                return;
            case Command.Commit:
                HandleCommit(session, packet, Constants);
                return;
        }
    }

    private static void HandleReset(GameSession session) {
        Array.Clear(session.ItemLockStaging);
    }

    private static void HandleStage(GameSession session, IByteReader packet) {
        bool unlock = packet.ReadBool(); // false - lock|true - unlock
        long itemUid = packet.ReadLong();

        for (short i = 0; i < session.ItemLockStaging.Length; i++) {
            if (session.ItemLockStaging[i] != default) {
                continue;
            }

            session.ItemLockStaging[i] = itemUid;
            session.Send(ItemLockPacket.Stage(itemUid, i));
            return;
        }
    }

    private static void HandleUnstage(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();

        for (int i = 0; i < session.ItemLockStaging.Length; i++) {
            if (session.ItemLockStaging[i] != itemUid) {
                continue;
            }

            session.ItemLockStaging[i] = default;
            session.Send(ItemLockPacket.Unstage(itemUid));
            return;
        }
    }

    private static void HandleCommit(GameSession session, IByteReader packet, ConstantsTable constants) {
        bool unlock = packet.ReadBool(); // false - lock|true - unlock

        lock (session.Item) {
            var updatedItems = new List<Item>();
            foreach (long itemUid in session.ItemLockStaging) {
                if (itemUid == default) {
                    continue;
                }

                Item? item = session.Item.Inventory.Get(itemUid);
                if (item == null) {
                    continue;
                }

                if (unlock && item.IsLocked) {
                    item.IsLocked = false;
                    item.UnlockTime = DateTimeOffset.UtcNow.AddSeconds(constants.ItemUnLockTime).ToUnixTimeSeconds();
                    updatedItems.Add(item);
                } else if (!unlock && !item.IsLocked) {
                    item.IsLocked = true;
                    item.UnlockTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    updatedItems.Add(item);
                }
            }

            Array.Clear(session.ItemLockStaging);
            session.Send(ItemLockPacket.Commit(updatedItems));
        }
    }
}
