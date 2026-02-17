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
    private readonly PlayerInfoLookup playerInfoLookup;
    private readonly Thread thread;
    private readonly Thread heartbeatThread;
    private readonly EventQueue scheduler;
    private readonly CancellationTokenSource tokenSource = new();
    private readonly ConcurrentDictionary<int, string> memoryStringBoards;
    private static int _globalIdCounter;

    private readonly ILogger logger = Log.ForContext<WorldServer>();

    private readonly LoginClient login;

    public WorldServer(GameStorage gameStorage, ChannelClientLookup channelClients, ServerTableMetadataStorage serverTableMetadata, GlobalPortalLookup globalPortalLookup, PlayerInfoLookup playerInfoLookup, LoginClient login, ItemMetadataStorage itemMetadata) {
        this.gameStorage = gameStorage;
        this.channelClients = channelClients;
        this.serverTableMetadata = serverTableMetadata;
        this.globalPortalLookup = globalPortalLookup;
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
        StartWorldEvents();
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
        int daysSinceFriday = ((int) now.DayOfWeek - (int) DayOfWeek.Friday + 7) % 7;
        DateTime lastFridayMidnight = now.Date.AddDays(-daysSinceFriday);

        if (lastReset < lastFridayMidnight) {
            db.WeeklyReset();
        }

        DateTime nextFriday = now.NextDayOfWeek(DayOfWeek.Friday);
        TimeSpan timeUntilFriday = nextFriday - now;
        scheduler.Schedule(ScheduleWeeklyReset, timeUntilFriday);
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
