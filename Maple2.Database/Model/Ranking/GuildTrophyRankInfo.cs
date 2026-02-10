using Maple2.Model.Game;

namespace Maple2.Database.Model.Ranking;

public record GuildTrophyRankInfo(
    int Rank,
    long GuildId,
    string Name,
    string Emblem,
    string LeaderName,
    AchievementInfo Trophy);
