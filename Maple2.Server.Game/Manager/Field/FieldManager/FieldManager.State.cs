using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using DotRecast.Core.Numerics;
using DotRecast.Detour.Crowd;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using Maple2.Tools.Collision;
using Maple2.Tools.DotRecast;
using Maple2.Tools.Extensions;
using Maple2.Tools.VectorMath;
using Maple2.Server.World.Service;
using Serilog;
using WorldTimeEventRequest = Maple2.Server.World.Service.TimeEventRequest;

namespace Maple2.Server.Game.Manager.Field;

public partial class FieldManager {
    // Actors
    public ConcurrentDictionary<int, FieldPlayer> Players { get; } = [];
    public ConcurrentDictionary<int, FieldNpc> Npcs { get; } = [];
    public ConcurrentDictionary<int, FieldNpc> Mobs { get; } = [];
    public ConcurrentDictionary<int, FieldPet> Pets { get; } = [];

    // Entities
    private readonly ConcurrentDictionary<string, FieldBreakable> fieldBreakables = new();
    private readonly ConcurrentDictionary<string, FieldLiftable> fieldLiftables = new();
    private readonly ConcurrentDictionary<string, FieldInteract> fieldInteracts = new();
    private readonly ConcurrentDictionary<string, FieldFunctionInteract> fieldFunctionInteracts = new();
    private readonly ConcurrentDictionary<string, FieldInteract> fieldAdBalloons = new();
    private readonly ConcurrentDictionary<string, FieldInteract> fieldChests = new();
    private readonly ConcurrentDictionary<int, FieldInstrument> fieldInstruments = new();
    private readonly ConcurrentDictionary<int, FieldItem> fieldItems = new();
    private readonly ConcurrentDictionary<int, FieldMobSpawn> fieldMobSpawns = new();
    private readonly ConcurrentDictionary<string, FieldSpawnPointNpc> fieldSpawnPointNpcs = new();
    private readonly ConcurrentDictionary<int, FieldPlayerSpawnPoint> fieldPlayerSpawnPoints = new();
    private readonly ConcurrentDictionary<int, FieldSpawnGroup> fieldSpawnGroups = new();
    private readonly ConcurrentDictionary<int, FieldSkill> fieldSkills = new();
    private readonly ConcurrentDictionary<int, FieldPortal> fieldPortals = new();

    private readonly ConcurrentDictionary<int, HongBao> hongBaos = new();

    private int fieldBossObjectId;

    private string? background;
    private readonly ConcurrentDictionary<FieldProperty, IFieldProperty> fieldProperties = new();

    public RoomTimer? RoomTimer { get; private set; }


    #region Helpers
    public ICollection<FieldNpc> EnumerateNpcs() => Npcs.Values.Concat(Mobs.Values).ToList();
    public IReadOnlyDictionary<int, FieldPlayer> GetPlayers() {
        return Players;
    }
    public ICollection<FieldPortal> GetPortals() => fieldPortals.Values;
    #endregion

    #region Spawn
    public FieldPlayer SpawnPlayer(GameSession session, Player player, int portalId = -1, in Vector3 position = default, in Vector3 rotation = default) {
        // TODO: Not sure what the difference is between instance ids.
        player.Character.MapId = MapId;
        player.Character.InstanceMapId = MapId;
        player.Character.RoomId = RoomId;

        var fieldPlayer = new FieldPlayer(session, player) {
            Position = position,
            Rotation = rotation,
        };
        session.Stats.ResetActor(fieldPlayer);
        session.Buffs.ResetActor(fieldPlayer);
        session.Animation.ResetActor(fieldPlayer);

        // Use Portal if needed.
        if (fieldPlayer.Position == default && Entities.Portals.TryGetValue(portalId, out Portal? portal)) {
            fieldPlayer.Position = portal.Position.Offset(portal.FrontOffset, portal.Rotation);
            if (portal.RandomRadius > 0) {
                fieldPlayer.Position += new Vector3(Random.Shared.Next(-portal.RandomRadius, portal.RandomRadius), Random.Shared.Next(-portal.RandomRadius, portal.RandomRadius), 0);
            }
            fieldPlayer.Rotation = portal.Rotation;
        }

        // Use SpawnPoint if needed.
        if (fieldPlayer.Position == default) {
            if (TryGetPlayerSpawn(-1, out FieldPlayerSpawnPoint? spawn)) {
                fieldPlayer.Position = spawn.Position + new Vector3(0, 0, 25);
                fieldPlayer.Rotation = spawn.Rotation;
            }
        }

        if (FieldInstance.Type == InstanceType.none || FieldInstance.SaveField) {
            if (Metadata.Property.EnterReturnId != 0) {
                player.Character.ReturnMaps.Push(Metadata.Property.EnterReturnId);
            }
        }

        return fieldPlayer;
    }

    public FieldNpc? SpawnNpc(NpcMetadata npc, Vector3 position, Vector3 rotation, bool disableAi = false, FieldMobSpawn? owner = null, SpawnPointNPC? spawnPointNpc = null, string spawnAnimation = "") {
        // Apply random offset if SpawnRadius is set
        Vector3 spawnPosition = position;
        if (spawnPointNpc?.SpawnRadius > 0) {
            float angle = Random.Shared.NextSingle() * MathF.PI * 2;
            float distance = Random.Shared.NextSingle() * spawnPointNpc.SpawnRadius;
            Vector3 offset = new Vector3(
                MathF.Cos(angle) * distance,
                MathF.Sin(angle) * distance,
                0
            );
            Vector3 randomizedPosition = position + offset;

            // Validate and snap to nearest valid navmesh position
            if (FindNearestPoly(randomizedPosition, out long nearestRef, out RcVec3f validPosition) && nearestRef != 0) {
                spawnPosition = DotRecastHelper.FromNavMeshSpace(validPosition);
                logger.Debug("[SpawnNpc] Applied spawn radius {Radius} to NPC {NpcId} at SpawnPoint {SpawnPointId}, offset: {Offset}, snapped from {Original} to {Final}",
                    spawnPointNpc.SpawnRadius, npc.Id, spawnPointNpc.SpawnPointId, offset, randomizedPosition, spawnPosition);
            } else {
                logger.Warning("[SpawnNpc] Randomized position {Position} invalid for NPC {NpcId}, using original spawn point {Original}",
                    randomizedPosition, npc.Id, position);
                spawnPosition = position;
            }
        }

        DtCrowdAgent agent = Navigation.AddAgent(npc, spawnPosition);

        AnimationMetadata? animation = NpcMetadata.GetAnimation(npc.Model.Name);
        string aiPath = disableAi ? string.Empty : npc.AiPath;
        var fieldNpc = new FieldNpc(this, NextLocalId(), agent, new Npc(npc, animation), aiPath, patrolDataUUID: spawnPointNpc?.PatrolData, spawnAnimation: spawnAnimation) {
            Owner = owner,
            Position = spawnPosition,
            Rotation = rotation,
            Origin = owner?.Position ?? spawnPosition,
            SpawnPointId = owner?.Value.Id ?? 0,
        };

        if (npc.Basic.Friendly > 0) {
            Npcs[fieldNpc.ObjectId] = fieldNpc;
        } else {
            Mobs[fieldNpc.ObjectId] = fieldNpc;
        }

        return fieldNpc;
    }

    public FieldNpc? SpawnNpc(NpcMetadata npc, SpawnPointNPC spawnPointNpc) {
        return SpawnNpc(npc, spawnPointNpc.Position, spawnPointNpc.Rotation, disableAi: false, owner: null, spawnPointNpc: spawnPointNpc);
    }

    public FieldPet? SpawnPet(Item pet, Vector3 position, Vector3 rotation, FieldMobSpawn? owner = null, FieldPlayer? player = null) {
        if (!NpcMetadata.TryGet(pet.Metadata.Property.PetId, out NpcMetadata? npc)) {
            logger.Error("Failed to get npc metadata for pet id {PetId}", pet.Metadata.Property.PetId);
            return null;
        }

        if (!ItemMetadata.TryGetPet(pet.Metadata.Property.PetId, out PetMetadata? petMetadata)) {
            logger.Error("Failed to get pet metadata for pet id {PetId}", pet.Metadata.Property.PetId);
            return null;
        }

        DtCrowdAgent agent = Navigation.AddAgent(npc, position);

        // We use GlobalId if there is an owner because players can move between maps.
        int objectId = player != null ? NextGlobalId() : NextLocalId();
        AnimationMetadata? animation = NpcMetadata.GetAnimation(npc.Model.Name);

        var fieldPet = new FieldPet(this, objectId, agent, new Npc(npc, animation), pet, petMetadata, Constant.PetFieldAiPath, player) {
            Owner = owner,
            Position = position,
            Rotation = rotation,
            Origin = owner?.Position ?? position,
        };
        Pets[fieldPet.ObjectId] = fieldPet;

        return fieldPet;
    }

    public FieldPortal SpawnPortal(Portal portal, Vector3 position = default, Vector3 rotation = default) {
        var fieldPortal = new FieldPortal(this, NextLocalId(), portal) {
            Position = position != default ? position : portal.Position,
            Rotation = rotation != default ? rotation : portal.Rotation,
        };
        fieldPortals[fieldPortal.ObjectId] = fieldPortal;

        return fieldPortal;
    }

    public FieldPortal SpawnPortal(Portal portal, int roomId, Vector3 position = default, Vector3 rotation = default) {
        var fieldPortal = new FieldPortal(this, NextLocalId(), portal) {
            Position = position != default ? position : portal.Position,
            Rotation = rotation != default ? rotation : portal.Rotation,
            RoomId = roomId,
        };
        fieldPortals[fieldPortal.ObjectId] = fieldPortal;

        return fieldPortal;
    }

    public FieldPortal SpawnPortal(QuestSummonPortal metadata, FieldNpc npc, FieldPlayer owner) {
        var portal = new Portal(NextLocalId(), metadata.MapId, metadata.PortalId, PortalType.Quest, PortalActionType.Interact, npc.Position.Offset(Constant.QuestPortalDistanceFromNpc, npc.Rotation), npc.Rotation,
            new Vector3(Constant.QuestPortalDistanceFromNpc, Constant.QuestPortalDimensionY, Constant.QuestPortalDimensionZ), Constant.QuestPortalDistanceFromNpc,
            0, true, false, true);
        var fieldPortal = new FieldQuestPortal(owner, this, NextLocalId(), portal) {
            Position = portal.Position,
            Rotation = portal.Rotation,
            EndTick = (FieldTick + (long) TimeSpan.FromSeconds(Constant.QuestPortalKeepTime).TotalMilliseconds).Truncate32(),
            StartTick = FieldTickInt,
            Model = Constant.QuestPortalKeepNif,
        };
        fieldPortals[fieldPortal.ObjectId] = fieldPortal;

        return fieldPortal;
    }

    public FieldPortal? SpawnCubePortal(PlotCube plotCube) {
        int targetMapId = MapId;
        long targetHomeAccountId = 0;
        CubePortalSettings? cubePortalSettings = plotCube.Interact?.PortalSettings;
        if (cubePortalSettings is null) {
            return null;
        }

        if (!string.IsNullOrEmpty(cubePortalSettings.DestinationTarget)) {
            switch (cubePortalSettings.Destination) {
                case CubePortalDestination.PortalInHome:
                    targetMapId = Constant.DefaultHomeMapId;
                    break;
                case CubePortalDestination.SelectedMap:
                    targetMapId = int.Parse(cubePortalSettings.DestinationTarget);
                    break;
                case CubePortalDestination.FriendHome:
                    targetMapId = Constant.DefaultHomeMapId;
                    targetHomeAccountId = long.Parse(cubePortalSettings.DestinationTarget);
                    break;
            }
        }
        var portal = new Portal(NextLocalId(), targetMapId, -1, PortalType.InHome, cubePortalSettings.Method, plotCube.Position, new Vector3(0, 0, plotCube.Rotation), new Vector3(200, 200, 250), 0, 0, Visible: true, MinimapVisible: false, Enable: true);
        FieldPortal fieldPortal = SpawnPortal(portal);
        fieldPortal.HomeId = targetHomeAccountId;
        cubePortalSettings.PortalObjectId = fieldPortal.ObjectId;

        return fieldPortal;
    }

    public FieldPortal? SpawnFieldToHomePortal(PlotCube plotCube, long targetHomeAccountId) {
        if (plotCube.Metadata.Install is { IndoorPortal: false }) {
            return null;
        }

        var transform = new Transform {
            Position = plotCube.Position,
            RotationAnglesDegrees = new Vector3(0, 0, plotCube.Rotation),
        };

        Vector3 newPosition = plotCube.Position;
        newPosition -= transform.FrontAxis * 75;
        newPosition.Z -= 75;

        var portal = new Portal(NextLocalId(), Constant.DefaultHomeMapId, -1, PortalType.FieldToHome, PortalActionType.Interact, newPosition, new Vector3(0, 0, plotCube.Rotation), new Vector3(75, 75, 75), 0, 0, Visible: true, MinimapVisible: false, Enable: true);
        FieldPortal fieldPortal = SpawnPortal(portal);
        fieldPortal.HomeId = targetHomeAccountId;

        return fieldPortal;
    }

    public FieldItem SpawnItem(IActor owner, Item item) {
        lock (item) {
            using GameStorage.Request db = GameStorage.Context();
            db.SaveItems(0, item);
        }

        var fieldItem = new FieldItem(this, NextLocalId(), item) {
            Owner = owner,
            Position = owner.Position,
            Rotation = owner.Rotation,
            Type = DropType.Player,
        };
        fieldItems[fieldItem.ObjectId] = fieldItem;

        return fieldItem;
    }

    public FieldItem SpawnItem(Vector3 position, Vector3 rotation, Item item, IFieldEntity? owner, long characterId = 0, bool fixedPosition = false) {
        var fieldItem = new FieldItem(this, NextLocalId(), item) {
            Owner = owner,
            Position = position,
            Rotation = rotation,
            FixedPosition = fixedPosition,
            ReceiverId = characterId,
            Type = characterId > 0 ? DropType.Default : DropType.Player,
        };
        fieldItems[fieldItem.ObjectId] = fieldItem;

        return fieldItem;
    }

    public void DropItem(IActor owner, Item item) {
        FieldItem fieldItem = SpawnItem(owner, item);
        Broadcast(FieldPacket.DropItem(fieldItem));
    }

    public void DropItem(Vector3 position, Vector3 rotation, Item item, IFieldEntity? owner = null, long characterId = 0, bool fixedPosition = false) {
        FieldItem fieldItem = SpawnItem(position, rotation, item, owner, characterId, fixedPosition);
        Broadcast(FieldPacket.DropItem(fieldItem));
    }

    public FieldBreakable? AddBreakable(string entityId, BreakableActor breakable) {
        if (fieldBreakables.ContainsKey(entityId)) {
            return null;
        }

        var fieldBreakable = new FieldBreakable(this, NextLocalId(), entityId, breakable) {
            Position = breakable.Position,
            Rotation = breakable.Rotation,
        };

        fieldBreakables.TryAdd(entityId, fieldBreakable);
        if (breakable.Id != 0) {
            triggerBreakable.TryAdd(breakable.Id, fieldBreakable);
        }
        return fieldBreakable;
    }

    public FieldLiftable? AddLiftable(string entityId, Liftable liftable) {
        if (fieldLiftables.ContainsKey(entityId)) {
            return null;
        }

        var fieldLiftable = new FieldLiftable(this, NextLocalId(), entityId, liftable) {
            Position = liftable.Position,
            Rotation = liftable.Rotation,
        };

        if (!fieldLiftables.TryAdd(entityId, fieldLiftable)) {
            return null;
        }
        return fieldLiftable;
    }

    public FieldInteract? AddInteract(string entityId, InteractObject interact) {
        if (!TableMetadata.InteractObjectTable.Entries.TryGetValue(interact.InteractId, out InteractObjectMetadata? metadata)) {
            return null;
        }
        IInteractObject interactObject = interact switch {
            Ms2InteractMesh mesh => new InteractMeshObject(entityId, mesh),
            Ms2Telescope telescope => new InteractTelescopeObject(entityId, telescope),
            Ms2SimpleUiObject ui => new InteractUiObject(entityId, ui),
            Ms2InteractDisplay display when metadata.Type == InteractType.DisplayImage => new InteractDisplayImage(entityId, display),
            Ms2InteractDisplay poster when metadata.Type == InteractType.GuildPoster => new InteractGuildPosterObject(entityId, poster),
            Ms2InteractActor actor => new InteractGatheringObject(entityId, actor),
            _ => throw new ArgumentException($"Unsupported Type: {metadata.Type}"),
        };

        return AddInteract(interact, interactObject);
    }

    public FieldInteract? AddInteract(InteractObject interactData, IInteractObject interactObject, InteractObjectMetadata? metadata = null) {
        if (metadata == null && !TableMetadata.InteractObjectTable.Entries.TryGetValue(interactData.InteractId, out metadata)) {
            return null;
        }

        var fieldInteract = new FieldInteract(this, NextLocalId(), interactObject.EntityId, metadata, interactObject) {
            Position = interactData.Position,
            Rotation = interactData.Rotation,
        };

        //TODO: Add treasure chests
        switch (interactObject) {
            case InteractBillBoardObject billboard:
                fieldAdBalloons[billboard.EntityId] = fieldInteract;
                break;
            default:
                fieldInteracts[interactObject.EntityId] = fieldInteract;
                break;
        }

        return fieldInteract;
    }

    public FieldMobSpawn? AddMobSpawn(MapMetadataSpawn metadata, Ms2RegionSpawn regionSpawn, ICollection<int> npcIds) {
        var spawnNpcs = new WeightedSet<NpcMetadata>();
        foreach (int npcId in npcIds) {
            if (!NpcMetadata.TryGet(npcId, out NpcMetadata? npc)) {
                continue;
            }
            if (npc.Basic.Difficulty < metadata.MinDifficulty || npc.Basic.Difficulty > metadata.MaxDifficulty) {
                continue;
            }

            int spawnWeight = (int) Lua.CalcNpcSpawnWeight(npc.Basic.MainTags.Length, npc.Basic.SubTags.Length, npc.Basic.RareDegree, npc.Basic.Difficulty);
            spawnNpcs.Add(npc, spawnWeight);
        }

        var spawnPets = new WeightedSet<ItemMetadata>();
        foreach ((NpcMetadata npc, int weight) in spawnNpcs) {
            if (!metadata.PetIds.TryGetValue(npc.Id, out int petId)) {
                continue;
            }

            if (!ItemMetadata.TryGetPet(petId, out ItemMetadata? pet)) {
                continue;
            }

            spawnPets.Add(pet, weight);
        }

        if (spawnNpcs.Count <= 0) {
            logger.Warning("No valid Npcs found from: {NpcIds}", string.Join(",", npcIds));
            return null;
        }

        var fieldMobSpawn = new FieldMobSpawn(this, NextLocalId(), metadata, spawnNpcs, spawnPets) {
            Position = regionSpawn.Position,
            Rotation = regionSpawn.UseRotAsSpawnDir ? regionSpawn.Rotation : default,
        };

        fieldMobSpawns[fieldMobSpawn.ObjectId] = fieldMobSpawn;
        return fieldMobSpawn;
    }

    public FieldSpawnPointNpc AddSpawnPointNpc(SpawnPointNPC metadata) {
        var fieldSpawnPointNpc = new FieldSpawnPointNpc(this, NextLocalId(), metadata) {
            Position = metadata.Position,
            Rotation = metadata.Rotation,
        };

        fieldSpawnPointNpcs[metadata.EntityId] = fieldSpawnPointNpc;
        fieldSpawnPointNpc.SpawnOnCreate();
        return fieldSpawnPointNpc;
    }

    public void ToggleCombineSpawn(SpawnGroupMetadata metadata, bool enable) {
        if (!fieldSpawnGroups.TryGetValue(metadata.GroupId, out FieldSpawnGroup? fieldSpawnGroup)) {
            fieldSpawnGroup = new FieldSpawnGroup(this, NextLocalId(), metadata);
            fieldSpawnGroups[metadata.GroupId] = fieldSpawnGroup;
        }

        fieldSpawnGroup.ToggleActive(enable);
    }

    public FieldNpc? SpawnFieldBoss(FieldBossMetadata metadata, int eventId) {
        if (metadata.NpcIds.Length == 0 || metadata.SpawnPointIds.Length == 0) {
            logger.Warning("[FieldBoss] Metadata {Id} has no NpcIds or SpawnPointIds", metadata.Id);
            return null;
        }

        if (metadata.Unique && fieldBossObjectId != 0) {
            logger.Warning("[FieldBoss] Unique boss {Id} already present in map {MapId}, skipping spawn", metadata.Id, MapId);
            return null;
        }

        // TimeEventData.Xml only uses 1 npc and spawnpointid for each event
        if (!NpcMetadata.TryGet(metadata.NpcIds.First(), out NpcMetadata? npcMetadata)) {
            logger.Warning("[FieldBoss] NPC {NpcId} not found", metadata.NpcIds.First());
            return null;
        }

        if (!Entities.RegionSpawns.TryGetValue(metadata.SpawnPointIds.First(), out Ms2RegionSpawn? regionSpawn)) {
            logger.Warning("[FieldBoss] RegionSpawn {SpawnPointId} not found in map {MapId}", metadata.SpawnPointIds.First(), MapId);
            return null;
        }

        FieldNpc? npc = SpawnNpc(npcMetadata, regionSpawn.Position, regionSpawn.Rotation);
        if (npc == null) {
            return null;
        }

        fieldBossObjectId = npc.ObjectId;
        npc.FieldBossDeathCallback = killedNpc => {
            fieldBossObjectId = 0;
            FieldPlayer? killer = killedNpc.GetLastAttacker();
            string killerName = killer?.Value.Character.Name ?? string.Empty;
            string bossName = npcMetadata.Name ?? string.Empty;

            Broadcast(NoticePacket.Message(new InterfaceText(StringCode.s_hunting_kill_boss, killerName, bossName)));

            // Notify World to remove this channel from the boss's alive channel list (for world map accuracy)
            try {
                FieldFactory.World.TimeEvent(new WorldTimeEventRequest {
                    FieldBossKilled = new WorldTimeEventRequest.Types.FieldBossKilled {
                        MetadataId = metadata.Id,
                        EventId = eventId,
                        Channel = GameServer.GetChannel(),
                    },
                });
            } catch (Exception ex) {
                logger.Error(ex, "[FieldBoss] Failed to notify world of boss {MetadataId} kill on channel {Channel}", metadata.Id, GameServer.GetChannel());
            }
        };
        Broadcast(FieldPacket.AddNpc(npc));
        logger.Information("[FieldBoss] Spawned {NpcId} (objectId={ObjectId}) for event {EventId} in map {MapId}", metadata.NpcIds[0], npc.ObjectId, eventId, MapId);
        return npc;
    }

    public FieldNpc? GetFieldBossNpc() {
        if (fieldBossObjectId == 0) return null;
        Mobs.TryGetValue(fieldBossObjectId, out FieldNpc? npc);
        return npc;
    }

    public void DespawnFieldBoss() {
        if (fieldBossObjectId == 0) {
            return;
        }

        int objectId = fieldBossObjectId;
        fieldBossObjectId = 0;
        RemoveNpc(objectId);
    }

    public void ToggleNpcSpawnPoint(int spawnId) {
        List<FieldSpawnPointNpc> spawns = fieldSpawnPointNpcs.Values.Where(spawn => spawn.Value.SpawnPointId == spawnId).ToList();
        foreach (FieldSpawnPointNpc spawn in spawns) {
            spawn.TriggerSpawn();
        }
    }

    public void SpawnInteractObject(SpawnInteractObjectMetadata metadata) {
        if (!Entities.RegionSpawns.TryGetValue(metadata.RegionSpawnId, out Ms2RegionSpawn? boxSpawn) ||
            !TableMetadata.InteractObjectTable.Entries.TryGetValue(metadata.InteractId, out InteractObjectMetadata? interactObjectMetadata)) {
            return;
        }
        Vector3B position = boxSpawn.Position;

        var interactObject = new InteractMeshObject($"EventCreate_{position.ConvertToInt()}", new Ms2InteractMesh(
            metadata.InteractId, boxSpawn.Position, boxSpawn.Rotation)) {
            Asset = metadata.Asset,
            Model = metadata.Model,
            NormalState = metadata.Normal,
            Reactable = metadata.Reactable,
            Scale = metadata.Scale,
        };

        var fieldInteract = new FieldInteract(this, NextLocalId(), interactObject.EntityId, interactObjectMetadata, interactObject) {
            Transform = new Transform {
                Position = boxSpawn.Position,
                RotationAnglesDegrees = boxSpawn.Rotation,
            },
            SpawnId = boxSpawn.Id,
        };
        fieldChests[interactObject.EntityId] = fieldInteract;
        Broadcast(InteractObjectPacket.Add(fieldInteract.Object));
    }

    public void AddSkill(Ms2TriggerSkill triggerSkill, int interval, in Vector3 position, in Vector3 rotation = default, int triggerId = 0) {
        if (!SkillMetadata.TryGet(triggerSkill.SkillId, triggerSkill.Level, out SkillMetadata? skillMetadata)) {
            logger.Warning("Invalid skill: {Id}", triggerSkill.SkillId);
            return;
        }

        var fieldSkill = new FieldSkill(this, NextLocalId(), FieldActor, skillMetadata, interval, triggerSkill.Count, position) {
            Position = position,
            Rotation = rotation,
            TriggerId = triggerId,
        };

        fieldSkills[fieldSkill.ObjectId] = fieldSkill;
        Broadcast(RegionSkillPacket.Add(fieldSkill));
    }

    public void AddSkill(SkillMetadata metadata, int interval, in Vector3 position, in Vector3 rotation = default, int triggerId = 0) {
        var fieldSkill = new FieldSkill(this, NextLocalId(), FieldActor, metadata, interval, position) {
            Position = position,
            Rotation = rotation,
            TriggerId = triggerId,
        };

        fieldSkills[fieldSkill.ObjectId] = fieldSkill;
        Broadcast(RegionSkillPacket.Add(fieldSkill));
    }

    public void AddSkill(IActor caster, SkillEffectMetadata effect, Vector3[] points, in Vector3 rotation = default) {
        Debug.Assert(effect.Splash != null, "Cannot add non-splash skill to field");

        foreach (SkillEffectMetadata.Skill skill in effect.Skills) {
            if (!SkillMetadata.TryGet(skill.Id, skill.Level, out SkillMetadata? metadata)) {
                continue;
            }

            int fireCount = effect.FireCount > 0 ? effect.FireCount : -1;
            var fieldSkill = new FieldSkill(this, NextLocalId(), caster, metadata, fireCount, effect.Splash, points) {
                Position = points[0],
                Rotation = rotation,
            };

            fieldSkills[fieldSkill.ObjectId] = fieldSkill;
            Broadcast(RegionSkillPacket.Add(fieldSkill));
        }
    }

    public void AddSkill(SkillRecord record) {
        SkillMetadataAttack attack = record.Attack;
        if (!TableMetadata.MagicPathTable.Entries.TryGetValue(attack.CubeMagicPathId, out IReadOnlyList<MagicPath>? cubeMagicPaths)) {
            logger.Error("No CubeMagicPath found for {CubeMagicPath})", attack.CubeMagicPathId);
            return;
        }

        Vector3[] cubePoints;
        if (attack.CubeMagicPathId != 0) {
            // TODO: If this is a CubeMagicPath, we always align the height. Ideally we can detect the floor instead.
            record.Position = record.Position.AlignHeight();
            cubePoints = new Vector3[cubeMagicPaths.Count];
            for (int i = 0; i < cubeMagicPaths.Count; i++) {
                MagicPath magicPath = cubeMagicPaths[i];
                Vector3 rotation = default;
                if (magicPath.Rotate) {
                    rotation = record.Rotation;
                }

                Vector3 position = record.Position + magicPath.FireOffset.Rotate(rotation);
                cubePoints[i] = magicPath.IgnoreAdjust ? position : position.Align();
            }
        } else {
            cubePoints = [record.Position];
        }

        // Condition-Skills are expected to be handled separately.
        foreach (SkillEffectMetadata effect in attack.Skills.Where(effect => effect.Splash != null)) {
            if (effect.Splash == null) {
                logger.Fatal("Invalid Splash-Skill being handled: {Effect}", effect);
                continue;
            }

            AddSkill(record.Caster, effect, cubePoints, record.Rotation);
        }
    }

    public IEnumerable<IActor> GetTargets(IActor caster, Prism[] prisms, ApplyTargetType targetType, int limit, ICollection<IActor>? ignore = null) {
        switch (targetType) {
            case ApplyTargetType.Friendly:
                if (caster is FieldNpc) {
                    return prisms.Filter(Mobs.Values, limit, ignore);
                } else if (caster is FieldPlayer) {
                    return prisms.Filter(Players.Values, limit, ignore);
                }
                Log.Debug("Unhandled ApplyTargetType:{Entity} for {caster.GetType()}", targetType, caster.GetType());
                return [];
            case ApplyTargetType.Hostile:
                if (caster is FieldNpc) {
                    return prisms.Filter(Players.Values, limit, ignore);
                } else if (caster is FieldPlayer) {
                    //TODO Include other players if PVP is Active
                    return prisms.Filter(Mobs.Values, limit, ignore);
                }
                Log.Debug("Unhandled ApplyTargetType:{Entity} for {caster.GetType()}", targetType, caster.GetType());
                return [];
            case ApplyTargetType.HungryMobs:
                return prisms.Filter(Pets.Values.Where(pet => pet.OwnerId == 0), limit, ignore);
            case ApplyTargetType.RegionBuff:
            case ApplyTargetType.RegionBuff2:
                return prisms.Filter(Players.Values, limit, ignore);
            default:
                Log.Debug("Unhandled SkillEntity:{Entity}", targetType);
                return [];
        }
    }

    public void RemoveSkill(int objectId) {
        if (fieldSkills.Remove(objectId, out _)) {
            Broadcast(RegionSkillPacket.Remove(objectId));
        }
    }

    public void RemoveSkillByTriggerId(int triggerId) {
        foreach (FieldSkill fieldSkill in fieldSkills.Values.Where(skill => skill.TriggerId == triggerId)) {
            fieldSkills.Remove(fieldSkill.ObjectId, out _);
            Broadcast(RegionSkillPacket.Remove(fieldSkill.ObjectId));
        }
    }
    #endregion

    public void AddFieldProperty(IFieldProperty fieldProperty) {
        fieldProperties[fieldProperty.Type] = fieldProperty;
        Broadcast(FieldPropertyPacket.Add(fieldProperty));
    }

    public void RemoveFieldProperty(FieldProperty fieldProperty) {
        fieldProperties.Remove(fieldProperty, out _);
        Broadcast(FieldPropertyPacket.Remove(fieldProperty));
    }

    public IFieldProperty GetFieldProperty(FieldProperty type) {
        if (fieldProperties.TryGetValue(type, out IFieldProperty? fieldProperty)) {
            return fieldProperty;
        }
        fieldProperty = new FieldPropertySightRange();
        AddFieldProperty(fieldProperty);
        return fieldProperty;
    }

    public void SetBackground(string ddsPath) {
        background = ddsPath;
        Broadcast(FieldPropertyPacket.Background(background));
    }

    private void SetBonusMapPortal(IList<MapMetadata> bonusMaps, Ms2RegionSpawn spawn) {
        // Spawn a hat within a random range of 5 min to 8 hours
        var delay = Random.Shared.Next(1, 97) * TimeSpan.FromMinutes(5);
        MapMetadata bonusMapMetadata = bonusMaps[Random.Shared.Next(bonusMaps.Count)];
        IField? bonusMap = FieldFactory.Create(bonusMapMetadata.Id);
        bonusMap?.Init();
        Console.WriteLine($"Creating bonus map {bonusMapMetadata.Id} at {spawn.Position} in {delay} ms.");
        if (bonusMap == null) {
            return;
        }
        bonusMap.SetRoomTimer(RoomTimerType.Clock, 90000);
        var portal = new Portal(NextLocalId(), bonusMapMetadata.Id, -1, PortalType.Event, PortalActionType.Interact, spawn.Position, spawn.Rotation,
            new Vector3(200, 200, 250), 0, 0, true, false, true);
        FieldPortal fieldPortal = SpawnPortal(portal, bonusMap.RoomId);
        fieldPortal.Model = Metadata.Property.Continent switch {
            Continent.VictoriaIsland => "Eff_event_portal_A01",
            Continent.KarkarIsland => "Eff_kr_sandswirl_01",
            Continent.ShadowWorld => "Eff_uw_potral_A01",
            Continent.Kritias => "Eff_ks_magichole_portal_A01",
            _ => "Eff_event_portal_A01",
        };
        fieldPortal.EndTick = (FieldTick + (long) TimeSpan.FromSeconds(30).TotalMilliseconds).Truncate32();
        Broadcast(PortalPacket.Add(fieldPortal));
        Scheduler.Schedule(() => SetBonusMapPortal(bonusMaps, spawn), delay);
    }

    public void SetRoomTimer(RoomTimerType type, int duration) {
        RoomTimer = new RoomTimer(this, type, duration);
    }

    public void AddHongBao(FieldPlayer owner, int sourceItemId, int itemId, int totalUser, int durationSec, int itemCount) {
        var hongBao = new HongBao(this, owner, sourceItemId, NextLocalId(), itemId, totalUser, FieldTick, durationSec, itemCount);
        if (!hongBaos.TryAdd(hongBao.ObjectId, hongBao)) {
            logger.Error("Failed to add hongbao {ObjectId}", hongBao.ObjectId);
            return;
        }

        Item? item = hongBao.Claim(owner);
        if (item == null) {
            return;
        }
        if (!owner.Session.Item.Inventory.Add(item, true)) {
            owner.Session.Item.MailItem(item);
        }
        Broadcast(PlayerHostPacket.UseHongBao(hongBao, item.Amount));
    }

    public void RemoveHongBao(int objectId) {
        hongBaos.TryRemove(objectId, out _);
    }

    public bool TryGetHongBao(int objectId, [NotNullWhen(true)] out HongBao? hongBao) {
        return hongBaos.TryGetValue(objectId, out hongBao);
    }


    #region Player Managed
    // GuideObject is not added to the field, it will be managed by |GameSession.State|
    public FieldGuideObject SpawnGuideObject(IActor<Player> owner, IGuideObject guideObject, Vector3 position = default) {
        if (position == default) {
            position = owner.Position;
        }
        var fieldGuideObject = new FieldGuideObject(this, NextLocalId(), guideObject) {
            CharacterId = owner.Value.Character.Id,
            Position = position,
            // rotation?
        };

        return fieldGuideObject;
    }

    public FieldInstrument SpawnInstrument(IActor<Player> owner, InstrumentMetadata instrument) {
        var fieldInstrument = new FieldInstrument(this, NextLocalId(), instrument) {
            OwnerId = owner.ObjectId,
            Position = owner.Position + new Vector3(0, 0, 1),
            Rotation = owner.Rotation,
        };

        fieldInstruments[fieldInstrument.ObjectId] = fieldInstrument;

        return fieldInstrument;
    }

    public FieldPortal SpawnEventPortal(FieldPlayer player, int fieldId, int portalDurationTick, string password) {
        var portal = new Portal(NextLocalId(), fieldId, -1, PortalType.Event, PortalActionType.Interact, player.Position, player.Rotation, new Vector3(200, 200, 250), 0, 0, true, false, true);
        FieldPortal fieldPortal = SpawnPortal(portal);
        fieldPortal.Model = "Eff_Com_Portal_E";
        fieldPortal.Password = password;
        fieldPortal.OwnerName = player.Value.Character.Name;
        fieldPortal.EndTick = (int) (Environment.TickCount64 + portalDurationTick);
        Broadcast(PortalPacket.Add(fieldPortal));
        return fieldPortal;
    }

    public FieldFunctionInteract? AddFieldFunctionInteract(PlotCube cube) {
        if (cube.Interact == null) {
            return null;
        }
        var fieldInteract = new FieldFunctionInteract(this, NextLocalId(), cube.Interact, cube.Id) {
            Position = cube.Position,
            Rotation = new Vector3(0, 0, cube.Rotation),
        };

        fieldFunctionInteracts[cube.Interact.Id] = fieldInteract;

        Broadcast(FunctionCubePacket.AddFunctionCube(cube.Interact));

        return fieldInteract;
    }

    public bool RemoveFieldFunctionInteract(string entityId) {
        return fieldFunctionInteracts.TryRemove(entityId, out FieldFunctionInteract? _);
    }

    public FieldFunctionInteract? TryGetFieldFunctionInteract(string entityId) {
        return fieldFunctionInteracts.TryGetValue(entityId, out FieldFunctionInteract? fieldFunctionInteract) ? fieldFunctionInteract : null;
    }
    #endregion

    #region Remove
    public virtual bool RemovePlayer(int objectId, [NotNullWhen(true)] out FieldPlayer? fieldPlayer) {
        if (Players.TryRemove(objectId, out fieldPlayer)) {
            CommitPlot(fieldPlayer.Session);
            Broadcast(FieldPacket.RemovePlayer(objectId));
            Broadcast(ProxyObjectPacket.RemovePlayer(objectId));

            return true;
        }

        return false;
    }

    public bool PickupItem(FieldPlayer looter, int objectId, [NotNullWhen(true)] out Item? item) {
        if (!fieldItems.TryRemove(objectId, out FieldItem? fieldItem)) {
            item = null;
            return false;
        }

        item = fieldItem.Value;
        fieldItem.Pickup(looter);
        Broadcast(FieldPacket.RemoveItem(objectId));
        return true;
    }

    public bool RemoveItem(int objectId) {
        if (!fieldItems.TryRemove(objectId, out _)) {
            return false;
        }

        Broadcast(FieldPacket.RemoveItem(objectId));
        return true;
    }

    public bool RemoveLiftable(string entityId) {
        if (!fieldLiftables.TryRemove(entityId, out FieldLiftable? fieldLiftable)) {
            return false;
        }

        if (Plots[0].Cubes.Remove(fieldLiftable.Position)) {
            Broadcast(CubePacket.RemoveCube(fieldLiftable.ObjectId, fieldLiftable.Position));
        }

        Broadcast(LiftablePacket.Remove(entityId));
        return true;
    }

    public bool RemoveInteract(IInteractObject interactObject, TimeSpan removeDelay = default) {
        if (!fieldAdBalloons.ContainsKey(interactObject.EntityId) && !fieldInteracts.ContainsKey(interactObject.EntityId)) {
            return false;
        }

        Scheduler.Schedule(() => {
            switch (interactObject) {
                case InteractBillBoardObject billboard:
                    fieldAdBalloons.TryRemove(billboard.EntityId, out _);
                    break;
                default:
                    fieldInteracts.TryRemove(interactObject.EntityId, out _);
                    fieldAdBalloons.TryRemove(interactObject.EntityId, out _);
                    break;
            }

            Broadcast(InteractObjectPacket.Remove(interactObject.EntityId));
        }, removeDelay);
        return true;
    }

    public bool RemoveNpc(int objectId, TimeSpan removeDelay = default) {
        if (!Mobs.TryGetValue(objectId, out _) && !Npcs.TryGetValue(objectId, out _)) {
            return false;
        }

        Scheduler.Schedule(() => {
            if (!Mobs.TryRemove(objectId, out FieldNpc? npc) && !Npcs.TryRemove(objectId, out npc)) {
                // Already removed
                return;
            }
            Broadcast(FieldPacket.RemoveNpc(objectId));
            Broadcast(ProxyObjectPacket.RemoveNpc(objectId));
            npc.Dispose();
        }, removeDelay);
        return true;
    }

    public bool RemovePet(int objectId, TimeSpan removeDelay = default) {
        if (!Pets.TryRemove(objectId, out FieldPet? pet)) {
            return false;
        }

        Scheduler.Schedule(() => {
            Broadcast(FieldPacket.RemovePet(objectId));
            Broadcast(ProxyObjectPacket.RemovePet(objectId));
            pet.Dispose();
        }, removeDelay);
        return true;
    }

    public bool RemovePortal(int objectId) {
        if (!fieldPortals.TryRemove(objectId, out FieldPortal? portal)) {
            return false;
        }

        if (portal is FieldQuestPortal questPortal) {
            questPortal.Owner.Session.Send(PortalPacket.Remove(questPortal.Value.Id));
        } else {
            Broadcast(PortalPacket.Remove(portal.Value.Id));
        }
        return true;
    }

    public bool RemoveInstrument(int objectId) {
        if (!fieldInstruments.TryRemove(objectId, out FieldInstrument? instrument)) {
            return false;
        }

        Broadcast(InstrumentPacket.StopScore(instrument));
        return true;
    }
    #endregion

    #region Events
    public void OnAddPlayer(FieldPlayer added) {
        Players[added.ObjectId] = added;
        // LOAD:
        foreach (FieldLiftable liftable in fieldLiftables.Values.Where(liftable => liftable.FinishTick > 0)) {
            added.Session.Send(LiftablePacket.Add(liftable));
        }
        added.Session.Send(LiftablePacket.Update(fieldLiftables.Values.Where(liftable => liftable.FinishTick == 0).ToList()));
        added.Session.Send(BreakablePacket.Update(fieldBreakables.Values));
        added.Session.Send(InteractObjectPacket.Load(fieldInteracts.Values));
        foreach (FieldInteract fieldInteract in fieldAdBalloons.Values) {
            added.Session.Send(InteractObjectPacket.Add(fieldInteract.Object));
        }
        foreach (FieldInteract fieldInteract in fieldChests.Values) {
            added.Session.Send(InteractObjectPacket.Add(fieldInteract.Object));
        }
        foreach (FieldInstrument fieldInstrument in fieldInstruments.Values) {
            if (fieldInstrument.Score != null) {
                added.Session.Send(InstrumentPacket.StartScore(fieldInstrument, fieldInstrument.Score));
            }
        }

        added.Session.Send(FunctionCubePacket.SendCubes(fieldFunctionInteracts.Values));

        foreach (Plot plot in Plots.Values) {
            foreach (PlotCube plotCube in plot.Cubes.Values) {
                if (plotCube.Interact?.NoticeSettings is not null) {
                    added.Session.Send(HomeActionPacket.SendCubeNoticeSettings(plotCube));
                }
            }
        }

        foreach (FieldPlayer fieldPlayer in Players.Values) {
            added.Session.Send(FieldPacket.AddPlayer(fieldPlayer.Session));
            if (fieldPlayer.Session.GuideObject != null) {
                added.Session.Send(GuideObjectPacket.Create(fieldPlayer.Session.GuideObject));
            }
            switch (fieldPlayer.Session.HeldCube) {
                case PlotCube plotCube:
                    added.Session.Send(SetCraftModePacket.Plot(fieldPlayer.ObjectId, plotCube));
                    break;
                case LiftableCube liftableCube:
                    added.Session.Send(SetCraftModePacket.Liftable(fieldPlayer.ObjectId, liftableCube));
                    break;
            }
        }
        Broadcast(FieldPacket.AddPlayer(added.Session), added.Session);
        added.Flag = PlayerObjectFlag.All;

        Broadcast(ProxyObjectPacket.AddPlayer(added), added.Session);
        foreach (FieldItem fieldItem in fieldItems.Values) {
            added.Session.Send(FieldPacket.DropItem(fieldItem));
        }
        foreach (FieldNpc fieldNpc in Npcs.Values.Concat(Mobs.Values)) {
            added.Session.Send(FieldPacket.AddNpc(fieldNpc));
        }
        foreach (FieldPet fieldPet in Pets.Values) {
            added.Session.Send(FieldPacket.AddPet(fieldPet));
        }
        foreach (FieldPortal fieldPortal in fieldPortals.Values) {
            switch (fieldPortal) {
                case FieldQuestPortal questPortal:
                    if (questPortal.Owner.ObjectId == added.ObjectId) {
                        added.Session.Send(PortalPacket.Add(questPortal));
                    }
                    continue;
                default:
                    added.Session.Send(PortalPacket.Add(fieldPortal));
                    continue;
            }
        }
        // ProxyGameObj
        foreach (FieldPlayer fieldPlayer in Players.Values) {
            added.Session.Send(ProxyObjectPacket.AddPlayer(fieldPlayer));
        }
        foreach (FieldNpc fieldNpc in Npcs.Values.Concat(Mobs.Values)) {
            added.Session.Send(ProxyObjectPacket.AddNpc(fieldNpc));
        }
        foreach (FieldPet fieldPet in Pets.Values) {
            added.Session.Send(ProxyObjectPacket.AddPet(fieldPet));
        }
        foreach (FieldSkill skillSource in fieldSkills.Values) {
            added.Session.Send(RegionSkillPacket.Add(skillSource));
        }

        added.Session.Send(TriggerPacket.Load(TriggerObjects));

        if (background != null) {
            added.Session.Send(FieldPropertyPacket.Background(background));
        }
        added.Session.Send(FieldPropertyPacket.Load(fieldProperties.Values));

        foreach (TickTimer timer in Timers.Values.Where(timer => timer.Display)) {
            Broadcast(TriggerPacket.TimerDialog(timer));
        }
    }
    #endregion Events
}
