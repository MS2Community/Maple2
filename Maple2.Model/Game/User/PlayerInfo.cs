using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Model.Game;

public class PlayerInfo : CharacterInfo, IPlayerInfo, IByteSerializable {
    // Home/Plot
    public string HomeName { get; set; }
    public int PlotMapId { get; set; }
    public int PlotNumber { get; set; }
    public int ApartmentNumber { get; set; }
    public long PlotExpiryTime { get; set; }
    // Trophy
    public AchievementInfo AchievementInfo { get; set; }
    // Premium
    public long PremiumTime { get; set; }
    public List<long> ClubIds { get; set; }
    public Dictionary<int, DungeonEnterLimit> DungeonEnterLimits { get; set; }
    public AdminPermissions AccountAdminPermissions { get; set; }

    public static implicit operator PlayerInfo(Player player) {
        return new PlayerInfo(player, player.Home.Name, player.Character.AchievementInfo, player.Character.ClubIds) {
            PlotMapId = player.Home.PlotMapId,
            PlotNumber = player.Home.PlotNumber,
            ApartmentNumber = player.Home.ApartmentNumber,
            PlotExpiryTime = player.Home.PlotExpiryTime,
            AchievementInfo = player.Character.AchievementInfo,
            PremiumTime = player.Character.PremiumTime,
            LastOnlineTime = player.Character.LastOnlineTime,
            DungeonEnterLimits = player.Character.DungeonEnterLimits,
            GuildId = player.Character.GuildId,
            GuildName = player.Character.GuildName,
            MentorRole = player.Character.MentorRole,
            Channel = player.Character.Channel,
        };
    }

    public PlayerInfo(CharacterInfo character, string homeName, AchievementInfo achievementInfo, IList<long> clubsIds) : base(character) {
        HomeName = string.IsNullOrWhiteSpace(homeName) ? "Unknown" : homeName;
        AchievementInfo = achievementInfo;
        ClubIds = new List<long>(clubsIds);
        DungeonEnterLimits = [];
        MentorRole = character.MentorRole;
        Channel = character.Channel;
    }

    public PlayerInfo Clone() {
        return (PlayerInfo) MemberwiseClone();
    }

    public void WriteTo(IByteWriter writer) {
        writer.WriteLong(AccountId);
        writer.WriteLong(CharacterId);
        writer.WriteUnicodeString(Name);
        writer.Write<Gender>(Gender);
        writer.WriteByte(1);
        writer.WriteLong(AccountId);
        writer.WriteInt(1);
        writer.WriteInt(MapId);
        writer.WriteInt(MapId);
        writer.WriteInt(PlotMapId);
        writer.WriteShort(Level);
        writer.WriteShort(Channel);
        writer.WriteInt((int) Job.Code());
        writer.Write<Job>(Job);
        writer.WriteInt((int) TotalHp);
        writer.WriteInt((int) CurrentHp);
        writer.Write<DeathState>(DeathState);
        writer.WriteLong();
        writer.WriteLong(); // Home Storage Access Time
        writer.WriteLong(); // Home Doctor Access Time
        writer.WriteInt(); // Outside Map Id
        writer.Write<Vector3>(default); // Outside Position
        writer.WriteInt(GearScore);
        writer.Write<SkinColor>(default);
        writer.WriteLong();
        writer.Write<AchievementInfo>(default);
        writer.WriteLong(GuildId);
        writer.WriteUnicodeString(GuildName);
        writer.WriteUnicodeString(Motto);
        writer.WriteUnicodeString(Picture);
        writer.WriteByte((byte) ClubIds.Count);
        foreach (long clubId in ClubIds) {
            bool unk = true;
            writer.WriteBool(unk);
            if (unk) {
                writer.WriteLong(clubId);
                writer.WriteUnicodeString(); // club name
            }
        }
        writer.WriteByte();
        writer.WriteClass<Mastery>(new Mastery());
        writer.WriteUnicodeString(); // Login username
        writer.WriteLong(); // Session Id
        writer.WriteLong();
        writer.WriteLong();
        writer.WriteInt();
        writer.Write<MentorRole>(MentorRole);
        writer.WriteBool(false);
        writer.WriteLong(); // Birthday
        writer.WriteInt(); // SuperChatId
        writer.WriteInt();
        writer.WriteLong(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        writer.WriteInt();
        writer.WriteLong();
        writer.WriteInt();
        writer.WriteInt();
        writer.WriteShort();
        writer.WriteLong();
    }
}

public class CharacterInfo {
    public long AccountId { get; }
    public long CharacterId { get; }

    public string Name { get; set; }
    public string Motto { get; set; }
    public string Picture { get; set; }
    public Gender Gender { get; set; }
    public Job Job { get; set; }
    public short Level { get; set; }
    public int GearScore { get; set; }
    // Health
    public long CurrentHp { get; set; }
    public long TotalHp { get; set; }
    public DeathState DeathState { get; set; }
    // Location
    public int MapId { get; set; }
    public short Channel { get; set; } = -1;
    public long LastOnlineTime { get; set; }
    public int RetryHeartbeat { get; set; } = 3;
    // Guild
    public long GuildId { get; set; }
    public string GuildName { get; set; } = string.Empty;

    public long UpdateTime { get; set; }
    public bool Online => Channel >= 0;
    public MentorRole MentorRole { get; set; }

    public CharacterInfo(long accountId, long characterId, string name, string motto, string picture, Gender gender, Job job, short level) {
        AccountId = accountId;
        CharacterId = characterId;
        Name = name;
        Motto = motto;
        Picture = picture;
        Gender = gender;
        Job = job;
        Level = level;
    }

    public CharacterInfo(CharacterInfo other) {
        AccountId = other.AccountId;
        CharacterId = other.CharacterId;
        Name = other.Name;
        Motto = other.Motto;
        Picture = other.Picture;
        Gender = other.Gender;
        Job = other.Job;
        Level = other.Level;
        MapId = other.MapId;
        Channel = other.Channel;
        LastOnlineTime = other.LastOnlineTime;
        GuildId = other.GuildId;
        GuildName = other.GuildName;
        MentorRole = other.MentorRole;
    }

    public static implicit operator CharacterInfo(Player player) {
        return new CharacterInfo(
            accountId: player.Account.Id,
            characterId: player.Character.Id,
            name: player.Character.Name,
            motto: player.Character.Motto,
            picture: player.Character.Picture,
            gender: player.Character.Gender,
            job: player.Character.Job,
            level: player.Character.Level) {
            MapId = player.Character.MapId,
            Channel = player.Character.Channel,
            GuildId = player.Character.GuildId,
            GuildName = player.Character.GuildName,
            MentorRole = player.Character.MentorRole,
        };
    }
}
