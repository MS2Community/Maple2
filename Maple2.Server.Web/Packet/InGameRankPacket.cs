using System;
using System.Collections.Generic;
using Maple2.Database.Model.Ranking;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Web.Packet;

public static class InGameRankPacket {
    public static ByteWriter Trophy(IList<TrophyRankInfo> rankInfos) {
        var pWriter = new ByteWriter();
        pWriter.Write<GameRankingType>(GameRankingType.Trophy);
        pWriter.WriteInt(1); // Mode ?
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(rankInfos.Count);
        foreach (TrophyRankInfo info in rankInfos) {
            pWriter.WriteInt(info.Rank);
            pWriter.WriteLong(info.CharacterId);
            pWriter.WriteUnicodeStringWithLength(info.Name);
            pWriter.WriteUnicodeStringWithLength(info.Profile);
            pWriter.WriteInt(info.Trophy.Total);
            pWriter.Write<AchievementInfo>(info.Trophy);
        }

        return pWriter;
    }

    public static ByteWriter PersonalRank(GameRankingType type, int rank) {
        var pWriter = new ByteWriter();
        pWriter.Write<GameRankingType>(type);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(rank);

        return pWriter;
    }

    public static ByteWriter GuildTrophy(IList<GuildTrophyRankInfo> rankInfos) {
        var pWriter = new ByteWriter();
        pWriter.Write<GameRankingType>(GameRankingType.GuildTrophy);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(rankInfos.Count);

        foreach (GuildTrophyRankInfo info in rankInfos) {
            pWriter.WriteInt(info.Rank);
            pWriter.WriteLong(info.GuildId);
            pWriter.WriteUnicodeStringWithLength(info.Name);
            pWriter.WriteUnicodeStringWithLength(info.Emblem);
            pWriter.WriteUnicodeStringWithLength(info.LeaderName);
            pWriter.WriteInt(info.Trophy.Total);
            pWriter.WriteInt(info.Trophy.Combat);
            pWriter.WriteInt(info.Trophy.Adventure);
            pWriter.WriteInt(info.Trophy.Lifestyle);
        }
        return pWriter;
    }

    // Can be used for previous season
    public static ByteWriter DarkDescent(GameRankingType type) {
        var pWriter = new ByteWriter();
        pWriter.Write<GameRankingType>(type);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(1); // count

        pWriter.WriteInt(0); // rank
        pWriter.WriteLong(0); // characterId
        pWriter.WriteUnicodeStringWithLength(string.Empty); // name
        pWriter.WriteUnicodeStringWithLength(string.Empty); // profile
        pWriter.WriteInt((int) JobCode.None); // jobCode
        pWriter.WriteInt(); // level
        pWriter.WriteLong(); // account id?
        pWriter.WriteUnicodeStringWithLength(); // timeStamp


        return pWriter;
    }

    public static ByteWriter Pvp(GameRankingType type) {
        var pWriter = new ByteWriter();
        pWriter.Write<GameRankingType>(type);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(1); // count

        pWriter.WriteInt(0); // rank
        pWriter.WriteLong(0); // characterId
        pWriter.WriteUnicodeStringWithLength(string.Empty); // name
        pWriter.WriteUnicodeStringWithLength(string.Empty); // profile
        pWriter.WriteInt(); // score
        pWriter.WriteInt(); // rank tier

        return pWriter;
    }

    public static ByteWriter Ugc(GameRankingType type) {
        var pWriter = new ByteWriter();
        pWriter.Write<GameRankingType>(type);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(1); // count

        pWriter.WriteInt(1); // rank
        pWriter.WriteLong(); // 0 ?
        pWriter.WriteUnicodeStringWithLength(string.Empty); // user name
        pWriter.WriteUnicodeStringWithLength(string.Empty); // user profile
        pWriter.WriteInt(); // score
        pWriter.WriteInt(); // 0 ?? perhaps used as a bool to disable/enable guests from entering
        pWriter.WriteLong(); // home id
        pWriter.WriteUnicodeStringWithLength(string.Empty); // home name

        return pWriter;
    }

    public static ByteWriter RaidClear(GameRankingType type) {
        var pWriter = new ByteWriter();
        pWriter.Write<GameRankingType>(type);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(1); // count

        pWriter.WriteInt(1); // rank
        pWriter.WriteLong(); // character id
        pWriter.WriteUnicodeStringWithLength(string.Empty); // name
        pWriter.WriteUnicodeStringWithLength(string.Empty); // profile
        pWriter.WriteInt(); // boss ranking ID
        pWriter.WriteInt(); // total clears
        pWriter.WriteInt((int) JobCode.None); // job code
        pWriter.WriteInt(); // level

        return pWriter;
    }

    public static ByteWriter RaidEarlyVictory() {
        var pWriter = new ByteWriter();
        pWriter.Write<GameRankingType>(GameRankingType.RaidEarlyVictory);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(1); // count

        pWriter.WriteInt(1); // rank
        pWriter.WriteLong(); // dungeon record id?
        pWriter.WriteLong(); // party leader character id
        pWriter.WriteUnicodeStringWithLength(string.Empty); // name
        pWriter.WriteUnicodeString(string.Empty); // clear time 00:01:39
        pWriter.WriteInt(); // boss ranking ID
        pWriter.WriteInt(); // clear in seconds ?
        pWriter.WriteUnicodeStringWithLength(); // clear timestamp
        pWriter.WriteInt(1); // party member count

        pWriter.WriteLong(); // account id ?
        pWriter.WriteUnicodeStringWithLength(string.Empty); // name
        pWriter.WriteUnicodeStringWithLength(string.Empty); // picture
        pWriter.WriteInt(); // level
        pWriter.Write<Job>(Job.Newbie); // job (not jobcode)

        return pWriter;

    }

    public static ByteWriter RaidShortestTime() {
        var pWriter = new ByteWriter();
        pWriter.Write<GameRankingType>(GameRankingType.RaidShortestTime);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(1); // count

        pWriter.WriteInt(1); // rank
        pWriter.WriteLong(); // character id
        pWriter.WriteUnicodeStringWithLength(string.Empty); // name
        pWriter.WriteUnicodeStringWithLength(string.Empty); // profile
        pWriter.WriteInt(); // boss ranking ID
        pWriter.WriteInt(); // clear in seconds
        pWriter.WriteUnicodeStringWithLength(); // clear timestamp

        return pWriter;
    }

    public static ByteWriter Arcade(GameRankingType type) {
        var pWriter = new ByteWriter();
        pWriter.Write<GameRankingType>(type);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(1); // count

        pWriter.WriteInt(1); // rank
        pWriter.WriteLong(); // character id
        pWriter.WriteUnicodeStringWithLength(string.Empty); // name
        pWriter.WriteUnicodeStringWithLength(string.Empty); // profile
        pWriter.WriteInt(); // arcade id
        pWriter.WriteInt(); // season id
        pWriter.WriteInt((int) JobCode.None); // job code
        pWriter.WriteInt(); // level
        pWriter.WriteInt(); // score
        pWriter.WriteUnicodeStringWithLength(); // timestamp

        return pWriter;
    }

    public static ByteWriter FortressRumbleSRankClear() {
        var pWriter = new ByteWriter();
        pWriter.Write<GameRankingType>(GameRankingType.FortressRumbleSRankClear);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(1); // count

        pWriter.WriteInt(1); // rank
        pWriter.WriteLong(); // character id
        pWriter.WriteUnicodeStringWithLength(string.Empty); // name
        pWriter.WriteUnicodeStringWithLength(string.Empty); // profile
        pWriter.WriteInt(); // fortress/dungeon id
        pWriter.WriteInt(); // score
        pWriter.WriteInt((int) JobCode.None);

        return pWriter;

    }

    public static ByteWriter FortressRumbleEarlyVictory() {
        var pWriter = new ByteWriter();
        pWriter.Write<GameRankingType>(GameRankingType.FortressRumbleEarlyVictory);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(1); // count

        pWriter.WriteInt(1); // rank
        pWriter.WriteLong(); // character id
        pWriter.WriteLong(); // account id
        pWriter.WriteUnicodeStringWithLength(string.Empty); // name
        pWriter.WriteUnicodeStringWithLength(string.Empty); // profile
        pWriter.WriteInt(); // level
        pWriter.Write<Job>(Job.Newbie); // job
        pWriter.WriteInt(); // fortress/dungeon id
        pWriter.WriteInt(); // clear in seconds
        pWriter.WriteUnicodeStringWithLength(); // clear timestamp

        return pWriter;
    }

    // same as above..
    public static ByteWriter FortressRumbleShortestTime() {
        var pWriter = new ByteWriter();
        pWriter.Write<GameRankingType>(GameRankingType.FortressRumbleShortestTime);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(1); // count

        pWriter.WriteInt(1); // rank
        pWriter.WriteLong(); // character id
        pWriter.WriteLong(); // account id
        pWriter.WriteUnicodeStringWithLength(string.Empty); // name
        pWriter.WriteUnicodeStringWithLength(string.Empty); // profile
        pWriter.WriteInt(); // level
        pWriter.Write<Job>(Job.Newbie); // job
        pWriter.WriteInt(); // fortress/dungeon id
        pWriter.WriteInt(); // clear in seconds
        pWriter.WriteUnicodeStringWithLength(); // clear timestamp

        return pWriter;
    }

    public static ByteWriter Colosseum() {
        var pWriter = new ByteWriter();
        pWriter.Write<GameRankingType>(GameRankingType.Colosseum);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(1); // count

        pWriter.WriteInt(1); // rank
        pWriter.WriteLong(); // character id
        pWriter.WriteUnicodeStringWithLength(string.Empty); // name
        pWriter.WriteUnicodeStringWithLength(string.Empty); // profile
        pWriter.WriteInt(); // colosseum id
        pWriter.WriteInt((int) JobCode.None); // job code
        pWriter.WriteInt(); // unknown value
        pWriter.WriteInt(); // season/week id
        pWriter.WriteInt(); // rounds cleared
        pWriter.WriteInt(); // clear in seconds
        pWriter.WriteInt(); // level
        pWriter.WriteUnicodeStringWithLength(); // clear timestamp
        pWriter.WriteUnicodeStringWithLength(); // clear timestamp again

        return pWriter;

    }
}
