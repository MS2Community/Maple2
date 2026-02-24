using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.DebugGraphics;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools.Collision;

namespace Maple2.Server.Game.Manager.Field;

public interface IField : IDisposable {
    public int RoomId { get; }
    public int DungeonId { get; }
    public bool Disposed { get; }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public GameStorage GameStorage { get; init; }
    public ItemMetadataStorage ItemMetadata { get; init; }
    public MapMetadataStorage MapMetadata { get; init; }
    public MapDataStorage MapData { get; init; }
    public NpcMetadataStorage NpcMetadata { get; init; }
    public AiMetadataStorage AiMetadata { get; init; }
    public SkillMetadataStorage SkillMetadata { get; init; }
    public TableMetadataStorage TableMetadata { get; init; }
    public FunctionCubeMetadataStorage FunctionCubeMetadata { get; init; }
    public ServerTableMetadataStorage ServerTableMetadata { get; init; }
    public ItemStatsCalculator ItemStatsCalc { get; init; }
    public IGraphicsContext DebugGraphicsContext { get; init; }
    // ReSharper restore All
    #endregion

    public ItemDropManager ItemDrop { get; }

    public Navigation Navigation { get; }
    public MapEntityMetadata Entities { get; init; }

    public ConcurrentDictionary<int, FieldPlayer> Players { get; }
    public ConcurrentDictionary<int, FieldNpc> Npcs { get; }
    public ConcurrentDictionary<int, FieldNpc> Mobs { get; }
    public ConcurrentDictionary<int, FieldPet> Pets { get; }

    public RoomTimer? RoomTimer { get; }
    public InstanceFieldMetadata FieldInstance { get; }
    public FieldType FieldType { get; }
    public int Size { get; init; }

    public int MapId { get; init; }
    public long FieldTick { get; }
    public int FieldTickInt { get; }
    public virtual void Init() { }

    public void AddSkill(SkillMetadata metadata, int interval, in Vector3 position, in Vector3 rotation = default, int triggerId = 0);
    public void AddSkill(SkillRecord record);
    public void AddSkill(IActor caster, SkillEffectMetadata effect, Vector3[] points, in Vector3 rotation = default);
    public IEnumerable<IActor> GetTargets(IActor caster, Prism[] prisms, SkillMetadataRange range, int targetCount, ICollection<IActor>? ignore = null);
    public IEnumerable<IActor> GetTargets(SkillRecord record, ICollection<IActor>? ignore = null);
    public void RemoveSkill(int objectId);
    public void Broadcast(ByteWriter packet, GameSession? sender = null);
    public void BroadcastAiMessage(ByteWriter packet);
    public void BroadcastAiType(GameSession requester);

    public void SetRoomTimer(RoomTimerType type, int duration); //TODO: MOVE THIS TO RANDOM ONLY


    public FieldItem SpawnItem(IActor owner, Item item);
    public FieldItem SpawnItem(Vector3 position, Vector3 rotation, Item item, IFieldEntity? owner = null, long characterId = 0, bool fixedPosition = false);

    public void DropItem(IActor owner, Item item);
    public void DropItem(Vector3 position, Vector3 rotation, Item item, IFieldEntity? owner = null, long characterId = 0, bool fixedPosition = false);

    public void EnsurePlayerPosition(FieldPlayer player);
    public bool TryGetPlayerById(long characterId, [NotNullWhen(true)] out FieldPlayer? player);
    public bool TryGetActor(int objectId, [NotNullWhen(true)] out IActor? actor);
    public bool TryGetPlayer(int objectId, [NotNullWhen(true)] out FieldPlayer? player);
    public bool TryGetPlayer(string name, [NotNullWhen(true)] out FieldPlayer? player);
    public bool TryGetPortal(int portalId, [NotNullWhen(true)] out FieldPortal? portal);
    public bool TryGetItem(int objectId, [NotNullWhen(true)] out FieldItem? fieldItem);
    public bool TryGetBreakable(string entityId, [NotNullWhen(true)] out FieldBreakable? fieldBreakable);
    public bool TryGetBreakable(int triggerId, [NotNullWhen(true)] out FieldBreakable? fieldBreakable);
    public bool TryGetLiftable(string entityId, [NotNullWhen(true)] out FieldLiftable? fieldLiftable);
    public ICollection<FieldInteract> EnumerateInteract();
    public ICollection<FieldLiftable> EnumerateLiftables();
    public bool TryGetInteract(string entityId, [NotNullWhen(true)] out FieldInteract? fieldInteract);
    public bool UsePortal(GameSession session, int portalId, string password);
    public bool LiftupCube(in Vector3B coordinates, [NotNullWhen(true)] out LiftupWeapon? liftupWeapon);
    public void MovePlayerAlongPath(string pathName);

    public bool RemoveNpc(int objectId, TimeSpan removeDelay = default);
    public bool RemovePet(int objectId, TimeSpan removeDelay = default);
}
