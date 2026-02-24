using System.Collections.Concurrent;
using Grpc.Core;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Channel.Service;
using Maple2.Server.World.Containers;
using Maple2.Tools.Extensions;
using Maple2.Tools.Scheduler;
using Serilog;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;
using LoginClient = Maple2.Server.Login.Service.Login.LoginClient;


namespace Maple2.Server.World;

public class WorldServer {
    private readonly GameStorage gameStorage;
    private readonly ChannelClientLookup channelClients;
    private readonly ServerTableMetadataStorage serverTableMetadata;
    private readonly ItemMetadataStorage itemMetadata;
    private readonly GlobalPortalLookup globalPortalLookup;
    private readonly WorldBossLookup worldBossLookup;
    private readonly PlayerInfoLookup playerInfoLookup;
    private readonly Thread thread;
    private readonly Thread heartbeatThread;
    private readonly EventQueue scheduler;
    private readonly CancellationTokenSource tokenSource = new();
    private readonly ConcurrentDictionary<int, string> memoryStringBoards;
    private static int _globalIdCounter;

    private readonly ILogger logger = Log.ForContext<WorldServer>();

    private readonly LoginClient login;

    public WorldServer(GameStorage gameStorage, ChannelClientLookup channelClients, ServerTableMetadataStorage serverTableMetadata, GlobalPortalLookup globalPortalLookup, WorldBossLookup worldBossLookup, PlayerInfoLookup playerInfoLookup, LoginClient login, ItemMetadataStorage itemMetadata) {
        this.gameStorage = gameStorage;
        this.channelClients = channelClients;
        this.serverTableMetadata = serverTableMetadata;
        this.globalPortalLookup = globalPortalLookup;
        this.worldBossLookup = worldBossLookup;
        this.playerInfoLookup = playerInfoLookup;
        this.login = login;
        this.itemMetadata = itemMetadata;
        scheduler = new EventQueue(logger);
        scheduler.Start();
        memoryStringBoards = [];

        // World initialization: set all characters offline and cleanup unowned items
        using GameStorage.Request db = gameStorage.Context();
        db.SetAllCharacterToOffline();
        db.DeleteUnownedItems();

        StartDailyReset();
        StartWeeklyReset();
        StartMonthlyReset();
        StartWorldEvents();
        StartWorldBossEvents();
        ScheduleGameEvents();
        FieldPlotExpiryCheck();
        thread = new Thread(Loop);
        thread.Start();

        heartbeatThread = new Thread(Heartbeat);
        heartbeatThread.Start();
    }

    private void Heartbeat() {
        while (!tokenSource.Token.IsCancellationRequested) {
            try {
                Task.Delay(TimeSpan.FromSeconds(30), tokenSource.Token).Wait(tokenSource.Token);

                login.Heartbeat(new HeartbeatRequest(), cancellationToken: tokenSource.Token);

                foreach (PlayerInfo playerInfo in playerInfoLookup.GetOnlinePlayerInfos()) {
                    if (playerInfo.CharacterId == 0) {
                        logger.Information("Player {CharacterId} is online without a character id, setting to offline", playerInfo.CharacterId);
                        playerInfo.Channel = -1;
                        continue;
                    }
                    if (!channelClients.TryGetClient(playerInfo.Channel, out ChannelClient? channel)) {
                        // Player is online and without a channel, set them to offline.
                        logger.Information("Player {CharacterId} is online without a channel, setting to offline", playerInfo.CharacterId);
                        SetOffline(playerInfo);
                        continue;
                    }

                    try {
                        HeartbeatResponse? response = channel.Heartbeat(new HeartbeatRequest {
                            CharacterId = playerInfo.CharacterId,
                        }, cancellationToken: tokenSource.Token);
                        if (response is { Success: false }) {
                            SetOffline(playerInfo);
                            continue;
                        }
                        playerInfo.RetryHeartbeat = 3;
                    } catch (RpcException) {
                        if (playerInfo.RetryHeartbeat >= 0) {
                            playerInfo.RetryHeartbeat--;
                            continue;
                        }
                        SetOffline(playerInfo);
                    }

                }
            } catch (OperationCanceledException) {
                break;
            } catch (Exception ex) {
                logger.Warning(ex, "Heartbeat loop error");
            }
        }
    }

    public void SetOffline(PlayerInfo playerInfo) {
        playerInfoLookup.Update(new PlayerUpdateRequest {
            AccountId = playerInfo.AccountId,
            CharacterId = playerInfo.CharacterId,
            LastOnlineTime = DateTime.UtcNow.ToEpochSeconds(),
            Channel = -1,
            Async = true,
        });
    }

    private void Loop() {
        while (!tokenSource.Token.IsCancellationRequested) {
            try {
                scheduler.InvokeAll();
            } catch (Exception e) {
                logger.Error(e, "Error in world server loop");
            }
            try {
                Task.Delay(TimeSpan.FromMinutes(1), tokenSource.Token).Wait();
            } catch { /* do nothing */
            }
        }
    }

    public void Stop() {
        tokenSource.Cancel();
        thread.Join();
        heartbeatThread.Join();
        scheduler.Stop();
    }

    #region Daily Reset
    private void StartDailyReset() {
        // Daily reset
        using GameStorage.Request db = gameStorage.Context();
        DateTime lastReset = db.GetLastDailyReset();

        // Get last midnight.
        DateTime now = DateTime.Now;
        var lastMidnight = new DateTime(now.Year, now.Month, now.Day);
        if (lastReset < lastMidnight) {
            db.DailyReset();
        }

        DateTime nextMidnight = lastMidnight.AddDays(1);
        TimeSpan timeUntilMidnight = nextMidnight - now;
        scheduler.Schedule(ScheduleDailyReset, timeUntilMidnight);
    }

    private void ScheduleDailyReset() {
        DailyReset();
        // Schedule it to repeat every once a day.
        scheduler.ScheduleRepeated(DailyReset, TimeSpan.FromDays(1), strict: true);
    }

    private void DailyReset() {
        using GameStorage.Request db = gameStorage.Context();
        db.DailyReset();
        foreach ((int channelId, ChannelClient channelClient) in channelClients) {
            channelClient.GameReset(new GameResetRequest {
                Daily = new GameResetRequest.Types.Daily(),
            });
        }
    }
    #endregion

    #region Weekly Reset
    private void StartWeeklyReset() {
        using GameStorage.Request db = gameStorage.Context();
        DateTime lastReset = db.GetLastWeeklyReset();

        DateTime now = DateTime.Now;
        int daysSinceReset = ((int) now.DayOfWeek - (int) Constant.ResetDay + 7) % 7;
        DateTime lastResetMidnight = now.Date.AddDays(-daysSinceReset);

        if (lastReset < lastResetMidnight) {
            db.WeeklyReset();
        }

        DateTime nextReset = now.NextDayOfWeek(Constant.ResetDay).Date;
        TimeSpan timeUntilReset = nextReset - now;
        scheduler.Schedule(ScheduleWeeklyReset, timeUntilReset);
    }

    private void ScheduleWeeklyReset() {
        WeeklyReset();
        scheduler.ScheduleRepeated(WeeklyReset, TimeSpan.FromDays(7), strict: true);
    }

    private void WeeklyReset() {
        using GameStorage.Request db = gameStorage.Context();
        db.WeeklyReset();
        foreach ((int channelId, ChannelClient channelClient) in channelClients) {
            channelClient.GameReset(new GameResetRequest {
                Weekly = new GameResetRequest.Types.Weekly(),
            });
        }
    }
    #endregion

    #region Monthly Reset
    private void StartMonthlyReset() {
        using GameStorage.Request db = gameStorage.Context();
        DateTime lastReset = db.GetLastMonthlyReset();

        DateTime now = DateTime.Now;
        DateTime firstOfMonth = new DateTime(now.Year, now.Month, 1);

        if (lastReset < firstOfMonth) {
            db.MonthlyReset();
        }

        DateTime nextMonth = firstOfMonth.AddMonths(1);
        TimeSpan timeUntilReset = nextMonth - now;
        scheduler.Schedule(ScheduleMonthlyReset, timeUntilReset);
    }

    private void ScheduleMonthlyReset() {
        MonthlyReset();
        DateTime now = DateTime.Now;
        DateTime nextMonth = new DateTime(now.Year, now.Month, 1).AddMonths(1);
        scheduler.Schedule(ScheduleMonthlyReset, nextMonth - now);
    }

    private void MonthlyReset() {
        using GameStorage.Request db = gameStorage.Context();
        db.MonthlyReset();
        foreach ((int channelId, ChannelClient channelClient) in channelClients) {
            channelClient.GameReset(new GameResetRequest {
                Monthly = new GameResetRequest.Types.Monthly(),
            });
        }
    }
    #endregion

    private void StartWorldEvents() {
        // Global Portal
        IReadOnlyDictionary<int, GlobalPortalMetadata> globalEvents = serverTableMetadata.TimeEventTable.GlobalPortal;
        foreach ((int eventId, GlobalPortalMetadata eventData) in globalEvents) {
            if (eventData.EndTime < DateTime.Now) {
                continue;
            }

            // There is no cycle time, so we skip it.
            if (eventData.CycleTime == TimeSpan.Zero) {
                continue;
            }
            DateTime startTime = eventData.StartTime;
            if (DateTime.Now > startTime) {
                // catch up to a time after the start time
                while (startTime < DateTime.Now) {
                    startTime += eventData.CycleTime;
                }
                if (startTime > eventData.EndTime) {
                    continue;
                }
                scheduler.Schedule(() => GlobalPortal(eventData, startTime), startTime - DateTime.Now);
            }
        }
    }

    private void GlobalPortal(GlobalPortalMetadata data, DateTime startTime) {
        // check probability
        bool run = !(data.Probability < 100 && Random.Shared.Next(100) > data.Probability);

        if (run) {
            DateTime now = DateTime.Now;
            globalPortalLookup.Create(data, (long) (now.ToEpochSeconds() + data.LifeTime.TotalMilliseconds), out int eventId);
            if (!globalPortalLookup.TryGet(out GlobalPortalManager? manager)) {
                logger.Error("Failed to create global portal");
                return;
            }

            manager.CreateFields();

            Task.Factory.StartNew(() => {
                Thread.Sleep(data.LifeTime);
                if (globalPortalLookup.TryGet(out GlobalPortalManager? globalPortalManager) && globalPortalManager.Portal.Id == eventId) {
                    globalPortalLookup.Dispose();
                }
            });
        }

        DateTime nextRunTime = startTime + data.CycleTime;
        if (data.RandomTime > TimeSpan.Zero) {
            nextRunTime += TimeSpan.FromMilliseconds(Random.Shared.Next((int) data.RandomTime.TotalMilliseconds));
        }

        if (data.EndTime < nextRunTime) {
            return;
        }

        scheduler.Schedule(() => GlobalPortal(data, nextRunTime), nextRunTime - DateTime.Now);
    }

    private void StartWorldBossEvents() {
        int scheduled = 0;
        foreach ((int _, WorldBossMetadata boss) in serverTableMetadata.TimeEventTable.WorldBoss) {
            if (boss.EndTime < DateTime.Now) {
                continue;
            }
            if (boss.CycleTime == TimeSpan.Zero) {
                continue;
            }

            DateTime startTime = boss.StartTime;
            if (DateTime.Now > startTime) {
                // Catch up to the next scheduled spawn after now
                while (startTime < DateTime.Now) {
                    startTime += boss.CycleTime;
                }
                if (startTime > boss.EndTime) {
                    continue;
                }
            }

            TimeSpan delay = startTime - DateTime.Now;
            scheduler.Schedule(() => SpawnWorldBoss(boss, startTime), delay);
            scheduled++;
        }
        logger.Information("WorldBoss scheduler started — {Count} bosses scheduled", scheduled);
    }

    private void SpawnWorldBoss(WorldBossMetadata metadata, DateTime spawnTime) {
        bool shouldSpawn = !(metadata.Probability < 100 && Random.Shared.Next(100) >= metadata.Probability);

        // Compute next spawn time before Create() so it can be broadcast with the announce
        DateTime nextSpawn = spawnTime + metadata.CycleTime;
        if (metadata.RandomTime > TimeSpan.Zero) {
            nextSpawn += TimeSpan.FromMilliseconds(Random.Shared.Next((int) metadata.RandomTime.TotalMilliseconds));
        }
        long nextSpawnTimestamp = nextSpawn <= metadata.EndTime ? new DateTimeOffset(nextSpawn).ToUnixTimeSeconds() : 0;

        if (!shouldSpawn) {
            logger.Debug("WorldBoss {Id} roll failed (probability {Probability}%) — skipping spawn, next at {NextSpawn:HH:mm:ss} UTC",
                metadata.Id, metadata.Probability, nextSpawn.ToUniversalTime());
        } else if (worldBossLookup.TryGet(metadata.Id, out _)) {
            logger.Debug("WorldBoss {Id} still active from previous cycle — skipping spawn", metadata.Id);
        } else {
            long spawnTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long endTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long) metadata.LifeTime.TotalMilliseconds;
            if (worldBossLookup.Create(metadata, endTick, nextSpawnTimestamp, out int eventId)) {
                if (worldBossLookup.TryGet(metadata.Id, out WorldBossManager? manager)) {
                    manager.Boss.SpawnTimestamp = spawnTimestamp;
                    manager.Announce();
                }
                logger.Information("WorldBoss {Id} (NpcId: {NpcId}) spawned — eventId: {EventId}, alive for {LifeTime:mm\\:ss}, next spawn at {NextSpawn:HH:mm:ss} UTC",
                    metadata.Id, metadata.NpcIds.Length > 0 ? metadata.NpcIds[0] : 0, eventId, metadata.LifeTime, nextSpawn.ToUniversalTime());

                TimeSpan warnDelay = metadata.LifeTime - TimeSpan.FromMinutes(1);
                _ = MonitorWorldBossLifetimeAsync(metadata.Id, warnDelay, metadata.LifeTime, tokenSource.Token);
            }
        }

        if (nextSpawn > metadata.EndTime) {
            logger.Information("WorldBoss {Id} will not respawn — next spawn {NextSpawn} is past EndTime {EndTime}", metadata.Id, nextSpawn, metadata.EndTime);
            return;
        }

        scheduler.Schedule(() => SpawnWorldBoss(metadata, nextSpawn), nextSpawn - DateTime.Now);
    }

    private async Task MonitorWorldBossLifetimeAsync(int metadataId, TimeSpan warnDelay, TimeSpan lifeTime, CancellationToken token) {
        try {
            if (warnDelay > TimeSpan.Zero) {
                await Task.Delay(warnDelay, token);
                if (worldBossLookup.TryGet(metadataId, out WorldBossManager? warnManager)) {
                    warnManager.WarnChannels();
                }
                await Task.Delay(TimeSpan.FromMinutes(1), token);
            } else {
                await Task.Delay(lifeTime, token);
            }
            worldBossLookup.Dispose(metadataId);
            logger.Information("WorldBoss {Id} lifetime expired — despawning", metadataId);
        } catch (OperationCanceledException) {
            // Server shutting down — do not despawn or log as error
        } catch (Exception ex) {
            logger.Error(ex, "Error monitoring field boss lifetime for metadata {MetadataId}", metadataId);
        }
    }

    private void ScheduleGameEvents() {
        IEnumerable<GameEvent> events = serverTableMetadata.GetGameEvents().ToList();
        // Add Events
        // Get only events that havent been started. Started events already get loaded on game/login servers on start up
        foreach (GameEvent data in events.Where(gameEvent => gameEvent.StartTime > DateTimeOffset.Now.ToUnixTimeSeconds())) {
            scheduler.Schedule(() => AddGameEvent(data.Id), TimeSpan.FromSeconds(data.StartTime - DateTimeOffset.Now.ToUnixTimeSeconds()));
        }

        // Remove Events
        foreach (GameEvent data in events.Where(gameEvent => gameEvent.EndTime > DateTimeOffset.Now.ToUnixTimeSeconds())) {
            scheduler.Schedule(() => RemoveGameEvent(data.Id), TimeSpan.FromSeconds(data.EndTime - DateTimeOffset.Now.ToUnixTimeSeconds()));
        }
    }

    public void FieldPlotExpiryCheck() {
        using GameStorage.Request db = gameStorage.Context();
        // Get all plots that have expired but are not yet pending
        List<PlotInfo> expiredPlots = db.GetPlotsToExpire();
        if (expiredPlots.Count > 0) {
            foreach (PlotInfo plot in expiredPlots) {
                bool forfeit = false;
                try {
                    if (plot.OwnerId > 0 && plot.ExpiryTime < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) {
                        SetPlotAsPending(db, plot);
                        forfeit = true;
                        // mark as open when 3 days has passed since the expiry time
                    } else if (plot.OwnerId == 0 && plot.ExpiryTime + Constant.UgcHomeSaleWaitingTime.TotalSeconds < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) {
                        logger.Information("Marking plot {PlotId} as open (no owner)", plot.Id);
                        db.SetPlotOpen(plot.Id); // Mark as open
                    } else {
                        continue; // Still valid, skip
                    }
                } catch (Exception e) {
                    logger.Error(e, "Error processing plot {PlotId} for expiry check", plot.Id);
                    continue; // Skip this plot if there's an error
                }

                // Notify channels about the expired plots
                foreach ((int _, ChannelClient channelClient) in channelClients) {
                    logger.Information("Notifying channel about expired plot {PlotId}", plot.Id);
                    channelClient.UpdateFieldPlot(new FieldPlotRequest {
                        MapId = plot.MapId,
                        PlotNumber = plot.Number,
                        UpdatePlot = new FieldPlotRequest.Types.UpdatePlot() {
                            AccountId = plot.OwnerId,
                            Forfeit = forfeit,
                        },
                    });
                }
            }
        }

        // Schedule next check for the next soonest expiry
        PlotInfo? nextPlot = db.GetSoonestPlotFromExpire();
        TimeSpan delay;
        if (nextPlot is not null) {
            DateTimeOffset nextExpiry = DateTimeOffset.FromUnixTimeSeconds(nextPlot.ExpiryTime);
            delay = nextExpiry - DateTimeOffset.UtcNow;
            if (delay < TimeSpan.Zero) {
                delay = TimeSpan.Zero;
            }
        } else {
            delay = TimeSpan.FromDays(1); // Default to 1 day if no plots are found
        }
        scheduler.Schedule(FieldPlotExpiryCheck, delay);
    }

    // Marks a plot as pending, removes its cubes, and adds them to the owner's inventory.
    private void SetPlotAsPending(GameStorage.Request db, PlotInfo plot) {
        logger.Information("Marking plot {PlotId} as pending (owner: {OwnerId})", plot.Id, plot.OwnerId);

        db.SetPlotPending(plot.Id);

        Plot? outdoorPlot = db.GetOutdoorPlotInfo(plot.Number, plot.MapId);
        if (outdoorPlot == null) {
            logger.Warning("Outdoor plot not found for plot id {PlotId}", plot.Id);
            return;
        }

        List<Item>? items = db.GetItemGroupsNoTracking(plot.OwnerId, ItemGroup.Furnishing).GetValueOrDefault(ItemGroup.Furnishing);
        if (items == null) {
            logger.Warning("No furnishing items found for owner id {OwnerId}", outdoorPlot.OwnerId);
            return;
        }

        var changedItems = new List<Item>();

        foreach (PlotCube cube in outdoorPlot.Cubes.Values.ToList()) {
            // remove cube from plot
            db.DeleteCube(cube);

            // add item to account inventory
            Item? stored = items.FirstOrDefault(existing => existing.Id == cube.ItemId && existing.Template?.Url == cube.Template?.Url);
            if (stored == null) {
                Item? item = CreateItem(cube.ItemId);
                if (item == null) {
                    continue;
                }
                item.Group = ItemGroup.Furnishing;
                db.CreateItem(outdoorPlot.OwnerId, item);
                continue;
            }

            stored.Amount += 1;
            if (!changedItems.Contains(stored)) {
                changedItems.Add(stored);
            }
        }

        if (changedItems.Count > 0) {
            db.SaveItems(plot.OwnerId, changedItems.ToArray());
        }
        return;

        Item? CreateItem(int itemId, int rarity = -1, int amount = 1) {
            if (!itemMetadata.TryGet(itemId, out ItemMetadata? metadata)) {
                return null;
            }

            if (rarity <= 0) {
                if (metadata.Option != null && metadata.Option.ConstantId is < 6 and > 0) {
                    rarity = metadata.Option.ConstantId;
                } else {
                    rarity = 1;
                }
            }

            return new Item(metadata, rarity, amount);
        }
    }

    private void AddGameEvent(int eventId) {
        foreach ((int channelId, ChannelClient channelClient) in channelClients) {
            channelClient.GameEvent(new GameEventRequest {
                Add = new GameEventRequest.Types.Add {
                    EventId = eventId,
                },
            });
        }
    }

    private void RemoveGameEvent(int eventId) {
        foreach ((int channelId, ChannelClient channelClient) in channelClients) {
            channelClient.GameEvent(new GameEventRequest {
                Remove = new GameEventRequest.Types.Remove {
                    EventId = eventId,
                },
            });
        }
    }

    private static int NextGlobalId() => Interlocked.Increment(ref _globalIdCounter);

    public int AddCustomStringBoard(string message) {
        if (string.IsNullOrEmpty(message)) {
            return -1;
        }

        int id = NextGlobalId();
        memoryStringBoards.TryAdd(id, message);
        return id;
    }

    public bool RemoveCustomStringBoard(int id) {
        return memoryStringBoards.TryRemove(id, out _);
    }

    public IReadOnlyDictionary<int, string> GetCustomStringBoards() {
        return memoryStringBoards;
    }
}
