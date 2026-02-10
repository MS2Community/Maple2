using Maple2.Model.Enum;

namespace Maple2.Model.Game;

public class Account {
    #region Immutable
    public DateTime LastModified { get; init; }
    public long Id { get; init; }

    public required string Username { get; init; }
    public Guid? MachineId { get; set; }
    #endregion

    public int MaxCharacters { get; set; }
    public int PrestigeLevel { get; set; }
    public int PrestigeLevelsGained { get; set; }
    public long PrestigeExp { get; set; }
    public long PrestigeCurrentExp { get; set; }
    public IList<PrestigeMission> PrestigeMissions { get; set; }
    public IList<int> PrestigeRewardsClaimed { get; set; }
    public long PremiumTime { get; set; }
    public IList<int> PremiumRewardsClaimed { get; set; }
    public int MesoMarketListed { get; set; }
    public int MesoMarketPurchased { get; set; }

    public int SurvivalLevel { get; set; }
    public long SurvivalExp { get; set; }
    public int SurvivalSilverLevelRewardClaimed { get; set; }
    public int SurvivalGoldLevelRewardClaimed { get; set; }
    public bool ActiveGoldPass { get; set; }
    public bool Online { get; set; }
    public AdminPermissions AdminPermissions { get; set; }

    public Account() {
        PremiumRewardsClaimed = new List<int>();
        PrestigeMissions = new List<PrestigeMission>();
        PrestigeRewardsClaimed = new List<int>();
        AdminPermissions = AdminPermissions.None;
    }
}
