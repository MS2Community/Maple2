using System.Text.Json.Serialization;

namespace Maple2.Server.Game.Config;

public sealed class MushkingPassConfig {
    public bool Enabled { get; set; } = true;
    public string SeasonName { get; set; } = "Pre-Season";
    public DateTime SeasonStartUtc { get; set; } = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    public DateTime SeasonEndUtc { get; set; } = new(2028, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    public int MaxLevel { get; set; } = 30;
    public int ExpPerLevel { get; set; } = 100;
    public MonsterExpConfig MonsterExp { get; set; } = new();
    public int GoldPassActivationItemId { get; set; }
    public int GoldPassActivationItemCount { get; set; } = 1;
    public List<PassRewardConfig> FreeRewards { get; set; } = [];
    public List<PassRewardConfig> GoldRewards { get; set; } = [];

    [JsonIgnore]
    public IReadOnlyDictionary<int, PassRewardConfig> FreeRewardsByLevel => freeRewardsByLevel ??= FreeRewards
        .GroupBy(entry => entry.Level)
        .ToDictionary(group => group.Key, group => group.Last());

    [JsonIgnore]
    public IReadOnlyDictionary<int, PassRewardConfig> GoldRewardsByLevel => goldRewardsByLevel ??= GoldRewards
        .GroupBy(entry => entry.Level)
        .ToDictionary(group => group.Key, group => group.Last());

    private Dictionary<int, PassRewardConfig>? freeRewardsByLevel;
    private Dictionary<int, PassRewardConfig>? goldRewardsByLevel;
}

public sealed class MonsterExpConfig {
    public int Normal { get; set; } = 2;
    public int Elite { get; set; } = 8;
    public int Boss { get; set; } = 30;
}

public sealed class PassRewardConfig {
    public int Level { get; set; }
    public int ItemId { get; set; }
    public int Rarity { get; set; } = -1;
    public int Amount { get; set; } = 1;
}
