using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Tools.Extensions;
using Microsoft.EntityFrameworkCore;
using Z.EntityFramework.Plus;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        public Guild? GetGuild(long guildId) {
            return LoadGuild(guildId, string.Empty);
        }

        public Guild? GetGuild(string guildName) {
            return LoadGuild(0, guildName);
        }

        public bool GuildExists(long guildId = 0, string guildName = "") {
            return Context.Guild.Any(guild => guild.Id == guildId || guild.Name == guildName);
        }

        public IList<GuildMember> GetGuildMembers(IPlayerInfoProvider provider, long guildId) {
            return Context.GuildMember.Where(member => member.GuildId == guildId)
                .AsEnumerable()
                .Select(member => {
                    PlayerInfo? info = provider.GetPlayerInfo(member.CharacterId);
                    return info == null ? null : new GuildMember {
                        GuildId = member.GuildId,
                        Info = info,
                        Message = member.Message,
                        Rank = member.Rank,
                        WeeklyContribution = member.WeeklyContribution,
                        TotalContribution = member.TotalContribution,
                        DailyDonationCount = member.DailyDonationCount,
                        JoinTime = member.CreationTime.ToEpochSeconds(),
                        CheckinTime = member.CheckinTime.ToEpochSeconds(),
                        DonationTime = member.DonationTime.ToEpochSeconds(),
                    };
                })
                .WhereNotNull()
                .ToList();
        }


        public IList<Guild> SearchGuilds(IPlayerInfoProvider provider, string guildName = "", GuildFocus? focus = null, int limit = 50) {
            IQueryable<Model.Guild> query = Context.Guild;
            if (!string.IsNullOrWhiteSpace(guildName)) {
                query = query.Where(guild => EF.Functions.Like(guild.Name, $"%{guildName}%"));
            }
            if (focus.HasValue && (int) focus.Value != 0) {
                query = query.Where(guild => guild.Focus == focus.Value);
            }

            List<long> guildIds = query.OrderBy(guild => guild.Name)
                .Take(limit)
                .Select(guild => guild.Id)
                .ToList();

            var result = new List<Guild>();
            foreach (long id in guildIds) {
                Guild? guild = LoadGuild(id, string.Empty);
                if (guild == null) {
                    continue;
                }

                foreach (GuildMember member in GetGuildMembers(provider, id)) {
                    guild.Members.TryAdd(member.CharacterId, member);
                    guild.AchievementInfo += member.Info.AchievementInfo;
                }
                result.Add(guild);
            }

            return result;
        }

        public GuildApplication? CreateGuildApplication(IPlayerInfoProvider provider, long guildId, long applicantId) {
            Guild? guild = LoadGuild(guildId, string.Empty);
            PlayerInfo? applicant = provider.GetPlayerInfo(applicantId);
            if (guild == null || applicant == null) {
                return null;
            }

            if (Context.GuildApplication.Any(app => app.GuildId == guildId && app.ApplicantId == applicantId)) {
                Model.GuildApplication existing = Context.GuildApplication.First(app => app.GuildId == guildId && app.ApplicantId == applicantId);
                return new GuildApplication {
                    Id = existing.Id,
                    Guild = guild,
                    Applicant = applicant,
                    CreationTime = existing.CreationTime.ToEpochSeconds(),
                };
            }

            var app = new Model.GuildApplication {
                GuildId = guildId,
                ApplicantId = applicantId,
            };
            Context.GuildApplication.Add(app);
            if (!SaveChanges()) {
                return null;
            }

            return new GuildApplication {
                Id = app.Id,
                Guild = guild,
                Applicant = applicant,
                CreationTime = app.CreationTime.ToEpochSeconds(),
            };
        }

        public GuildApplication? GetGuildApplication(IPlayerInfoProvider provider, long applicationId) {
            Model.GuildApplication? app = Context.GuildApplication.FirstOrDefault(app => app.Id == applicationId);
            if (app == null) {
                return null;
            }

            Guild? guild = LoadGuild(app.GuildId, string.Empty);
            PlayerInfo? applicant = provider.GetPlayerInfo(app.ApplicantId);
            if (guild == null || applicant == null) {
                return null;
            }

            return new GuildApplication {
                Id = app.Id,
                Guild = guild,
                Applicant = applicant,
                CreationTime = app.CreationTime.ToEpochSeconds(),
            };
        }

        public IList<GuildApplication> GetGuildApplications(IPlayerInfoProvider provider, long guildId) {
            List<Model.GuildApplication> applications = Context.GuildApplication.Where(app => app.GuildId == guildId)
                .OrderByDescending(app => app.CreationTime)
                .ToList();

            return applications
                .Select(app => {
                    Guild? guild = LoadGuild(app.GuildId, string.Empty);
                    PlayerInfo? applicant = provider.GetPlayerInfo(app.ApplicantId);
                    if (guild == null || applicant == null) {
                        return null;
                    }

                    return new GuildApplication {
                        Id = app.Id,
                        Guild = guild,
                        Applicant = applicant,
                        CreationTime = app.CreationTime.ToEpochSeconds(),
                    };
                })
                .WhereNotNull()
                .ToList();
        }

        public IList<GuildApplication> GetGuildApplicationsByApplicant(IPlayerInfoProvider provider, long applicantId) {
            List<Model.GuildApplication> applications = Context.GuildApplication.Where(app => app.ApplicantId == applicantId)
                .OrderByDescending(app => app.CreationTime)
                .ToList();

            return applications
                .Select(app => {
                    Guild? guild = LoadGuild(app.GuildId, string.Empty);
                    PlayerInfo? applicant = provider.GetPlayerInfo(app.ApplicantId);
                    if (guild == null || applicant == null) {
                        return null;
                    }

                    return new GuildApplication {
                        Id = app.Id,
                        Guild = guild,
                        Applicant = applicant,
                        CreationTime = app.CreationTime.ToEpochSeconds(),
                    };
                })
                .WhereNotNull()
                .ToList();
        }

        public Guild? CreateGuild(string name, long leaderId) {
            BeginTransaction();

            var guild = new Model.Guild {
                Name = name,
                LeaderId = leaderId,
                HouseRank = 1,
                HouseTheme = 1,
                Ranks = [
                    new Model.GuildRank {Name = "Master", Permission = GuildPermission.All},
                    new Model.GuildRank {Name = "Jr. Master", Permission = GuildPermission.Default},
                    new Model.GuildRank {Name = "Member 1", Permission = GuildPermission.Default},
                    new Model.GuildRank {Name = "Member 2", Permission = GuildPermission.Default},
                    new Model.GuildRank {Name = "New Member 1", Permission = GuildPermission.Default},
                    new Model.GuildRank {Name = "New Member 2", Permission = GuildPermission.Default},
                ],
                Buffs = [
                    new Model.GuildBuff {Id = 1, Level = 1},
                    new Model.GuildBuff {Id = 2, Level = 1},
                    new Model.GuildBuff {Id = 3, Level = 1},
                    new Model.GuildBuff {Id = 4, Level = 1},
                    new Model.GuildBuff {Id = 10001, Level = 1},
                    new Model.GuildBuff {Id = 10002, Level = 1},
                    new Model.GuildBuff {Id = 10003, Level = 1},
                    new Model.GuildBuff {Id = 10004, Level = 1},
                    new Model.GuildBuff {Id = 10005, Level = 1},
                ],
                Posters = [],
                Npcs = [],
            };
            Context.Guild.Add(guild);
            if (!SaveChanges()) {
                return null;
            }

            var guildLeader = new Model.GuildMember {
                GuildId = guild.Id,
                CharacterId = leaderId,
                Rank = 0,
            };
            Context.GuildMember.Add(guildLeader);
            if (!SaveChanges()) {
                return null;
            }

            return Commit() ? LoadGuild(guild.Id, string.Empty) : null;
        }

        public GuildMember? CreateGuildMember(long guildId, PlayerInfo info) {
            var member = new Model.GuildMember {
                GuildId = guildId,
                CharacterId = info.CharacterId,
                Rank = 5,
            };
            Context.GuildMember.Add(member);
            if (!SaveChanges()) {
                return null;
            }

            return new GuildMember {
                GuildId = member.GuildId,
                Info = info,
                Rank = member.Rank,
                JoinTime = member.CreationTime.ToEpochSeconds(),
            };
        }

        public bool SaveGuild(Guild guild) {
            // Don't save guild if it was disbanded.
            if (!Context.Guild.Any(model => model.Id == guild.Id)) {
                return false;
            }

            BeginTransaction();

            Context.Guild.Update(guild);
            SaveGuildMembers(guild.Id, guild.Members.Values);

            return Commit();
        }

        public bool DeleteGuild(long guildId) {
            BeginTransaction();

            int count = Context.Guild.Where(guild => guild.Id == guildId).Delete();
            if (count == 0) {
                return false;
            }

            Context.GuildMember.Where(member => member.GuildId == guildId).Delete();
            Context.GuildApplication.Where(app => app.GuildId == guildId).Delete();

            return Commit();
        }

        public bool DeleteGuildMember(long guildId, long characterId) {
            int count = Context.GuildMember.Where(member => member.GuildId == guildId && member.CharacterId == characterId).Delete();
            return SaveChanges() && count > 0;
        }

        public bool DeleteGuildApplication(long applicationId) {
            int count = Context.GuildApplication.Where(app => app.Id == applicationId).Delete();
            return SaveChanges() && count > 0;
        }

        public bool DeleteGuildApplications(long characterId) {
            int count = Context.GuildApplication.Where(app => app.ApplicantId == characterId).Delete();
            return SaveChanges() && count > 0;
        }

        public bool SaveGuildMembers(long guildId, ICollection<GuildMember> members) {
            Dictionary<long, GuildMember> saveMembers = members
                .ToDictionary(member => member.CharacterId, member => member);
            HashSet<long> existingMembers = Context.GuildMember
                .Where(member => member.GuildId == guildId)
                .Select(member => member.CharacterId)
                .ToHashSet();

            foreach ((long characterId, GuildMember gameMember) in saveMembers) {
                if (existingMembers.Contains(characterId)) {
                    Context.GuildMember.Update(gameMember);
                } else {
                    Context.GuildMember.Add(gameMember);
                }
            }

            return SaveChanges();
        }

        public bool SaveGuildMember(GuildMember member) {
            Model.GuildMember? model = Context.GuildMember.Find(member.GuildId, member.CharacterId);
            if (model == null) {
                return false;
            }

            Context.GuildMember.Update(member);
            return SaveChanges();
        }

        // Note: GuildMembers must be loaded separately.
        private Guild? LoadGuild(long guildId, string guildName) {
            IQueryable<Model.Guild> query = guildId > 0
                ? Context.Guild.Where(guild => guild.Id == guildId)
                : Context.Guild.Where(guild => guild.Name == guildName);
            return query
                .Join(Context.Character, guild => guild.LeaderId, character => character.Id,
                    (guild, character) => new Tuple<Model.Guild, Model.Character>(guild, character))
                .AsEnumerable()
                .Select(entry => {
                    Model.Guild guild = entry.Item1;
                    Character character = entry.Item2;
                    return new Guild(guild.Id, guild.Name, character.AccountId, character.Id, character.Name) {
                        Emblem = guild.Emblem,
                        Notice = guild.Notice,
                        CreationTime = guild.CreationTime.ToEpochSeconds(),
                        Focus = guild.Focus,
                        Experience = guild.Experience,
                        Funds = guild.Funds,
                        HouseRank = guild.HouseRank,
                        HouseTheme = guild.HouseTheme,
                        Ranks = guild.Ranks.Select((rank, i) => new GuildRank {
                            Id = (byte) i,
                            Name = rank.Name,
                            Permission = rank.Permission,
                        }).ToArray(),
                        Buffs = guild.Buffs.Select(skill => new GuildBuff {
                            Id = skill.Id,
                            Level = skill.Level,
                            ExpiryTime = skill.ExpiryTime,
                        }).ToList(),
                        Posters = guild.Posters.Select(poster => new GuildPoster {
                            Id = poster.Id,
                            Picture = poster.Picture,
                            OwnerId = poster.OwnerId,
                            OwnerName = poster.OwnerName,
                        }).ToList(),
                        Npcs = guild.Npcs.Select(npc => new GuildNpc {
                            Type = npc.Type,
                            Level = npc.Level,
                        }).ToList(),
                    };
                })
                .FirstOrDefault();
        }
    }
}
