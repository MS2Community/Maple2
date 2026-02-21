using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Field;
using Maple2.Model.Game.Ugc;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.DebugGraphics;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Field;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools.DotRecast;
using Maple2.Tools.Extensions;
using Maple2.Tools.Scheduler;
using Serilog;

namespace Maple2.Server.Game.Manager.Field;

// FieldManager is instantiated by Autofac
// ReSharper disable once ClassNeverInstantiated.Global
public partial class FieldManager : IField {
    private static int _globalIdCounter = 10000000;
    private int localIdCounter = 50000000;
    private DateTime? fieldEmptySince;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public GameStorage GameStorage { get; init; } = null!;
    public ItemMetadataStorage ItemMetadata { get; init; } = null!;
    public MapMetadataStorage MapMetadata { get; init; } = null!;
    public MapDataStorage MapData { get; init; } = null!;
    public NpcMetadataStorage NpcMetadata { get; init; } = null!;
    public AiMetadataStorage AiMetadata { get; init; } = null!;
    public SkillMetadataStorage SkillMetadata { get; init; } = null!;
    public TableMetadataStorage TableMetadata { get; init; } = null!;
    public FunctionCubeMetadataStorage FunctionCubeMetadata { get; init; } = null!;
    public ServerTableMetadataStorage ServerTableMetadata { get; init; } = null!;
    public RideMetadataStorage RideMetadata { get; init; } = null!;
    public ItemStatsCalculator ItemStatsCalc { get; init; } = null!;
    public TriggerCache TriggerCache { get; init; } = null!;
    public Factory FieldFactory { get; init; } = null!;
    public IGraphicsContext DebugGraphicsContext { get; init; } = null!;
    // ReSharper restore All
    #endregion

    public readonly MapMetadata Metadata;
    public MapEntityMetadata Entities { get; init; }
    public Navigation Navigation { get; init; }
    public readonly PerformanceStageManager? PerformanceStage;
    public FieldAccelerationStructure? AccelerationStructure { get; private set; }
    private readonly UgcMapMetadata ugcMetadata;

    internal readonly EventQueue Scheduler;
    internal readonly FieldActor FieldActor;
    private readonly CancellationTokenSource cancel;
    private readonly Thread thread;
    private readonly List<(FieldPacketHandler handler, GameSession session, ByteReader reader)> queuedPackets;
    private bool initialized;
    public bool Disposed { get; private set; }

    private readonly ILogger logger = Log.Logger.ForContext<FieldManager>();

    public ItemDropManager ItemDrop { get; }

    public int MapId { get; init; }
    public int RoomId { get; init; }
    public int DungeonId { get; init; }
    public int Size { get; init; }
    public FieldType FieldType { get; init; }
    public InstanceFieldMetadata FieldInstance { get; private set; }
    public readonly AiManager Ai;
    public IFieldRenderer? DebugRenderer { get; private set; }

    public FieldManager(MapMetadata metadata, UgcMapMetadata ugcMetadata, MapEntityMetadata entities, NpcMetadataStorage npcMetadata, long ownerId = 0) {
        Metadata = metadata;
        MapId = metadata.Id;
        this.ugcMetadata = ugcMetadata;
        Entities = entities;
        TriggerObjects = new TriggerCollection(entities);

        Scheduler = new EventQueue(logger);
        FieldActor = new FieldActor(this, NextLocalId(), metadata, npcMetadata); // pulls from argument because member NpcMetadata is null here
        cancel = new CancellationTokenSource();
        thread = new Thread(UpdateLoop);
        queuedPackets = new();
        Ai = new AiManager(this);
        RoomId = NextGlobalId();

        FieldInstance = new InstanceFieldMetadata(0, InstanceType.none, 0, false, 0, true, 0, 0, 0, 0);

        ItemDrop = new ItemDropManager(this);

        Navigation = new Navigation(metadata.XBlock);

        if (MapId is Constant.PerformanceMapId) {
            PerformanceStage = new PerformanceStageManager(this);
        }

        FieldType = FieldType.Default;
    }

    // Init is separate from constructor to allow properties to be injected first.
    public virtual void Init() {
        if (initialized) {
            return;
        }

        initialized = true;
        FieldTick = Environment.TickCount64;

        if (MapData.TryGet(Metadata.XBlock, out FieldAccelerationStructure? accelerationStructure)) {
            AccelerationStructure = accelerationStructure;
        } else {
            logger.Error("Failed to load acceleration structure for map {MapId}", MapId);
        }

        if (ServerTableMetadata.InstanceFieldTable.Entries.TryGetValue(Metadata.Id, out InstanceFieldMetadata? instanceField)) {
            FieldInstance = instanceField;
        }

        if (ugcMetadata.Plots.Count > 0) {
            using GameStorage.Request db = GameStorage.Context();
            foreach (Plot plot in db.LoadPlotsForMap(MapId)) {
                Plots[plot.Number] = plot;

                plot.Cubes.Values
                    .Where(plotCube => plotCube.Interact != null)
                    .ToList()
                    .ForEach(plotCube => AddFieldFunctionInteract(plotCube));

                plot.Cubes.Values
                    .Where(plotCube => plotCube.Metadata.Install is { IndoorPortal: true })
                    .ToList()
                    .ForEach(plotCube => SpawnFieldToHomePortal(plotCube, plot.OwnerId));
            }
        }

        // Create default to place liftable cubes
        if (MapId is not Constant.DefaultHomeMapId) {
            Plots[0] = new Plot(new UgcMapGroup(0,
                0,
                0,
                new UgcMapGroup.Cost(0, 0, 0),
                new UgcMapGroup.Cost(0, 0, 0),
                new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0)));
        }

        foreach ((Guid guid, BreakableActor breakable) in Entities.BreakableActors) {
            AddBreakable(guid.ToString("N"), breakable);
        }
        foreach ((Guid guid, Liftable liftable) in Entities.Liftables) {
            AddLiftable(guid.ToString("N"), liftable);
        }
        foreach ((Guid guid, InteractObject interact) in Entities.Interacts) {
            AddInteract(guid.ToString("N"), interact);
        }

        foreach (SpawnPointNPC spawnPointNpc in Entities.NpcSpawns) {
            AddSpawnPointNpc(spawnPointNpc);
        }

        foreach (SpawnPointPC spawnPoint in Entities.PlayerSpawns) {
            int fieldId = spawnPoint.SpawnPointId;
            if (fieldPlayerSpawnPoints.ContainsKey(fieldId)) {
                // Duplicate spawn point id (ex. unassigned ID), use next local id
                fieldId = NextLocalId();
            }
            fieldPlayerSpawnPoints[fieldId] = new FieldPlayerSpawnPoint(this, NextLocalId(), spawnPoint, fieldId);
        }

        foreach (TriggerModel trigger in Entities.TriggerModels.Values) {
            AddTrigger(trigger);
        }

        foreach (Portal portal in Entities.Portals.Values) {
            SpawnPortal(portal);
        }

        foreach (FieldTrigger trigger in fieldTriggers.Values) {
            trigger.Update(FieldTick);
        }

        IList<MapMetadata> bonusMaps = MapMetadata.GetMapsByType(Metadata.Property.Continent, MapType.PocketRealm);
        foreach (MapMetadataSpawn spawn in Metadata.Spawns) {
            if (!Entities.RegionSpawns.TryGetValue(spawn.Id, out Ms2RegionSpawn? regionSpawn)) {
                continue;
            }

            var npcIds = new HashSet<int>();
            foreach (string tag in spawn.Tags) {
                if (NpcMetadata.TryLookupTag(tag, out IReadOnlyCollection<int>? tagNpcIds)) {
                    foreach (int tagNpcId in tagNpcIds) {
                        npcIds.Add(tagNpcId);
                    }
                }
            }

            if (npcIds.Count > 0 && spawn.Population > 0) {
                AddMobSpawn(spawn, regionSpawn, npcIds);
                continue;
            }

            if (spawn.Tags.Contains("보너스맵")) { // Bonus Map
                // Spawn a hat within a random range of 5 min to 8 hours
                TimeSpan delay = Random.Shared.Next(1, 97) * TimeSpan.FromMinutes(5);
                Scheduler.Schedule(() => SetBonusMapPortal(bonusMaps, regionSpawn), delay);
            }
        }

        foreach (Ms2RegionSkill regionSkill in Entities.RegionSkills) {
            if (!SkillMetadata.TryGet(regionSkill.SkillId, regionSkill.Level, out SkillMetadata? skill)) {
                continue;
            }

            AddSkill(skill, regionSkill.Interval, regionSkill.Position, regionSkill.Rotation);
        }

        foreach (Ms2CubeSkill cubeSkill in Entities.CubeSkills) {
            if (!SkillMetadata.TryGet(cubeSkill.SkillId, cubeSkill.Level, out SkillMetadata? skill)) {
                continue;
            }

            AddCubeSkill(skill, cubeSkill.Position, cubeSkill.Rotation);
        }

        foreach (BannerTable.Entry entry in TableMetadata.BannerTable.Entries) {
            if (entry.MapId != MapId) {
                continue;
            }

            using GameStorage.Request db = GameStorage.Context();

            FieldUgcBanner ugcBanner = new(this, entry.Id, MapId, db.FindBannerSlotsByBannerId(entry.Id));
            Banners[entry.Id] = ugcBanner;

            DateTimeOffset dateTimeOffset = DateTimeOffset.UtcNow;

            BannerSlot? slot = ugcBanner.Slots.FirstOrDefault(x => x.ActivateTime.Day == dateTimeOffset.Day && x.ActivateTime.Hour == dateTimeOffset.Hour);

            if (slot is null || slot.Expired || slot.Active) {
                continue;
            }

            slot.Active = true;
        }

        Scheduler.Start();
        thread.Start();
        DebugRenderer = DebugGraphicsContext.FieldAdded(this);
    }

    /// <summary>
    /// Generates an ObjectId unique across all map instances.
    /// </summary>
    /// <returns>Returns a globally unique ObjectId</returns>
    public static int NextGlobalId() => Interlocked.Increment(ref _globalIdCounter);

    /// <summary>
    /// Generates an ObjectId unique to this specific map instance.
    /// </summary>
    /// <returns>Returns a local ObjectId</returns>
    private int NextLocalId() => Interlocked.Increment(ref localIdCounter);

    // Use this to keep systems in sync. Do not use Environment.TickCount directly
    public long FieldTick { get; private set; }

    /// <summary>
    /// FieldTick truncated to 32-bit int. Use this when sending tick values to the client or when
    /// int tick values are required (e.g., TimeSync packets, performance stage).
    /// This properly handles overflow to match Environment.TickCount behavior.
    /// </summary>
    public int FieldTickInt => FieldTick.Truncate32();

    public void QueuePacket(FieldPacketHandler handler, GameSession session, ByteReader reader) {
        lock (queuedPackets) {
            queuedPackets.Add((handler, session, reader));
        }
    }

    private void ProcessPackets() {
        lock (queuedPackets) {
            foreach ((FieldPacketHandler handler, GameSession session, ByteReader reader) packet in queuedPackets) {
                try {
                    if (packet.session.State is SessionState.Disconnected) {
                        continue;
                    }
                    packet.handler.Handle(packet.session, packet.reader);
                } catch (Exception ex) {
                    if (ex is IndexOutOfRangeException) {
                        logger.Error("Error processing packet {OpCode} for account ID {AccountId}. Packet: {Packet}", packet.handler.OpCode, packet.session.AccountId, packet.ToString());
                    } else {
                        logger.Error(ex, "Error processing handler {Handler} for account ID {AccountId}. Packet: {Packet}", packet.handler.OpCode, packet.session.AccountId, packet.ToString());
                    }
                } finally {
                    ArrayPool<byte>.Shared.Return(packet.reader.Buffer);
                }
            }

            queuedPackets.Clear();
        }
    }

    private void UpdateLoop() {
        while (!cancel.IsCancellationRequested) {
            if (!(DebugRenderer?.IsActive ?? false)) {
                Update();
            }

            // Environment.TickCount has ~16ms precision so sleep until next update
            Thread.Sleep(15);
        }
    }

    public void Update() {
        ProcessPackets();

        if (Players.IsEmpty) {
            return;
        }

        Scheduler.InvokeAll();

        FieldTick = Environment.TickCount64;
        foreach (FieldTrigger trigger in fieldTriggers.Values) trigger.Update(FieldTick);

        foreach (FieldPlayer player in Players.Values) player.Update(FieldTick);
        foreach (FieldNpc npc in Npcs.Values) npc.Update(FieldTick);
        foreach (FieldNpc mob in Mobs.Values) mob.Update(FieldTick);
        foreach (FieldPet pet in Pets.Values) pet.Update(FieldTick);
        foreach (FieldBreakable breakable in fieldBreakables.Values) breakable.Update(FieldTick);
        foreach (FieldLiftable liftable in fieldLiftables.Values) liftable.Update(FieldTick);
        foreach (FieldInteract interact in fieldInteracts.Values) interact.Update(FieldTick);
        foreach (FieldFunctionInteract interact in fieldFunctionInteracts.Values) interact.Update(FieldTick);
        foreach (FieldInteract interact in fieldAdBalloons.Values) interact.Update(FieldTick);
        foreach (FieldItem item in fieldItems.Values) item.Update(FieldTick);
        foreach (FieldMobSpawn mobSpawn in fieldMobSpawns.Values) mobSpawn.Update(FieldTick);
        foreach (FieldSpawnPointNpc spawnPointNpc in fieldSpawnPointNpcs.Values) spawnPointNpc.Update(FieldTick);
        foreach (FieldSkill skill in fieldSkills.Values) skill.Update(FieldTick);
        foreach (FieldSkill skill in cubeSkills.Values) skill.Update(FieldTick);
        foreach (FieldPortal portal in fieldPortals.Values) portal.Update(FieldTick);
        UpdateBanners();

        RoomTimer?.Update(FieldTick);
        PerformanceStage?.Update();
    }

    public void EnsurePlayerPosition(FieldPlayer player) {
        if (Entities.BoundingBox.Contains(player.Position, 0.001f)) {
            return;
        }

        player.FallDamage(Constant.FallBoundingAddedDistance);
        player.MoveToPosition(player.LastGroundPosition.Align() + new Vector3(0, 0, 150f), default);
    }

    public bool ValidPosition(Vector3 position) {
        return FindNearestPoly(position, out long nearestRef, out _) && nearestRef != 0;
    }

    public bool FindNearestPoly(Vector3 point, out long nearestRef, out RcVec3f position) {
        if (point.X is float.NaN or float.PositiveInfinity or float.NegativeInfinity || point.Y is float.NaN or float.PositiveInfinity or float.NegativeInfinity || point.Z is float.NaN or float.PositiveInfinity or float.NegativeInfinity) {
            nearestRef = 0;
            position = default;
            logger.Error("Invalid point {Point} in field {MapId}", point, MapId);
            return false;
        }
        RcVec3f pointToNavMesh = DotRecastHelper.ToNavMeshSpace(point);
        return FindNearestPoly(pointToNavMesh, out nearestRef, out position);
    }

    public bool FindNearestPoly(RcVec3f point, out long nearestRef, out RcVec3f position, [CallerMemberName] string caller = "") {
        if (point.X is float.NaN or float.PositiveInfinity or float.NegativeInfinity || point.Y is float.NaN or float.PositiveInfinity or float.NegativeInfinity || point.Z is float.NaN or float.PositiveInfinity or float.NegativeInfinity) {
            nearestRef = 0;
            position = default;
            logger.Error("Invalid point {Point} in field {MapId} called by {Caller}", point, MapId, caller);
            return false;
        }

        DtStatus status = Navigation.Crowd.GetNavMeshQuery().FindNearestPoly(point, new RcVec3f(2, 4, 2), Navigation.Crowd.GetFilter(0), out nearestRef, out position, out _);
        if (status.Failed()) {
            logger.Warning("Failed to find nearest poly from position {Source} in field {MapId}", point, MapId);
            return false;
        }

        return true;
    }

    public bool TryGetPlayerById(long characterId, [NotNullWhen(true)] out FieldPlayer? player) {
        foreach (FieldPlayer fieldPlayer in Players.Values) {
            if (fieldPlayer.Value.Character.Id == characterId) {
                player = fieldPlayer;
                return true;
            }
        }

        player = null;
        return false;
    }

    public bool TryGetActor(int objectId, [NotNullWhen(true)] out IActor? actor) {
        if (Players.TryGetValue(objectId, out FieldPlayer? player)) {
            actor = player;
            return true;
        }

        if (Npcs.TryGetValue(objectId, out FieldNpc? npc)) {
            actor = npc;
            return true;
        }

        if (Mobs.TryGetValue(objectId, out FieldNpc? mob)) {
            actor = mob;
            return true;
        }

        if (Pets.TryGetValue(objectId, out FieldPet? pet)) {
            actor = pet;
            return true;
        }

        actor = null;

        return false;
    }

    public IEnumerable<IActor> GetActorsBySpawnId(int spawnId) {
        foreach (FieldNpc npc in Npcs.Values) {
            if (npc.SpawnPointId == spawnId) {
                yield return npc;
            }
        }

        foreach (FieldNpc mob in Mobs.Values) {
            if (mob.SpawnPointId == spawnId) {
                yield return mob;
            }
        }

        foreach (FieldPet pet in Pets.Values) {
            if (pet.SpawnPointId == spawnId) {
                yield return pet;
            }
        }
    }

    public bool TryGetPlayer(int objectId, [NotNullWhen(true)] out FieldPlayer? player) {
        return Players.TryGetValue(objectId, out player);
    }

    public bool TryGetPlayer(string name, [NotNullWhen(true)] out FieldPlayer? player) {
        player = Players.Values.FirstOrDefault(p => p.Value.Character.Name == name);
        return player != null;
    }

    public bool TryGetPortal(int portalId, [NotNullWhen(true)] out FieldPortal? portal) {
        portal = fieldPortals.Values.FirstOrDefault(p => p.Value.Id == portalId);
        return portal != null;
    }

    public bool TryGetPlayerSpawn(int id, [NotNullWhen(true)] out FieldPlayerSpawnPoint? playerSpawnPoint) {
        // Get random spawn point if id is -1
        if (id < 0) {
            List<FieldPlayerSpawnPoint> enabledSpawns = fieldPlayerSpawnPoints.Values.Where(spawn => spawn.Enable).ToList();
            playerSpawnPoint = enabledSpawns.Count > 0 ? enabledSpawns[Random.Shared.Next(enabledSpawns.Count)] : null;
            return playerSpawnPoint != null;
        }
        return fieldPlayerSpawnPoints.TryGetValue(id, out playerSpawnPoint);
    }

    public List<FieldPlayerSpawnPoint> GetPlayerSpawns() {
        return fieldPlayerSpawnPoints.Values.ToList();
    }

    public bool TryGetItem(int objectId, [NotNullWhen(true)] out FieldItem? fieldItem) {
        return fieldItems.TryGetValue(objectId, out fieldItem);
    }

    public bool TryGetBreakable(string entityId, [NotNullWhen(true)] out FieldBreakable? fieldBreakable) {
        return fieldBreakables.TryGetValue(entityId, out fieldBreakable);
    }

    public bool TryGetBreakable(int triggerId, [NotNullWhen(true)] out FieldBreakable? fieldBreakable) {
        return triggerBreakable.TryGetValue(triggerId, out fieldBreakable);
    }

    public bool TryGetLiftable(string entityId, [NotNullWhen(true)] out FieldLiftable? fieldLiftable) {
        return fieldLiftables.TryGetValue(entityId, out fieldLiftable);
    }

    public ICollection<FieldInteract> EnumerateInteract() => fieldInteracts.Values;
    public ICollection<FieldLiftable> EnumerateLiftables() => fieldLiftables.Values;
    public bool TryGetInteract(string entityId, [NotNullWhen(true)] out FieldInteract? fieldInteract) {
        return fieldInteracts.TryGetValue(entityId, out fieldInteract) || fieldAdBalloons.TryGetValue(entityId, out fieldInteract) || fieldChests.TryGetValue(entityId, out fieldInteract);
    }

    public IEnumerable<FieldInteract> GetInteractObjectsBySpawnId(int spawnId) {
        return fieldInteracts.Values.Where(interact => interact.SpawnId == spawnId)
            .Concat(fieldAdBalloons.Values.Where(interact => interact.SpawnId == spawnId))
            .Concat(fieldChests.Values.Where(interact => interact.SpawnId == spawnId));
    }

    public bool UsePortal(GameSession session, int portalId, string password) {
        if (!TryGetPortal(portalId, out FieldPortal? fieldPortal)) {
            return false;
        }

        if (!fieldPortal.Enabled) {
            session.Send(NoticePacket.MessageBox(new InterfaceText($"Cannot use disabled portal: {portalId}")));
            return false;
        }

        if (!string.IsNullOrEmpty(fieldPortal.Password) && password != fieldPortal.Password) {
            session.Send(NoticePacket.Message(StringCode.s_home_password_mismatch, NoticePacket.Flags.Alert));
            return false;
        }

        /* TODO: Remove portal once capacity is reached for Event portals
        if (fieldPortal.Value.Type == PortalType.Event) {
            RemovePortal(fieldPortal.ObjectId);
        }
        */

        // MoveByPortal (same map)
        Portal srcPortal = fieldPortal;
        switch (srcPortal.Type) {
            case PortalType.InHome:
                Plot? fieldPlot = session.Housing.GetFieldPlot();
                PlotCube? cubePortal = fieldPlot?.Cubes.Values.FirstOrDefault(x => x.Interact?.PortalSettings is not null && x.Interact.PortalSettings.PortalObjectId == fieldPortal.ObjectId);
                if (cubePortal is null) {
                    return false;
                }

                switch (cubePortal.Interact!.PortalSettings!.Destination) {
                    case CubePortalDestination.PortalInHome:
                        PlotCube? destinationCube = fieldPlot?.Cubes.Values.FirstOrDefault(x => x.Interact?.PortalSettings is not null && x.Interact.PortalSettings.PortalName == cubePortal.Interact.PortalSettings.DestinationTarget);
                        if (destinationCube is null) {
                            return false;
                        }

                        session.Send(PortalPacket.MoveByPortal(session.Player, destinationCube.Position, default));
                        return true;
                    case CubePortalDestination.SelectedMap:
                        session.Migrate(srcPortal.TargetMapId);
                        return true;
                    case CubePortalDestination.FriendHome:
                        Home? home = GetHome(session, fieldPortal);
                        if (home is null) {
                            session.Send(FieldEnterPacket.Error(MigrationError.s_move_err_no_server));
                            return false;
                        }

                        session.Migrate(home.Indoor.MapId, home.Indoor.OwnerId);
                        return true;
                }
                return false;
            case PortalType.LeaveDungeon:
                //TODO: Migrate back to original channel
                session.Send(session.PrepareField(session.Player.Value.Character.ReturnMaps.Peek())
                    ? FieldEnterPacket.Request(session.Player)
                    : FieldEnterPacket.Error(MigrationError.s_move_err_default));
                return true;
            case PortalType.DungeonReturnToLobby:
                if (this is not DungeonFieldManager dungeonField || dungeonField.Lobby is null) {
                    logger.Warning("DungeonReturnToLobby portal used in non-dungeon map {MapId}", MapId);
                    return false;
                }
                session.Send(session.PrepareField(dungeonField.Lobby.MapId, portalId: 2, roomId: dungeonField.Lobby.RoomId)
                    ? FieldEnterPacket.Request(session.Player)
                    : FieldEnterPacket.Error(MigrationError.s_move_err_default));
                return true;
            case PortalType.Quest:
                session.Field?.RemovePortal(fieldPortal.ObjectId);
                break;
            case PortalType.FieldToHome:
                session.Migrate(Constant.DefaultHomeMapId, fieldPortal.HomeId);
                break;
        }

        if (srcPortal.TargetMapId == MapId) {
            if (TryGetPortal(srcPortal.TargetPortalId, out FieldPortal? dstPortal)) {
                Vector3 position = dstPortal.Position;
                Vector3 rotation = dstPortal.Rotation;

                if (dstPortal.Value.ActionType is PortalActionType.Touch) {
                    // add one blocks or use offset in the direction of front axis
                    float offset = Constant.BlockSize;
                    if (dstPortal.Value.FrontOffset != 0) {
                        offset = dstPortal.Value.FrontOffset;
                    }

                    Vector3 forward = dstPortal.Transform.FrontAxis;

                    position += forward * offset;
                }
                session.Send(PortalPacket.MoveByPortal(session.Player, position, rotation));
            }

            return true;
        }

        if (srcPortal.TargetMapId == 0) {
            session.ReturnField();
            return true;
        }

        session.ConditionUpdate(ConditionType.map, codeLong: srcPortal.TargetMapId);

        session.Send(session.PrepareField(srcPortal.TargetMapId, portalId: srcPortal.TargetPortalId)
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(MigrationError.s_move_err_default));
        return true;
    }

    public bool LiftupCube(in Vector3B coordinates, [NotNullWhen(true)] out LiftupWeapon? liftupWeapon) {
        if (!Entities.ObjectWeapons.TryGetValue(coordinates, out ObjectWeapon? objectWeapon)) {
            liftupWeapon = null;
            return false;
        }

        int itemId = objectWeapon.ItemIds[Environment.TickCount % objectWeapon.ItemIds.Length];
        if (!ItemMetadata.TryGet(itemId, out ItemMetadata? item) || !(item.Skill?.WeaponId > 0)) {
            liftupWeapon = null;
            return false;
        }

        liftupWeapon = new LiftupWeapon(objectWeapon, itemId, item.Skill.WeaponId, item.Skill.WeaponLevel);

        if (objectWeapon.SpawnNpcId != 0 && Random.Shared.NextSingle() < objectWeapon.SpawnNpcRate) {
            if (NpcMetadata.TryGet(objectWeapon.SpawnNpcId, out NpcMetadata? metadata)) {
                FieldNpc? fieldNpc = SpawnNpc(metadata, objectWeapon.Position, objectWeapon.Rotation);
                if (fieldNpc != null) {
                    Broadcast(FieldPacket.AddNpc(fieldNpc));
                    Broadcast(ProxyObjectPacket.AddNpc(fieldNpc));
                }
            }
        }

        return true;
    }

    public void MovePlayerAlongPath(string pathName) {
        MS2PatrolData? patrolData = Entities.Patrols.FirstOrDefault(p => p.Name == pathName);
        if (patrolData is null) {
            return;
        }

        foreach (FieldPlayer player in Players.Values) {
            if (!NpcMetadata.TryGet(Constant.DummyNpc(player.Value.Character.Gender), out NpcMetadata? npcMetadata)) {
                continue;
            }

            FieldNpc? dummyNpc = SpawnNpc(npcMetadata, player.Position, player.Rotation);
            if (dummyNpc is null) {
                continue;
            }
            Broadcast(FieldPacket.AddNpc(dummyNpc));
            Broadcast(ProxyObjectPacket.AddNpc(dummyNpc));

            dummyNpc.SetPatrolData(patrolData);
            dummyNpc.MovementState.CleanupPatrolData(player);
            player.Session.Send(FollowNpcPacket.FollowNpc(dummyNpc.ObjectId));
        }
    }

    public void VibrateObjects(DamageRecord record, Vector3 position) {
        if (record.AttackMetadata.BrokenOffence == 0) {
            // No items are going to vibrate/break.
            return;
        }

        float rangeDistance = record.AttackMetadata.Range.Distance;
        if (AccelerationStructure is null) {
            return;
        }

        List<FieldVibrateEntity> vibrateObjects = AccelerationStructure.QueryVibrateObjectsCenterList(position, 2 * new Vector3(rangeDistance, rangeDistance, rangeDistance));
        foreach (FieldVibrateEntity vibrate in vibrateObjects) {
            if (vibrate.BreakDefense < record.AttackMetadata.BrokenOffence) {
                // TODO Keep a record of when the vibrate object was broken. Don't send if it's currently broken and respawning.
            }
            Broadcast(VibratePacket.Attack(vibrate.Id.Id, record));
        }
    }

    private static Home? GetHome(GameSession session, FieldPortal fieldPortal) {
        using GameStorage.Request db = session.GameStorage.Context();
        Home? home = db.GetHome(fieldPortal.HomeId);
        return home;
    }

    #region DebugUtils
    public void BroadcastAiMessage(ByteWriter packet) {
        foreach ((int objectId, FieldPlayer player) in Players) {
            if (player.DebugAi) {
                player.Session.Send(packet);
            }
        }
    }

    public void BroadcastAiType(GameSession requester) {
        foreach ((int objectId, FieldNpc npc) in Npcs) {
            npc.SendDebugAiInfo(requester);
        }

        foreach ((int objectId, FieldPet npc) in Pets) {
            npc.SendDebugAiInfo(requester);
        }
    }
    #endregion

    public void Broadcast(ByteWriter packet, GameSession? sender = null) {
        if (!initialized) {
            return;
        }

        foreach (FieldPlayer fieldPlayer in Players.Values) {
            if (fieldPlayer.Session == sender) continue;
            fieldPlayer.Session.Send(packet);
        }
    }

    public void BroadcastNpcControl(FieldNpc npc) {
        if (!initialized) {
            return;
        }

        foreach (FieldPlayer fieldPlayer in Players.Values) {
            NpcScriptManager? npcScript = fieldPlayer.Session.NpcScript;
            if (npcScript is not null && npcScript.Npc is not null && npcScript.Npc.Value.Id == npc.Value.Id && npc.Value.Metadata.LookAtTarget.UseTalkMotion) {
                fieldPlayer.Session.Send(NpcControlPacket.Talk(npc));
                continue;
            }

            fieldPlayer.Session.Send(NpcControlPacket.Control(npc));
        }
    }

    public void Dispose() {
        if (Disposed) {
            return;
        }

        logger.Debug("Disposing FieldManager {MapId}", MapId);

        DebugGraphicsContext.FieldRemoved(this);
        try {
            cancel.Cancel();
        } catch (Exception e) {
            logger.Error(e, "Failed to cancel FieldManager thread, cancel.Cancel() threw an exception. Disposed: {Disposed}", Disposed);
        }
        cancel.Dispose();
        thread.Join();
        Navigation.Dispose();

        Disposed = true;
        logger.Debug("Disposed FieldManager {MapId}", MapId);
    }
}
