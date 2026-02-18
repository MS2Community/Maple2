using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Autofac;
using Grpc.Core;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Network;
using Maple2.Server.Core.Packets;
using Maple2.Server.Core.Sync;
using Maple2.Server.Game.Commands;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Maple2.Server.Game.Util.Sync;
using Maple2.Server.World.Service;
using Maple2.Tools.Extensions;
using Maple2.Tools.Scheduler;
using MigrationType = Maple2.Model.Enum.MigrationType;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Game.Session;

public sealed partial class GameSession : Core.Network.Session {
    protected override PatchType Type => PatchType.Ignore;
    public const int FIELD_KEY = 0x1234;

    // gameDisposeState: 0 = active, 1 = disposing, 2 = disposed
    private int gameDisposeState;
    private readonly GameServer server;

    public readonly CommandRouter CommandHandler;
    public readonly EventQueue Scheduler;

    public int ServerTick;
    public int ClientTick;

    public int Latency;
    public string PlayerName => Player.Value.Character.Name;
    public Guid MachineId { get; private set; }
    private bool preMigrationSaved;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    // ReSharper disable UnusedAutoPropertyAccessor.Global
    public required GameStorage GameStorage { get; init; }
    public required WorldClient World { get; init; }
    public required ItemMetadataStorage ItemMetadata { get; init; }
    public required SkillMetadataStorage SkillMetadata { get; init; }
    public required TableMetadataStorage TableMetadata { get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata { get; init; }
    public required MapMetadataStorage MapMetadata { get; init; }
    public required NpcMetadataStorage NpcMetadata { get; init; }
    public required AchievementMetadataStorage AchievementMetadata { get; init; }
    public required QuestMetadataStorage QuestMetadata { get; init; }
    public required ScriptMetadataStorage ScriptMetadata { get; init; }
    public required FunctionCubeMetadataStorage FunctionCubeMetadata { get; init; }
    public required RideMetadataStorage RideMetadata { get; init; }
    public required FieldManager.Factory FieldFactory { get; init; }
    public required ItemStatsCalculator ItemStatsCalc { private get; init; }
    public required PlayerInfoStorage PlayerInfo { get; init; }
    // ReSharper restore All
    // ReSharper restore UnusedAutoPropertyAccessor.Global
    #endregion

    public ConfigManager Config { get; set; } = null!;
    public MailManager Mail { get; set; } = null!;
    public GuildManager Guild { get; set; } = null!;
    public BuddyManager Buddy { get; set; } = null!;
    public ItemManager Item { get; set; } = null!;
    public HousingManager Housing { get; set; } = null!;
    public CurrencyManager Currency { get; set; } = null!;
    public MasteryManager Mastery { get; set; } = null!;
    public StatsManager Stats { get; set; } = null!;
    public BuffManager Buffs { get; set; } = null!;
    public ItemEnchantManager ItemEnchant { get; set; } = null!;
    public ItemMergeManager ItemMerge { get; set; } = null!;
    public ItemBoxManager ItemBox { get; set; } = null!;
    public BeautyManager Beauty { get; set; } = null!;
    public GameEventManager GameEvent { get; set; } = null!;
    public ExperienceManager Exp { get; set; } = null!;
    public AchievementManager Achievement { get; set; } = null!;
    public QuestManager Quest { get; set; } = null!;
    public ShopManager Shop { get; set; } = null!;
    public UgcMarketManager UgcMarket { get; set; } = null!;
    public BlackMarketManager BlackMarket { get; set; } = null!;
    public FieldManager? Field { get; set; }
    public FieldPlayer Player { get; private set; } = null!;
    public PartyManager Party { get; set; } = null!;
    public ConcurrentDictionary<int, GroupChatManager> GroupChats { get; set; }
    public ConcurrentDictionary<long, ClubManager> Clubs { get; set; }
    public SurvivalManager Survival { get; set; } = null!;
    public MarriageManager Marriage { get; set; } = null!;
    public FishingManager Fishing { get; set; } = null!;
    public DungeonManager Dungeon { get; set; } = null!;
    public AnimationManager Animation { get; set; } = null!;
    public RideManager Ride { get; set; } = null!;
    public MentoringManager Mentoring { get; set; } = null!;

    public GameSession(TcpClient tcpClient, GameServer server, IComponentContext context) : base(tcpClient) {
        this.server = server;
        State = SessionState.ChangeMap;
        CommandHandler = context.Resolve<CommandRouter>(new NamedParameter("session", this));
        Scheduler = new EventQueue(Logger);
        Scheduler.ScheduleRepeated(() => Send(TimeSyncPacket.Request()), TimeSpan.FromSeconds(1));

        OnLoop += Scheduler.InvokeAll;
        GroupChats = new ConcurrentDictionary<int, GroupChatManager>();
        Clubs = new ConcurrentDictionary<long, ClubManager>();
    }

    public bool FindSession(long characterId, [NotNullWhen(true)] out GameSession? other) {
        return server.GetSession(characterId, out other);
    }

    public bool FindField(int mapId, [NotNullWhen(true)] out FieldManager? field) {
        field = server.GetField(mapId);
        return field is not null;
    }

    public bool EnterServer(long accountId, Guid machineId, MigrateInResponse migrateResponse) {
        long characterId = migrateResponse.CharacterId;
        int channel = migrateResponse.Channel;
        int mapId = migrateResponse.MapId;
        int portalId = migrateResponse.PortalId;
        long ownerId = migrateResponse.OwnerId;
        int roomId = migrateResponse.RoomId;
        var migrationType = (MigrationType) migrateResponse.Type;
        var position = new Vector3(migrateResponse.PositionX, migrateResponse.PositionY, migrateResponse.PositionZ);

        AccountId = accountId;
        CharacterId = characterId;
        MachineId = machineId;

        State = SessionState.ChangeMap;
        server.OnConnected(this);

        using GameStorage.Request db = GameStorage.Context();
        db.BeginTransaction();
        int objectId = FieldManager.NextGlobalId();
        Player? player;
        try {
            AcquireLock(AccountId, 5);
            player = db.LoadPlayer(AccountId, CharacterId, objectId, GameServer.GetChannel());
            db.Commit();
        } finally {
            ReleaseLock(AccountId);
        }
        if (player == null) {
            Logger.Warning("Failed to load player from database: {AccountId}, {CharacterId}", AccountId, CharacterId);
            Send(MigrationPacket.MoveResult(MigrationError.s_move_err_default));
            return false;
        }

        Player = new FieldPlayer(this, player);
        Animation = new AnimationManager(this);
        Currency = new CurrencyManager(this);
        Mastery = new MasteryManager(this);
        Stats = new StatsManager(Player, ServerTableMetadata.UserStatTable);
        Config = new ConfigManager(db, this);
        Housing = new HousingManager(this, TableMetadata);
        Mail = new MailManager(this);
        ItemEnchant = new ItemEnchantManager(this);
        ItemMerge = new ItemMergeManager(this);
        ItemBox = new ItemBoxManager(this);
        Beauty = new BeautyManager(this);
        GameEvent = new GameEventManager(this);
        Exp = new ExperienceManager(this);
        Achievement = new AchievementManager(this);
        Quest = new QuestManager(this);
        Shop = new ShopManager(this);
        Guild = new GuildManager(this);
        Buddy = new BuddyManager(db, this);
        Item = new ItemManager(db, this, ItemStatsCalc);
        Buffs = new BuffManager(Player);
        UgcMarket = new UgcMarketManager(this);
        BlackMarket = new BlackMarketManager(this);
        Survival = new SurvivalManager(this);
        Marriage = new MarriageManager(this);
        Fishing = new FishingManager(this, TableMetadata, ServerTableMetadata);
        Dungeon = new DungeonManager(this);
        Ride = new RideManager(this);
        Mentoring = new MentoringManager(this);
        CommandHandler.RegisterCommands(); // Refresh commands with proper permissions
        GroupChatInfoResponse groupChatInfoRequest = World.GroupChatInfo(new GroupChatInfoRequest {
            CharacterId = CharacterId,
        });

        foreach (GroupChatInfo groupChatInfo in groupChatInfoRequest.Infos) {
            var manager = new GroupChatManager(groupChatInfo, this);
            GroupChats.TryAdd(groupChatInfo.Id, manager);
        }

        int fieldId = mapId == 0 ? player.Character.MapId : mapId;
        if (!PrepareField(fieldId, out FieldManager? fieldManager, portalId: portalId, ownerId: ownerId, roomId: roomId, position: position)) {
            Send(MigrationPacket.MoveResult(MigrationError.s_move_err_default));
            return false;
        }
        switch (migrationType) {
            case MigrationType.DecorPlanner:
            case MigrationType.BlueprintDesigner:
                player.Home.EnterPlanner((PlotMode) migrationType);
                Housing.GetFieldPlot()?.SetPlannerMode((PlotMode) migrationType);
                break;
            case MigrationType.Dungeon:
                if (fieldManager is not DungeonFieldManager dungeonField) {
                    Logger.Error("Failed to load dungeon field for dungeon migration");
                    Send(MigrationPacket.MoveResult(MigrationError.s_move_err_default));
                    return false;
                }
                Dungeon.SetDungeon(dungeonField);
                break;
            case MigrationType.Normal:
            default:
                break;
        }

        var playerUpdate = new PlayerUpdateRequest {
            AccountId = accountId,
            CharacterId = characterId,
            LastOnlineTime = Player.Value.Character.LastOnlineTime,
        };
        playerUpdate.SetFields(UpdateField.All, player);
        playerUpdate.Health = new HealthUpdate {
            CurrentHp = Stats.Values[BasicAttribute.Health].Current,
            TotalHp = Stats.Values[BasicAttribute.Health].Total,
        };
        PlayerInfo.SendUpdate(playerUpdate);

        //session.Send(Packet.Of(SendOp.REQUEST_SYSTEM_INFO));
        Send(MigrationPacket.MoveResult(MigrationError.ok));

        Survival.Load();
        // MeretMarket
        // UserConditionEvent
        // PCBangBonus
        Guild.Load();
        foreach ((int id, GroupChatManager groupChat) in GroupChats) {
            groupChat.Load();
        }

        ClubInfoResponse clubInfoResponse = World.ClubInfo(new ClubInfoRequest {
            CharacterId = CharacterId,
        });

        foreach (ClubInfo clubInfo in clubInfoResponse.Clubs) {
            var manager = ClubManager.Create(clubInfo, this);
            if (manager == null) {
                Logger.Error("Failed to create club manager for club {ClubId}", clubInfo.Id);
                continue;
            }
            if (Clubs.TryAdd(clubInfo.Id, manager)) {
                manager.Load();
            }
        }

        Buddy.Load();

        try {
            PlayerConfigResponse configResponse = World.PlayerConfig(new PlayerConfigRequest {
                Get = new PlayerConfigRequest.Types.Get(),
                RequesterId = CharacterId,
            });
            long currentTick = Environment.TickCount64;
            Buffs.SetCacheBuffs(configResponse.Buffs, currentTick);
            Config.SetCacheSkillCooldowns(configResponse.SkillCooldowns, currentTick);
            Config.SetDeathPenalty(configResponse.DeathInfo, currentTick);

        } catch (RpcException ex) {
            Logger.Warning(ex, "Failed to load cache player config");
        }

        Send(SurvivalPacket.UpdateStats(player.Account));

        Send(TimeSyncPacket.Reset(DateTimeOffset.UtcNow));
        Send(TimeSyncPacket.Set(DateTimeOffset.UtcNow));

        Stats.Refresh();

        Send(RequestPacket.TickSync(Environment.TickCount));

        try {
            ChannelsResponse response = World.Channels(new ChannelsRequest());
            Send(ChannelPacket.Load(response.Channels));
        } catch (RpcException ex) {
            Logger.Warning(ex, "Failed to populate channels");
        }
        Send(ServerEnterPacket.Request(Player));

        // Ugc
        // Cash
        // Gvg
        // Pvp
        Send(StateSyncPacket.SyncNumber(0));
        // SyncWorld
        Send(PrestigePacket.Load(player.Account));
        Send(PrestigePacket.LoadMissions(player.Account));
        Item.Inventory.Load();
        Item.Furnishing.Load();
        // Load Quests
        Quest.Load();
        // Send(QuestPacket.LoadSkyFortressMissions(Array.Empty<int>()));
        // Send(QuestPacket.LoadKritiasMissions(Array.Empty<int>()));
        // Send(QuestPacket.LoadQuests(Array.Empty<int>()));
        Achievement.Load();
        // MaidCraftItem
        // UserMaid
        // UserEnv
        Send(UserEnvPacket.LoadTitles(Player.Value.Unlock.Titles));
        Send(UserEnvPacket.InteractedObjects(Player.Value.Unlock.InteractedObjects));
        Send(UserEnvPacket.GatheringCounts(Config.GatheringCounts));
        Send(UserEnvPacket.LoadClaimedRewards(Player.Value.Unlock.MasteryRewardsClaimed));
        Send(FishingPacket.LoadAlbum(Player.Value.Unlock.FishAlbum.Values));
        Pet?.Load();
        Send(PetPacket.LoadCollection(Player.Value.Unlock.Pets));
        // LegionBattle
        // CharacterAbility
        Config.LoadKeyTable();
        Send(GuideRecordPacket.Load(Config.GuideRecords));
        // DailyWonder*
        GameEvent.Load();
        Send(GameEventPacket.Load(server.GetEvents().ToArray()));
        Send(BannerListPacket.Load(server.GetSystemBanners()));
        Dungeon.Load();
        Send(InGameRankPacket.Load());
        Send(FieldEnterPacket.Request(Player));
        Party = new PartyManager(World, this);
        Send(HomeCommandPacket.LoadHome(AccountId));
        // ResponseCube
        // Mentor
        Send(MentorPacket.MyList());
        Send(MentorPacket.Unknown12());
        Config.LoadChatStickers();
        // Mail
        Mail.Notify(true);
        // BypassKey
        // AH
        Config.LoadWardrobe();

        // Online Notifications

        return true;
    }

    private void LeaveField() {
        Array.Clear(ItemLockStaging);
        Array.Clear(DismantleStaging);
        DismantleOpened = false;
        Trade?.Dispose();
        Storage?.Dispose();
        Pet?.Dispose();
        Instrument = null;
        GuideObject = null;
        HeldCube = null;
        HeldLiftup = null;
        NpcScript = null;
        MiniGameRecord = null;

        Buffs?.LeaveField();

        if (Field != null) {
            Scheduler.Stop();
            Field.RemovePlayer(Player.ObjectId, out _);
        }
    }

    public bool PrepareField(int mapId, int portalId = -1, long ownerId = 0, int roomId = 0, in Vector3 position = default, in Vector3 rotation = default) {
        return PrepareFieldInternal(mapId, out _, portalId, ownerId, roomId, position, rotation);
    }

    public bool PrepareField(int mapId, [NotNullWhen(true)] out FieldManager? newField, int portalId = -1, long ownerId = 0, int roomId = 0, in Vector3 position = default, in Vector3 rotation = default) {
        return PrepareFieldInternal(mapId, out newField, portalId, ownerId, roomId, position, rotation);
    }

    private bool PrepareFieldInternal(int mapId, out FieldManager? newField, int portalId, long ownerId, int roomId, in Vector3 position, in Vector3 rotation) {
        // If entering home without instanceKey set, default to own home.
        if (mapId == Player.Value.Home.Indoor.MapId && ownerId == 0) {
            ownerId = AccountId;
        }

        if (Field is DungeonFieldManager dungeonField) {
            if (dungeonField.Lobby!.RoomFields.TryGetValue(mapId, out DungeonFieldManager? nextDungeonField)) {
                newField = nextDungeonField;
            } else if (mapId == dungeonField.Lobby.MapId) {
                newField = dungeonField.Lobby;
            } else {
                Migrate(mapId);
                newField = null;
                return false;
            }
        } else {
            newField = FieldFactory.Get(mapId, ownerId: ownerId, roomId: roomId);
            if (newField == null) {
                return false;
            }
        }

        State = SessionState.ChangeMap;
        LeaveField();

        Field = newField;
        Player.Dispose();
        Player = Field.SpawnPlayer(this, Player, portalId, position, rotation);
        Config.Skill.UpdatePassiveBuffs();
        Player.Buffs.LoadFieldBuffs();
        Player.CheckRegen();

        return true;
    }

    public bool EnterField() {
        if (Field == null) return false;

        if (!Player.Value.Unlock.Maps.Contains(Player.Value.Character.MapId)) {
            if (Field.Metadata.Property.ExploreType > 0) {
                ExpType expType = Field.Metadata.Property.ExploreType == 1 ? ExpType.mapCommon : ExpType.mapHidden;
                Exp.AddExp(expType);
            }

            ConditionUpdate(ConditionType.explore_continent, codeLong: (int) Field.Metadata.Property.Continent);
            ConditionUpdate(ConditionType.continent, codeLong: (int) Field.Metadata.Property.Continent);
            ConditionUpdate(ConditionType.explore, codeLong: Field.MapId);
        }

        Player.Value.Unlock.Maps.Add(Player.Value.Character.MapId);
        Config.LoadHotBars();
        Field.OnAddPlayer(Player);
        Scheduler.Start();
        State = SessionState.Connected;

        PlayerInfo.SendUpdate(new PlayerUpdateRequest {
            AccountId = AccountId,
            CharacterId = CharacterId,
            MapId = Field.MapId,
            Async = true,
        });

        Send(StatsPacket.Init(Player));
        Field.Broadcast(StatsPacket.Update(Player), Player.Session);

        //TODO: Save current hp/sp/ep in memory. This will help determine if player is dead upon login.
        var pWriter = Packet.Of(SendOp.UserState);
        pWriter.WriteInt(Player.ObjectId);
        pWriter.Write<ActorState>(ActorState.Fall);
        Send(pWriter);

        Send(EmotePacket.Load(Player.Value.Unlock.Emotes.Select(id => new Emote(id)).ToList()));
        Config.InitMacros();
        Config.LoadSkillCooldowns();
        Dungeon.LoadField();
        Marriage.Load();

        Send(CubePacket.DesignRankReward(Player.Value.Home));
        Send(CubePacket.UpdateProfile(Player, true));
        Send(CubePacket.ReturnMap(Player.Value.Character.ReturnMaps.Peek()));

        Config.LoadLapenshard();
        Config.LoadRevival();
        Config.LoadStatAttributes();
        Config.LoadSkillPoints();
        Player.Buffs.LoadFieldBuffs();

        TimeEventResponse globalEventResponse = World.TimeEvent(new TimeEventRequest {
            GetGlobalPortal = new TimeEventRequest.Types.GetGlobalPortal(),
        });

        if (globalEventResponse.Error != 0 && globalEventResponse.GlobalPortalInfo != null) {
            if (ServerTableMetadata.TimeEventTable.GlobalPortal.TryGetValue(globalEventResponse.GlobalPortalInfo.MetadataId, out GlobalPortalMetadata? portal)) {
                Send(GlobalPortalPacket.Announce(portal, globalEventResponse.GlobalPortalInfo.EventId));
            }
        }

        Send(PremiumCubPacket.Activate(Player.ObjectId, Player.Value.Account.PremiumTime));
        Send(PremiumCubPacket.LoadItems(Player.Value.Account.PremiumRewardsClaimed));
        ConditionUpdate(ConditionType.map, codeLong: Player.Value.Character.MapId);
        ConditionUpdate(ConditionType.job_change, codeLong: (int) Player.Value.Character.Job.Code());

        SessionSave();

        // Update the client with the latest channel list.
        ChannelsResponse response = World.Channels(new ChannelsRequest());
        Send(ChannelPacket.Load(response.Channels));
        Send(ServerListPacket.Load(Target.SERVER_NAME, [new IPEndPoint(Target.LoginIp, Target.LoginPort)], response.Channels));
        return true;
    }

    public void ReturnField() {
        Player player = Player.Value;
        if (player.Home.IsHomeSetup && !Player.Field.Plots.IsEmpty && (Player.Session.Housing.GetFieldPlot()?.IsPlanner ?? false)) {
            MigrateToPlanner(PlotMode.Normal);
            return;
        }

        Character character = player.Character;
        int mapId = character.ReturnMaps.Peek();
        Vector3 position = character.ReturnPosition;

        if (!MapMetadata.TryGet(mapId, out _)) {
            mapId = Constant.DefaultReturnMapId;
            position = default;
        }

        character.ReturnPosition = default;

        if (character.MapId is Constant.DefaultHomeMapId) {
            Migrate(mapId);
            return;
        }

        if (Guild.Guild is not null) {
            TableMetadata.GuildTable.Houses.TryGetValue(Guild.Guild.HouseRank, out IReadOnlyDictionary<int, GuildTable.House>? houseRank);

            if (houseRank is not null) {
                houseRank.TryGetValue(Guild.Guild.HouseTheme, out GuildTable.House? house);

                if (house?.MapId == character.MapId) {
                    Migrate(mapId);
                    return;
                }
            }
        }

        // If returning to a map, pass in the spawn point.
        Send(PrepareField(mapId, position: position)
            ? FieldEnterPacket.Request(Player)
            : FieldEnterPacket.Error(MigrationError.s_move_err_default));
    }

    /// <summary>
    /// Updates game condition values for achievement and quest.
    /// </summary>
    /// <param name="conditionType">Condition Type to update</param>
    /// <param name="counter">Condition value to progress by. Default is 1.</param>
    /// <param name="targetString">condition target parameter in string.</param>
    /// <param name="targetLong">condition target parameter in long.</param>
    /// <param name="codeString">condition code parameter in string.</param>
    /// <param name="codeLong">condition code parameter in long.</param>
    public void ConditionUpdate(ConditionType conditionType, long counter = 1, string targetString = "", long targetLong = 0, string codeString = "", long codeLong = 0) {
        Achievement.Update(conditionType, counter, targetString, targetLong, codeString, codeLong);
        Quest.Update(conditionType, counter, targetString, targetLong, codeString, codeLong);
    }

    public IList<GameEvent> FindEvent(GameEventType type) => server.FindEvent(type);

    public GameEvent? FindEvent(int id) => server.FindEvent(id);
    public IEnumerable<GameEvent> Events => server.GetEvents();

    public IEnumerable<PremiumMarketItem> GetPremiumMarketItems(params int[] tabIds) => server.GetPremiumMarketItems(tabIds);

    public PremiumMarketItem? GetPremiumMarketItem(int id, int subId = 0) => server.GetPremiumMarketItem(id, subId);

    public RewardRecord GetRewardContent(int id) {
        if (!TableMetadata.RewardContentTable.BaseEntries.TryGetValue(id, out RewardContentTable.Base? baseMetadata)) {
            return new RewardRecord();
        }

        long exp = 0;
        if (baseMetadata.ExpTableId > 0) {
            if (baseMetadata.ExpTableId > 100000 && TableMetadata.RewardContentTable.ExpStaticEntries.TryGetValue(baseMetadata.ExpTableId, out exp)) {
                // Static exp entry found
            } else if (TableMetadata.ExpTable.ExpBase.TryGetValue(baseMetadata.ExpTableId, out IReadOnlyDictionary<int, long>? expTable)) {
                exp = expTable[Player.Value.Character.Level];
            }
            if (baseMetadata.ExpFactor > 0f) {
                exp = (long) (exp * baseMetadata.ExpFactor);
            }
            Exp.AddExp(exp);
        }

        long meso = 0;
        if (baseMetadata.MesoTableId > 0) {
            if (baseMetadata.MesoTableId > 100000 && TableMetadata.RewardContentTable.MesoStaticEntries.TryGetValue(baseMetadata.MesoTableId, out meso)) {
                // Static meso entry found
            } else if (TableMetadata.RewardContentTable.MesoEntries.TryGetValue(baseMetadata.MesoTableId, out Dictionary<int, long>? mesoTable)) {
                if (!mesoTable.TryGetValue(Player.Value.Character.Level, out meso)) {
                    Logger.Error("Failed to find meso for level {Level} in table {TableId}", Player.Value.Character.Level, baseMetadata.MesoTableId);
                }
            }
            if (baseMetadata.MesoFactor > 0f) {
                meso = (long) (meso * baseMetadata.MesoFactor);
            }
            Currency.Meso += meso;
        }

        long prestigeExp = 0;
        if (baseMetadata.PrestigeExpTableId > 0 && ServerTableMetadata.PrestigeIdExpTable.Entries.TryGetValue(baseMetadata.PrestigeExpTableId, out PrestigeIdExpTable.Entry? prestigeExpTable)) {
            // TODO: Prestige exp given this way is fixed. Need to revise how exp is given.
        }

        List<Item> items = [];
        if (baseMetadata.ItemTableId > 0 && TableMetadata.RewardContentTable.ItemEntries.TryGetValue(baseMetadata.ItemTableId, out RewardContentTable.Item? itemMetadata)) {
            foreach (RewardContentTable.Item.Data data in itemMetadata.ItemData) {
                if ((data.MinLevel > 0 && Player.Value.Character.Level < data.MinLevel) ||
                    (data.MaxLevel > 0 && Player.Value.Character.Level > data.MaxLevel)) {
                    continue;
                }
                foreach (RewardItem rewardItem in data.RewardItems) {
                    Item? item = Field?.ItemDrop.CreateItem(rewardItem.ItemId, rewardItem.Rarity, rewardItem.Amount);
                    if (item != null) {
                        items.Add(item);
                    }
                }
            }
            foreach (Item item in items) {
                if (!Item.Inventory.Add(item, true)) {
                    Item.MailItem(item);
                }
            }
        }
        return new RewardRecord(items, exp, prestigeExp, meso);
    }

    public void ChannelBroadcast(ByteWriter packet) {
        server.Broadcast(packet);
    }

    public void DailyReset() {
        // Gathering counts reset
        Config.GatheringCounts.Clear();
        Send(UserEnvPacket.GatheringCounts(Config.GatheringCounts));
        // Death Counter
        Config.AddInstantReviveCount(-1);
        // Premium Rewards Claimed
        Player.Value.Account.PremiumRewardsClaimed.Clear();
        Send(PremiumCubPacket.LoadItems(Player.Value.Account.PremiumRewardsClaimed));
        // Prestige
        Player.Value.Account.PrestigeExp = Player.Value.Account.PrestigeCurrentExp;
        Player.Value.Account.PrestigeLevelsGained = 0;
        Send(PrestigePacket.Load(Player.Value.Account));
        // Home
        Player.Value.Home.DecorationRewardTimestamp = 0;
        Send(CubePacket.DesignRankReward(Player.Value.Home));
        // Meso Market
        Player.Value.Account.MesoMarketListed = 0;
        Send(MesoMarketPacket.Quota(Player.Value.Account.MesoMarketListed, Player.Value.Account.MesoMarketPurchased));
        // Shop restock
        Shop.DailyReset();
        // Dungeon daily clears
        Dungeon.ResetDailyClears();
    }

    public void WeeklyReset() {
        // Prestige Rewards
        Player.Value.Account.PrestigeRewardsClaimed.Clear();
        Send(PrestigePacket.Load(Player.Value.Account));
        // Dungeon enter limits
        Dungeon.UpdateDungeonEnterLimit();
        // Dungeon weekly clears
        Dungeon.ResetWeeklyClears();
        // Shop restock
        Shop.WeeklyReset();
    }

    public void MonthlyReset() {
        // Meso Market
        Player.Value.Account.MesoMarketPurchased = 0;
        Send(MesoMarketPacket.Quota(Player.Value.Account.MesoMarketListed, Player.Value.Account.MesoMarketPurchased));
    }

    public void MigrateToPlanner(PlotMode plotMode) {
        try {
            var request = new MigrateOutRequest {
                AccountId = AccountId,
                CharacterId = CharacterId,
                MachineId = MachineId.ToString(),
                Server = Server.World.Service.Server.Game,
                MapId = Constant.DefaultHomeMapId,
                OwnerId = AccountId,
                InstancedContent = true,
                Type = (World.Service.MigrationType) plotMode,
                RoomId = plotMode is not PlotMode.Normal ? -1 : 0,
            };

            MigrateOutResponse response = World.MigrateOut(request);
            var endpoint = new IPEndPoint(IPAddress.Parse(response.IpAddress), response.Port);
            Send(MigrationPacket.GameToGame(endpoint, response.Token, Constant.DefaultHomeMapId));
            State = SessionState.ChangeMap;
            MigrationSave();
        } catch (RpcException ex) {
            Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
            Send(NoticePacket.Disconnect(new InterfaceText(ex.Message)));
        } finally {
            Disconnect();
        }
    }

    public void Migrate(int mapId, long ownerId = 0) {
        bool isInstanced = ServerTableMetadata.InstanceFieldTable.Entries.ContainsKey(mapId);

        try {
            var request = new MigrateOutRequest {
                AccountId = AccountId,
                CharacterId = CharacterId,
                MachineId = MachineId.ToString(),
                Server = Server.World.Service.Server.Game,
                MapId = mapId,
                OwnerId = ownerId,
                InstancedContent = isInstanced,
            };

            MigrateOutResponse response = World.MigrateOut(request);
            var endpoint = new IPEndPoint(IPAddress.Parse(response.IpAddress), response.Port);
            Send(MigrationPacket.GameToGame(endpoint, response.Token, mapId));

            if (isInstanced) {
                Player.Value.Character.ReturnChannel = Player.Value.Character.Channel;
            } else {
                Player.Value.Character.MapId = mapId;
                Player.Value.Character.ReturnChannel = 0;
            }

            State = SessionState.ChangeMap;
            MigrationSave();
        } catch (RpcException ex) {
            Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
            Send(NoticePacket.Disconnect(new InterfaceText(ex.Message)));
        } finally {
            Disconnect();
        }
    }

    private void AcquireLock(long accountId, int maxRetries = 3) {
        int retryCount = 0;
        const int backoffMs = 500;

        while (retryCount < maxRetries) {
            LockResponse? response = World.AcquireLock(new LockRequest {
                AccountId = accountId,
            });

            if (string.IsNullOrEmpty(response.Error)) {
                return;
            }

            retryCount++;
            Thread.Sleep(backoffMs);
        }

        Logger.Error("Failed to acquire lock for account {AccountId} after {MaxRetries} retries", accountId, maxRetries);
    }

    private void ReleaseLock(long accountId) {
        try {
            LockResponse response = World.ReleaseLock(new LockRequest {
                AccountId = accountId,
            });
            if (!string.IsNullOrEmpty(response.Error)) {
                Logger.Warning("Failed to release lock for account {AccountId}: {ErrorMessage}", accountId, response.Error);
            }
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to release lock for account {AccountId}", accountId);
        }
    }

    #region Dispose
    ~GameSession() => Dispose(false);

    protected override void Dispose(bool disposing) {
        // Ensure dispose is only run once
        if (Interlocked.CompareExchange(ref gameDisposeState, 1, 0) != 0) return;

        // Snapshot values needed after teardown
        long fieldTickSnapshot = Field?.FieldTick ?? Environment.TickCount64;

        if (State == SessionState.Connected) {
            PlayerInfo.SendUpdate(new PlayerUpdateRequest {
                AccountId = AccountId,
                CharacterId = CharacterId,
                LastOnlineTime = DateTime.UtcNow.ToEpochSeconds(),
                MapId = 0,
                Channel = -1,
                Async = false,
            });

            Party.CheckDisband();
        }

        try {
            Scheduler.Stop();
            if (OnLoop != null) OnLoop -= Scheduler.InvokeAll;
            server.OnDisconnected(this);
            LeaveField();
            Player.Value.Character.Channel = -1;
            Player.Value.Account.Online = false;
            State = SessionState.Disconnected;

            // Early base dispose to stop further network sends
            base.Dispose(disposing);

            // Cache config & persistence
            SaveCacheConfig();
            MigrationSave();
        } catch (Exception ex) {
            Logger.Error(ex, "Error during session cleanup for {Player}", PlayerName);
        } finally {
            SafeDispose(Guild);
            SafeDispose(Buddy);
            SafeDispose(Party);
            foreach ((int groupChatId, GroupChatManager groupChat) in GroupChats) {
                try { groupChat.CheckDisband(); } catch (Exception ex) { Logger.Error(ex, "Error disbanding group chat {Id} for {Player}", groupChatId, PlayerName); }
            }
            foreach ((long clubId, ClubManager club) in Clubs) {
                SafeDispose(club);
            }
            try { Player.Dispose(); } catch (Exception ex) { Logger.Error(ex, "Error disposing player for {Player}", PlayerName); }
            Interlocked.Exchange(ref gameDisposeState, 2);
        }
        return;

        void SafeDispose(IDisposable? disp) {
            if (disp == null) return;
            try { disp.Dispose(); } catch (Exception ex) { Logger.Error(ex, "Error disposing component for {Player}", PlayerName); }
        }

        void SaveCacheConfig() {
            List<Buff> buffs = Buffs.GetSaveCacheBuffs();
            IList<SkillCooldown> skillCooldowns = Config.GetCurrentSkillCooldowns();

            long stopTime = DateTime.Now.ToEpochSeconds();
            long fieldTick = fieldTickSnapshot;

            try {
                PlayerConfigResponse _ = World.PlayerConfig(new PlayerConfigRequest {
                    Save = new PlayerConfigRequest.Types.Save {
                        Buffs = {
                            buffs
                                .Where(buff => buff.EndTick - fieldTick > 0)
                                .Select(buff => new BuffInfo {
                                    Id = buff.Id,
                                    Level = buff.Level,
                                    MsRemaining = (buff.EndTick - fieldTick).Truncate32(),
                                    Stacks = buff.Stacks,
                                    Enabled = buff.Enabled,
                                    StopTime = stopTime,
                                }),
                        },
                        SkillCooldowns = {
                            skillCooldowns
                                .Where(cooldown => cooldown.EndTick - fieldTick > 0)
                                .Select(cooldown => new SkillCooldownInfo {
                                    SkillId = cooldown.SkillId,
                                    SkillLevel = cooldown.Level,
                                    GroupId = cooldown.GroupId,
                                    MsRemaining = (cooldown.EndTick - fieldTick).Truncate32(),
                                    StopTime = stopTime,
                                    Charges = cooldown.Charges,
                                }),
                        },
                        DeathInfo = new DeathInfo {
                            Count = Config.DeathCount,
                            MsRemaining = (int) (Config.DeathPenaltyEndTick - fieldTick),
                            StopTime = stopTime,
                        },
                    },
                    RequesterId = CharacterId,
                });
            } catch (Exception ex) { Logger.Error(ex, "Error saving buffs for {Player}", PlayerName); }
        }
    }
    #endregion

    public override string ToString() {
        return $"GameSession: {Player?.Value?.Character?.Name} ({CharacterId}) [{State}] // Account: {AccountId}, Machine: {MachineId}, Channel: {Player?.Value?.Character?.Channel}, Map: {Player?.Value?.Character?.MapId}]";
    }

    public void MigrationSave() {
        if (preMigrationSaved) return;

        try {
            AcquireLock(AccountId, 5);
            Save();
            preMigrationSaved = true;
        } catch (Exception ex) {
            Logger.Error(ex, "MigrationSave failed AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
        } finally {
            ReleaseLock(AccountId);
        }
    }

    public void SessionSave() {
        try {
            AcquireLock(AccountId, 5);
            Save();
        } catch (Exception ex) {
            Logger.Error(ex, "SessionSave failed AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
        } finally {
            ReleaseLock(AccountId);
        }
    }

    // Don't call this directly, since it does not acquire the account lock.
    private void Save() {
        using GameStorage.Request db = GameStorage.Context();
        db.BeginTransaction();
        db.SavePlayer(Player);
        TrySaveComponent(UgcMarket.Save);
        TrySaveComponent(Config.Save);
        TrySaveComponent(Shop.Save);
        TrySaveComponent(Item.Save);
        TrySaveComponent(Survival.Save);
        TrySaveComponent(Housing.Save);
        TrySaveComponent(GameEvent.Save);
        TrySaveComponent(Achievement.Save);
        TrySaveComponent(Quest.Save);
        TrySaveComponent(Dungeon.Save);
        db.Commit();
        db.SaveChanges();
        return;

        void TrySaveComponent(Action<GameStorage.Request> action) {
            try { action(db); } catch (Exception ex) { Logger.Error(ex, "Error saving component for {Player}", PlayerName); }
        }
    }
}
