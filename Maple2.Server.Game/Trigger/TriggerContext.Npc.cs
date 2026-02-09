using System.Numerics;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Trigger.Helpers;

namespace Maple2.Server.Game.Trigger;

public partial class TriggerContext {
    public void ChangeMonster(int removeSpawnId, int addSpawnId) {
        DebugLog("[ChangeMonster] removeSpawnId:{RemoveId}, addSpawnId:{AddId}", removeSpawnId, addSpawnId);
        foreach (FieldNpc fieldNpc in Field.EnumerateNpcs()) {
            if (fieldNpc.SpawnPointId == removeSpawnId) {
                Field.RemoveNpc(fieldNpc.ObjectId);
            }
        }

        SpawnMonster(addSpawnId);
    }

    public void SpawnMonster(int[] spawnIds, bool autoTarget, int delay) {
        WarnLog("[CreateMonster] spawnIds:{SpawnIds}, autoTarget:{AutoTarget}, delay:{Delay}", string.Join(", ", spawnIds), autoTarget, delay);
        foreach (int spawnId in spawnIds) {
            if (delay > 0) {
                Events.Schedule(() => SpawnNpc(spawnId, autoTarget), TimeSpan.FromMilliseconds(delay));
            } else {
                SpawnNpc(spawnId, autoTarget);
            }
        }
    }

    public void DestroyMonster(int[] spawnIds, bool arg2) {
        WarnLog("[DestroyMonster] spawnIds:{SpawnIds}, Arg2:{Arg2}", string.Join(", ", spawnIds), arg2);
        if (spawnIds.Contains(-1)) {
            foreach (FieldNpc fieldNpc in Field.EnumerateNpcs()) {
                Field.RemoveNpc(fieldNpc.ObjectId);
            }
            return;
        }

        foreach (int spawnPointId in spawnIds) {
            foreach (FieldNpc fieldNpc in Field.EnumerateNpcs()) {
                if (fieldNpc.SpawnPointId == spawnPointId) {
                    Field.RemoveNpc(fieldNpc.ObjectId);
                }
            }
        }
    }

    private void SpawnMonster(int spawnId) {
        DebugLog("[SpawnMonster] spawnId:{SpawnId}", spawnId);
        SpawnNpc(spawnId);
    }

    public void InitNpcRotation(int[] spawnIds) {
        ErrorLog("[InitNpcRotation] spawnIds:{SpawnIds}", string.Join(", ", spawnIds));
    }

    public void LimitSpawnNpcCount(int limitCount, string desc) {
        ErrorLog("[LimitSpawnNpcCount] limitCount:{Count}", limitCount);
    }

    public void MoveNpc(int spawnId, string patrolName) {
        DebugLog("[MoveNpc] spawnId:{SpawnId} patrolName:{PatrolName}", spawnId, patrolName);

        List<FieldNpc> fieldNpcs = Field.EnumerateNpcs().Where(npc => npc.SpawnPointId == spawnId).ToList();
        if (fieldNpcs.Count == 0) {
            return;
        }

        MS2PatrolData? patrolData = Field.Entities.Patrols.FirstOrDefault(patrol => patrol.Name == patrolName);

        if (patrolData is null) {
            return;
        }

        foreach (FieldNpc fieldNpc in fieldNpcs) {
            fieldNpc.SetPatrolData(patrolData);
        }
    }

    public void MoveNpcToPos(int spawnId, Vector3 position, Vector3 rotation) {
        WarnLog("[MoveNpcToPos] spawnId:{SpawnId}", spawnId);
        foreach (FieldNpc npc in Field.EnumerateNpcs()) {
            if (npc.SpawnPointId == spawnId) {
                npc.Position = position;
                npc.Rotation = rotation;
            }
        }
    }

    public void NpcRemoveAdditionalEffect(int spawnId, int additionalEffectId) {
        WarnLog("[NpcRemoveAdditionalEffect] spawnId:{SpawnId}", spawnId);
        foreach (FieldNpc npc in Field.EnumerateNpcs()) {
            if (npc.SpawnPointId == spawnId) {
                npc.Buffs.Remove(additionalEffectId, npc.ObjectId);
            }
        }
    }

    public void NpcToPatrolInBox(int boxId, int npcId, string spawnId, string patrolName) {
        ErrorLog("[NpcToPatrolInBox] boxId:{BoxId}, npcId:{NpcId}, spawnId:{SpawnId}, patrolName:{PatrolName}", spawnId, npcId, spawnId, patrolName);
    }

    public void SetAiExtraData(string key, int value, bool isModify, int boxId) {
        WarnLog("[SetAiExtraData] key:{Key}, value:{Value}, isModify:{IsModify}, boxId:{BoxId}", key, value, isModify, boxId);
        var npcs = boxId != 0 ? NpcsInBox(boxId) : Field.EnumerateNpcs();
        foreach (FieldNpc npc in npcs) {
            // Assumed that we increment the current by the value if isModify is true
            if (isModify) {
                if (npc.AiExtraData.TryGetValue(key, out int oldValue)) {
                    npc.AiExtraData[key] = oldValue + value;
                    continue;
                }
            }
            npc.AiExtraData[key] = value;
        }
    }

    public void SetDialogue(int type, int spawnId, string script, int delay, int arg5, Align align) {
        DebugLog("[SetDialogue] type:{Type}, spawnId:{SpawnId}, script:{Script}, delay:{Delay}, arg5:{Arg5}, align:{Align}",
            type, spawnId, script, delay, arg5, align);

        if (spawnId == 0) {
            var player = Field.Players.Values.FirstOrDefault();
            if (player == null) {
                return;
            }
            Broadcast(CinematicPacket.BalloonTalk(false, player.ObjectId, script, delay * 1000, 0));
            return;
        }

        if (type == 1) {
            var npc = Field.EnumerateNpcs().FirstOrDefault(npc => npc.SpawnPointId == spawnId);
            if (npc == null) {
                return;
            }

            Broadcast(CinematicPacket.BalloonTalk(false, npc.ObjectId, script, delay * 1000, 0));
            return;
        }

        Broadcast(CinematicPacket.Talk(spawnId, spawnId.ToString(), script, delay * 1000, align));
    }

    public void SetNpcDuelHpBar(bool isOpen, int spawnId, int durationTick, int npcHpStep) {
        DebugLog("[SetNpcDuelHpBar] isOpen:{IsOpen}, spawnId:{SpawnId}, durationTick:{Duration}, npcHpStep:{HpStep}", isOpen, spawnId, durationTick, npcHpStep);
        IActor? actor = Field.GetActorsBySpawnId(spawnId).FirstOrDefault();
        if (actor is null) {
            return;
        }

        Broadcast(TriggerPacket.DuelHpBar(isOpen, actor.ObjectId, durationTick, npcHpStep));
    }

    public void SetNpcEmotionLoop(int spawnId, string sequenceName, float duration) {
        WarnLog("[SetNpcEmotionLoop] spawnId:{SpawnId}, sequenceName:{SequenceName}, durationTick:{Duration}", spawnId, sequenceName, duration);

        FieldNpc? fieldNpc = Field.EnumerateNpcs().FirstOrDefault(npc => npc.SpawnPointId == spawnId);
        fieldNpc?.Animate(sequenceName, duration);
    }

    public void SetNpcEmotionSequence(int spawnId, string sequenceName, int durationTick) {
        WarnLog("[SetNpcEmotionSequence] spawnId:{SpawnId}, sequenceName:{SequenceName}, durationTick:{Duration}", spawnId, sequenceName, durationTick);

        FieldNpc? fieldNpc = Field.EnumerateNpcs().FirstOrDefault(npc => npc.SpawnPointId == spawnId);
        fieldNpc?.Animate(sequenceName);
    }

    public void SetNpcRotation(int spawnId, float rotation) {
        WarnLog("[SetNpcRotation] spawnId:{SpawnId}, rotation:{Rotation}", spawnId, rotation);
        foreach (FieldNpc npc in Field.EnumerateNpcs()) {
            if (npc.SpawnPointId == spawnId) {
                npc.Rotation = npc.Rotation with {
                    Z = rotation,
                };
            }
        }
    }

    public void SpawnNpcRange(int[] spawnIds, bool isAutoTargeting, int randomPickCount, int score) {
        DebugLog("[SpawnNpcRange] spawnIds:{SpawnIds}, isAutoTargeting:{AutoTarget}, randomPickCount:{RandomCount}, score:{Score}",
            string.Join(", ", spawnIds), isAutoTargeting, randomPickCount, score);

        foreach (int spawnId in spawnIds) {
            SpawnNpc(spawnId);
        }
    }

    #region Conditions
    public bool CheckNpcAdditionalEffect(int spawnId, int additionalEffectId, int level, bool negate) {
        DebugLog("[CheckNpcAdditionalEffect] spawnId:{SpawnId}, additionalEffectId:{EffectId}, level:{Level}", spawnId, additionalEffectId, level);
        IEnumerable<IActor> actors = Field.GetActorsBySpawnId(spawnId);
        bool hasAnyBuff = false;
        foreach (IActor actor in actors) {
            if (actor is not FieldNpc) {
                continue;
            }

            if (!actor.Buffs.HasBuff(additionalEffectId, (short) level)) continue;

            hasAnyBuff = true;
            break;
        }

        return negate ? !hasAnyBuff : hasAnyBuff;
    }

    public bool MonsterDead(int[] spawnIds, bool autoTarget) {
        DebugLog("[MonsterDead] spawnIds:{SpawnIds}, arg2:{Arg2}", string.Join(", ", spawnIds), autoTarget);
        IEnumerable<FieldNpc> matchingMobs = Field.Mobs.Values.Where(x => spawnIds.Contains(x.SpawnPointId));

        foreach (FieldNpc mob in matchingMobs) {
            if (!mob.IsDead) {
                return false;
            }
        }

        // Either no mobs were found or they are all dead
        return true;
    }

    public bool MonsterInCombat(int[] spawnIds, bool negate) {
        DebugLog("[MonsterInCombat] spawnIds:{SpawnIds}", string.Join(", ", spawnIds));
        foreach (FieldNpc mob in Field.Mobs.Values) {
            if (mob.SpawnPointId > 0 || !spawnIds.Contains(mob.SpawnPointId)) {
                continue;
            }

            if (mob.BattleState.InBattle) {
                return !negate;
            }
        }

        return negate;
    }

    public bool NpcDetected(int boxId, int[] spawnIds, bool negate) {
        DebugLog("[NpcDetected] boxId:{BoxId}, spawnIds:{SpawnIds}", boxId, string.Join(", ", spawnIds));
        if (spawnIds.Length == 0 || spawnIds[0] == 0) {
            return NpcsInBox(boxId).Any();
        }

        foreach (FieldNpc mob in NpcsInBox(boxId)) {
            if (mob.SpawnPointId > 0 && spawnIds.Contains(mob.SpawnPointId)) {
                return !negate;
            }
        }

        return negate;
    }

    public bool NpcIsDeadByStringId(string stringId) {
        ErrorLog("[NpcIsDeadByStringId] stringId:{Round}", stringId);
        return false;
    }
    #endregion

    private IEnumerable<FieldNpc> MonstersInBox(params int[] boxIds) {
        if (boxIds.Length == 0 || boxIds[0] == 0) {
            return Field.Mobs.Values;
        }

        IEnumerable<TriggerBox> boxes = boxIds
            .Select(boxId => Objects.Boxes.GetValueOrDefault(boxId))
            .Where(box => box != null)!;

        return Field.Mobs.Values.Where(mob => boxes.Any(box => box.Contains(mob.Position)));
    }

    private IEnumerable<FieldNpc> NpcsInBox(params int[] boxIds) {
        if (boxIds.Length == 0 || boxIds[0] == 0) {
            return Field.EnumerateNpcs();
        }

        IEnumerable<TriggerBox> boxes = boxIds
            .Select(boxId => Objects.Boxes.GetValueOrDefault(boxId))
            .Where(box => box != null)!;

        return Field.EnumerateNpcs().Where(mob => boxes.Any(box => box.Contains(mob.Position) || box.Intersects(mob.Shape)));
    }

    private void SpawnNpc(int spawnId, bool useSpawnAnimation = false) {
        if (!Field.Entities.EventNpcSpawns.TryGetValue(spawnId, out EventSpawnPointNPC? spawn)) {
            logger.Error("[SpawnNpc] Invalid spawnId:{SpawnId}", spawnId);
            return;
        }

        DebugLog("[SpawnNpc] spawnId:{SpawnId}, SpawnRadius:{SpawnRadius}, NpcCount:{NpcCount}, Position:{Position}",
            spawnId, spawn.SpawnRadius, spawn.NpcCount, spawn.Position);

        // NpcCount is the total number of NPCs that can exist at this spawn point.
        // Each NpcList entry's Count is how many of that type can spawn.
        // Build a pool of individual NPC ids from the list, shuffle, and take up to NpcCount.
        List<int> npcPool = [];
        foreach (SpawnPointNPCListEntry entry in spawn.NpcList) {
            for (int j = 0; j < entry.Count; j++) {
                npcPool.Add(entry.NpcId);
            }
        }

        // Shuffle and cap at NpcCount
        IEnumerable<int> npcIdsToSpawn = npcPool.OrderBy(_ => Random.Shared.Next()).Take(spawn.NpcCount);

        foreach (int npcId in npcIdsToSpawn) {
            if (!Field.NpcMetadata.TryGet(npcId, out NpcMetadata? npc)) {
                logger.Error("[SpawnNpc] Invalid npcId:{NpcId}", npcId);
                continue;
            }

            string spawnAnimationString = string.Empty;
            if (!string.IsNullOrEmpty(spawn.SpawnAnimation) && useSpawnAnimation) {
                spawnAnimationString = spawn.SpawnAnimation;
            }
            FieldNpc? fieldNpc = Field.SpawnNpc(npc, spawn.Position, spawn.Rotation, spawnPointNpc: spawn, spawnAnimation: spawnAnimationString);
            if (fieldNpc == null) {
                logger.Error("[SpawnNpc] Failed to spawn npcId:{NpcId}", npcId);
                continue;
            }

            fieldNpc.SpawnPointId = spawnId;

            Field.Broadcast(FieldPacket.AddNpc(fieldNpc));
            Field.Broadcast(ProxyObjectPacket.AddNpc(fieldNpc));
        }
    }
}
