using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Tools.Scheduler;
using Serilog;
using Serilog.Core;

namespace Maple2.Server.Game.Trigger;

public partial class TriggerContext : ITriggerContext {
    private readonly FieldTrigger owner;
    private readonly ILogger logger = Log.Logger.ForContext<TriggerContext>();

    private FieldManager Field => owner.Field;
    private TriggerCollection Objects => owner.Field.TriggerObjects;

    private float currentRandom = float.MaxValue;

    // Skip state class reference, must instantiate before using.
    private TriggerState? skipState;
    public readonly EventQueue Events;
    public long StartTick;

    public TriggerContext(FieldTrigger owner) {
        this.owner = owner;

        Events = new EventQueue(logger);
        Events.Start();
        StartTick = Environment.TickCount64;
    }

    public bool TryGetSkip([NotNullWhen(true)] out TriggerState? state) {
        if (skipState == null) {
            state = null;
            return false;
        }

        state = skipState;
        return true;
    }

    private void Broadcast(ByteWriter packet) => Field.Broadcast(packet);

    private string lastDebugKey = "";

    [Conditional("TRIGGER_DEBUG")]
    [MessageTemplateFormatMethod("messageTemplate")]
    internal void DebugLog(string messageTemplate, params object[] args) {
        LogOnce(logger.Debug, messageTemplate, args);
    }

    [Conditional("TRIGGER_DEBUG")]
    [MessageTemplateFormatMethod("messageTemplate")]
    internal void WarnLog(string messageTemplate, params object[] args) {
        LogOnce(logger.Warning, messageTemplate, args);
    }

    [Conditional("TRIGGER_DEBUG")]
    [MessageTemplateFormatMethod("messageTemplate")]
    internal void ErrorLog(string messageTemplate, params object[] args) {
        LogOnce(logger.Error, messageTemplate, args);
    }

    private void LogOnce(Action<string, object[]> logAction, string messageTemplate, params object[] args) {
        string key = messageTemplate + string.Join(", ", args);
        if (key == lastDebugKey) {
            return;
        }

        logAction($"{owner.Value.Name} {messageTemplate}", args);
        lastDebugKey = key;
    }

    // Accessors
    public bool ShadowExpeditionPoints(int score) {
        ErrorLog("[GetShadowExpeditionPoints]");
        return 0 >= score;
    }

    public bool DungeonVariable(int id, int value) {
        ErrorLog("[GetDungeonVariable] id:{Id}", id);
        return false;
    }

    public bool NpcDamage(int spawnPointId, float damage, OperatorType operatorType) {
        ErrorLog("[GetNpcDamageRate] spawnPointId:{Id}, damage:{Damage}, operatorType:{Operator}", spawnPointId, damage, operatorType);
        return operatorType switch {
            OperatorType.Greater => damage > 1.0f,
            OperatorType.GreaterEqual => damage >= 1.0f,
            OperatorType.Equal => Math.Abs(damage - 1.0f) < 0.0001f,
            OperatorType.LessEqual => damage <= 1.0f,
            OperatorType.Less => damage < 1.0f,
            _ => false,
        };
    }

    public bool NpcHp(int spawnPointId, bool isRelative, int value, CompareType compareType) {
        ErrorLog("[GetNpcHpRate] spawnPointId:{Id}, isRelative:{IsRelative}, value:{Value}, compareType:{CompareType}", spawnPointId, isRelative, value, compareType);
        return compareType switch {
            CompareType.lower => value > 100,
            CompareType.lowerEqual => value >= 100,
            CompareType.higher => value < 100,
            CompareType.higherEqual => value <= 100,
            _ => false,
        };
    }

    public bool DungeonId(int dungeonId) {
        ErrorLog("[GetDungeonId]");
        return dungeonId == 0;
    }

    public bool DungeonLevel(int level) {
        ErrorLog("[GetDungeonLevel]");
        return level == 3;
    }

    public bool DungeonMaxUserCount(int value) {
        ErrorLog("[GetDungeonMaxUserCount]");
        return value == 1;
    }

    public bool DungeonRound(int round) {
        ErrorLog("[GetDungeonRoundsRequired]");
        return int.MaxValue == round;
    }

    public bool CheckUser(bool negate) {
        if (negate) {
            return Field.Players.IsEmpty;
        }
        return !Field.Players.IsEmpty;
    }

    public bool UserCount(int count) {
        return Field.Players.Count == count;
    }

    public bool CountUsers(int boxId, int userTagId, int minUsers, OperatorType operatorType, bool negate) {
        DebugLog("[GetUserCount] boxId:{BoxId}, userTagId:{TagId}", boxId, userTagId);
        if (!Objects.Boxes.TryGetValue(boxId, out TriggerBox? box)) {
            return negate;
        }

        int count;
        if (userTagId > 0) {
            count = Field.Players.Values.Count(player => player.TagId == userTagId && box.Contains(player.Position));
        } else {
            count = Field.Players.Values.Count(player => box.Contains(player.Position));
        }

        bool result = operatorType switch {
            OperatorType.Greater => count > minUsers,
            OperatorType.GreaterEqual => count >= minUsers,
            OperatorType.Equal => count == minUsers,
            OperatorType.LessEqual => count <= minUsers,
            OperatorType.Less => count < minUsers,
            _ => false,
        };
        return negate ? !result : result;
    }

    public bool NpcExtraData(int spawnId, string extraDataKey, int extraDataValue, OperatorType operatorType) {
        WarnLog("[GetNpcExtraData] spawnId:{SpawnId}, extraDataKey:{Key}, extraDataValue:{Value}, operatorType:{Operator}", spawnId, extraDataKey, extraDataValue, operatorType);
        var npc = Field.EnumerateNpcs().FirstOrDefault(npc => npc.SpawnPointId == spawnId);
        if (npc is null) {
            return false;
        }
        int extraData = npc.AiExtraData.GetValueOrDefault(extraDataKey, 0);
        return operatorType switch {
            OperatorType.Greater => extraData > extraDataValue,
            OperatorType.GreaterEqual => extraData >= extraDataValue,
            OperatorType.Equal => extraData == extraDataValue,
            OperatorType.LessEqual => extraData <= extraDataValue,
            OperatorType.Less => extraData < extraDataValue,
            _ => false,
        };
    }

    public bool DungeonPlayTime(int playSeconds) {
        ErrorLog("[GetDungeonPlayTime]");
        return playSeconds == 0;
    }

    // Scripts seem to just check if this is "Fail"
    public bool DungeonState(string checkState) {
        ErrorLog("[GetDungeonState]");
        return checkState == "";
    }

    public bool DungeonFirstUserMissionScore(int score, OperatorType operatorType) {
        ErrorLog("[GetDungeonFirstUserMissionScore]");
        return operatorType switch {
            OperatorType.Greater => score > 0,
            OperatorType.GreaterEqual => score >= 0,
            OperatorType.Equal => score == 0,
            OperatorType.LessEqual => score <= 0,
            OperatorType.Less => score < 0,
            _ => false,
        };
    }

    public bool ScoreBoardScore(int score, OperatorType operatorType) {
        ErrorLog("[GetScoreBoardScore]");
        return operatorType switch {
            OperatorType.Greater => score > 0,
            OperatorType.GreaterEqual => score >= 0,
            OperatorType.Equal => score == 0,
            OperatorType.LessEqual => score <= 0,
            OperatorType.Less => score < 0,
            _ => false,
        };
    }

    public bool UserValue(string key, int value, bool negate) {
        WarnLog("[GetUserValue] key:{Key}", key);
        int userValue = Field.UserValues.GetValueOrDefault(key, 0);
        if (negate) {
            return userValue != value;
        }
        return userValue == value;
    }

    public void DebugString(string value, string feature) {
        logger.Debug("{Value} [{Feature}]", value, feature);
    }

    public void WriteLog(string logName, string @event, int triggerId, string subEvent, int level) {
        logger.Information("{Log}: {Event}, {TriggerId}, {SubEvent}, {Level}", logName, @event, triggerId, subEvent, level);
    }

    #region Conditions
    public bool DayOfWeek(int[] dayOfWeeks, string description, bool negate) {
        if (negate) {
            return !dayOfWeeks.Contains((int) DateTime.UtcNow.DayOfWeek + 1);
        }
        return dayOfWeeks.Contains((int) DateTime.UtcNow.DayOfWeek + 1);
    }

    public bool RandomCondition(float rate, string description) {
        if (rate < 0f || rate > 100f) {
            LogOnce(logger.Error, "[RandomCondition] Invalid rate: {Rate}", rate);
            return false;
        }

        if (currentRandom >= 100f) {
            currentRandom = Random.Shared.NextSingle() * 100;
        }

        currentRandom -= rate;
        if (currentRandom > rate) {
            return false;
        }

        currentRandom = float.MaxValue; // Reset
        return true;
    }

    public bool WaitAndResetTick(int waitTick) {
        long tickNow = Environment.TickCount64;
        if (tickNow <= StartTick + waitTick) {
            return false;
        }

        StartTick = tickNow;
        return true;
    }

    public bool WaitTick(int waitTick) {
        return Environment.TickCount64 > StartTick + waitTick;
    }
    #endregion
}
