using System.Diagnostics;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Tools;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Manager.Config;

public class SkillInfo : IByteSerializable {
    public const int SKILL_TYPES = 4;
    public const int SKILL_RANKS = 2;

    private readonly JobTable jobTable;
    private readonly SkillMetadataStorage skillMetadata;

    public Job Job { get; private set; }
    public readonly IDictionary<int, Skill>[,] Skills;
    public readonly IDictionary<int, Skill>[,] SubSkills;

    public SkillInfo(JobTable jobTable, SkillMetadataStorage skillMetadata, Job job, SkillTab? skillTab) {
        this.jobTable = jobTable;
        this.skillMetadata = skillMetadata;

        Skills = new IDictionary<int, Skill>[SKILL_TYPES, SKILL_RANKS];
        SubSkills = new IDictionary<int, Skill>[SKILL_TYPES, SKILL_RANKS];
        for (int i = 0; i < SKILL_TYPES; i++) {
            for (int j = 0; j < SKILL_RANKS; j++) {
                Skills[i, j] = new Dictionary<int, Skill>();
                SubSkills[i, j] = new Dictionary<int, Skill>();
            }
        }

        SetJob(job);
        SetTab(skillTab);

        // Disable all notifications when loading.
        foreach (IDictionary<int, Skill> dict in Skills) {
            foreach (Skill skill in dict.Values) {
                skill.Notify = false;
            }
        }
    }

    public void SetJob(Job job) {
        // If job is unchanged, we do not need to update Skills.
        if (Job == job) {
            return;
        }

        if (!jobTable.Entries.TryGetValue(job.Code(), out JobTable.Entry? jobTableEntry)) {
            throw new ArgumentException($"No Table found for job: {job} => {job.Code()}");
        }

        foreach (IDictionary<int, Skill> dict in Skills) {
            dict.Clear();
        }
        foreach (IDictionary<int, Skill> dict in SubSkills) {
            dict.Clear();
        }

        var baseSkills = new HashSet<int>(jobTableEntry.BaseSkills);
        foreach ((SkillRank rank, JobTable.Skill[] jobSkills) in jobTableEntry.Skills) {
            Debug.Assert(rank is SkillRank.Basic or SkillRank.Awakening);
            if (rank == SkillRank.Awakening && !job.IsAwakening()) {
                continue;
            }

            foreach (JobTable.Skill skillData in jobSkills) {
                short baseLevel = (short) (baseSkills.Contains(skillData.Main) ? 1 : 0);
                var subSkills = new List<Skill>(skillData.Sub.Length);
                foreach (int subSkillId in skillData.Sub) {
                    if (!skillMetadata.TryGet(subSkillId, 1, out SkillMetadata? subMetadata)) {
                        Log.Warning("Skipping invalid subSkillId:{SkillId}", subSkillId);
                        continue;
                    }

                    var subSkill = new Skill(subSkillId, baseLevel, subMetadata.Property.MaxLevel);
                    subSkills.Add(subSkill);
                    SubSkills[(int) subMetadata.Property.Type, (int) rank].Add(subSkillId, subSkill);
                }

                if (!skillMetadata.TryGet(skillData.Main, 1, out SkillMetadata? metadata)) {
                    throw new InvalidOperationException($"Nonexistent skillId:{skillData.Main}");
                }

                var skill = new Skill(skillData.Main, subSkills.ToArray(), baseLevel, metadata.Property.MaxLevel);
                Skills[(int) metadata.Property.Type, (int) rank].Add(skill.Id, skill);
                Log.Verbose("+Skill[{Type}, {Rank}] = {Id} ({Name})", metadata.Property.Type, rank, skill.Id, metadata.Name);
            }
        }

        Job = job;
    }

    public void SetTab(SkillTab? skillTab) {
        var skillMap = new Dictionary<int, (int, int)>();
        if (skillTab != null) {
            foreach ((int skillId, int points) in skillTab.Skills) {
                skillMap[skillId] = (skillId, points);
            }
        }

        foreach (IDictionary<int, Skill> dict in Skills) {
            foreach (Skill skill in dict.Values) {
                if (skillMap.TryGetValue(skill.Id, out (int SkillId, int Points) tabSkill)) {
                    skill.SetLevel((short) (skill.BaseLevel + tabSkill.Points));
                } else {
                    skill.Reset();
                }
            }
        }
    }

    /// <summary>
    /// Returns <b>Main</b> skills with specified filtering.
    /// </summary>
    /// <param name="type">Type of main skills to return.</param>
    /// <param name="rank">Rank of main skills to return.</param>
    /// <returns>Enumerable of skills that match the filter.</returns>
    public IEnumerable<Skill> GetMainSkills(SkillType type, SkillRank rank) {
        if (rank is SkillRank.Basic or SkillRank.Both) {
            foreach (Skill skill in Skills[(int) type, (int) SkillRank.Basic].Values) {
                yield return skill;
            }
        }

        if (rank is SkillRank.Awakening or SkillRank.Both) {
            foreach (Skill skill in Skills[(int) type, (int) SkillRank.Awakening].Values) {
                yield return skill;
            }
        }
    }

    /// <summary>
    /// Returns <b>Main</b> job-specific skills with specified filtering.
    /// </summary>
    /// <param name="type">Type of main skills to return.</param>
    /// <param name="rank">Rank of main skills to return.</param>
    /// <returns>Enumerable of skills that match the filter.</returns>
    public SortedDictionary<int, Skill> GetMainLearnedJobSkills(SkillType type, SkillRank rank) {
        return new SortedDictionary<int, Skill>(GetMainSkills(type, rank)
            .Where(skill => skill is { Level: > 0, Id: < 20000000 })
            .ToDictionary(skill => skill.Id, skill => skill));
    }

    /// <summary>
    /// Returns <b>Main</b> and <b>Sub</b> skills with specified filtering.
    /// </summary>
    /// <param name="type">Type of skills to return.</param>
    /// <param name="rank">Rank of skills to return.</param>
    /// <returns>Enumerable of skills that match the filter.</returns>
    public IEnumerable<Skill> GetSkills(SkillType type, SkillRank rank) {
        if (rank is SkillRank.Basic or SkillRank.Both) {
            foreach (Skill skill in Skills[(int) type, (int) SkillRank.Basic].Values) {
                yield return skill;
            }
            foreach (Skill skill in SubSkills[(int) type, (int) SkillRank.Basic].Values) {
                yield return skill;
            }
        }

        if (rank is SkillRank.Awakening or SkillRank.Both) {
            foreach (Skill skill in Skills[(int) type, (int) SkillRank.Awakening].Values) {
                yield return skill;
            }
            foreach (Skill skill in SubSkills[(int) type, (int) SkillRank.Awakening].Values) {
                yield return skill;
            }
        }
    }

    public Skill? GetMainSkill(int skillId, SkillRank rank = SkillRank.Both) {
        for (int i = 0; i < SKILL_TYPES; i++) {
            if (rank is SkillRank.Basic or SkillRank.Both) {
                if (Skills[i, (int) SkillRank.Basic].TryGetValue(skillId, out Skill? skill)) {
                    return skill;
                }
            }

            if (rank is SkillRank.Awakening or SkillRank.Both) {
                if (Skills[i, (int) SkillRank.Awakening].TryGetValue(skillId, out Skill? skill)) {
                    return skill;
                }
            }
        }

        return null;
    }

    public Skill? GetSkill(int skillId, SkillRank rank = SkillRank.Both) {
        Skill? mainSkill = GetMainSkill(skillId, rank);
        if (mainSkill != null) {
            return mainSkill;
        }

        for (int i = 0; i < SKILL_TYPES; i++) {
            if (rank is SkillRank.Basic or SkillRank.Both) {
                if (SubSkills[i, (int) SkillRank.Basic].TryGetValue(skillId, out Skill? skill)) {
                    return skill;
                }
            }

            if (rank is SkillRank.Awakening or SkillRank.Both) {
                if (SubSkills[i, (int) SkillRank.Awakening].TryGetValue(skillId, out Skill? skill)) {
                    return skill;
                }
            }
        }

        return null;
    }

    public void WriteTo(IByteWriter writer) {
        writer.WriteInt((int) Job);
        writer.WriteByte(1); // Count

        writer.WriteInt((int) Job.Code());
        for (int i = 0; i < SKILL_TYPES; i++) {
            int count = 0;
            for (int j = 0; j < SKILL_RANKS; j++) {
                count += Skills[i, j].Count + SubSkills[i, j].Count;
            }
            writer.WriteByte((byte) count);

            for (int j = 0; j < SKILL_RANKS; j++) {
                foreach (Skill skill in Skills[i, j].Values) {
                    writer.WriteClass<Skill>(skill);
                }
                foreach (Skill subSkill in SubSkills[i, j].Values) {
                    writer.WriteClass<Skill>(subSkill);
                }
            }
        }
    }

    public class Skill : IByteSerializable {
        public readonly int Id;
        public readonly short BaseLevel;
        public readonly short MaxLevel;
        public short Level { get; private set; }
        public bool Notify;

        private readonly Skill[] subSkills;

        public Skill(int id, Skill[] subSkills, short baseLevel, short maxLevel) {
            Id = id;
            this.subSkills = subSkills;
            BaseLevel = baseLevel;
            MaxLevel = maxLevel;
            Level = baseLevel;
        }

        public Skill(int id, short baseLevel, short maxLevel) : this(id, [], baseLevel, maxLevel) { }

        public void Reset() {
            Level = BaseLevel;
            Notify = false;
        }

        public void SetLevel(short level, bool notify = true) {
            if (notify && BaseLevel == 0 && Level == 0 && level > 0) {
                Notify = notify;
            }

            Level = Math.Clamp(level, BaseLevel, MaxLevel);
            foreach (Skill subSkill in subSkills) {
                subSkill.SetLevel(level, false);
            }
        }

        public void WriteTo(IByteWriter writer) {
            writer.WriteBool(Notify);
            writer.WriteBool(Level > 0);
            writer.WriteInt(Id);
            writer.WriteInt(Math.Max((int) Level, 1));
            writer.WriteByte();

            Notify = false;
        }
    }
}
