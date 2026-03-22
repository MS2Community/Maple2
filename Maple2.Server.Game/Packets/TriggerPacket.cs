using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class TriggerPacket {
    private enum Command : byte {
        Load = 2,
        Update = 3,
        CameraStart = 5,
        CameraEnd = 6,
        Ui = 8,
        EditScript = 11,
        Dialog = 14,
        Unknown15 = 15,
        AddEffect = 16,
        RemoveEffect = 17,
        SidePopup = 18,
        SetVisibleUi = 19,
        DuelHpBar = 20,
        ResetScript = 21,
        Effect1 = 22,
        Effect2 = 23,
        Unknown24 = 24,
    }

    public static ByteWriter Load(IReadOnlyCollection<ITriggerObject> triggers) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Load);
        pWriter.WriteInt(triggers.Count);
        foreach (ITriggerObject trigger in triggers) {
            pWriter.WriteClass<ITriggerObject>(trigger);
        }

        return pWriter;
    }

    public static ByteWriter Update(ITriggerObject trigger) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Update);
        pWriter.WriteClass<ITriggerObject>(trigger);

        return pWriter;
    }

    public static ByteWriter CameraStart(ICollection<int> pathIds, bool unknown = false) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.CameraStart);
        pWriter.WriteByte((byte) pathIds.Count);
        foreach (int pathId in pathIds) {
            pWriter.WriteInt(pathId);
        }
        pWriter.WriteBool(unknown);

        return pWriter;
    }

    public static ByteWriter CameraEnd() {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.CameraEnd);

        return pWriter;
    }

    public static ByteWriter EditScript(string scriptXml) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.EditScript);
        pWriter.WriteString(scriptXml);

        return pWriter;
    }

    public static ByteWriter Unknown15(ICollection<int> ids) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Unknown15);
        pWriter.WriteInt(ids.Count);
        foreach (int id in ids) {
            pWriter.WriteInt(id);
        }

        return pWriter;
    }

    public static ByteWriter AddEffect(int objectId, string nifPath, bool isOutline = false, float scale = 1f, float rotateZ = 0f) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.AddEffect);
        pWriter.WriteInt(objectId);
        pWriter.WriteString(nifPath);
        pWriter.WriteBool(isOutline);
        pWriter.WriteFloat(scale);
        pWriter.WriteFloat(rotateZ);

        return pWriter;
    }

    public static ByteWriter RemoveEffect(int objectId) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.RemoveEffect);
        pWriter.WriteInt(objectId);

        return pWriter;
    }

    public static ByteWriter SidePopupTalk(SideNpcTalkType type = SideNpcTalkType.Default, int duration = 0, string illustration = "", string voice = "", string script = "", string sound = "", string usm = "") {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.SidePopup);
        pWriter.Write<SideNpcTalkType>(type);
        pWriter.WriteInt(duration);
        pWriter.WriteString(usm);
        pWriter.WriteString(illustration);
        pWriter.WriteString(voice);
        pWriter.WriteString(sound);
        pWriter.WriteUnicodeString(script);

        return pWriter;
    }

    public static ByteWriter SetVisibleUi(bool visible, string uiNames) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.SetVisibleUi);
        pWriter.WriteBool(visible);
        pWriter.WriteString(uiNames);

        return pWriter;
    }

    public static ByteWriter DuelHpBar(bool set, int objectId = 0, int durationTick = 0, int hpStep = 0) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.DuelHpBar);
        pWriter.WriteBool(set);
        if (set) {
            pWriter.WriteInt(objectId);
            pWriter.WriteInt(durationTick);
            pWriter.WriteInt(hpStep);
        }

        return pWriter;
    }

    public static ByteWriter ResetScript(bool error = false) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.ResetScript);
        pWriter.WriteInt(error ? 1 : 0); // no error=>s_user_trigger_msg_rollback

        return pWriter;
    }

    public static ByteWriter Effect1(ICollection<int> objectIds, string effect) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Effect1);
        pWriter.WriteInt(objectIds.Count);
        foreach (int objectId in objectIds) {
            pWriter.WriteInt(objectId);
        }
        pWriter.WriteUnicodeString(effect);

        return pWriter;
    }

    public static ByteWriter Effect2(ICollection<int> objectIds, string effect) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Effect2);
        pWriter.WriteInt(objectIds.Count);
        foreach (int objectId in objectIds) {
            pWriter.WriteInt(objectId);
        }
        pWriter.WriteUnicodeString(effect);

        return pWriter;
    }

    public static ByteWriter Unknown24() {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Unknown24);

        return pWriter;
    }


    private enum UiCommand : byte {
        Guide = 1,
        ShowSummary = 2,
        HideSummary = 3,
        StartMovie = 4,
        SkipMovie = 5,
        EmotionSequence = 7,
        EmotionLoop = 8,
        FaceEmotion = 9,
        PlayerRotation = 10,
        Unknown = 11,
        TypingGame = 12,
    }

    public static ByteWriter UiGuide(int eventId) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.Guide);
        pWriter.WriteInt(eventId);

        return pWriter;
    }

    public static ByteWriter UiShowSummary(int entityId, int textId, int duration) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.ShowSummary);
        pWriter.WriteInt(entityId);
        pWriter.WriteInt(textId);
        pWriter.WriteInt(duration);

        return pWriter;
    }

    public static ByteWriter UiHideSummary(int entityId) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.HideSummary);
        pWriter.WriteInt(entityId);

        return pWriter;
    }

    public static ByteWriter UiStartMovie(string fileName, int movieId) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.StartMovie);
        pWriter.WriteString(fileName);
        pWriter.WriteInt(movieId);

        return pWriter;
    }

    public static ByteWriter UiSkipMovie(int movieId) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.SkipMovie);
        pWriter.WriteInt(movieId);

        return pWriter;
    }

    public static ByteWriter UiEmotionSequence(ICollection<string> sequenceNames) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.EmotionSequence);
        pWriter.WriteInt(sequenceNames.Count);
        foreach (string sequenceName in sequenceNames) {
            pWriter.WriteUnicodeString(sequenceName);
        }

        return pWriter;
    }

    public static ByteWriter UiEmotionLoop(string sequenceName, int duration, bool loop) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.EmotionLoop);
        pWriter.WriteBool(loop);
        pWriter.WriteInt(duration);
        pWriter.WriteUnicodeString(sequenceName);

        return pWriter;
    }

    public static ByteWriter UiFaceEmotion(int objectId, string emotionName) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.FaceEmotion);
        pWriter.WriteInt(objectId);
        pWriter.WriteString(emotionName);

        return pWriter;
    }

    public static ByteWriter UiPlayerRotation(in Vector3S rotation, bool rotateXY = false) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.PlayerRotation);
        pWriter.Write<Vector3S>(rotation);
        pWriter.WriteBool(rotateXY);

        return pWriter;
    }

    public static ByteWriter UiUnknown(in Vector3 unknown, bool flag = false) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.Unknown);
        pWriter.Write<Vector3S>(unknown);
        pWriter.WriteBool(flag);

        return pWriter;
    }

    public static ByteWriter UiTypingGame(int digits, int duration) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Ui);
        pWriter.Write<UiCommand>(UiCommand.TypingGame);
        pWriter.WriteInt(digits);
        pWriter.WriteInt(duration);

        return pWriter;
    }


    private enum DialogCommand : byte {
        Timer = 1,
        PvpObserver = 2,
        PvpRanking = 3,
        PvpTeamScore = 4,
        PvpPlayer = 5,
    }

    public static ByteWriter TimerDialog(TickTimer timer) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Dialog);
        pWriter.Write<DialogCommand>(DialogCommand.Timer);
        pWriter.WriteInt(timer.StartTick);
        pWriter.WriteInt(timer.Duration);
        pWriter.WriteBool(timer.AutoRemove);
        pWriter.WriteInt(timer.VerticalOffset);
        pWriter.WriteUnicodeString(timer.Type);

        return pWriter;
    }

    public static ByteWriter PvpObserverDialog(int duration, FieldPlayer player1, FieldPlayer player2, bool autoRemove = true) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Dialog);
        pWriter.Write<DialogCommand>(DialogCommand.PvpObserver);
        pWriter.WriteInt(Environment.TickCount);
        pWriter.WriteInt(duration);
        pWriter.WriteBool(autoRemove);

        pWriter.WriteLong(player1.Value.Character.Id); // characterId
        pWriter.WriteInt(5);                           // currentHp
        pWriter.WriteInt(10);                          // maxHp
        pWriter.WriteByte(1);                          // wins

        pWriter.WriteLong(player2.Value.Character.Id); // characterId
        pWriter.WriteInt(5);                           // currentHp
        pWriter.WriteInt(10);                          // maxHp
        pWriter.WriteByte(1);                          // wins

        return pWriter;
    }

    public static ByteWriter PvpRankingDialog(int duration, bool autoRemove = true) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Dialog);
        pWriter.Write<DialogCommand>(DialogCommand.PvpRanking);
        pWriter.WriteInt(Environment.TickCount);
        pWriter.WriteInt(duration);
        pWriter.WriteBool(autoRemove);

        return pWriter;
    }

    public static ByteWriter PvpTeamScoreDialog(int duration, bool autoRemove = true) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Dialog);
        pWriter.Write<DialogCommand>(DialogCommand.PvpTeamScore);
        pWriter.WriteInt(Environment.TickCount);
        pWriter.WriteInt(duration);
        pWriter.WriteBool(autoRemove);

        return pWriter;
    }

    public static ByteWriter PvpPlayerDialog(int duration, FieldPlayer player1, FieldPlayer player2, bool autoRemove = true) {
        var pWriter = Packet.Of(SendOp.Trigger);
        pWriter.Write<Command>(Command.Dialog);
        pWriter.Write<DialogCommand>(DialogCommand.PvpPlayer);
        pWriter.WriteInt(Environment.TickCount);
        pWriter.WriteInt(duration);
        pWriter.WriteBool(autoRemove);

        pWriter.WriteLong(player1.Value.Character.Id); // characterId
        pWriter.WriteInt(5);                           // currentHp
        pWriter.WriteInt(10);                          // maxHp
        pWriter.WriteByte(1);                          // wins
        pWriter.WriteInt(100);                         // points

        pWriter.WriteLong(player2.Value.Character.Id); // characterId
        pWriter.WriteInt(5);                           // currentHp
        pWriter.WriteInt(10);                          // maxHp
        pWriter.WriteByte(1);                          // wins
        pWriter.WriteInt(100);                         // points


        return pWriter;
    }
}
