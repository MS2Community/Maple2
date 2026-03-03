using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class NpcTalkHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.NpcTalk;

    private enum Command : byte {
        Close = 0,
        Talk = 1,
        Continue = 2,
        EnchantUnknown = 4,
        Enchant = 6,
        Quest = 7,
        AcceptAllianceQuest = 8,
        TalkAlliance = 9,
        Custom = 11,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required NpcMetadataStorage NpcMetadata { private get; init; }
    public required ScriptMetadataStorage ScriptMetadata { private get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Close:
                HandleClose(session);
                return;
            case Command.Talk:
                HandleTalk(session, packet);
                return;
            case Command.Continue:
                HandleContinue(session, packet);
                return;
            case Command.EnchantUnknown:
                return;
            case Command.Enchant:
                HandleEnchant(session, packet);
                return;
            case Command.Quest:
                HandleQuest(session, packet);
                return;
            case Command.AcceptAllianceQuest:
                HandleAcceptAllianceQuest(session, packet);
                return;
            case Command.TalkAlliance:
                HandleTalkAlliance(session);
                return;
            case Command.Custom:
                return;
        }
    }

    private void HandleClose(GameSession session) {
        session.NpcScript?.Npc?.StopTalk();
        session.NpcScript = null;
        session.Shop.ClearActiveShop();
    }

    private void HandleTalk(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        int objectId = packet.ReadInt();
        if (!session.Field.Npcs.TryGetValue(objectId, out FieldNpc? npc)) {
            return; // Invalid Npc
        }

        int options = 0;
        var talkType = NpcTalkType.None;
        if (npc.Value.Metadata.Basic.ShopId > 0) {
            session.Shop.Load(npc.Value.Metadata.Basic.ShopId, npc.Value.Id);
            talkType |= NpcTalkType.Dialog;
            options++;
        }

        ScriptMetadata.TryGet(npc.Value.Id, out ScriptMetadata? metadata);
        ScriptState? scriptState = NpcTalkUtil.GetInitialScriptType(session, ScriptStateType.Script, metadata, npc.Value.Id);
        ScriptState? selectState = NpcTalkUtil.GetInitialScriptType(session, ScriptStateType.Select, metadata, npc.Value.Id);
        ScriptState? questState = NpcTalkUtil.GetInitialScriptType(session, ScriptStateType.Quest, metadata, npc.Value.Id);

        if (questState != null) {
            talkType |= NpcTalkType.Quest;
            options++;
        }

        if (scriptState?.Type == ScriptStateType.Job) {
            // Job script only counts as an additional option if quests are present.
            if (talkType.HasFlag(NpcTalkType.Quest)) {
                options++;
            }
            talkType |= NpcTalkType.Dialog;
        } else if (scriptState != null) {
            talkType |= NpcTalkType.Talk;
            options++;
        }

        if (options > 1 && selectState != null) {
            talkType |= NpcTalkType.Select;
        }

        switch (npc.Value.Metadata.Basic.Kind) {
            case >= 30 and < 40: // Beauty
            case 2: // Storage
            case 86: // TODO: BlackMarket
            case 88: // TODO: Birthday
            case 501:
                talkType |= NpcTalkType.Dialog;
                break;
            case >= 100 and <= 104: // Sky Fortress
            case >= 105 and <= 107: // Kritias
            case 108: // Humanitas
                talkType = NpcTalkType.Dialog;
                break;
        }

        // Determine which script to use.
        ScriptState? selectedState;
        if (npc.Value.Metadata.Basic.Kind is >= 100 and <= 108 || talkType.HasFlag(NpcTalkType.Select)) {
            selectedState = selectState;
        } else if (talkType.HasFlag(NpcTalkType.Quest)) {
            if (questState == null) {
                session.Send(NpcTalkPacket.Close());
                npc.StopTalk();
                return;
            }
            selectedState = questState;
            talkType = NpcTalkType.Quest;
            // now that quest is selected, change the metadata to the quest's metadata
            if (!ScriptMetadata.TryGet(session.Quest.GetAvailableQuests(npc.Value.Id).Keys.Min(), out metadata)) {
                session.Send(NpcTalkPacket.Close());
                npc.StopTalk();
                return;
            }
        } else if (scriptState == null && selectState == null) {
            if (talkType.HasFlag(NpcTalkType.Dialog)) {
                session.Send(NpcTalkPacket.Respond(npc, talkType, default));
                return;
            }
            session.Send(NpcTalkPacket.Close());
            npc.StopTalk();
            return;
        } else {
            selectedState = scriptState ?? selectState;
        }

        session.NpcScript = new NpcScriptManager(session, npc, metadata, selectedState, talkType);
        if (!session.NpcScript.BeginNpcTalk()) {
            session.NpcScript = null;
            return;
        }

        session.ConditionUpdate(ConditionType.dialogue, codeLong: npc.Value.Id);
        session.ConditionUpdate(ConditionType.talk_in, codeLong: npc.Value.Id);
    }

    private void HandleContinue(GameSession session, IByteReader packet) {
        session.NpcScript?.ProcessScriptFunction(false);
        // Not talking to an Npc.
        if (session.NpcScript == null) {
            return;
        }
        int pick = packet.ReadInt();

        /* The ordering is
        / Quests
        / Dialog
        / Talk */
        int addedOptions = 0;
        if (session.NpcScript.TalkType.HasFlag(NpcTalkType.Select)) {
            if (session.NpcScript.TalkType.HasFlag(NpcTalkType.Quest)) {
                addedOptions += session.NpcScript.Quests.Count;
                // Picked quest
                if (pick < addedOptions) {
                    FieldNpc? npc = session.NpcScript.Npc;
                    if (npc == null) {
                        session.Send(NpcTalkPacket.Close());
                        return;
                    }
                    if (!session.ScriptMetadata.TryGet(session.NpcScript.Quests.ElementAt(pick).Value.Id, out ScriptMetadata? metadata)) {
                        session.Send(NpcTalkPacket.Respond(npc, NpcTalkType.None, default));
                        session.NpcScript = null;
                        return;
                    }
                    ScriptState? state = NpcTalkUtil.GetQuestScriptState(session, metadata, npc.Value.Id);
                    session.NpcScript = new NpcScriptManager(session, npc, metadata, state, NpcTalkType.Quest);
                    if (!session.NpcScript.BeginQuest()) {
                        session.NpcScript = null;
                    }
                    return;
                }
            }

            NpcDialogue dialogue;
            if (session.NpcScript.TalkType.HasFlag(NpcTalkType.Dialog)) {
                addedOptions++;
                if (pick < addedOptions) {
                    session.NpcScript.EnterDialog();
                    dialogue = new NpcDialogue(session.NpcScript.State?.Id ?? 0, session.NpcScript.Index, session.NpcScript.Button);
                    session.Send(NpcTalkPacket.Continue(session.NpcScript.TalkType, dialogue));
                    return;
                }
            }

            session.NpcScript.EnterTalk();
            dialogue = new NpcDialogue(session.NpcScript.State?.Id ?? 0, session.NpcScript.Index, session.NpcScript.Button);
            session.Send(NpcTalkPacket.Continue(session.NpcScript.TalkType, dialogue));
            return;
        }


        // Attempt to Continue, if |false|, the dialogue has terminated.
        if (!session.NpcScript.Continue(pick)) {
            session.NpcScript = null;
        }
    }

    private void HandleEnchant(GameSession session, IByteReader packet) {
        int npcId = packet.ReadInt();
        int scriptId = packet.ReadInt();
        var eventType = packet.Read<ScriptEventType>();

        switch (eventType) {
            case ScriptEventType.EnchantSelect:
            case ScriptEventType.PeachySelect:
                if (!session.ScriptMetadata.TryGet(npcId, out ScriptMetadata? script) ||
                    !script.States.TryGetValue(Constants.EnchantMasterScriptID, out ScriptState? state)) {
                    return;
                }

                CinematicEventScript[] eventScripts = state.Contents.First().Events;
                session.NpcScript = new NpcScriptManager(session, eventScripts);
                return;
            case ScriptEventType.EmpowerSelect:
                session.NpcScript?.EmpowerEvent();
                return;
            case ScriptEventType.EnchantComplete:
                session.ItemEnchant.NpcTalkEvent(eventType);
                return;
        }
    }

    private void HandleQuest(GameSession session, IByteReader packet) {
        if (session.NpcScript == null) {
            return;
        }

        int questId = packet.ReadInt();
        packet.ReadShort(); // 2 or 0. 2 = Start quest, 0 = Complete quest.

        FieldNpc? npc = session.NpcScript.Npc;
        if (npc == null) {
            session.Send(NpcTalkPacket.Close());
            return;
        }
        if (!session.ScriptMetadata.TryGet(questId, out ScriptMetadata? metadata)) {
            session.Send(NpcTalkPacket.Respond(npc, NpcTalkType.None, default));
            session.NpcScript = null;
            return;
        }

        ScriptState? state = NpcTalkUtil.GetQuestScriptState(session, metadata, npc.Value.Id);
        session.NpcScript = new NpcScriptManager(session, npc, metadata, state, NpcTalkType.Quest);

        if (!session.NpcScript.BeginQuest()) {
            session.NpcScript = null;
        }
    }

    private void HandleAcceptAllianceQuest(GameSession session, IByteReader packet) {
        if (session.NpcScript == null) {
            return;
        }

        int questId = packet.ReadInt();
        packet.ReadShort(); // 2 or 0. 2 = Start quest, 0 = Complete quest.

        // TODO: similar to HandleQuest but we'll need to check questId against the available quests for the player.
    }

    private void HandleTalkAlliance(GameSession session) {
        if (session.NpcScript == null) {
            return;
        }
        session.NpcScript.EnterTalk();
        var dialogue = new NpcDialogue(session.NpcScript.State?.Id ?? 0, session.NpcScript.Index, session.NpcScript.Button);
        session.Send(NpcTalkPacket.AllianceTalk(session.NpcScript.TalkType, dialogue));
    }
}
