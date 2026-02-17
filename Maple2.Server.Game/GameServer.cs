using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Autofac;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.PacketLib.Tools;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Network;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.DebugGraphics;

namespace Maple2.Server.Game;

public class GameServer : Server<GameSession> {
    private readonly object mutex = new();
    private readonly FieldManager.Factory fieldFactory;
    private readonly HashSet<GameSession> connectingSessions;
    private readonly Dictionary<long, GameSession> sessions;
    private readonly ImmutableList<SystemBanner> bannerCache;
    private readonly ConcurrentDictionary<int, PremiumMarketItem> premiumMarketCache;
    private readonly GameStorage gameStorage;
    private readonly IGraphicsContext debugGraphicsContext;

    private readonly ItemMetadataStorage itemMetadataStorage;

    private static short _channel = 0;

    public GameServer(FieldManager.Factory fieldFactory, PacketRouter<GameSession> router, IComponentContext context, GameStorage gameStorage, ItemMetadataStorage itemMetadataStorage, ServerTableMetadataStorage serverTableMetadataStorage, IGraphicsContext debugGraphicsContext, int port, int channel)
            : base((ushort) port, router, context, serverTableMetadataStorage) {
        _channel = (short) channel;
        this.fieldFactory = fieldFactory;
        connectingSessions = [];
        sessions = new Dictionary<long, GameSession>();
        this.gameStorage = gameStorage;
        this.debugGraphicsContext = debugGraphicsContext;
        this.itemMetadataStorage = itemMetadataStorage;

        using GameStorage.Request db = gameStorage.Context();
        bannerCache = db.GetBanners().ToImmutableList();

        premiumMarketCache = new ConcurrentDictionary<int, PremiumMarketItem>();
        foreach ((int id, MeretMarketItemMetadata marketItemMetadata) in serverTableMetadataStorage.MeretMarketTable.Entries) {
            if (marketItemMetadata.ParentId != 0) {
                if (premiumMarketCache.TryGetValue(marketItemMetadata.ParentId, out PremiumMarketItem? parentItem) &&
                    itemMetadataStorage.TryGet(parentItem.Metadata.ItemId, out ItemMetadata? subItemMetadata)) {
                    parentItem.AdditionalQuantities.Add(new PremiumMarketItem(marketItemMetadata, subItemMetadata));
                }
                continue;
            }

            if (!itemMetadataStorage.TryGet(marketItemMetadata.ItemId, out ItemMetadata? itemMetadata)) {
                continue;
            }
            premiumMarketCache.TryAdd(id, new PremiumMarketItem(marketItemMetadata, itemMetadata));
        }

        debugGraphicsContext.Initialize();
    }

    public override void OnConnected(GameSession session) {
        lock (mutex) {
            connectingSessions.Remove(session);
            sessions[session.CharacterId] = session;
        }
    }

    public override void OnDisconnected(GameSession session) {
        lock (mutex) {
            connectingSessions.Remove(session);
            sessions.Remove(session.CharacterId);
        }
    }

    public bool GetSession(long characterId, [NotNullWhen(true)] out GameSession? session) {
        lock (mutex) {
            return sessions.TryGetValue(characterId, out session);
        }
    }

    public GameSession? GetSessionByAccountId(long accountId) {
        lock (mutex) {
            return sessions.Values.FirstOrDefault(session => session.AccountId == accountId);
        }
    }

    public List<GameSession> GetSessions() {
        lock (mutex) {
            return sessions.Values.ToList();
        }
    }

    protected override void AddSession(GameSession session) {
        lock (mutex) {
            connectingSessions.Add(session);
        }

        Logger.Information("Game client connecting: {Session}", session);
        session.Start();
    }

    public FieldManager? GetField(int mapId, int roomId = 0) {
        return fieldFactory.Get(mapId, roomId: roomId);
    }

    public DungeonFieldManager? CreateDungeon(DungeonRoomMetadata metadata, long requesterId, int size, int partyId) {
        return fieldFactory.CreateDungeon(metadata, requesterId, size, partyId);
    }

    public MigrationError DestroyDungeon(int roomId) {
        return fieldFactory.DestroyDungeon(roomId);
    }

    public IList<GameEvent> FindEvent(GameEventType type) {
        return eventCache.Values.Where(gameEvent => gameEvent.Metadata.Type == type && gameEvent.IsActive()).ToList();
    }

    public GameEvent? FindEvent(int eventId) {
        return eventCache.TryGetValue(eventId, out GameEvent? gameEvent) && gameEvent.IsActive() ? gameEvent : null;
    }

    public void AddEvent(GameEvent gameEvent) {
        if (!eventCache.TryAdd(gameEvent.Id, gameEvent)) {
            return;
        }

        lock (mutex) {
            foreach (GameSession session in sessions.Values) {
                session.Send(GameEventPacket.Add(gameEvent));
            }
        }
    }

    public void RemoveEvent(int eventId) {
        if (!eventCache.Remove(eventId, out GameEvent? gameEvent)) {
            return;
        }

        lock (mutex) {
            foreach (GameSession session in sessions.Values) {
                session.Send(GameEventPacket.Remove(gameEvent.Id));
            }
        }
    }

    public IEnumerable<GameEvent> GetEvents() => eventCache.Values.Where(gameEvent => gameEvent.IsActive());

    public IList<SystemBanner> GetSystemBanners() => bannerCache;

    public ICollection<PremiumMarketItem> GetPremiumMarketItems(params int[] tabIds) {
        if (tabIds.Length == 0) {
            return premiumMarketCache.Values;
        }

        return premiumMarketCache.Values.Where(item => tabIds.Contains(item.TabId)).ToList();
    }

    public PremiumMarketItem? GetPremiumMarketItem(int id, int subId) {
        if (subId == 0) {
            return premiumMarketCache.GetValueOrDefault(id);
        }

        return premiumMarketCache.TryGetValue(id, out PremiumMarketItem? item) ? item.AdditionalQuantities.FirstOrDefault(subItem => subItem.Id == subId) : null;
    }

    public void DailyReset() {
        lock (mutex) {
            foreach (GameSession session in sessions.Values) {
                session.DailyReset();
            }
        }
    }

    public void WeeklyReset() {
        lock (mutex) {
            foreach (GameSession session in sessions.Values) {
                session.WeeklyReset();
            }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken) {
        debugGraphicsContext.CleanUp();

        lock (mutex) {
            foreach (GameSession session in connectingSessions) {
                session.Send(NoticePacket.Disconnect(new InterfaceText("GameServer Maintenance")));
                session.Dispose();
            }
            foreach (GameSession session in sessions.Values) {
                session.Send(NoticePacket.Disconnect(new InterfaceText("GameServer Maintenance")));
                session.Dispose();
            }
            fieldFactory.Dispose();
        }

        return base.StopAsync(cancellationToken);
    }

    public void Broadcast(ByteWriter packet) {
        lock (mutex) {
            foreach (GameSession session in sessions.Values) {
                session.Send(packet);
            }
        }
    }

    public static short GetChannel() {
        return _channel;
    }
}
