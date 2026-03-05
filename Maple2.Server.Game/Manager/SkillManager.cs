using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Config;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Game.Manager;

public class SkillManager {
    private readonly GameSession session;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    private ConstantsTable Constants => session.ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    public readonly SkillBook SkillBook;
    public readonly SkillInfo SkillInfo;

    private readonly ILogger logger = Log.ForContext<SkillManager>();

    public SkillManager(GameSession session, SkillBook skillBook) {
        this.session = session;

        SkillBook = skillBook;

        JobTable jobTable = session.TableMetadata.JobTable;
        Job job = session.Player.Value.Character.Job;
        SkillInfo = new SkillInfo(jobTable, session.SkillMetadata, job, GetActiveTab());
    }

    public void LoadSkillBook() {
        session.Send(SkillBookPacket.Load(SkillBook));
    }

    public void UpdatePassiveBuffs(bool notifyField = true) {
        // TODO: Only remove buffs that have been unlearned.
        /*foreach (Buff buff in session.Player.Buffs.Buffs.Values) {
            if (buff.StartTick == buff.EndTick) {
                buff.Remove();
            }
        }*/

        // Add job passive skills to Player.
        foreach (SkillInfo.Skill skill in session.Config.Skill.SkillInfo.GetSkills(SkillType.Passive, SkillRank.Both)) {
            if (skill.Level <= 0) {
                continue;
            }

            if (!session.SkillMetadata.TryGet(skill.Id, skill.Level, out SkillMetadata? metadata)) {
                logger.Error("Invalid skill: {SkillId},{Level}", skill.Id, skill.Level);
                continue;
            }

            logger.Information("Applying passive skill {Name}: {SkillId},{Level}", metadata.Name, metadata.Id, metadata.Level);
            foreach (SkillEffectMetadata effect in metadata.Data.Skills) {
                if (effect.Condition is not { Target: SkillTargetType.Owner }) {
                    continue;
                }

                session.Player.ApplyEffect(session.Player, session.Player, effect, session.Player.Field.FieldTick, EventConditionType.Activate, skillId: metadata.Id, notifyField: notifyField);
            }
        }
    }

    #region SkillBook
    public SkillTab? GetActiveTab() => GetSkillTab(SkillBook.ActiveSkillTabId);

    public SkillTab? GetSkillTab(long id) {
        return SkillBook.SkillTabs.FirstOrDefault(skillTab => skillTab.Id == id);
    }

    public bool SaveSkillTab(long activeSkillTabId, SkillRank ranks, SkillTab? tab = null) {
        if (GetSkillTab(activeSkillTabId) != null) {
            SkillBook.ActiveSkillTabId = activeSkillTabId;
        }

        // Switching Active Tab
        if (tab == null) {
            SkillTab? activeTab = GetActiveTab();
            if (activeTab != null) {
                SkillInfo.SetTab(activeTab);
            }
            session.Send(SkillBookPacket.Save(SkillBook, 0, ranks));
            return true;
        }

        // AddOrUpdate SkillTab
        SkillTab? existingTab = GetSkillTab(tab.Id);
        if (existingTab == null) {
            // Need to create a new tab
            bool result = CreateSkillTab(tab);
            if (result) {
                session.Send(SkillBookPacket.Save(SkillBook, tab.Id, ranks));
            }
            return result;
        }

        // Clear all skills for the rank we are saving as they will be set again.
        int[] skillIds = existingTab.Skills.Keys.ToArray();
        foreach (int skillId in skillIds) {
            if (SkillInfo.GetMainSkill(skillId, ranks) != null) {
                existingTab.Skills.Remove(skillId);
            }
        }

        foreach ((int skillId, int points) in tab.Skills) {
            SkillInfo.Skill? skill = SkillInfo.GetMainSkill(skillId, ranks);
            if (skill != null) {
                existingTab.Skills.Add(skillId, points);
            }
        }

        session.Send(SkillBookPacket.Save(SkillBook, tab.Id, ranks));
        return true;
    }

    public bool ExpandSkillTabs() {
        if (SkillBook.SkillTabs.Count < SkillBook.MaxSkillTabs) {
            return true;
        }

        if (SkillBook.MaxSkillTabs >= Constant.MaxSkillTabCount) {
            return false;
        }
        if (session.Currency.Meret < Constants.SkillBookTreeAddTabFeeMerat) {
            return false;
        }

        session.Currency.Meret -= Constants.SkillBookTreeAddTabFeeMerat;
        SkillBook.MaxSkillTabs++;
        session.Send(SkillBookPacket.Expand(SkillBook));

        return true;
    }

    private bool CreateSkillTab(SkillTab skillTab) {
        using GameStorage.Request db = session.GameStorage.Context();
        if (SkillBook.SkillTabs.Count >= SkillBook.MaxSkillTabs) {
            return false;
        }

        SkillTab? createdSkillTab = db.CreateSkillTab(session.CharacterId, skillTab);
        if (createdSkillTab == null) {
            return false;
        }

        SkillBook.SkillTabs.Add(createdSkillTab);
        return true;
    }
    #endregion

    #region SkillInfo
    public void UpdateSkill(int skillId, short level, bool enabled) {
        SkillInfo.Skill? skill = SkillInfo.GetMainSkill(skillId);
        if (skill == null) {
            return;
        }

        // Level must be set to 0 if not enabled since there is a placeholder value of 1.
        if (!enabled) {
            skill.SetLevel(0);
            return;
        }

        skill.SetLevel(level);
    }
    #endregion

    public void ResetSkills(SkillRank rank = SkillRank.Both) {
        foreach (SkillType type in Enum.GetValues(typeof(SkillType))) {
            foreach (SkillInfo.Skill skill in SkillInfo.GetMainSkills(type, rank)) {
                skill.SetLevel(0);
            }
        }
    }
}
