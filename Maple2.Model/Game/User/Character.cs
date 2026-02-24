using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Tools;

namespace Maple2.Model.Game;

public class Character {
    #region Immutable
    public long CreationTime { get; init; }
    public DateTime LastModified { get; set; }

    public long Id { get; init; }
    public long AccountId { get; init; }
    #endregion

    public long DeleteTime;
    public long LastOnlineTime;

    public required string Name;
    public Gender Gender;
    public int MapId;
    public Job Job;

    public SkinColor SkinColor;
    public short Level = 1;
    public long Exp;
    public long RestExp;

    public int Title;
    public short Insignia;

    public int RoomId;
    public int InstanceMapId;
    public short Channel;
    public short ReturnChannel;

    public long StorageCooldown;
    public long DoctorCooldown;

    public LimitedStack<int> ReturnMaps = new LimitedStack<int>(3);
    public Vector3 ReturnPosition;
    public string Picture = string.Empty;
    public string Motto = string.Empty;
    public string GuildName = string.Empty;
    public long GuildId;
    public int PartyId;
    public List<long> ClubIds = [];
    public required Mastery Mastery;
    public AchievementInfo AchievementInfo;
    public MarriageInfo MarriageInfo = new();
    public readonly Dictionary<int, DungeonEnterLimit> DungeonEnterLimits = [];
    public short DeathCount;
    public long DeathTick;
    public DeathState DeathState;
    public long PremiumTime;
    public MentorRole MentorRole;
}
