using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Util;

internal static class HousingFunctionFurnitureRegistry {
    private static readonly HashSet<int> SmartComputerItemIds = new() {
        50400005,
        50400178,
    };

    internal enum FurnitureBehavior {
        None,
        Trap,
        InstallNpc,
        FunctionUi,
        CharmBuff,
    }

    internal sealed record FurnitureDefinition(
        int ItemId,
        FurnitureBehavior Behavior,
        int BuffId = 0,
        short BuffLevel = 1,
        int NpcId = 0,
        float TriggerRadius = 150f,
        int AutoStateChangeTime = 2500,
        string DebugName = ""
    );

    private static readonly IReadOnlyDictionary<int, FurnitureDefinition> Definitions = new Dictionary<int, FurnitureDefinition> {
        [50300001] = new(50300001, FurnitureBehavior.Trap, BuffId: 40011011, TriggerRadius: 180f, DebugName: "ca_skillobj_trap_A01_"),
        [50300002] = new(50300002, FurnitureBehavior.Trap, BuffId: 40501014, TriggerRadius: 180f, DebugName: "ho_skillobj_trap_A01_"),
        [50300003] = new(50300003, FurnitureBehavior.Trap, BuffId: 50000087, TriggerRadius: 180f, DebugName: "lu_skillobj_trap_A01_"),
        [50300004] = new(50300004, FurnitureBehavior.Trap, BuffId: 40011011, TriggerRadius: 180f, DebugName: "co_skillobj_Confusion_A01_"),
        [50300005] = new(50300005, FurnitureBehavior.Trap, BuffId: 40501014, TriggerRadius: 180f, DebugName: "co_skillobj_Frozen_A01_"),
        [50300006] = new(50300006, FurnitureBehavior.Trap, BuffId: 50000087, TriggerRadius: 180f, DebugName: "co_skillobj_Blind_A01_"),
        [50300007] = new(50300007, FurnitureBehavior.Trap, BuffId: 10000031, BuffLevel: 13, TriggerRadius: 220f, AutoStateChangeTime: 1800, DebugName: "co_skillobj_electricfan_A01_"),
        [50300008] = new(50300008, FurnitureBehavior.Trap, BuffId: 10000031, BuffLevel: 13, TriggerRadius: 220f, AutoStateChangeTime: 1800, DebugName: "co_skillobj_octopus_A01_"),
        [50300009] = new(50300009, FurnitureBehavior.Trap, BuffId: 10000031, BuffLevel: 13, TriggerRadius: 220f, AutoStateChangeTime: 1800, DebugName: "co_skillobj_octopus_B01_"),
        [50300010] = new(50300010, FurnitureBehavior.Trap, BuffId: 10000031, BuffLevel: 13, TriggerRadius: 220f, AutoStateChangeTime: 1800, DebugName: "co_skillobj_crepper_B03"),
        [50300014] = new(50300014, FurnitureBehavior.InstallNpc, NpcId: 52000005, AutoStateChangeTime: 1500, DebugName: "51000001_DamageMeter_puppet_A01_"),
        [50300015] = new(50300015, FurnitureBehavior.InstallNpc, NpcId: 52000006, AutoStateChangeTime: 1500, DebugName: "52000006_DamageMeter_snowman_A01_"),
        [50400005] = new(50400005, FurnitureBehavior.FunctionUi, AutoStateChangeTime: 1000, DebugName: "co_functobj_pc_B01_"),
        [50400178] = new(50400178, FurnitureBehavior.FunctionUi, AutoStateChangeTime: 1000, DebugName: "co_functobj_editor_A01_"),
    };

    // Exact charm/trophy item -> buff mappings resolved from item descriptions and client additional-effect strings.
    // Items that only say “可通过动作键启动” still fall back by trophy level until their original behavior is traced.
    private static readonly IReadOnlyDictionary<int, int> CharmItemBuffOverrides = new Dictionary<int, int> {
        [50710002] = 90000143,
        [50710006] = 90000140,
        [50710007] = 90000145,
        [50710010] = 79010010,
        [50710011] = 79010011,
        [50710012] = 79070012,
        [50710018] = 79070013,
        [50750001] = 90000134,
        [50750002] = 90000139,
        [50750003] = 90000140,
        [50750004] = 90000141,
        [50750005] = 90000142,
        [50760000] = 90000215,
        [50760003] = 90000318,
        [50770000] = 90000240,
        [50770001] = 90000241,
        [50770002] = 90000242,
        [50770003] = 90000243,
        [50770004] = 90000252,
        [50770007] = 90000277,
        [50770008] = 90000278,
        [50770009] = 79070009,
        [50770010] = 90000740,
    };

    // Fallback pools intentionally use only 60-minute trophy-style additional effects.
    // This avoids the old issue where charm furniture incorrectly granted 15-minute food/event buffs.
    private static readonly int[] LowTierCharmBuffs = new[] {
        90000138, 90000143, 90000145, 90000177, 90000240, 90000241,
    };

    private static readonly int[] MidTierCharmBuffs = new[] {
        79070009, 79070012, 79070013, 90000146, 90000252, 90000318,
    };

    private static readonly int[] HighTierCharmBuffs = new[] {
        79010010, 79010011, 90000168, 90000169, 90000170, 90000740,
    };

    private static readonly int[] TopTierCharmBuffs = new[] {
        79010010, 79010011, 90000740,
    };

    private static readonly int[] CharmBuffIds = CharmItemBuffOverrides.Values
        .Concat(LowTierCharmBuffs)
        .Concat(MidTierCharmBuffs)
        .Concat(HighTierCharmBuffs)
        .Concat(TopTierCharmBuffs)
        .Distinct()
        .ToArray();

    private static readonly HashSet<int> CharmBuffIdSet = new(CharmBuffIds);

    private static readonly ConcurrentDictionary<string, long> TrapCooldowns = new();

    public static bool TryGetDefinition(ItemMetadata itemMetadata, out FurnitureDefinition definition) {
        if (Definitions.TryGetValue(itemMetadata.Id, out FurnitureDefinition? found)) {
            definition = found;
            return true;
        }

        if (itemMetadata.Housing is { TrophyId: > 0 } housing) {
            definition = new FurnitureDefinition(
                itemMetadata.Id,
                FurnitureBehavior.CharmBuff,
                BuffId: ResolveCharmBuffId(itemMetadata.Id, housing.TrophyId, housing.TrophyLevel),
                DebugName: $"housing_trophy_{housing.TrophyId}_{housing.TrophyLevel}");
            return true;
        }

        definition = default!;
        return false;
    }

    public static FunctionCubeMetadata? Resolve(ItemMetadata itemMetadata, FunctionCubeMetadataStorage storage) {
        ItemMetadataInstall? install = itemMetadata.Install;
        if (install is null) {
            return null;
        }

        if (TryGetDefinition(itemMetadata, out FurnitureDefinition definition)) {
            return CreateSyntheticMetadata(install, definition);
        }

        return storage.TryGet(install.ObjectCubeId, out FunctionCubeMetadata? existing) ? existing : null;
    }

    public static bool EnsureInteract(PlotCube cube, FunctionCubeMetadataStorage storage) {
        FunctionCubeMetadata? metadata = Resolve(cube.Metadata, storage);
        if (metadata is null) {
            return false;
        }

        if (cube.Interact is null || cube.Interact.Metadata.ControlType != metadata.ControlType || cube.Interact.Metadata.Id != metadata.Id) {
            CubePortalSettings? portalSettings = cube.Interact?.PortalSettings;
            CubeNoticeSettings? noticeSettings = cube.Interact?.NoticeSettings;
            cube.Interact = new InteractCube(cube.Position, metadata) {
                PortalSettings = portalSettings ?? cube.Interact?.PortalSettings,
                NoticeSettings = noticeSettings ?? cube.Interact?.NoticeSettings,
            };
        }

        return true;
    }

    public static void Materialize(FieldManager field, PlotCube cube) {
        if (cube.Interact is null || !TryGetDefinition(cube.Metadata, out FurnitureDefinition definition)) {
            return;
        }

        switch (definition.Behavior) {
            case FurnitureBehavior.Trap:
                EnsureTrapVisual(field, cube, definition);
                break;
            case FurnitureBehavior.InstallNpc:
                EnsureNpcSpawned(field, cube, definition);
                break;
            case FurnitureBehavior.FunctionUi:
                EnsureConfigurableNotice(cube);
                EnsureSmartComputerTemplate(cube);
                TryInstallSavedComputerTrigger(field, cube);
                break;
            case FurnitureBehavior.CharmBuff:
                EnsureConfigurableNotice(cube);
                break;
        }
    }

    public static void Cleanup(FieldManager field, PlotCube cube) {
        if (cube.Interact is null) {
            return;
        }

        if (cube.Interact.SpawnedNpcObjectId != 0) {
            field.RemoveNpc(cube.Interact.SpawnedNpcObjectId);
            cube.Interact.SpawnedNpcObjectId = 0;
        }

        string visualEntityId = GetTrapVisualEntityId(cube);
        if (field.TryGetInteract(visualEntityId, out FieldInteract? visualInteract)) {
            field.RemoveInteract(visualInteract.Object);
        }
    }

    public static bool TryHandleUse(GameSession session, PlotCube cube, FieldFunctionInteract fieldInteract) {
        if (session.Field is null || cube.Interact is null || !TryGetDefinition(cube.Metadata, out FurnitureDefinition definition)) {
            return false;
        }

        switch (definition.Behavior) {
            case FurnitureBehavior.CharmBuff:
                return HandleCharmBuff(session, cube, fieldInteract, definition);
            case FurnitureBehavior.FunctionUi:
                return HandleFunctionUi(session, cube, fieldInteract);
            default:
                return false;
        }
    }

    public static bool TryTriggerTrap(GameSession session, PlotCube cube) {
        if (session.Field is null || !TryGetDefinition(cube.Metadata, out FurnitureDefinition definition) || definition.Behavior is not FurnitureBehavior.Trap) {
            return false;
        }

        Vector3 cubeWorldPosition = cube.Position;
        Vector2 playerPosition = new(session.Player.Position.X, session.Player.Position.Y);
        Vector2 cubePosition = new(cubeWorldPosition.X, cubeWorldPosition.Y);
        float distance = Vector2.Distance(playerPosition, cubePosition);
        float zDistance = MathF.Abs(session.Player.Position.Z - cubeWorldPosition.Z);
        if (distance > definition.TriggerRadius || zDistance > 180f) {
            return false;
        }

        string cooldownKey = $"{session.CharacterId}:{cube.Id}";
        long now = session.Field.FieldTick;
        if (TrapCooldowns.TryGetValue(cooldownKey, out long nextAllowedTick) && now < nextAllowedTick) {
            return false;
        }
        TrapCooldowns[cooldownKey] = now + Math.Max(definition.AutoStateChangeTime, 2500);

        bool hasRuntime = EnsureRuntimeInteract(session.Field, cube, out _);
        if (hasRuntime && cube.Interact is not null) {
            cube.Interact.State = InteractCubeState.InUse;
            cube.Interact.InteractingCharacterId = session.CharacterId;
            session.Field.Broadcast(FunctionCubePacket.UpdateFunctionCube(cube.Interact));
            session.Field.Broadcast(FunctionCubePacket.UseFurniture(session.CharacterId, cube.Interact));
        }

        if (definition.BuffId > 0) {
            session.Player.Buffs.AddBuff(session.Player, session.Player, definition.BuffId, definition.BuffLevel, session.Field.FieldTick, notifyField: true);
        }

        return true;
    }

    internal static bool IsSmartComputer(PlotCube cube) => SmartComputerItemIds.Contains(cube.ItemId);

    internal static bool IsHousingCharmBuff(int buffId) => CharmBuffIdSet.Contains(buffId);

    internal static bool OpenSmartComputerEditor(GameSession session, PlotCube cube) {
        if (session.Field is null || !IsSmartComputer(cube)) {
            return false;
        }

        EnsureInteract(cube, session.FunctionCubeMetadata);
        EnsureConfigurableNotice(cube);
        EnsureSmartComputerTemplate(cube);

        if (cube.Interact?.NoticeSettings is null) {
            return false;
        }

        cube.Interact.State = InteractCubeState.InUse;
        cube.Interact.InteractingCharacterId = session.CharacterId;
        session.Field.Broadcast(FunctionCubePacket.UpdateFunctionCube(cube.Interact));
        session.Field.Broadcast(FunctionCubePacket.UseFurniture(session.CharacterId, cube.Interact));

        session.EditingSmartComputerCubeId = cube.Id;
        cube.Interact.NoticeSettings.Notice = GetSmartComputerScript(session);
        cube.Interact.NoticeSettings.Distance = 1;

        session.Send(HomeActionPacket.SendCubeNoticeSettings(cube, editing: true));
        return true;
    }

    internal static string ApplyComputerScript(GameSession session, PlotCube cube) {
        if (!IsSmartComputer(cube)) {
            return "已保存。";
        }

        if (session.Field is not HomeFieldManager homeField || homeField.OwnerId != session.AccountId) {
            return "只有房主可以保存智能电脑配置。";
        }

        if (cube.Interact?.NoticeSettings is null) {
            return "智能电脑未初始化配置区。";
        }

        Plot? plot = session.Housing.GetFieldPlot();
        if (plot is null || session.Field is null) {
            return "当前不在有效的房屋场景中。";
        }

        string script = cube.Interact.NoticeSettings.Notice ?? string.Empty;
        if (string.IsNullOrWhiteSpace(script)) {
            return "智能电脑已清空配置。";
        }

        int changedStates = 0;
        int materialized = 0;
        int portalUpdates = 0;
        int noticeUpdates = 0;
        int variables = 0;
        int errors = 0;

        foreach (string rawLine in script.Replace("\r", string.Empty).Split("\n")) {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith("//")) {
                continue;
            }

            List<string> tokens = Tokenize(line);
            if (tokens.Count == 0) {
                continue;
            }

            string command = tokens[0].ToLowerInvariant();
            switch (command) {
                case "toggle":
                    if (TryParseCoord(tokens, 1, out Vector3B toggleCoord) && TryGetCube(plot, toggleCoord, out PlotCube? toggleCube) &&
                        TryToggleCube(session, toggleCube)) {
                        changedStates++;
                    } else {
                        errors++;
                    }
                    break;
                case "state":
                    if (TryParseCoord(tokens, 1, out Vector3B stateCoord) &&
                        TryGetCube(plot, stateCoord, out PlotCube? stateCube) &&
                        tokens.Count >= 5 &&
                        TryParseCubeState(tokens[4], out InteractCubeState state) &&
                        TrySetCubeState(session, stateCube, state)) {
                        changedStates++;
                    } else {
                        errors++;
                    }
                    break;
                case "itemstate":
                    if (tokens.Count >= 3 &&
                        int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stateItemId) &&
                        TryParseCubeState(tokens[2], out InteractCubeState itemState)) {
                        foreach (PlotCube target in plot.Cubes.Values.Where(x => x.ItemId == stateItemId)) {
                            if (TrySetCubeState(session, target, itemState)) {
                                changedStates++;
                            }
                        }
                    } else {
                        errors++;
                    }
                    break;
                case "respawn":
                    if (TryParseCoord(tokens, 1, out Vector3B spawnCoord) && TryGetCube(plot, spawnCoord, out PlotCube? spawnCube) &&
                        TryRespawnCube(session, spawnCube)) {
                        materialized++;
                    } else {
                        errors++;
                    }
                    break;
                case "despawn":
                    if (TryParseCoord(tokens, 1, out Vector3B despawnCoord) && TryGetCube(plot, despawnCoord, out PlotCube? despawnCube) &&
                        TryDespawnCube(session, despawnCube)) {
                        materialized++;
                    } else {
                        errors++;
                    }
                    break;
                case "portal":
                    if (tokens.Count >= 8 &&
                        TryParseCoord(tokens, 1, out Vector3B portalCoord) &&
                        TryGetCube(plot, portalCoord, out PlotCube? portalCube) &&
                        ApplyPortalCommand(session, plot, portalCube, tokens[4], tokens[5], tokens[6], string.Join(' ', tokens.Skip(7)))) {
                        portalUpdates++;
                    } else {
                        errors++;
                    }
                    break;
                case "note":
                    if (tokens.Count >= 6 &&
                        TryParseCoord(tokens, 1, out Vector3B noteCoord) &&
                        TryGetCube(plot, noteCoord, out PlotCube? noteCube) &&
                        byte.TryParse(tokens[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte noticeDistance) &&
                        ApplyNoticeCommand(session, noteCube, noticeDistance, string.Join(' ', tokens.Skip(5)))) {
                        noticeUpdates++;
                    } else {
                        errors++;
                    }
                    break;
                case "var":
                    variables++;
                    break;
                default:
                    errors++;
                    break;
            }
        }

        session.Housing.SaveHome();
        return $"电脑已保存：开关 {changedStates}，实体 {materialized}，传送 {portalUpdates}，文字 {noticeUpdates}，变量 {variables}，错误 {errors}";
    }

    private static bool HandleCharmBuff(GameSession session, PlotCube cube, FieldFunctionInteract fieldInteract, FurnitureDefinition definition) {
        if (cube.Interact is null) {
            return false;
        }

        fieldInteract.Use();

        foreach (int buffId in CharmBuffIds) {
            session.Player.Buffs.Remove(buffId, session.Player.ObjectId);
        }

        if (definition.BuffId > 0) {
            session.Player.Buffs.AddBuff(session.Player, session.Player, definition.BuffId, definition.BuffLevel, session.Field!.FieldTick, notifyField: true);
        }

        cube.Interact.State = InteractCubeState.InUse;
        cube.Interact.InteractingCharacterId = session.CharacterId;
        session.Field!.Broadcast(FunctionCubePacket.UpdateFunctionCube(cube.Interact));
        session.Field.Broadcast(FunctionCubePacket.UseFurniture(session.CharacterId, cube.Interact));
        return true;
    }

    private static bool HandleFunctionUi(GameSession session, PlotCube cube, FieldFunctionInteract fieldInteract) {
        if (cube.Interact is null || session.Field is null) {
            return false;
        }

        EnsureConfigurableNotice(cube);
        cube.Interact.State = InteractCubeState.InUse;
        cube.Interact.InteractingCharacterId = session.CharacterId;
        session.Field.Broadcast(FunctionCubePacket.UpdateFunctionCube(cube.Interact));
        session.Field.Broadcast(FunctionCubePacket.UseFurniture(session.CharacterId, cube.Interact));

        if (IsSmartComputer(cube)) {
            return OpenSmartComputerEditor(session, cube);
        }

        bool ownerEditing = session.Field is HomeFieldManager homeField && homeField.OwnerId == session.AccountId;
        session.Send(HomeActionPacket.SendCubeNoticeSettings(cube, editing: ownerEditing));
        return true;
    }

    private static bool EnsureRuntimeInteract(FieldManager field, PlotCube cube, out FieldFunctionInteract? fieldInteract) {
        EnsureInteract(cube, field.FunctionCubeMetadata);
        fieldInteract = cube.Interact is null ? null : field.TryGetFieldFunctionInteract(cube.Interact.Id);
        if (fieldInteract is not null) {
            return true;
        }

        fieldInteract = field.AddFieldFunctionInteract(cube);
        return fieldInteract is not null;
    }

    private static void EnsureConfigurableNotice(PlotCube cube) {
        if (cube.Interact is null || cube.Interact.NoticeSettings is not null) {
            return;
        }

        cube.Interact.NoticeSettings = new CubeNoticeSettings();
    }

    private const string DefaultSmartComputerScript =
        "# 简易智能电脑脚本\n" +
        "# 每行一个命令，保存后立即应用\n" +
        "# toggle x y z\n" +
        "# state x y z available|inuse|none\n" +
        "# respawn x y z\n" +
        "# despawn x y z\n" +
        "# portal x y z 传送门名 move 目标\n" +
        "# note x y z 1 文本内容\n";

    private static void EnsureSmartComputerTemplate(PlotCube cube) {
        if (!IsSmartComputer(cube) || cube.Interact?.NoticeSettings is null || !string.IsNullOrWhiteSpace(cube.Interact.NoticeSettings.Notice)) {
            return;
        }

        cube.Interact.NoticeSettings.Notice = DefaultSmartComputerScript;
        cube.Interact.NoticeSettings.Distance = 1;
    }

    internal static bool TrySaveSmartComputerScript(GameSession session, string xml, out string message) {
        message = string.Empty;
        if (session.Field is null || session.Housing.GetFieldPlot() is not Plot plot) {
            message = "当前不在房屋场景中。";
            return false;
        }

        PlotCube? cube = plot.Cubes.Values.FirstOrDefault(x => x.Id == session.EditingSmartComputerCubeId && IsSmartComputer(x));
        if (cube is null) {
            message = "未找到当前正在编辑的智能电脑。";
            return false;
        }

        EnsureInteract(cube, session.FunctionCubeMetadata);
        EnsureConfigurableNotice(cube);
        cube.Interact!.NoticeSettings!.Notice = SanitizeSmartComputerScript(xml);
        cube.Interact.NoticeSettings.Distance = 1;

        session.Housing.SaveHome();
        message = ApplyComputerScript(session, cube);
        return true;
    }

    internal static string GetSmartComputerScript(GameSession session) {
        if (session.Housing.GetFieldPlot() is not Plot plot) {
            return DefaultSmartComputerScript;
        }

        PlotCube? cube = plot.Cubes.Values.FirstOrDefault(x => x.Id == session.EditingSmartComputerCubeId && IsSmartComputer(x));
        return SanitizeSmartComputerScript(cube?.Interact?.NoticeSettings?.Notice);
    }

    private static string SanitizeSmartComputerScript(string? script) {
        if (string.IsNullOrWhiteSpace(script)) {
            return DefaultSmartComputerScript;
        }

        return script
            .TrimStart('\ufeff')
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }

    private static string GetComputerTriggerName(PlotCube cube) => $"home_user_trigger_{cube.Id}";

    private static void TryInstallSavedComputerTrigger(FieldManager field, PlotCube cube) {
        if (!IsSmartComputer(cube) || cube.Interact?.NoticeSettings is null) {
            return;
        }

        // 简易电脑版不再尝试加载原版 TriggerTool XML。
        // 脚本在玩家保存时即时应用，并以纯文本形式保存在 NoticeSettings 中。
    }

    private static bool ShouldSpawnTrapVisual(FurnitureDefinition definition) => definition.ItemId is 50300007 or 50300008 or 50300009 or 50300010;

    private static string GetTrapVisualEntityId(PlotCube cube) => $"housing_funcvis_{cube.Id}";

    private static void EnsureTrapVisual(FieldManager field, PlotCube cube, FurnitureDefinition definition) {
        if (!ShouldSpawnTrapVisual(definition)) {
            return;
        }

        string entityId = GetTrapVisualEntityId(cube);
        if (field.TryGetInteract(entityId, out _)) {
            return;
        }

        Vector3 position = cube.Position;
        Vector3 rotation = new(0, 0, cube.Rotation);
        const int interactId = 99003007;
        var mesh = new Ms2InteractMesh(interactId, position, rotation);
        var meshObject = new InteractMeshObject(entityId, mesh) {
            Model = "InteractMeshObject",
            Asset = definition.DebugName,
            NormalState = "Idle_A",
            Reactable = "Idle_A",
            Scale = 1f,
        };

        var metadata = new InteractObjectMetadata(
            Id: interactId,
            Type: InteractType.Mesh,
            Collection: 0,
            ReactCount: 0,
            TargetPortalId: 0,
            GuildPosterId: 0,
            WeaponItemId: 0,
            Item: new InteractObjectMetadataItem(0, 0, 0, 0, 0),
            Time: new InteractObjectMetadataTime(0, 0, 0),
            Drop: new InteractObjectMetadataDrop(0, Array.Empty<int>(), Array.Empty<int>(), 0, 0),
            AdditionalEffect: new InteractObjectMetadataEffect(
                Array.Empty<InteractObjectMetadataEffect.ConditionEffect>(),
                Array.Empty<InteractObjectMetadataEffect.InvokeEffect>(),
                0,
                0),
            Spawn: Array.Empty<InteractObjectMetadataSpawn>());

        FieldInteract? fieldInteract = field.AddInteract(mesh, meshObject, metadata);
        if (fieldInteract is null) {
            return;
        }

        field.Broadcast(InteractObjectPacket.Add(fieldInteract.Object));
    }

    private static bool EnsureNpcSpawned(FieldManager field, PlotCube cube, FurnitureDefinition definition) {
        if (cube.Interact is null || definition.NpcId <= 0) {
            return false;
        }

        if (cube.Interact.SpawnedNpcObjectId != 0 && (field.Npcs.ContainsKey(cube.Interact.SpawnedNpcObjectId) || field.Mobs.ContainsKey(cube.Interact.SpawnedNpcObjectId))) {
            return true;
        }

        cube.Interact.SpawnedNpcObjectId = 0;
        if (!field.NpcMetadata.TryGet(definition.NpcId, out NpcMetadata? npcMetadata)) {
            return false;
        }

        Vector3 spawnPosition = cube.Position + RotateOffset(cube.Rotation, new Vector3(0, 60, 0));
        Vector3 spawnRotation = new(0, 0, cube.Rotation);
        FieldNpc? fieldNpc = field.SpawnNpc(npcMetadata, spawnPosition, spawnRotation, disableAi: true);
        if (fieldNpc is null) {
            return false;
        }

        cube.Interact.SpawnedNpcObjectId = fieldNpc.ObjectId;
        field.Broadcast(FieldPacket.AddNpc(fieldNpc));
        field.Broadcast(ProxyObjectPacket.AddNpc(fieldNpc));
        return true;
    }

    private static bool TryToggleCube(GameSession session, PlotCube cube) {
        if (session.Field is null) {
            return false;
        }

        EnsureInteract(cube, session.Field.FunctionCubeMetadata);
        if (cube.Interact is null) {
            return false;
        }

        InteractCubeState nextState = cube.Interact.State is InteractCubeState.InUse ? InteractCubeState.Available : InteractCubeState.InUse;
        return TrySetCubeState(session, cube, nextState);
    }

    private static bool TrySetCubeState(GameSession session, PlotCube cube, InteractCubeState state) {
        if (session.Field is null) {
            return false;
        }

        EnsureInteract(cube, session.Field.FunctionCubeMetadata);
        if (cube.Interact is null) {
            return false;
        }

        EnsureRuntimeInteract(session.Field, cube, out _);

        cube.Interact.State = state;
        cube.Interact.InteractingCharacterId = state is InteractCubeState.InUse ? session.CharacterId : 0;
        session.Field.Broadcast(FunctionCubePacket.UpdateFunctionCube(cube.Interact));

        if (TryGetDefinition(cube.Metadata, out FurnitureDefinition definition) && definition.Behavior is FurnitureBehavior.InstallNpc) {
            if (state is InteractCubeState.None) {
                Cleanup(session.Field, cube);
            } else {
                EnsureNpcSpawned(session.Field, cube, definition);
            }
        }

        return true;
    }

    private static bool TryRespawnCube(GameSession session, PlotCube cube) {
        if (session.Field is null) {
            return false;
        }

        EnsureInteract(cube, session.Field.FunctionCubeMetadata);
        if (!EnsureRuntimeInteract(session.Field, cube, out _)) {
            return false;
        }

        if (TryGetDefinition(cube.Metadata, out FurnitureDefinition definition) && definition.Behavior is FurnitureBehavior.InstallNpc) {
            Cleanup(session.Field, cube);
            return EnsureNpcSpawned(session.Field, cube, definition);
        }

        session.Field.Broadcast(FunctionCubePacket.UpdateFunctionCube(cube.Interact!));
        return true;
    }

    private static bool TryDespawnCube(GameSession session, PlotCube cube) {
        if (session.Field is null) {
            return false;
        }

        EnsureInteract(cube, session.Field.FunctionCubeMetadata);
        if (cube.Interact is null) {
            return false;
        }

        Cleanup(session.Field, cube);
        cube.Interact.State = InteractCubeState.None;
        cube.Interact.InteractingCharacterId = 0;
        session.Field.Broadcast(FunctionCubePacket.UpdateFunctionCube(cube.Interact));
        return true;
    }

    private static bool ApplyPortalCommand(GameSession session, Plot plot, PlotCube cube, string portalName, string methodToken, string destinationToken, string destinationTarget) {
        if (session.Field is null) {
            return false;
        }

        EnsureInteract(cube, session.Field.FunctionCubeMetadata);
        if (cube.Interact is null) {
            return false;
        }

        cube.Interact.PortalSettings ??= new CubePortalSettings(cube.Position);
        cube.Interact.PortalSettings.PortalName = portalName;
        cube.Interact.PortalSettings.Method = ParsePortalMethod(methodToken);
        cube.Interact.PortalSettings.Destination = ParsePortalDestination(destinationToken);
        cube.Interact.PortalSettings.DestinationTarget = destinationTarget;

        foreach (FieldPortal portal in session.Field.GetPortals()) {
            session.Field.RemovePortal(portal.ObjectId);
        }

        List<PlotCube> cubePortals = plot.Cubes.Values
            .Where(x => x.ItemId is Constant.InteriorPortalCubeId && x.Interact?.PortalSettings is not null)
            .ToList();

        foreach (PlotCube cubePortal in cubePortals) {
            FieldPortal? fieldPortal = session.Field.SpawnCubePortal(cubePortal);
            if (fieldPortal is null) {
                continue;
            }

            session.Field.Broadcast(PortalPacket.Add(fieldPortal));
        }

        return true;
    }

    private static bool ApplyNoticeCommand(GameSession session, PlotCube cube, byte distance, string text) {
        if (session.Field is null) {
            return false;
        }

        EnsureInteract(cube, session.Field.FunctionCubeMetadata);
        if (cube.Interact is null) {
            return false;
        }

        EnsureConfigurableNotice(cube);
        cube.Interact.NoticeSettings!.Distance = distance;
        cube.Interact.NoticeSettings.Notice = text;
        session.Field.Broadcast(HomeActionPacket.SendCubeNoticeSettings(cube, editing: false));
        return true;
    }

    private static bool TryGetCube(Plot plot, Vector3B coord, out PlotCube? cube) =>
        plot.Cubes.TryGetValue(coord, out cube);

    private static bool TryParseCoord(IReadOnlyList<string> tokens, int startIndex, out Vector3B coord) {
        coord = default;
        if (tokens.Count <= startIndex + 2) {
            return false;
        }

        if (!int.TryParse(tokens[startIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
            !int.TryParse(tokens[startIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) ||
            !int.TryParse(tokens[startIndex + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z)) {
            return false;
        }

        coord = new Vector3B(x, y, z);
        return true;
    }

    private static bool TryParseCubeState(string token, out InteractCubeState state) {
        switch (token.Trim().ToLowerInvariant()) {
            case "none":
            case "0":
                state = InteractCubeState.None;
                return true;
            case "available":
            case "on":
            case "1":
                state = InteractCubeState.Available;
                return true;
            case "inuse":
            case "active":
            case "off":
            case "2":
                state = InteractCubeState.InUse;
                return true;
            default:
                state = default;
                return false;
        }
    }

    private static PortalActionType ParsePortalMethod(string token) {
        if (byte.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte value) && Enum.IsDefined(typeof(PortalActionType), (int) value)) {
            return (PortalActionType) value;
        }

        return token.Trim().ToLowerInvariant() switch {
            "touch" => PortalActionType.Touch,
            _ => PortalActionType.Interact,
        };
    }

    private static CubePortalDestination ParsePortalDestination(string token) {
        if (byte.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte value) && Enum.IsDefined(typeof(CubePortalDestination), value)) {
            return (CubePortalDestination) value;
        }

        return token.Trim().ToLowerInvariant() switch {
            "home" or "portalinhome" => CubePortalDestination.PortalInHome,
            "map" or "selectedmap" => CubePortalDestination.SelectedMap,
            "friend" or "friendhome" => CubePortalDestination.FriendHome,
            _ => CubePortalDestination.PortalInHome,
        };
    }

    private static List<string> Tokenize(string line) {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        foreach (char ch in line) {
            if (ch == '"') {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes) {
                if (sb.Length > 0) {
                    tokens.Add(sb.ToString());
                    sb.Clear();
                }
                continue;
            }

            sb.Append(ch);
        }

        if (sb.Length > 0) {
            tokens.Add(sb.ToString());
        }

        return tokens;
    }

    private static FunctionCubeMetadata CreateSyntheticMetadata(ItemMetadataInstall install, FurnitureDefinition definition) {
        InteractCubeControlType controlType = definition.Behavior switch {
            FurnitureBehavior.Trap => InteractCubeControlType.Skill,
            FurnitureBehavior.InstallNpc => InteractCubeControlType.InstallNPC,
            FurnitureBehavior.FunctionUi => InteractCubeControlType.FunctionUI,
            FurnitureBehavior.CharmBuff => InteractCubeControlType.FunctionUI,
            _ => InteractCubeControlType.None,
        };

        int recipeId = definition.Behavior switch {
            FurnitureBehavior.Trap => definition.BuffId,
            FurnitureBehavior.InstallNpc => definition.NpcId,
            FurnitureBehavior.CharmBuff => definition.BuffId,
            _ => 0,
        };

        ConfigurableCubeType configurableCubeType = definition.Behavior switch {
            FurnitureBehavior.FunctionUi => ConfigurableCubeType.UGCNotice,
            FurnitureBehavior.CharmBuff => ConfigurableCubeType.UGCNotice,
            _ => ConfigurableCubeType.None,
        };

        return new FunctionCubeMetadata(
            Id: install.ObjectCubeId != 0 ? install.ObjectCubeId : definition.ItemId,
            RecipeId: recipeId,
            ConfigurableCubeType: configurableCubeType,
            DefaultState: InteractCubeState.Available,
            ControlType: controlType,
            AutoStateChange: new[] { (int) InteractCubeState.Available },
            AutoStateChangeTime: definition.AutoStateChangeTime,
            Nurturing: null
        );
    }

    private static int ResolveCharmBuffId(int itemId, int trophyId, int trophyLevel) {
        if (CharmItemBuffOverrides.TryGetValue(itemId, out int exactBuffId)) {
            return exactBuffId;
        }

        int[] pool = trophyLevel switch {
            <= 3 => LowTierCharmBuffs,
            <= 6 => MidTierCharmBuffs,
            <= 8 => HighTierCharmBuffs,
            _ => TopTierCharmBuffs,
        };

        int index = Math.Abs((itemId * 31) ^ trophyId) % pool.Length;
        return pool[index];
    }

    private static Vector3 RotateOffset(float rotationDegrees, Vector3 offset) {
        float radians = MathF.PI * rotationDegrees / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        return new Vector3(
            offset.X * cos - offset.Y * sin,
            offset.X * sin + offset.Y * cos,
            offset.Z
        );
    }
}
