using System.Xml.Linq;
using Serilog;

namespace Maple2.Server.Game.Config;

public sealed class SurvivalPassXmlConfig {
    private static readonly ILogger Logger = Log.Logger.ForContext<SurvivalPassXmlConfig>();

    public SortedDictionary<int, long> LevelThresholds { get; } = new SortedDictionary<int, long>();
    public Dictionary<int, SurvivalRewardEntry> FreeRewards { get; } = new Dictionary<int, SurvivalRewardEntry>();
    public Dictionary<int, SurvivalRewardEntry> PaidRewards { get; } = new Dictionary<int, SurvivalRewardEntry>();

    public int ActivationItemId { get; private set; }
    public int ActivationItemCount { get; private set; } = 1;
    public int MonsterKillExp { get; private set; } = 1;
    public int EliteKillExp { get; private set; } = 5;
    public int BossKillExp { get; private set; } = 20;
    public bool AllowDirectActivateWithoutItem { get; private set; }

    public static SurvivalPassXmlConfig Load() {
        var config = new SurvivalPassXmlConfig();
        string baseDir = AppContext.BaseDirectory;

        config.LoadServerConfig(FindFile(baseDir, "survivalserverconfig.xml"));
        config.LoadLevels(FindFile(baseDir, "survivallevel.xml"));
        config.LoadRewards(FindFile(baseDir, "survivalpassreward.xml"), config.FreeRewards);
        config.LoadRewards(FindFile(baseDir, "survivalpassreward_paid.xml"), config.PaidRewards);

        if (config.LevelThresholds.Count == 0) {
            config.LevelThresholds[1] = 0;
        }

        Logger.Information("Loaded survival config thresholds={Thresholds} freeRewards={FreeRewards} paidRewards={PaidRewards} activationItem={ItemId} x{ItemCount}",
            config.LevelThresholds.Count, config.FreeRewards.Count, config.PaidRewards.Count, config.ActivationItemId, config.ActivationItemCount);
        return config;
    }

    private void LoadServerConfig(string path) {
        if (!File.Exists(path)) {
            return;
        }

        XDocument document = XDocument.Load(path);
        XElement? node = document.Root == null ? null : document.Root.Element("survivalPassServer");
        if (node == null) {
            return;
        }

        ActivationItemId = ParseInt(node.Attribute("activationItemId") != null ? node.Attribute("activationItemId")!.Value : null);
        ActivationItemCount = Math.Max(1, ParseInt(node.Attribute("activationItemCount") != null ? node.Attribute("activationItemCount")!.Value : null, 1));
        MonsterKillExp = Math.Max(1, ParseInt(node.Attribute("monsterKillExp") != null ? node.Attribute("monsterKillExp")!.Value : null, 1));
        EliteKillExp = Math.Max(1, ParseInt(node.Attribute("eliteKillExp") != null ? node.Attribute("eliteKillExp")!.Value : null, 5));
        BossKillExp = Math.Max(1, ParseInt(node.Attribute("bossKillExp") != null ? node.Attribute("bossKillExp")!.Value : null, 20));
        AllowDirectActivateWithoutItem = ParseBool(node.Attribute("allowDirectActivateWithoutItem") != null ? node.Attribute("allowDirectActivateWithoutItem")!.Value : null, false);
    }

    private void LoadLevels(string path) {
        if (!File.Exists(path)) {
            Logger.Warning("Missing survival level config: {Path}", path);
            return;
        }

        XDocument document = XDocument.Load(path);
        IEnumerable<XElement> elements = document.Root != null ? document.Root.Elements("survivalLevelExp") : Enumerable.Empty<XElement>();
        foreach (XElement node in elements) {
            if (!IsFeatureMatch(node)) {
                continue;
            }
            int level = ParseInt(node.Attribute("level") != null ? node.Attribute("level")!.Value : null);
            long reqExp = ParseLong(node.Attribute("reqExp") != null ? node.Attribute("reqExp")!.Value : null);
            if (level <= 0) {
                continue;
            }
            LevelThresholds[level] = reqExp;
        }
    }

    private void LoadRewards(string path, Dictionary<int, SurvivalRewardEntry> target) {
        if (!File.Exists(path)) {
            Logger.Warning("Missing survival reward config: {Path}", path);
            return;
        }

        XDocument document = XDocument.Load(path);
        IEnumerable<XElement> elements = document.Root != null ? document.Root.Elements("survivalPassReward") : Enumerable.Empty<XElement>();
        foreach (XElement node in elements) {
            if (!IsFeatureMatch(node)) {
                continue;
            }

            int id = ParseInt(node.Attribute("id") != null ? node.Attribute("id")!.Value : null, 1);
            if (id != 1) {
                continue;
            }

            int level = ParseInt(node.Attribute("level") != null ? node.Attribute("level")!.Value : null);
            if (level <= 0) {
                continue;
            }

            var grants = new List<SurvivalRewardGrant>();
            AddGrant(node, 1, grants);
            AddGrant(node, 2, grants);
            if (grants.Count == 0) {
                continue;
            }

            target[level] = new SurvivalRewardEntry(level, grants.ToArray());
        }
    }

    private static bool IsFeatureMatch(XElement node) {
        string feature = node.Attribute("feature") != null ? node.Attribute("feature")!.Value : string.Empty;
        return string.IsNullOrEmpty(feature) || string.Equals(feature, "SurvivalContents03", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddGrant(XElement node, int index, IList<SurvivalRewardGrant> grants) {
        string type = node.Attribute("type" + index) != null ? node.Attribute("type" + index)!.Value : string.Empty;
        if (string.IsNullOrWhiteSpace(type)) {
            return;
        }

        string idRaw = node.Attribute("id" + index) != null ? node.Attribute("id" + index)!.Value : string.Empty;
        string valueRaw = node.Attribute("value" + index) != null ? node.Attribute("value" + index)!.Value : string.Empty;
        string countRaw = node.Attribute("count" + index) != null ? node.Attribute("count" + index)!.Value : string.Empty;

        grants.Add(new SurvivalRewardGrant(type.Trim(), idRaw, valueRaw, countRaw));
    }

    private static string FindFile(string baseDir, string fileName) {
        string[] candidates = new[] {
            Path.Combine(baseDir, fileName),
            Path.Combine(baseDir, "config", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "config", fileName)
        };
        foreach (string candidate in candidates) {
            if (File.Exists(candidate)) {
                return candidate;
            }
        }
        return Path.Combine(baseDir, "config", fileName);
    }

    private static int ParseInt(string? value, int fallback = 0) {
        int parsed;
        return int.TryParse(value, out parsed) ? parsed : fallback;
    }

    private static long ParseLong(string? value, long fallback = 0) {
        long parsed;
        return long.TryParse(value, out parsed) ? parsed : fallback;
    }

    private static bool ParseBool(string? value, bool fallback = false) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        bool parsedBool;
        if (bool.TryParse(value, out parsedBool)) {
            return parsedBool;
        }
        int parsedInt;
        if (int.TryParse(value, out parsedInt)) {
            return parsedInt != 0;
        }
        return fallback;
    }
}

public sealed class SurvivalRewardEntry {
    public int Level { get; private set; }
    public SurvivalRewardGrant[] Grants { get; private set; }

    public SurvivalRewardEntry(int level, SurvivalRewardGrant[] grants) {
        Level = level;
        Grants = grants;
    }
}

public sealed class SurvivalRewardGrant {
    public string Type { get; private set; }
    public string IdRaw { get; private set; }
    public string ValueRaw { get; private set; }
    public string CountRaw { get; private set; }

    public SurvivalRewardGrant(string type, string idRaw, string valueRaw, string countRaw) {
        Type = type;
        IdRaw = idRaw ?? string.Empty;
        ValueRaw = valueRaw ?? string.Empty;
        CountRaw = countRaw ?? string.Empty;
    }
}
