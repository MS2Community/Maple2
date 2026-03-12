using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.PacketHandlers;

public class SkillBookHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestSkillBookTree;

    private enum Command : byte {
        Load = 0,
        Save = 1,
        Rename = 2,
        Expand = 4,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Load:
                HandleLoad(session);
                return;
            case Command.Save:
                HandleSave(session, packet);
                return;
            case Command.Rename:
                HandleRename(session, packet);
                return;
            case Command.Expand:
                HandleExpand(session, packet);
                return;
        }
    }

    private void HandleLoad(GameSession session) {
        session.Config.Skill.LoadSkillBook();
    }

    private void HandleSave(GameSession session, IByteReader packet) {
        long activeSkillTab = packet.ReadLong();
        long savedSkillTab = packet.ReadLong();
        var ranksToSave = (SkillRank) packet.ReadInt();

        if (!Enum.IsDefined<SkillRank>(ranksToSave)) {
            return;
        }

        // Switching Active Tab
        if (savedSkillTab == 0) {
            if (session.Config.Skill.SaveSkillTab(activeSkillTab, ranksToSave)) {
                session.Config.UpdateHotbarSkills();
                session.Config.Skill.UpdatePassiveBuffs();
            }
            return;
        }

        int tabCount = packet.ReadInt();
        for (int i = 0; i < tabCount; i++) {
            var skillTab = packet.ReadClass<SkillTab>();
            if (skillTab.Id != savedSkillTab) continue;

            if (session.Config.Skill.SaveSkillTab(activeSkillTab, ranksToSave, skillTab)) {
                session.Config.UpdateHotbarSkills();
                session.Config.Skill.UpdatePassiveBuffs();
            }
            return;
        }
    }

    private void HandleRename(GameSession session, IByteReader packet) {
        long skillTabId = packet.ReadLong();
        string skillTabName = packet.ReadUnicodeString();

        SkillTab? skillTab = session.Config.Skill.GetSkillTab(skillTabId);
        if (skillTab == null) {
            return;
        }

        skillTab.Name = skillTabName;
        session.Send(SkillBookPacket.Rename(skillTab));
    }

    private void HandleExpand(GameSession session, IByteReader packet) {
        int constant = packet.ReadInt();
        packet.ReadBool();
        if (constant != 20272) {
            return;
        }

        session.Config.Skill.ExpandSkillTabs();
    }
}
