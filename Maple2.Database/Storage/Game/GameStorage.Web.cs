using Maple2.Database.Model.Ranking;
using Maple2.Model.Game;
using Character = Maple2.Database.Model.Character;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {

        #region Ranking
        // This is not an efficient way to fetch rankings. This is only done for proof of concept.
        // Ideally, we would store the rank records in db and fetch that. any current season data should be refreshed on a daily basis.
        public TrophyRankInfo? GetTrophyRankInfo(long characterId) {
            Character? character = Context.Character.Find(characterId);
            if (character == null) {
                return null;
            }
            AchievementInfo achievementInfo = GetAchievementInfo(character.AccountId, character.Id);

            // Get all characters with their account IDs
            var allCharacters = Context.Character
                .Select(c => new {
                    CharacterId = c.Id,
                    AccountId = c.AccountId,
                })
                .ToList();

            // Calculate total trophies for each character
            var characterTrophies = new List<(long CharacterId, int TotalTrophies)>();
            foreach (var characterEntry in allCharacters) {
                AchievementInfo info = GetAchievementInfo(characterEntry.AccountId, characterEntry.CharacterId);
                characterTrophies.Add((characterEntry.CharacterId, info.Total));
            }

            // Sort by trophy count (descending) and find our character's position
            characterTrophies = characterTrophies.OrderByDescending(ct => ct.TotalTrophies).ToList();
            int rank = characterTrophies.FindIndex(ct => ct.CharacterId == character.Id) + 1;

            // If character not found (shouldn't happen), default to last place
            if (rank == 0) {
                rank = characterTrophies.Count + 1;
            }
            return new TrophyRankInfo(
                Rank: rank,
                CharacterId: character.Id,
                Name: character.Name,
                Profile: character.Profile?.Picture ?? string.Empty,
                Trophy: achievementInfo);
        }

        public TrophyRankInfo? GetTrophyRankInfo(string name) {
            long characterId = GetCharacterId(name);
            if (characterId == default) {
                return null;
            }

            return GetTrophyRankInfo(characterId);
        }

        public IList<TrophyRankInfo> GetTrophyRankings() {
            // Get all characters with their account IDs
            var characters = Context.Character
                .Select(c => new {
                    CharacterId = c.Id,
                    AccountId = c.AccountId,
                    Name = c.Name,
                    Profile = c.Profile.Picture,
                })
                .ToList();

            // Calculate total trophies for each character (including account-wide trophies)
            var characterRankings = new List<(int Rank, long CharacterId, string Name, string Profile, AchievementInfo Trophy)>();

            foreach (var character in characters) {
                // Get achievement info for this character (combines account and character trophies)
                AchievementInfo achievementInfo = GetAchievementInfo(character.AccountId, character.CharacterId);
                if (achievementInfo.Total <= 0) {
                    continue;
                }
                // Add to our list
                characterRankings.Add((0, character.CharacterId, character.Name, character.Profile ?? string.Empty, achievementInfo));
            }

            // Sort by total trophy count and assign ranks
            characterRankings = characterRankings
                .OrderByDescending(r => r.Trophy.Total)
                .Select((r, index) => (index + 1, r.CharacterId, r.Name, r.Profile, r.Trophy))
                .Take(200)
                .ToList();

            // Convert to TrophyRankInfo objects
            return characterRankings
                .Select(r => new TrophyRankInfo(
                    Rank: r.Rank,
                    CharacterId: r.CharacterId,
                    Name: r.Name,
                    Profile: r.Profile,
                    Trophy: r.Trophy))
                .ToList();
        }
        public GuildTrophyRankInfo? GetGuildTrophyRankInfo(long guildId) {
            var guild = Context.Guild
                .Where(g => g.Id == guildId)
                .Select(g => new { g.Id, g.Name, g.Emblem, g.LeaderId })
                .FirstOrDefault();
            if (guild == null) {
                return null;
            }

            string leaderName = Context.Character
                .Where(c => c.Id == guild.LeaderId)
                .Select(c => c.Name)
                .FirstOrDefault() ?? string.Empty;

            AchievementInfo guildTrophy = ComputeGuildTrophy(guildId);

            // Compute all guild totals to determine rank
            var allGuildIds = Context.Guild.Select(g => g.Id).ToList();
            var guildTotals = new List<(long GuildId, int Total)>();
            foreach (long id in allGuildIds) {
                AchievementInfo info = ComputeGuildTrophy(id);
                guildTotals.Add((id, info.Total));
            }

            guildTotals = guildTotals.OrderByDescending(g => g.Total).ToList();
            int rank = guildTotals.FindIndex(g => g.GuildId == guildId) + 1;
            if (rank == 0) {
                rank = guildTotals.Count + 1;
            }

            return new GuildTrophyRankInfo(
                Rank: rank,
                GuildId: guild.Id,
                Name: guild.Name,
                Emblem: guild.Emblem,
                LeaderName: leaderName,
                Trophy: guildTrophy);
        }

        public GuildTrophyRankInfo? GetGuildTrophyRankInfo(string guildName) {
            long? guildId = Context.Guild
                .Where(g => g.Name == guildName)
                .Select(g => (long?) g.Id)
                .FirstOrDefault();
            if (guildId == null) {
                return null;
            }

            return GetGuildTrophyRankInfo(guildId.Value);
        }

        public long GetGuildIdByCharacterId(long characterId) {
            return Context.GuildMember
                .Where(m => m.CharacterId == characterId)
                .Select(m => m.GuildId)
                .FirstOrDefault();
        }

        public IList<GuildTrophyRankInfo> GetGuildTrophyRankings() {
            // Read guild data with Select projection to avoid EF tracking issues
            var guilds = Context.Guild
                .Select(g => new { g.Id, g.Name, g.Emblem, g.LeaderId })
                .ToList();

            // Batch lookup leader names
            var leaderIds = guilds.Select(g => g.LeaderId).Distinct().ToList();
            var leaderNames = Context.Character
                .Where(c => leaderIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name })
                .ToDictionary(c => c.Id, c => c.Name);

            var rankings = new List<GuildTrophyRankInfo>();
            foreach (var guild in guilds) {
                AchievementInfo trophy = ComputeGuildTrophy(guild.Id);
                if (trophy.Total <= 0) {
                    continue;
                }
                string leaderName = leaderNames.GetValueOrDefault(guild.LeaderId, string.Empty);
                rankings.Add(new GuildTrophyRankInfo(0, guild.Id, guild.Name, guild.Emblem, leaderName, trophy));
            }

            return rankings
                .OrderByDescending(r => r.Trophy.Total)
                .Select((r, index) => new GuildTrophyRankInfo(
                    Rank: index + 1,
                    GuildId: r.GuildId,
                    Name: r.Name,
                    Emblem: r.Emblem,
                    LeaderName: r.LeaderName,
                    Trophy: r.Trophy))
                .Take(200)
                .ToList();
        }

        private AchievementInfo ComputeGuildTrophy(long guildId) {
            // Get all member character IDs and their account IDs via Select projection
            var members = Context.GuildMember
                .Where(m => m.GuildId == guildId)
                .Select(m => m.CharacterId)
                .ToList();

            // Batch lookup account IDs
            var characterInfo = Context.Character
                .Where(c => members.Contains(c.Id))
                .Select(c => new { c.Id, c.AccountId })
                .ToList();

            var total = new AchievementInfo();
            foreach (var info in characterInfo) {
                total += GetAchievementInfo(info.AccountId, info.Id);
            }

            return total;
        }
        #endregion

        public IList<long> GetMentorList(long accountId, long characterId) {
            // Get only characters that have been modified in the last 30 days (including time)
            DateTime thirtyDaysAgo = DateTime.Now.AddDays(-30);

            // Get filtered character IDs, excluding the current account and character
            // Group by AccountId to ensure we only get one character per account
            List<long> filteredCharacterIds = Context.Character
                .Where(c => c.LastModified >= thirtyDaysAgo &&
                           c.AccountId != accountId &&
                           c.Id != characterId)
                .GroupBy(c => c.AccountId)  // Group by AccountId
                .Select(g => g.OrderByDescending(c => c.LastModified).First().Id)  // Take the most recently modified character from each account
                .ToList();  // Materialize the query here

            // Randomize the order and take up to 50
            return filteredCharacterIds
                .OrderBy(_ => Random.Shared.Next())  // Randomize order
                .Take(50)
                .ToList();
        }
    }
}
