using System.Collections.Concurrent;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Config;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Serilog;

namespace Maple2.Server.Game.Manager;

public sealed class SurvivalManager {
    private static readonly Lazy<SurvivalPassXmlConfig> ConfigLoader = new Lazy<SurvivalPassXmlConfig>(SurvivalPassXmlConfig.Load);
    private static SurvivalPassXmlConfig Config {
        get { return ConfigLoader.Value; }
    }

    private readonly GameSession session;
    private readonly ILogger logger = Log.Logger.ForContext<SurvivalManager>();
    private readonly ConcurrentDictionary<MedalType, Dictionary<int, Medal>> inventory;
    private readonly ConcurrentDictionary<MedalType, Medal> equip;

    private int SurvivalLevel {
        get { return session.Player.Value.Account.SurvivalLevel; }
        set { session.Player.Value.Account.SurvivalLevel = value; }
    }

    private long SurvivalExp {
        get { return session.Player.Value.Account.SurvivalExp; }
        set { session.Player.Value.Account.SurvivalExp = value; }
    }

    private int SurvivalSilverLevelRewardClaimed {
        get { return session.Player.Value.Account.SurvivalSilverLevelRewardClaimed; }
        set { session.Player.Value.Account.SurvivalSilverLevelRewardClaimed = value; }
    }

    private int SurvivalGoldLevelRewardClaimed {
        get { return session.Player.Value.Account.SurvivalGoldLevelRewardClaimed; }
        set { session.Player.Value.Account.SurvivalGoldLevelRewardClaimed = value; }
    }

    private bool ActiveGoldPass {
        get { return session.Player.Value.Account.ActiveGoldPass; }
        set { session.Player.Value.Account.ActiveGoldPass = value; }
    }

    public SurvivalManager(GameSession session) {
        this.session = session;
        inventory = new ConcurrentDictionary<MedalType, Dictionary<int, Medal>>();
        equip = new ConcurrentDictionary<MedalType, Medal>();
        foreach (MedalType type in Enum.GetValues<MedalType>()) {
            equip[type] = new Medal(0, type);
            inventory[type] = new Dictionary<int, Medal>();
        }

        using GameStorage.Request db = session.GameStorage.Context();
        List<Medal> medals = db.GetMedals(session.CharacterId);
        foreach (Medal medal in medals) {
            Dictionary<int, Medal> dict;
            if (!inventory.TryGetValue(medal.Type, out dict!)) {
                dict = new Dictionary<int, Medal>();
                inventory[medal.Type] = dict;
            }
            dict[medal.Id] = medal;
            if (medal.Slot != -1) {
                equip[medal.Type] = medal;
            }
        }

        NormalizeProgress();
    }

    public void Load() {
        NormalizeProgress();
        session.Send(SurvivalPacket.UpdateStats(session.Player.Value.Account, GetDisplayExp(), 0));
        session.Send(SurvivalPacket.LoadMedals(inventory, equip));
    }

    public void Save(GameStorage.Request db) {
        var medals = inventory.Values.SelectMany(dict => dict.Values).ToArray();
        db.SaveMedals(session.CharacterId, medals);
    }

    public long GetDisplayExp() {
        long requiredExp = GetRequiredExpForCurrentLevel();
        if (requiredExp <= 0) {
            return 0;
        }

        if (SurvivalExp < 0) {
            return 0;
        }

        return Math.Min(SurvivalExp, requiredExp - 1);
    }

    public void AddPassExp(int amount) {
        if (amount <= 0) {
            return;
        }

        int oldLevel = SurvivalLevel;
        SurvivalExp += amount;

        while (true) {
            long requiredExp = GetRequiredExpForCurrentLevel();
            if (requiredExp <= 0 || SurvivalExp < requiredExp) {
                break;
            }

            SurvivalExp -= requiredExp;
            SurvivalLevel++;
        }

        if (oldLevel != SurvivalLevel) {
            logger.Information("Survival level up account={AccountId} old={OldLevel} new={NewLevel} expInLevel={Exp}", session.Player.Value.Account.Id, oldLevel, SurvivalLevel, SurvivalExp);
        }

        session.Send(SurvivalPacket.UpdateStats(session.Player.Value.Account, GetDisplayExp(), amount));
    }

    public int GetPassExpForNpc(FieldNpc npc) {
        if (npc.Value.IsBoss) {
            return Config.BossKillExp;
        }

        NpcMetadataBasic basic = npc.Value.Metadata.Basic;
        bool hasEliteTag = basic.MainTags.Any(tag => string.Equals(tag, "elite", StringComparison.OrdinalIgnoreCase) || string.Equals(tag, "champion", StringComparison.OrdinalIgnoreCase))
            || basic.SubTags.Any(tag => string.Equals(tag, "elite", StringComparison.OrdinalIgnoreCase) || string.Equals(tag, "champion", StringComparison.OrdinalIgnoreCase));
        bool isElite = basic.RareDegree >= 2 && hasEliteTag;

        return isElite ? Config.EliteKillExp : Config.MonsterKillExp;
    }

    public bool TryUseGoldPassActivationItem(Item item) {
        if (item == null) {
            return false;
        }
        if (Config.ActivationItemId <= 0 || item.Id != Config.ActivationItemId) {
            return false;
        }

        logger.Information("Gold Pass activation requested by item use account={AccountId} itemId={ItemId} uid={Uid} amount={Amount}",
            session.Player.Value.Account.Id, item.Id, item.Uid, item.Amount);
        return TryActivateGoldPass(item);
    }

    public bool TryUsePassExpItem(Item item) {
        if (item == null || item.Metadata.Function?.Type != ItemFunction.SurvivalLevelExp) {
            return false;
        }

        Dictionary<string, string> parameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);
        string expStr;
        int expAmount;
        if (!parameters.TryGetValue("exp", out expStr!) || !int.TryParse(expStr, out expAmount) || expAmount <= 0) {
            return false;
        }

        if (!session.Item.Inventory.Consume(item.Uid, 1)) {
            return true;
        }

        AddPassExp(expAmount);
        return true;
    }

    public bool TryUseSkinItem(Item item) {
        if (item == null || item.Metadata.Function?.Type != ItemFunction.SurvivalSkin) {
            return false;
        }

        AddMedal(item);
        session.Item.Inventory.Consume(item.Uid, 1);
        return true;
    }

    public bool TryActivateGoldPass() {
        if (ActiveGoldPass) {
            logger.Information("Gold Pass already active account={AccountId}", session.Player.Value.Account.Id);
            return true;
        }

        if (Config.ActivationItemId <= 0) {
            if (!Config.AllowDirectActivateWithoutItem) {
                logger.Information("Gold Pass activation rejected: no activation item configured and direct activation disabled account={AccountId}", session.Player.Value.Account.Id);
                return false;
            }

            ActivateGoldPass();
            logger.Information("Gold Pass activated without item account={AccountId}", session.Player.Value.Account.Id);
            return true;
        }

        Item? item = session.Item.Inventory.Find(Config.ActivationItemId).FirstOrDefault();
        if (item == null) {
            logger.Information("Gold Pass activation failed: item not found account={AccountId} itemId={ItemId}", session.Player.Value.Account.Id, Config.ActivationItemId);
            return false;
        }

        return TryActivateGoldPass(item);
    }


    private bool TryActivateGoldPass(Item item) {
        if (ActiveGoldPass) {
            logger.Information("Gold Pass already active account={AccountId}", session.Player.Value.Account.Id);
            return true;
        }
        if (item == null || item.Id != Config.ActivationItemId) {
            logger.Information("Gold Pass activation failed: invalid item account={AccountId} itemId={ItemId} expected={ExpectedItemId}",
                session.Player.Value.Account.Id, item != null ? item.Id : 0, Config.ActivationItemId);
            return false;
        }
        if (item.Amount < Config.ActivationItemCount) {
            logger.Information("Gold Pass activation failed: insufficient item count account={AccountId} itemId={ItemId} have={Have} need={Need}",
                session.Player.Value.Account.Id, item.Id, item.Amount, Config.ActivationItemCount);
            return false;
        }
        if (!session.Item.Inventory.Consume(item.Uid, Config.ActivationItemCount)) {
            logger.Information("Gold Pass activation failed: consume returned false account={AccountId} itemId={ItemId} uid={Uid}",
                session.Player.Value.Account.Id, item.Id, item.Uid);
            return false;
        }

        ActivateGoldPass();
        logger.Information("Gold Pass activated account={AccountId} by itemId={ItemId} uid={Uid}", session.Player.Value.Account.Id, item.Id, item.Uid);
        return true;
    }

    private void ActivateGoldPass() {
        ActiveGoldPass = true;
        session.Send(SurvivalPacket.UpdateStats(session.Player.Value.Account, GetDisplayExp(), 0));
    }

    public bool TryClaimNextReward() {
        NormalizeProgress();

        int nextFree = SurvivalSilverLevelRewardClaimed + 1;
        if (CanClaimReward(nextFree, false)) {
            return TryClaimReward(nextFree, false);
        }

        int nextPaid = SurvivalGoldLevelRewardClaimed + 1;
        if (CanClaimReward(nextPaid, true)) {
            return TryClaimReward(nextPaid, true);
        }

        return false;
    }

    public bool TryClaimReward(int level, bool paidTrack) {
        NormalizeProgress();
        if (!CanClaimReward(level, paidTrack)) {
            return false;
        }

        Dictionary<int, SurvivalRewardEntry> rewards = paidTrack ? Config.PaidRewards : Config.FreeRewards;
        SurvivalRewardEntry entry;
        if (!rewards.TryGetValue(level, out entry!)) {
            if (paidTrack) {
                SurvivalGoldLevelRewardClaimed = level;
            } else {
                SurvivalSilverLevelRewardClaimed = level;
            }
            session.Send(SurvivalPacket.UpdateStats(session.Player.Value.Account, GetDisplayExp(), 0));
            return true;
        }

        foreach (SurvivalRewardGrant grant in entry.Grants) {
            if (!TryGrantReward(grant)) {
                return false;
            }
        }

        if (paidTrack) {
            SurvivalGoldLevelRewardClaimed = level;
        } else {
            SurvivalSilverLevelRewardClaimed = level;
        }

        session.Send(SurvivalPacket.UpdateStats(session.Player.Value.Account, GetDisplayExp(), 0));
        return true;
    }

    private bool CanClaimReward(int level, bool paidTrack) {
        if (level <= 0 || level > SurvivalLevel) {
            return false;
        }
        if (paidTrack && !ActiveGoldPass) {
            return false;
        }
        return paidTrack ? level == SurvivalGoldLevelRewardClaimed + 1 : level == SurvivalSilverLevelRewardClaimed + 1;
    }

    private long GetLevelThreshold(int level) {
        long threshold;
        return Config.LevelThresholds.TryGetValue(level, out threshold) ? threshold : 0;
    }

    private long GetRequiredExpForCurrentLevel() {
        long currentThreshold = GetLevelThreshold(SurvivalLevel);
        long nextThreshold = GetNextLevelThreshold(SurvivalLevel);
        long requiredExp = nextThreshold - currentThreshold;
        return Math.Max(0, requiredExp);
    }

    private long GetNextLevelThreshold(int level) {
        long threshold;
        return Config.LevelThresholds.TryGetValue(level + 1, out threshold) ? threshold : GetLevelThreshold(level);
    }

    private void NormalizeProgress() {
        if (SurvivalLevel <= 0) {
            SurvivalLevel = 1;
        }
        if (SurvivalExp < 0) {
            SurvivalExp = 0;
        }

        while (true) {
            long requiredExp = GetRequiredExpForCurrentLevel();
            if (requiredExp <= 0 || SurvivalExp < requiredExp) {
                break;
            }

            SurvivalExp -= requiredExp;
            SurvivalLevel++;
        }

        if (SurvivalSilverLevelRewardClaimed > SurvivalLevel) {
            SurvivalSilverLevelRewardClaimed = SurvivalLevel;
        }
        if (SurvivalGoldLevelRewardClaimed > SurvivalLevel) {
            SurvivalGoldLevelRewardClaimed = SurvivalLevel;
        }
    }

    private bool TryGrantReward(SurvivalRewardGrant grant) {
        string type = grant.Type.Trim();
        if (string.Equals(type, "additionalEffect", StringComparison.OrdinalIgnoreCase)) {
            logger.Information("Skipping unsupported survival additionalEffect reward id={IdRaw}", grant.IdRaw);
            return true;
        }

        int[] ids = ParseIntArray(grant.IdRaw);
        int[] values = ParseIntArray(grant.ValueRaw);
        int[] counts = ParseIntArray(grant.CountRaw);

        if (string.Equals(type, "genderItem", StringComparison.OrdinalIgnoreCase)) {
            int chosenIndex = session.Player.Value.Character.Gender == Gender.Female ? 1 : 0;
            int itemId = GetValueAt(ids, chosenIndex);
            int rarity = Math.Max(1, GetValueAt(values, chosenIndex, 1));
            int count = Math.Max(1, GetValueAt(counts, chosenIndex, 1));
            return TryGrantItem(itemId, rarity, count);
        }

        if (string.Equals(type, "item", StringComparison.OrdinalIgnoreCase)) {
            int itemId = GetValueAt(ids, 0);
            int rarity = Math.Max(1, GetValueAt(values, 0, 1));
            int count = Math.Max(1, GetValueAt(counts, 0, 1));
            return TryGrantItem(itemId, rarity, count);
        }

        logger.Information("Skipping unsupported survival reward type={Type}", type);
        return true;
    }

    private bool TryGrantItem(int itemId, int rarity, int amount) {
        if (itemId <= 0) {
            return true;
        }
        if (session.Field == null) {
            return false;
        }
        ItemMetadata metadata;
        if (!session.ItemMetadata.TryGet(itemId, out metadata!)) {
            logger.Warning("Missing item metadata for survival reward itemId={ItemId}", itemId);
            return false;
        }

        Item? item = session.Field.ItemDrop.CreateItem(itemId, rarity, amount);
        if (item == null) {
            logger.Warning("Failed to create survival reward item itemId={ItemId}", itemId);
            return false;
        }
        return session.Item.Inventory.Add(item, true);
    }

    private static int[] ParseIntArray(string csv) {
        if (string.IsNullOrWhiteSpace(csv)) {
            return Array.Empty<int>();
        }
        return csv.Split(',').Select(part => {
            int parsed;
            return int.TryParse(part, out parsed) ? parsed : 0;
        }).ToArray();
    }

    private static int GetValueAt(int[] values, int index, int fallback = 0) {
        if (values.Length == 0) {
            return fallback;
        }
        if (index < values.Length) {
            return values[index];
        }
        return values[values.Length - 1];
    }

    public void AddMedal(Item item) {
        Dictionary<string, string> parameters = XmlParseUtil.GetParameters(item.Metadata.Function != null ? item.Metadata.Function.Parameters : null);
        string idStr;
        int id;
        if (!parameters.TryGetValue("id", out idStr!) || !int.TryParse(idStr, out id)) {
            logger.Warning("Failed to add medal: missing or invalid ID parameter");
            return;
        }

        string typeStr;
        if (!parameters.TryGetValue("type", out typeStr!)) {
            logger.Warning("Failed to add medal: missing or invalid type parameter");
            return;
        }

        MedalType type = typeStr switch {
            "effectTail" => MedalType.Tail,
            "gliding" => MedalType.Gliding,
            "riding" => MedalType.Riding,
            _ => throw new InvalidOperationException("Invalid medal type: " + typeStr),
        };

        long expiryTime = DateTime.MaxValue.ToEpochSeconds() - 1;
        string durationStr;
        if (parameters.TryGetValue("durationSec", out durationStr!) && int.TryParse(durationStr, out int durationSec)) {
            expiryTime = (long)(DateTime.Now.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds + durationSec;
        } else {
            string endDateStr;
            if (parameters.TryGetValue("endDate", out endDateStr!) && DateTime.TryParseExact(endDateStr, "yyyy-MM-dd-HH-mm-ss", null, System.Globalization.DateTimeStyles.None, out DateTime endDate)) {
                expiryTime = endDate.ToEpochSeconds();
            }
        }

        Medal existing;
        if (inventory[type].TryGetValue(id, out existing!)) {
            existing.ExpiryTime = Math.Min(existing.ExpiryTime + expiryTime, DateTime.MaxValue.ToEpochSeconds() - 1);
            session.Send(SurvivalPacket.LoadMedals(inventory, equip));
            return;
        }

        Medal? medal = CreateMedal(id, type, expiryTime);
        if (medal == null) {
            return;
        }

        Dictionary<int, Medal> dict;
        if (!inventory.TryGetValue(medal.Type, out dict!)) {
            dict = new Dictionary<int, Medal>();
            inventory[medal.Type] = dict;
        }

        dict[medal.Id] = medal;
        session.Send(SurvivalPacket.LoadMedals(inventory, equip));
    }

    public bool Equip(MedalType type, int id) {
        if (!Enum.IsDefined(type)) {
            return false;
        }

        if (id == 0) {
            Unequip(type);
            session.Send(SurvivalPacket.LoadMedals(inventory, equip));
            return true;
        }

        Medal medal;
        if (!inventory[type].TryGetValue(id, out medal!)) {
            return false;
        }

        if (medal.Slot != -1) {
            return false;
        }

        if (equip[type].Id != 0) {
            Medal equipped = equip[type];
            equipped.Slot = -1;
        }

        equip[type] = medal;
        medal.Slot = (short)type;
        session.Send(SurvivalPacket.LoadMedals(inventory, equip));
        return true;
    }

    private void Unequip(MedalType type) {
        Medal medal = equip[type];
        equip[type] = new Medal(0, type);
        medal.Slot = -1;
    }

    private Medal? CreateMedal(int id, MedalType type, long expiryTime) {
        var medal = new Medal(id, type) { ExpiryTime = expiryTime };
        using GameStorage.Request db = session.GameStorage.Context();
        return db.CreateMedal(session.CharacterId, medal);
    }
}
