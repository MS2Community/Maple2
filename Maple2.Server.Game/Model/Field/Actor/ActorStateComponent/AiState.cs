using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Packets;
using Maple2.Tools;
using Serilog;
using static Maple2.Model.Metadata.AiMetadata;
using static Maple2.Server.Game.Model.ActorStateComponent.TaskState;
using System;

namespace Maple2.Server.Game.Model.ActorStateComponent;

public class AiState {
    protected readonly ILogger Logger = Log.ForContext<AiState>();
    private readonly FieldNpc actor;
    public AiMetadata? AiMetadata { get; private set; }
    private Node? battle;
    private Node? battleEnd;

    private List<StackEntry> aiStack = new List<StackEntry>();
    private NpcTask? currentTask = null;
    private long currentLimitTick = 0;

    private AiMetadata? lastEvaluated;
    private DecisionTreeType currentTree;

    private Dictionary<string, long> coolTimes = new();
    private List<Node> reservedNodes = new List<Node>();

    private enum DecisionTreeType {
        None,
        Battle,
        BattleEnd,
    }

    public AiState(FieldNpc actor, string aiPath) {
        this.actor = actor;
        if (string.IsNullOrEmpty(aiPath)) {
            return;
        }

        if (aiPath == "AI_DefaultNew.xml") {
            aiPath = Constant.DefaultAiPath;
        }

        if (!actor.Field.AiMetadata.TryGet(aiPath, out AiMetadata? metadata)) {
            Logger.Error("{AiPath} could not be found for {ValueId}. setting as default AI", aiPath, actor.Value.Id);
            if (!actor.Field.AiMetadata.TryGet(Constant.DefaultAiPath, out metadata)) {
                throw new KeyNotFoundException($"Could not find default AI {Constant.DefaultAiPath} for {actor.Value.Id}");
            }
        }

        AiMetadata = metadata;

        if (AiMetadata.Battle.Length != 0) {
            battle = new Node("battle", AiMetadata.Battle);
        }

        if (AiMetadata.BattleEnd.Length != 0) {
            battleEnd = new Node("battle", AiMetadata.BattleEnd);
        }

        if (AiMetadata.Reserved.Length != 0) {
            reservedNodes = AiMetadata.Reserved.Cast<Node>().ToList();
        }
    }

    public bool IsProcessingNodes() {
        return aiStack.Count > 0;
    }

    public bool IsWaitingOnTask(long tickCount) {
        if (currentTask is null) {
            return false;
        }

        if (currentLimitTick != 0 && tickCount >= currentLimitTick) {
            return false;
        }

        return !currentTask.IsDone;
    }

    public void Update(long tickCount) {
        if (IsWaitingOnTask(tickCount)) {
            return;
        }

        currentTask?.Cancel();

        currentTask = null;
        currentLimitTick = 0;

        if (actor.IsDead) {
            return;
        }

        if (AiMetadata is null) {
            if (actor.Value.Metadata.AiPath != "") {
                actor.AppendDebugMessage("Missing AI\n");
                actor.AppendDebugMessage(actor.Value.Metadata.AiPath + "\n");
            }

            aiStack.Clear();
            currentTree = DecisionTreeType.None;

            return;
        }

        bool isInBattle = actor.BattleState.InBattle;
        if (!isInBattle) {
            actor.AiExtraData["__battle_start_tick"] = 0;

            bool suppressBattleEnd = actor.AiExtraData.GetValueOrDefault("__suppress_battle_end", 0) != 0;
            long currentHp = actor.Stats.Values[BasicAttribute.Health].Current;
            bool isActuallyDead = currentHp <= 0;

            if (currentTree == DecisionTreeType.Battle) {
                aiStack.Clear();

                if (suppressBattleEnd) {
                    actor.AiExtraData["__suppress_battle_end"] = 0;
                    currentTree = DecisionTreeType.None;
                } else {
                    currentTree = isActuallyDead && battleEnd is not null
                        ? DecisionTreeType.BattleEnd
                        : DecisionTreeType.None;
                }
            } else if (currentTree == DecisionTreeType.BattleEnd && aiStack.Count == 0) {
                currentTree = DecisionTreeType.None;
            }

            return;
    } else if (currentTree == DecisionTreeType.BattleEnd) {
            aiStack.Clear();

            currentTree = DecisionTreeType.None;
        }

        if (isInBattle && battle is not null) {
            currentTree = DecisionTreeType.Battle;
        }

        if (lastEvaluated != AiMetadata) {
            aiStack.Clear();
        }

        lastEvaluated = AiMetadata;

        foreach (var node in reservedNodes.ToList()) {
            if (ProcessCondition((dynamic) node)) {
                aiStack.Clear();
                reservedNodes.Remove(node);
                Push(node);
            }
        }

        if (aiStack.Count == 0) {
            if (currentTree == DecisionTreeType.Battle) {
                Push(battle!);
            } else if (currentTree == DecisionTreeType.BattleEnd) {
                Push(battleEnd!);
            }
        }

        while (aiStack.Count > 0) {
            StackEntry entry = aiStack.Last();

            int index = entry.Index;
            int last = aiStack.Count - 1;

            if (index >= entry.Node.Entries.Length) {
                aiStack.RemoveAt(last);

                continue;
            }

            int nextIndex = entry.LockIndex ? entry.Node.Entries.Length : index + 1;

            aiStack[last] = new StackEntry() {
                Node = entry.Node,
                Index = nextIndex,
                LockIndex = entry.LockIndex,
            };

            Process(entry.Node.Entries[index]);

            if (IsWaitingOnTask(tickCount)) {
                break;
            }
        }
    }

    private void SetNodeTask(NpcTask? task, long limit = 0) {
        currentTask = task;
        currentLimitTick = limit == 0 ? 0 : actor.Field.FieldTick + limit;
    }

    private struct StackEntry {
        public Node Node;
        public int Index;
        public bool LockIndex;
    }

    private void Push(Node node) {
        aiStack.Add(new StackEntry() {
            Node = node,
        });
    }

    private void Push(AiPreset aiPreset) {
        actor.AppendDebugMessage($"> Preset: '{aiPreset.Name}'\n");

        AiPresetDefinition? definition = AiMetadata?.AiPresets.First(value => value.Name == aiPreset.Name);

        if (definition is null) {
            return;
        }

        Push(definition);
    }

    private SkillMetadata? TryGetSkill(int idx, short levelOverride = 0) {
        // idx starts at 1
        if (idx < 1 || idx > actor.Skills.Length) {
            actor.AppendDebugMessage($"{actor.Value.Metadata.Name}[{actor.Value.Id}]\n");
            actor.AppendDebugMessage($"{AiMetadata!.Name}\n");
            actor.AppendDebugMessage($"Invalid Skill Idx {idx}\n");

            Logger.Warning($"Missing skillIdx {idx} in {actor.Value.Metadata.Name}[{actor.Value.Id}] script '{AiMetadata!.Name}'. Xml.m2d/m2h might be out of date. Check the MS2 Hub resources!");

            return null;
        }

        SkillMetadata? skill = actor.Skills[idx - 1];

        if (skill is not null && levelOverride != 0) {
            actor.Field.SkillMetadata.TryGet(skill.Id, levelOverride, out skill);
        }

        if (skill is null) {
            actor.AppendDebugMessage($"{actor.Value.Metadata.Name}[{actor.Value.Id}]\n");
            actor.AppendDebugMessage($"{AiMetadata!.Name}\n");
            actor.AppendDebugMessage($"Missing Skill Idx {idx}\n");

            Logger.Warning($"Missing skillIdx {idx} in {actor.Value.Metadata.Name}[{actor.Value.Id}] script '{AiMetadata!.Name}'. Xml.m2d/m2h might be out of date. Check the MS2 Hub resources!");
        }

        return skill;
    }
    private NpcMetadata? ResolveSummonMetadata(SummonNode node) {
        if (node.NpcId >= 20000000 && actor.Field.NpcMetadata.TryGet(node.NpcId, out NpcMetadata? direct)) {
            return direct;
        }

        string currentAiPath = actor.Value.Metadata.AiPath ?? string.Empty;
        string normalizedCurrent = currentAiPath.Replace('\\', '/');
        int preferredDifficulty = actor.Value.Metadata.Basic.Difficulty;
        int preferredId = actor.Value.Id;

        NpcMetadata? FindPreferred(string aiPath) => actor.Field.NpcMetadata.FindByAiPath(aiPath, preferredDifficulty, preferredId);
        if (actor.Value.Id == 23200081 && node.NpcId == 3) {
    if (actor.Field.NpcMetadata.TryGet(23200082, out NpcMetadata? kanduraChaosNext)) {
        return kanduraChaosNext;
    }
}

if (actor.Value.Id == 23000081 && node.NpcId == 3) {
    if (actor.Field.NpcMetadata.TryGet(23000082, out NpcMetadata? kanduraRaidNext)) {
        return kanduraRaidNext;
    }
}
        // 超链接之树 / Kandura
        if (normalizedCurrent.Contains("BossDungeon/KanduraBigBurster", StringComparison.OrdinalIgnoreCase)) {
            bool chaos = normalizedCurrent.Contains("Chaos", StringComparison.OrdinalIgnoreCase);

            return node.NpcId switch {
                1 => FindPreferred("BossDungeon/KanduraBigBurster/AI_SoldierPhysicalBlueSummon.xml"),
                2 => FindPreferred("BossDungeon/KanduraBigBurster/AI_SoldierPhysicalRedSummon.xml"),
                3 => FindPreferred(
                    chaos
                        ? "BossDungeon/KanduraBigBurster/AI_KanduraBigBurster_Chaos.xml"
                        : "BossDungeon/KanduraBigBurster/AI_KanduraBigBurster.xml"
                ),
                4 => FindPreferred("BossDungeon/KanduraBigBurster/AI_SoldierPhysicalHandArmorSummon.xml"),
                _ => actor.Field.NpcMetadata.FindByRelativeAiAlias(currentAiPath, node.NpcId)
            };
        }

        // 月光船长要塞 / 船长默克
        if (normalizedCurrent.Contains("BossDungeon/CaptainHookFish01", StringComparison.OrdinalIgnoreCase)) {
            return node.NpcId switch {
                1 => FindPreferred("BossDungeon/CaptainHookFish01/AI_MermanSmallBlue.xml"),
                2 => FindPreferred("BossDungeon/CaptainHookFish01/AI_MermanFatBlue.xml"),
                3 => FindPreferred("BossDungeon/CaptainHookFish01/AI_PirateSkullCannonSummonLeft.xml")
                     ?? FindPreferred("BossDungeon/CaptainHookFish01/AI_PirateSkullCannonSummon.xml"),
                4 => FindPreferred("BossDungeon/CaptainHookFish01/AI_PirateSkullDaggerSummon.xml"),
                5 => FindPreferred("BossDungeon/CaptainHookFish01/AI_PirateSkullCannonSummonRight.xml")
                     ?? FindPreferred("BossDungeon/CaptainHookFish01/AI_PirateSkullVikingSickleSummon.xml"),
                _ => actor.Field.NpcMetadata.FindByRelativeAiAlias(currentAiPath, node.NpcId)
            };
        }

        // 路贝里斯克 / BarkhantBlue
        if (normalizedCurrent.Contains("BossDungeon/BarkhantBlue", StringComparison.OrdinalIgnoreCase)) {
            bool chaos = normalizedCurrent.Contains("/Chaos/", StringComparison.OrdinalIgnoreCase);
            return node.NpcId switch {
                1 => FindPreferred(chaos ? "BossDungeon/BarkhantBlue/Chaos/AI_KnightHollowArmorPurple_ThrowWheel.xml" : "BossDungeon/BarkhantBlue/AI_KnightHollowArmorPurple_ThrowWheel_TypeA.xml"),
                2 => FindPreferred(chaos ? "BossDungeon/BarkhantBlue/Chaos/AI_CerberosTallPurple.xml" : "BossDungeon/BarkhantBlue/AI_CerberosTallPurple_TypeA.xml"),
                3 => FindPreferred(chaos ? "BossDungeon/BarkhantBlue/Chaos/AI_CrowDevilWhite_Close.xml" : "BossDungeon/BarkhantBlue/AI_CrowDevilWhite_TypeA.xml"),
                4 => FindPreferred(chaos ? "BossDungeon/BarkhantBlue/Chaos/AI_CerberosTallPurple.xml" : "BossDungeon/BarkhantBlue/AI_CerberosTallPurple_TypeB.xml"),
                5 => FindPreferred(chaos ? "BossDungeon/BarkhantBlue/Chaos/AI_DragonDevilBigHeadBlue.xml" : "BossDungeon/BarkhantBlue/AI_DragonDevilBigHeadBlueSummon_TypeA.xml"),
                6 => FindPreferred(chaos ? "BossDungeon/BarkhantBlue/Chaos/AI_BarkhantRedSummon_Chaos.xml" : "BossDungeon/BarkhantBlue/AI_BarkhantRedSummon.xml"),
                7 => FindPreferred(chaos ? "BossDungeon/BarkhantBlue/Chaos/AI_BarkhantWhiteSummon_Chaos.xml" : "BossDungeon/BarkhantBlue/AI_BarkhantWhiteSummon.xml"),
                8 => FindPreferred(chaos ? "BossDungeon/BarkhantBlue/Chaos/AI_CrowDevilWhite_Close.xml" : "BossDungeon/BarkhantBlue/AI_DragonDevilBigHeadBlueSummon_TypeB.xml"),
                9 => FindPreferred(chaos ? "BossDungeon/BarkhantBlue/Chaos/AI_CerberosTallPurple.xml" : "BossDungeon/BarkhantBlue/AI_CerberosTallPurple_TypeC.xml"),
                10 => FindPreferred(chaos ? "BossDungeon/BarkhantBlue/Chaos/AI_DragonDevilBigHeadBlueSummon_TypeC.xml" : "BossDungeon/BarkhantBlue/AI_DragonDevilBigHeadBlueSummon_TypeC.xml"),
                11 => FindPreferred(chaos ? "BossDungeon/BarkhantBlue/Chaos/AI_CrowDevilWhite_Long.xml" : "BossDungeon/BarkhantBlue/AI_KnightHollowArmorPurple_ThrowWheel_TypeB.xml"),
                _ => actor.Field.NpcMetadata.FindByRelativeAiAlias(currentAiPath, node.NpcId)
            };
        }
        if (normalizedCurrent.EndsWith("AI_BarkhantBlue_Quest.xml", StringComparison.OrdinalIgnoreCase)) {
            return node.NpcId switch {
                6 => actor.Field.NpcMetadata.TryGet(29000204, out NpcMetadata? red6) ? red6 : null,
                7 => actor.Field.NpcMetadata.TryGet(29000205, out NpcMetadata? white7) ? white7 : null,
                8 => actor.Field.NpcMetadata.TryGet(29000204, out NpcMetadata? red8) ? red8 : null,
                9 => actor.Field.NpcMetadata.TryGet(29000205, out NpcMetadata? white9) ? white9 : null,
                10 => actor.Field.NpcMetadata.TryGet(21402237, out NpcMetadata? p3Mob) ? p3Mob : null,
                _ => actor.Field.NpcMetadata.FindByRelativeAiAlias(currentAiPath, node.NpcId)
            };
        }
        // 不灭神殿 / Balrog
        if (normalizedCurrent.Contains("BossDungeon/Balrog", StringComparison.OrdinalIgnoreCase) ||
            normalizedCurrent.Contains("BossDungeon/DungeonOS03", StringComparison.OrdinalIgnoreCase)) {
            if (normalizedCurrent.EndsWith("AI_Balrog.xml", StringComparison.OrdinalIgnoreCase)) {
                return node.NpcId switch {
                    1 => FindPreferred("BossDungeon/Balrog/AI_DragonDevilBigHeadRedSummonTypeA.xml"),
                    2 => FindPreferred("BossDungeon/Balrog/AI_Tristan_Chaos.xml"),
                    _ => actor.Field.NpcMetadata.FindByRelativeAiAlias(currentAiPath, node.NpcId)
                };
            }

            if (normalizedCurrent.EndsWith("AI_Balrog_Chaos.xml", StringComparison.OrdinalIgnoreCase)) {
                return node.NpcId switch {
                    1 => FindPreferred("BossDungeon/Balrog/AI_Tristan_Chaos.xml"),
                    2 => FindPreferred("BossDungeon/Balrog/AI_DragonDevilBigHeadRedSummonTypeB.xml"),
                    _ => actor.Field.NpcMetadata.FindByRelativeAiAlias(currentAiPath, node.NpcId)
                };
            }
        }

        return actor.Field.NpcMetadata.FindByRelativeAiAlias(currentAiPath, node.NpcId);
    }

    private void Push(Entry entry) {
        Push((dynamic) entry);
    }

    private bool CanEvaluateNode(Entry entry) {
        if (entry is not Node node) {
            return true;
        }

        bool hasTicked = coolTimes.TryGetValue(GetNodeKey(entry), out long lastTick);

        Type parent = entry.GetType();
        long? initialCooltime = parent.GetProperty("InitialCooltime")?.GetValue(entry, null) as long?;
        if (initialCooltime.HasValue && !hasTicked) {
            if (initialCooltime.Value > 0) {
                coolTimes[GetNodeKey(entry)] = actor.Field.FieldTick + initialCooltime.Value;
                return false;
            }
        }

        if (!hasTicked) {
            coolTimes[GetNodeKey(entry)] = 0;
            return true;
        }


        long? cooltime = parent.GetProperty("Cooltime")?.GetValue(entry, null) as long?;

        if (cooltime.HasValue) {
            return lastTick + cooltime.Value < actor.Field.FieldTick;
        }

        return lastTick < actor.Field.FieldTick;
    }

    private bool PerformOperation(AiConditionOp op, int value, int currentValue) {
        switch (op) {
            case AiConditionOp.Equal:
                return value == currentValue;
            case AiConditionOp.Greater:
                return value < currentValue;
            case AiConditionOp.Less:
                return value > currentValue;
            case AiConditionOp.GreaterEqual:
                return value <= currentValue;
            case AiConditionOp.LessEqual:
                return value >= currentValue;
            default:
                throw new ArgumentOutOfRangeException(nameof(op), op, null);
        }
    }
    private string GetNodeKey(Entry entry) {
        return $"{entry.Name}_{entry.GetHashCode()}";
    }

    private void Process(Entry entry) {
        if (entry is Node node) {
            actor.AppendDebugMessage($"> Node: {entry.Name}\n");

            ProcessNode((dynamic) entry);

            if (node.Entries.Length > 0 && aiStack.Last().Node is not SelectNode && aiStack.Last().Node != node && CanEvaluateNode(node.Entries[0])) {
                Push(node);
            }

            coolTimes[GetNodeKey(entry)] = actor.Field.FieldTick;
            return;
        }

        if (entry is AiPreset) {
            actor.AppendDebugMessage($"> AiPreset: {entry.Name}\n");
            Push(entry);
        }
    }

    private void ProcessNode(TraceNode node) {
        if (!actor.Field.TryGetActor(actor.BattleState.TargetId, out IActor? _)) {
            return;
        }

        float distance = 0;

        if (node.SkillIdx != 0) {
            SkillMetadata? skill = TryGetSkill(node.SkillIdx);

            if (skill is null) {
                return;
            }

            distance = skill.Data.Detect.Distance; // naive impl, might need to revisit
        }

        float speed = 1;

        if (node.Speed != 0) {
            speed = node.Speed;
        }

        NpcTask task = actor.MovementState.TryMoveTargetDistance(actor.BattleState.Target!, distance, true, node.Animation, speed);

        SetNodeTask(task, node.Limit);
    }

    private void ProcessNode(SkillNode node) {
        SkillMetadata? skill = TryGetSkill(node.Idx);

        if (skill is null) {
            return;
        }

        NpcTask task = actor.CastAiSkill(skill.Id, skill.Level, node.FaceTarget, node.FacePos);

        if (node.IsKeepBattle) {
            actor.BattleState.KeepBattle = true;
        }

        SetNodeTask(task, node.Limit);
    }

    private void ProcessNode(TeleportNode node) {
        actor.Position = node.Pos;
        actor.SendControl = true;

        if (node.FacePos != Vector3.Zero) {
            Vector3 offset = node.FacePos - actor.Position;
            float squareDistance = offset.LengthSquared();

            if (squareDistance > 1e-5f) {
                actor.Transform.LookTo((1 / MathF.Sqrt(squareDistance)) * offset);
            }
        }

        actor.Field.Broadcast(ProxyObjectPacket.UpdateNpc(actor));
    }

    private void ProcessNode(StandbyNode node) {
        NpcTask task = actor.MovementState.TryStandby(null, false);

        if (node.IsKeepBattle) {
            actor.BattleState.KeepBattle = true;
        }

        SetNodeTask(task, node.Limit);
    }

    private void ProcessNode(SetDataNode node) {
        actor.AiExtraData[node.Key] = node.Value;
    }

    private void ProcessNode(TargetNode node) {
        actor.BattleState.TargetNode = node;
    }

    private void ProcessNode(SayNode node) {
        actor.Field.Broadcast(CinematicPacket.BalloonTalk(true, actor.ObjectId, node.Message, node.DurationTick, node.DelayTick));

        actor.AppendDebugMessage($"Say '{node.Message}'\n");
    }

    private void ProcessNode(SetValueNode node) {
        // Assumed that we increment the current by the value if isModify is true
        if (node.IsModify) {
            if (actor.AiExtraData.TryGetValue(node.Key, out int oldValue)) {
                actor.AiExtraData[node.Key] = oldValue + node.Value;
                return;
            }
        }
        actor.AiExtraData[node.Key] = node.Value;
    }

    private void ProcessNode(ConditionsNode node) {
        Condition? passed = null;

        foreach (Condition condition in node.Conditions) {
            if (ProcessCondition((dynamic) condition)) {
                passed = condition;

                break;
            }
        }

        if (passed is null) {
            actor.AppendDebugMessage("Failed conditions\n");

            return;
        }

        actor.AppendDebugMessage($"+ Condition: '{passed.Name}'\n");

        Push(passed);
    }

    private void ProcessNode(JumpNode node) {
        NpcTask? task = null;
        if (node.HeightMultiplier > 0) {
            task = actor.MovementState.TryFlyTo(node.Pos, true, speed: node.Speed, lookAt: true);
        } else {
            task = actor.MovementState.TryMoveTo(node.Pos, true, speed: node.Speed, lookAt: true);
        }

        if (task is not null) {
            SetNodeTask(task, 0);
        } else {
            actor.Position = node.Pos;
            actor.SendControl = true;
            actor.Field.Broadcast(ProxyObjectPacket.UpdateNpc(actor));
        }

        if (node.IsKeepBattle) {
            actor.BattleState.KeepBattle = true;
        }
    }

    private void ProcessNode(SelectNode node) {
        var weightedEntries = new WeightedSet<(Entry, int)>();

        for (int i = 0; i < node.Prob.Length; ++i) {
            if (!CanEvaluateNode(node.Entries[i])) {
                continue;
            }

            weightedEntries.Add((node.Entries[i], i), node.Prob[i]);
        }

        if (weightedEntries.Count == 0) {
            return;
        }

        (Entry entry, int index) selected = weightedEntries.Get();

        aiStack[aiStack.Count - 1] = new StackEntry {
            Node = node,
            Index = selected.index,
            LockIndex = true,
        };

        Push(selected.entry);
        Process(selected.entry);
    }

    private void ProcessNode(MoveNode node) {
        NpcTask task = actor.MovementState.TryMoveTo(node.Destination, true, node.Animation, node.Speed);

        SetNodeTask(task, node.Limit);
    }

    private void ProcessNode(SummonNode node) {
        NpcMetadata? npcData = ResolveSummonMetadata(node);
        if (npcData is null) {
            return;
        }
        Logger.Warning("[AISummon] actorId:{ActorId}, actorAi:{ActorAi}, alias:{Alias} -> npcId:{NpcId}, npcAi:{NpcAi}",
            actor.Value.Id,
            actor.Value.Metadata.AiPath,
            node.NpcId,
            npcData.Id,
            npcData.AiPath);

        int count = node.NpcCount > 0
            ? node.NpcCount
            : (node.NpcCountMax > 0 ? Random.Shared.Next(1, node.NpcCountMax + 1) : 1);

        bool detachFromMaster = node.Master == NodeSummonMaster.None;
        string path = actor.Value.Metadata.AiPath?.Replace('\\', '/') ?? "";
        bool replacementBoss = detachFromMaster && path.Contains("KanduraBigBurster", StringComparison.OrdinalIgnoreCase) && node.NpcId == 3;

        void SpawnOne() {
            Vector3 position = node.SummonPos + node.SummonPosOffset;
            if (position == Vector3.Zero) {
                position = actor.Position;
            }

            if (node.SummonRadius != Vector3.Zero) {
                float rx = node.SummonRadius.X == 0 ? 0 : (float) (Random.Shared.NextDouble() * 2 - 1) * node.SummonRadius.X;
                float ry = node.SummonRadius.Y == 0 ? 0 : (float) (Random.Shared.NextDouble() * 2 - 1) * node.SummonRadius.Y;
                float rz = node.SummonRadius.Z == 0 ? 0 : (float) (Random.Shared.NextDouble() * 2 - 1) * node.SummonRadius.Z;
                position += new Vector3(rx, ry, rz);
            }

            FieldNpc? summoned = actor.Field.SpawnNpc(
                npcData,
                position,
                node.SummonRot == Vector3.Zero ? actor.Rotation : node.SummonRot
            );

            if (summoned is null) {
                return;
            }

            if (!detachFromMaster) {
                summoned.AiExtraData["__master_oid"] = actor.ObjectId;
            }

            summoned.AiExtraData["__summon_group"] = node.Group;

            if (detachFromMaster) {
                summoned.SpawnPointId = actor.SpawnPointId;
                summoned.BattleState.TargetNode = actor.BattleState.TargetNode;
                summoned.BattleState.KeepBattle = actor.BattleState.InBattle || node.IsKeepBattle;

                if (actor.BattleState.Target is not null) {
                    summoned.BattleState.ForceTarget(actor.BattleState.Target);
                }

                if (actor.BattleState.GrabbedUser is not null) {
                    summoned.BattleState.GrabbedUser = actor.BattleState.GrabbedUser;
                }

                int inheritedBattleStart = actor.AiExtraData.GetValueOrDefault("__battle_start_tick", 0);
                if (inheritedBattleStart != 0) {
                    summoned.AiExtraData["__battle_start_tick"] = inheritedBattleStart;
                }
            }

            if (replacementBoss) {
                summoned.AiExtraData["__replacement_spawn"] = 1;
                actor.AiExtraData["__replacement_remove"] = 1;

                // 超链接之树变身期间，先把旧的清关键清掉，避免旧Boss流程污染
                actor.Field.UserValues["KanduraNormalDead"] = 0;
                actor.Field.UserValues["ThirdPhaseEnd"] = 0;

                // 让新Boss明确知道自己已经是二阶段接力，不是独立开场
                summoned.AiExtraData["SecondPhaseStart"] = 1;
            }
            bool inheritMasterHp =
                node.Option.Contains(NodeSummonOption.MasterHp) || node.Option.Contains(NodeSummonOption.LinkHp);

            if (inheritMasterHp) {
                Stat masterHp = actor.Stats.Values[BasicAttribute.Health];
                Stat summonHp = summoned.Stats.Values[BasicAttribute.Health];

                if (masterHp.Total > 0 && summonHp.Total > 0) {
                    double ratio = Math.Clamp((double) masterHp.Current / masterHp.Total, 0d, 1d);
                    summonHp.Current = Math.Clamp((long) Math.Round(summonHp.Total * ratio), 1, summonHp.Total);
                }
            }

            actor.Field.Broadcast(FieldPacket.AddNpc(summoned));
            actor.Field.Broadcast(ProxyObjectPacket.AddNpc(summoned));
        }

        for (int i = 0; i < count; i++) {
            if (node.DelayTick > 0) {
                actor.Field.Scheduler.Schedule(SpawnOne, TimeSpan.FromMilliseconds(node.DelayTick));
            } else {
                SpawnOne();
            }
        }

        if (node.IsKeepBattle) {
            actor.BattleState.KeepBattle = true;
        }
    }

    private void ProcessNode(TriggerSetUserValueNode node) {
        long hp = actor.Stats.Values[BasicAttribute.Health].Current;
        bool isDead = actor.IsDead;

        // 超链接之树：23200082 只允许“真正死亡后”再写 ThirdPhaseEnd
        if (node.Key == "ThirdPhaseEnd" && actor.Value.Id == 23200082) {
            if (!isDead && hp > 0) {
                Logger.Warning(
                    "[AISuppressUserValue] actorId:{ActorId}, key:{Key}, value:{Value}, hp:{Hp}, dead:{Dead}",
                    actor.Value.Id, node.Key, node.Value, hp, isDead
                );
                return;
            }
        }

        // 超链接之树：23200081 正在变身替换时，不允许把自己算成 KanduraNormalDead
        if (node.Key == "KanduraNormalDead" && actor.Value.Id == 23200081) {
            if (actor.AiExtraData.GetValueOrDefault("__replacement_remove", 0) != 0) {
                Logger.Warning(
                    "[AISuppressUserValue] actorId:{ActorId}, key:{Key}, value:{Value}, replacementRemove=1",
                    actor.Value.Id, node.Key, node.Value
                );
                return;
            }
        }

        Logger.Warning(
            "[AIUserValue] actorId:{ActorId}, key:{Key}, value:{Value}, hp:{Hp}, dead:{Dead}",
            actor.Value.Id, node.Key, node.Value, hp, isDead
        );

        actor.Field.UserValues[node.Key] = node.Value;
    }
    private void ProcessNode(RideNode node) { }

    private void ProcessNode(SetSlaveValueNode node) {
        List<FieldNpc> slaves = new();

        foreach (FieldNpc npc in actor.Field.EnumerateNpcs()) {
            if (npc.ObjectId == actor.ObjectId || npc.IsDead) {
                continue;
            }

            if (npc.AiExtraData.GetValueOrDefault("__master_oid", 0) != actor.ObjectId) {
                continue;
            }

            slaves.Add(npc);
        }

        if (slaves.Count == 0) {
            return;
        }

        void Apply(FieldNpc target) {
            int value = node.IsRandom && node.Value > 0 ? Random.Shared.Next(0, node.Value + 1) : node.Value;

            if (node.IsModify) {
                target.AiExtraData[node.Key] = target.AiExtraData.GetValueOrDefault(node.Key, 0) + value;
            } else {
                target.AiExtraData[node.Key] = value;
            }

            if (node.IsKeepBattle) {
                target.BattleState.KeepBattle = true;
            }
        }

        if (node.IsRandom) {
            Apply(slaves[Random.Shared.Next(slaves.Count)]);
            return;
        }

        foreach (FieldNpc slave in slaves) {
            Apply(slave);
        }
    }

    private void ProcessNode(SetMasterValueNode node) {
        int masterOid = actor.AiExtraData.GetValueOrDefault("__master_oid", 0);
        if (masterOid == 0) {
            return;
        }

        if (!actor.Field.TryGetActor(masterOid, out IActor? masterActor) || masterActor is not FieldNpc masterNpc || masterNpc.IsDead) {
            return;
        }

        int value = node.IsRandom && node.Value > 0 ? Random.Shared.Next(0, node.Value + 1) : node.Value;

        if (node.IsModify) {
            masterNpc.AiExtraData[node.Key] = masterNpc.AiExtraData.GetValueOrDefault(node.Key, 0) + value;
        } else {
            masterNpc.AiExtraData[node.Key] = value;
        }

        if (node.IsKeepBattle) {
            masterNpc.BattleState.KeepBattle = true;
        }
    }

    private void ProcessNode(RunawayNode node) {
        if (!actor.Field.TryGetActor(actor.BattleState.TargetId, out IActor? target)) {
            return;
        }

        float distance = float.MaxValue;

        if (node.SkillIdx != 0) {
            SkillMetadata? skill = TryGetSkill(node.SkillIdx);

            if (skill is null) {
                return;
            }

            distance = skill.Data.Detect.Distance; // naive impl, might need to revisit
        }

        NpcTask task = actor.MovementState.TryMoveTargetDistance(actor.BattleState.Target!, distance, true, node.Animation, 1);

        SetNodeTask(task, node.Limit);
    }

    private void ProcessNode(MinimumHpNode node) {
        Stat health = actor.Stats.Values[BasicAttribute.Health];
        float currentHpPercent = ((float) health.Current / (float) health.Total) * 100f;

        if (currentHpPercent > node.HpPercent) {
            return;
        }

        // Heal back to node.HpPercent
        long newHp = (long) ((node.HpPercent / 100) * health.Total);
        actor.Stats.Values[BasicAttribute.Health].Current = Math.Clamp(newHp, 0, health.Total);
    }

    private void ProcessNode(BuffNode node) {
        IActor target = actor;

        if (node.IsTarget) {
            IActor? newTarget = null;

            if (!actor.Field.TryGetActor(actor.BattleState.TargetId, out newTarget)) {
                return;
            }

            target = newTarget;
        }

        if (node.Type == NodeBuffType.Remove) {
            target.Buffs.Remove(node.Id, actor.ObjectId);

            return;
        }

        target.Buffs.AddBuff(actor, actor, node.Id, node.Level, actor.Field.FieldTick);
    }

    private void ProcessNode(TargetEffectNode node) {
        actor.Field.Broadcast(NpcNoticePacket.TargetEffect(actor.BattleState.TargetId, node.EffectName));
    }

    private void ProcessNode(ShowVibrateNode node) { }

    private void ProcessNode(SidePopupNode node) {
        actor.Field.Broadcast(NpcNoticePacket.SidePopup(node.Type, node.Duration, node.Illust, node.Voice, node.Script, node.Sound));
    }

    private void ProcessNode(SetValueRangeTargetNode node) {
        int range = node.Radius;
        int height = node.Height;

        if (range == 0) {
            range = 10;
        }

        if (height == 0) {
            height = 10;
        }

        foreach (FieldNpc npc in actor.Field.EnumerateNpcs()) {
            if (Vector2.Distance(new Vector2(npc.Position.X, npc.Position.Y), new Vector2(actor.Position.X, actor.Position.Y)) > range) {
                continue;
            }

            if (npc.Position.Z < actor.Position.Z - height) {
                continue;
            }

            if (npc.Position.Z > actor.Position.Z + height) {
                continue;
            }

            if (node.IsModify) {
                if (npc.AiExtraData.TryGetValue(node.Key, out int oldValue)) {
                    npc.AiExtraData[node.Key] = oldValue + node.Value;
                    continue;
                }
            }
            npc.AiExtraData[node.Key] = node.Value;
        }
    }

    private void ProcessNode(AnnounceNode node) {
        actor.Field.Broadcast(NpcNoticePacket.Announce(node.Message, node.DurationTick));
    }

    private void ProcessNode(ModifyRoomTimeNode node) { }

    private void ProcessNode(HideVibrateAllNode node) { }

    private void ProcessNode(TriggerModifyUserValueNode node) {
        actor.Field.UserValues[node.Key] = node.Value;
    }

    private void ProcessNode(RemoveSlavesNode node) {
        List<int> removeIds = new();

        foreach (FieldNpc npc in actor.Field.EnumerateNpcs()) {
            if (npc.ObjectId == actor.ObjectId) {
                continue;
            }

            if (npc.AiExtraData.GetValueOrDefault("__master_oid", 0) != actor.ObjectId) {
                continue;
            }

            if (node.IsKeepBattle) {
                npc.BattleState.KeepBattle = true;
            }

            removeIds.Add(npc.ObjectId);
        }

        foreach (int objectId in removeIds) {
            actor.Field.RemoveNpc(objectId, TimeSpan.FromMilliseconds(100));
        }
    }

    private void ProcessNode(CreateRandomRoomNode node) { }

    private void ProcessNode(CreateInteractObjectNode node) { }

    private void ProcessNode(RemoveMeNode node) {
     Logger.Warning(
    "[AIRemoveMe] actorId:{ActorId}, hp:{Hp}, dead:{Dead}, replacementRemove:{ReplacementRemove}",
    actor.Value.Id,
    actor.Stats.Values[BasicAttribute.Health].Current,
    actor.IsDead,
    actor.AiExtraData.GetValueOrDefault("__replacement_remove", 0)
);
        bool replacementRemove = actor.AiExtraData.GetValueOrDefault("__replacement_remove", 0) != 0;

        if (replacementRemove) {
            actor.AiExtraData["__suppress_battle_end"] = 1;
            actor.AiExtraData["__replacement_remove"] = 0;
        }

        actor.BattleState.KeepBattle = false;
        actor.Field.RemoveNpc(actor.ObjectId, TimeSpan.FromMilliseconds(100));
    }

    private void ProcessNode(SuicideNode node) {
        actor.Stats.Values[BasicAttribute.Health].Current = 0;
    }


    private bool ProcessCondition(DistanceOverCondition node) {
        if (actor.BattleState.Target == null) return false;
        float dist = (actor.BattleState.Target.Position - actor.Position).LengthSquared();
        return dist > node.Value * node.Value;
    }

    private bool ProcessCondition(CombatTimeCondition node) {
        return false;
    }

    private bool ProcessCondition(DistanceLessCondition node) {
        if (actor.BattleState.Target == null) return false;
        float dist = (actor.BattleState.Target.Position - actor.Position).LengthSquared();
        return dist < node.Value * node.Value;
    }

    private bool ProcessCondition(SkillRangeCondition node) {
        SkillMetadata? skill = TryGetSkill(node.SkillIdx, node.SkillLev);

        if (skill is null) {
            return false;
        }

        if (actor.BattleState.Target is null) {
            return false;
        }

        float targetDistance = (actor.BattleState.Target.Position - actor.Position).LengthSquared();
        // +10 to account for the npc moving to skill range distance & allowing for it to attack slightly outside
        float detectDistance = skill.Data.Detect.Distance + 10;

        return detectDistance * detectDistance >= targetDistance; // naive, need to implement more sophisticated collision detection
    }

    private bool ProcessCondition(ExtraDataCondition node) {
        int value = 0;
        if (actor is FieldPet pet) {
            switch (node.Key) {
                case "tamingPoint":
                    value = pet.TamingPoint;
                    break;
                default: // aiPresets
                    return pet.Metadata.AiPresets.Contains(node.Key);
            }
        }

        return PerformOperation(node.Op, node.Value, actor.AiExtraData.GetValueOrDefault(node.Key, value));
    }

    private bool ProcessCondition(SlaveCountCondition node) {
        int count = 0;

        foreach (FieldNpc npc in actor.Field.EnumerateNpcs()) {
            if (npc.ObjectId == actor.ObjectId || npc.IsDead) {
                continue;
            }

            if (npc.AiExtraData.GetValueOrDefault("__master_oid", 0) != actor.ObjectId) {
                continue;
            }

            if (node.UseSummonGroup && npc.AiExtraData.GetValueOrDefault("__summon_group", 0) != node.SummonGroup) {
                continue;
            }

            count++;
        }

        return count == node.Count;
    }

    private bool ProcessCondition(SlaveCountOpCondition node) {
        int count = 0;

        foreach (FieldNpc npc in actor.Field.EnumerateNpcs()) {
            if (npc.ObjectId == actor.ObjectId || npc.IsDead) {
                continue;
            }

            if (npc.AiExtraData.GetValueOrDefault("__master_oid", 0) != actor.ObjectId) {
                continue;
            }

            count++;
        }

        return PerformOperation(node.SlaveCountOp, node.SlaveCount, count);
    }

    private bool ProcessCondition(HpOverCondition node) {
        Stat health = actor.Stats.Values[BasicAttribute.Health];

        return node.Value <= ((float) health.Current / (float) health.Total) * 100f;
    }

    private bool ProcessCondition(StateCondition node) {
        if (actor.BattleState.Target is null) {
            return false;
        }

        ActorState state = ActorState.None;

        if (actor.BattleState.Target is FieldPlayer player) {
            state = player.State;
        }

        if (actor.BattleState.Target is FieldNpc npc) {
            state = npc.MovementState.State;
        }

        return node.TargetState switch {
            AiConditionTargetState.GrabTarget => state == ActorState.GrabTarget,
            AiConditionTargetState.HoldMe => state == ActorState.Hold,
            _ => false,
        };
    }

    private bool ProcessCondition(AdditionalCondition node) {
        if (actor.BattleState.Target is null && node.IsTarget) {
            return false;
        }
        if (node.IsTarget) {
            return actor.BattleState.Target!.Buffs.HasBuff(node.Id, node.Level, node.OverlapCount);
        }

        return actor.Buffs.HasBuff(node.Id, node.Level, node.OverlapCount);
    }

    private bool ProcessCondition(HpLessCondition node) {
        Stat health = actor.Stats.Values[BasicAttribute.Health];

        return node.Value >= ((float) health.Current / (float) health.Total) * 100f;
    }

    private bool ProcessCondition(TrueCondition node) {
        return true;
    }
}
