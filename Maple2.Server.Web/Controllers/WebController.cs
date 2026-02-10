using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Maple2.Database.Model.Ranking;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Web.Packet;
using Maple2.Tools;
using Maple2.Tools.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using Enum = System.Enum;

namespace Maple2.Server.Web.Controllers;

[Route("")]
public class WebController : ControllerBase {

    private readonly WebStorage webStorage;
    private readonly GameStorage gameStorage;
    private readonly IMemoryCache cache;
    private static readonly TimeSpan RankingCacheDuration = TimeSpan.FromHours(1);

    public WebController(WebStorage webStorage, GameStorage gameStorage, IMemoryCache cache) {
        this.webStorage = webStorage;
        this.gameStorage = gameStorage;
        this.cache = cache;
    }

    [HttpPost("irrq.aspx")]
    public async Task<IResult> Rankings() {
        Stream bodyStream = Request.Body;
        var memoryStream = new MemoryStream();
        await bodyStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0; // reset position to beginning of stream before returning
        if (memoryStream.Length == 0) {
            return Results.BadRequest("Request was empty");
        }
        IByteReader packet = new ByteReader(memoryStream.ToArray());
        var type = packet.Read<GameRankingType>();
        int unknown = packet.ReadInt(); // start page maybe?
        long accountId = packet.ReadLong();
        long characterId = packet.ReadLong();
        long characterIdSearch = packet.ReadLong(); // parameter
        int rankingId = packet.ReadInt(); // boss ranking id like 23200007
        int seasonId = packet.ReadInt(); // defined in season xmls
        string userName = packet.ReadUnicodeStringWithLength(); // search string

        if (!Enum.IsDefined(typeof(GameRankingType), type)) {
            Log.Logger.Debug("Unimplemented ranking type: {Type}", type);
            return Results.BadRequest($"Invalid ranking type: {type}");
        }

        Log.Logger.Debug("[Request]Rankings: type={Type}, unknown={unknown} accountId={AccountId} characterId={CharacterId}, characterIdSearch={characterIdSearch}, rankingId={RankingId}, seasonId={seasonId}, userName={UserName}", type, unknown, accountId, characterId, characterIdSearch, rankingId, seasonId, userName);

        ByteWriter pWriter = type switch {
            GameRankingType.Trophy => Trophy(userName),
            GameRankingType.PersonalTrophy => PersonalTrophy(characterId),
            GameRankingType.GuildTrophy => GuildTrophy(userName),
            GameRankingType.PersonalGuildTrophy => PersonalGuildTrophy(characterId),
            _ => new ByteWriter(),
        };

        return Results.File(ZlibCompress(pWriter), "application/octet-stream");
    }

    [HttpPost("ruq.aspx")]
    public async Task<IResult> Info() {
        Stream bodyStream = Request.Body;
        var memoryStream = new MemoryStream();
        await bodyStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0; // reset position to beginning of stream before returning
        if (memoryStream.Length == 0) {
            return Results.BadRequest("Request was empty");
        }
        IByteReader packet = new ByteReader(memoryStream.ToArray());
        int header = packet.ReadInt(); // 0?
        long accountId = packet.ReadLong();
        long characterId = packet.ReadLong();
        int unknown = packet.ReadInt(); // 5 if mentee ?


        if (unknown != 5) {
            Log.Logger.Debug("Unimplemented info type: {Type}", unknown);
            return Results.BadRequest($"Invalid info type: {unknown}");
        }

        return Results.File(ZlibCompress(MenteeList(accountId, characterId)), "application/octet-stream");
    }

    [HttpPost("urq.aspx")]
    public async Task<IResult> Upload() {
        Stream bodyStream = Request.Body;
        var memoryStream = new MemoryStream();
        await bodyStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0; // reset position to beginning of stream before returning
        if (memoryStream.Length == 0) {
            return Results.BadRequest("Request was empty");
        }

        IByteReader packet = new ByteReader(memoryStream.ToArray());
        packet.ReadInt();
        var type = (UgcType) packet.ReadInt();
        long accountId = packet.ReadLong();
        long characterId = packet.ReadLong();
        long ugcUid = packet.ReadLong();
        int id = packet.ReadInt(); // item id, guild id, others?
        packet.ReadInt();
        packet.ReadLong();

        byte[] fileBytes = packet.ReadBytes(packet.Available);

        Log.Logger.Debug("Upload: type={Type}, accountId={AccountId}, characterId={CharacterId}, ugcUid={UgcUid}, id={Id}", type, accountId, characterId, ugcUid, id);

        UgcResource? resource = null;
        if (ugcUid != 0) {
            using WebStorage.Request db = webStorage.Context();
            resource = db.GetUgc(ugcUid);
            if (resource == null) {
                return Results.NotFound($"{ugcUid} does not exist.");
            }
        }

        if (type is UgcType.ItemIcon or UgcType.Item or UgcType.BlueprintIcon or UgcType.LayoutBlueprint && resource == null) {
            return Results.BadRequest("Invalid UGC resource.");
        }

        return type switch {
            UgcType.ProfileAvatar => UploadProfileAvatar(fileBytes, characterId),
            UgcType.Item or UgcType.Mount or UgcType.Furniture => UploadItem(fileBytes, id, ugcUid, resource!),
            UgcType.ItemIcon => UploadItemIcon(fileBytes, id, ugcUid, resource!),
            UgcType.Banner => UploadBanner(fileBytes, id, ugcUid),
            UgcType.GuildEmblem => HandleGuildEmblem(fileBytes, id, ugcUid),
            UgcType.GuildBanner => HandleGuildBanner(fileBytes, id, ugcUid),
            UgcType.BlueprintIcon => HandleBlueprintIcon(fileBytes, ugcUid, resource!),
            UgcType.LayoutBlueprint => HandleBlueprintPreview(fileBytes, ugcUid, resource!),
            _ => HandleUnknownMode(type),
        };
    }

    #region Ugc
    private static IResult UploadProfileAvatar(byte[] fileBytes, long characterId) {
        string filePath = Path.Combine(Paths.WEB_DATA_DIR, "profiles", characterId.ToString());
        try {
            // Deleting old files in the character folder
            if (Path.Exists(filePath)) {
                Directory.Delete(filePath, true);
            }
            Directory.CreateDirectory(filePath);
        } catch (Exception ex) {
            Log.Error(ex, "Failed preparing directory: {Path}", filePath);
            return Results.Problem("Internal Server Error", statusCode: 500);
        }

        string uniqueFileName = Guid.NewGuid().ToString();
        System.IO.File.WriteAllBytes(Path.Combine(filePath, $"{uniqueFileName}.png"), fileBytes);
        return Results.Text($"0,data/profiles/avatar/{characterId}/{uniqueFileName}.png");
    }

    private IResult UploadItem(byte[] fileBytes, int itemId, long ugcId, UgcResource resource) {
        string filePath = Path.Combine(Paths.WEB_DATA_DIR, "items", itemId.ToString());
        try {
            Directory.CreateDirectory(filePath);
        } catch (Exception ex) {
            Log.Error(ex, "Failed preparing directory: {Path}", filePath);
            return Results.Problem("Internal Server Error", statusCode: 500);
        }
        using WebStorage.Request db = webStorage.Context();
        string ugcPath = $"item/ms2/01/{itemId}/{resource.Id}.m2u";

        db.UpdatePath(ugcId, ugcPath);
        System.IO.File.WriteAllBytes(Path.Combine(filePath, $"{resource.Id}.m2u"), fileBytes);
        return Results.Text($"0,{ugcPath}");
    }

    private IResult UploadItemIcon(byte[] fileBytes, int itemId, long ugcId, UgcResource resource) {
        string filePath = Path.Combine(Paths.WEB_DATA_DIR, "itemicon", itemId.ToString());
        try {
            Directory.CreateDirectory(filePath);
        } catch (Exception ex) {
            Log.Error(ex, "Failed preparing directory: {Path}", filePath);
            return Results.Problem("Internal Server Error", statusCode: 500);
        }
        //TODO: Verify that the item exists in the database
        string ugcPath = $"itemicon/ms2/01/{itemId}/{resource.Id}.png";

        System.IO.File.WriteAllBytes(Path.Combine(filePath, $"{ugcId}.png"), fileBytes);
        return Results.Text($"0,{ugcPath}");
    }

    private IResult UploadBanner(byte[] fileBytes, int bannerId, long ugcId) {
        string filePath = Path.Combine(Paths.WEB_DATA_DIR, "banner", bannerId.ToString());
        try {
            Directory.CreateDirectory(filePath);
        } catch (Exception ex) {
            Log.Error(ex, "Failed preparing directory: {Path}", filePath);
            return Results.Problem("Internal Server Error", statusCode: 500);
        }
        using WebStorage.Request db = webStorage.Context();
        string ugcPath = $"banner/ms2/01/{bannerId}/{ugcId}.m2u";

        db.UpdatePath(ugcId, ugcPath);

        System.IO.File.WriteAllBytes(Path.Combine(filePath, $"{ugcId}.m2u"), fileBytes);
        return Results.Text($"0,{ugcPath}");
    }

    private IResult HandleGuildEmblem(byte[] fileBytes, int guildId, long ugcId) {
        string filePath = Path.Combine(Paths.WEB_DATA_DIR, "guildmark", guildId.ToString());
        try {
            Directory.CreateDirectory(filePath);
        } catch (Exception ex) {
            Log.Error(ex, "Failed preparing directory: {Path}", filePath);
            return Results.Problem("Internal Server Error", statusCode: 500);
        }
        using WebStorage.Request db = webStorage.Context();
        string ugcPath = $"guildmark/ms2/01/{guildId}/{ugcId}.png";

        db.UpdatePath(ugcId, ugcPath);

        System.IO.File.WriteAllBytes(Path.Combine(filePath, $"{ugcId}.png"), fileBytes);
        return Results.Text($"0,{ugcPath}");
    }

    private IResult HandleGuildBanner(byte[] fileBytes, int guildId, long ugcId) {
        string filePath = Path.Combine(Paths.WEB_DATA_DIR, "guildmark", guildId.ToString(), "banner");
        try {
            Directory.CreateDirectory(filePath);
        } catch (Exception ex) {
            Log.Error(ex, "Failed preparing directory: {Path}", filePath);
            return Results.Problem("Internal Server Error", statusCode: 500);
        }
        using WebStorage.Request db = webStorage.Context();
        string ugcPath = $"guildmark/ms2/01/{guildId}/banner/{ugcId}.png";

        db.UpdatePath(ugcId, ugcPath);

        System.IO.File.WriteAllBytes(Path.Combine(filePath, $"{ugcId}.png"), fileBytes);
        return Results.Text($"0,{ugcPath}");
    }

    private IResult HandleBlueprintIcon(byte[] fileBytes, long ugcUid, UgcResource resource) {
        string filePath = Path.Combine(Paths.WEB_DATA_DIR, "blueprint", ugcUid.ToString());
        try {
            Directory.CreateDirectory(filePath);
        } catch (Exception ex) {
            Log.Error(ex, "Failed preparing directory: {Path}", filePath);
            return Results.Problem("Internal Server Error", statusCode: 500);
        }

        string ugcPath = $"blueprint/ms2/01/{ugcUid}/{resource.Id}_icon.png";

        System.IO.File.WriteAllBytes(Path.Combine(filePath, $"{resource.Id}_icon.png"), fileBytes);
        return Results.Text($"0,{ugcPath}");
    }

    private IResult HandleBlueprintPreview(byte[] fileBytes, long ugcUid, UgcResource resource) {
        string filePath = Path.Combine(Paths.WEB_DATA_DIR, "blueprint", ugcUid.ToString());
        try {
            Directory.CreateDirectory(filePath);
        } catch (Exception ex) {
            Log.Error(ex, "Failed preparing directory: {Path}", filePath);
            return Results.Problem("Internal Server Error", statusCode: 500);
        }
        using WebStorage.Request db = webStorage.Context();
        string ugcPath = $"blueprint/ms2/01/{ugcUid}/{resource.Id}.png";

        db.UpdatePath(ugcUid, ugcPath);

        System.IO.File.WriteAllBytes(Path.Combine(filePath, $"{resource.Id}.png"), fileBytes);
        return Results.Text($"0,{ugcPath}");
    }


    private static IResult HandleUnknownMode(UgcType mode) {
        Log.Logger.Warning("Invalid upload mode: {Mode}", mode);
        return Results.BadRequest($"Invalid upload mode: {mode}");
    }
    #endregion

    #region Ranking
    public ByteWriter Trophy(string userName) {
        string cacheKey = $"Trophy_{userName ?? "all"}";

        if (!cache.TryGetValue(cacheKey, out ByteWriter? cachedResult)) {
            List<TrophyRankInfo> rankInfos = [];
            using GameStorage.Request db = gameStorage.Context();
            if (!string.IsNullOrEmpty(userName)) {
                TrophyRankInfo? info = db.GetTrophyRankInfo(userName);
                if (info != null) {
                    rankInfos.Add(info);
                }
            } else {
                IList<TrophyRankInfo> infos = db.GetTrophyRankings();
                rankInfos.AddRange(infos);
            }

            cachedResult = InGameRankPacket.Trophy(rankInfos);
            cache.Set(cacheKey, cachedResult, RankingCacheDuration);
        }

        return cachedResult!;
    }

    public ByteWriter PersonalTrophy(long characterId) {
        string cacheKey = $"PersonalTrophy_{characterId}";

        if (!cache.TryGetValue(cacheKey, out ByteWriter? cachedResult)) {
            using GameStorage.Request db = gameStorage.Context();
            TrophyRankInfo? info = db.GetTrophyRankInfo(characterId);
            cachedResult = InGameRankPacket.PersonalRank(GameRankingType.PersonalTrophy, info?.Rank ?? 0);
            cache.Set(cacheKey, cachedResult, RankingCacheDuration);
        }

        return cachedResult!;
    }

    public ByteWriter GuildTrophy(string userName) {
        if (!string.IsNullOrEmpty(userName)) {
            string cacheKey = $"GuildTrophy_{userName}";

            if (!cache.TryGetValue(cacheKey, out ByteWriter? cachedResult)) {
                // Search by guild name, check full rankings cache first then fall back to DB
                IList<GuildTrophyRankInfo> rankings = GetCachedGuildTrophyRankings();
                GuildTrophyRankInfo? cached = rankings.FirstOrDefault(r =>
                    r.Name.Equals(userName, StringComparison.OrdinalIgnoreCase));

                if (cached != null) {
                    cachedResult = InGameRankPacket.GuildTrophy(new[] { cached });
                } else {
                    using GameStorage.Request db = gameStorage.Context();
                    GuildTrophyRankInfo? info = db.GetGuildTrophyRankInfo(userName);
                    cachedResult = InGameRankPacket.GuildTrophy(info != null ? new[] { info } : []);
                }

                cache.Set(cacheKey, cachedResult, RankingCacheDuration);
            }

            return cachedResult!;
        }

        return InGameRankPacket.GuildTrophy(GetCachedGuildTrophyRankings());
    }

    public ByteWriter PersonalGuildTrophy(long characterId) {
        string cacheKey = $"PersonalGuildTrophy_{characterId}";

        if (!cache.TryGetValue(cacheKey, out ByteWriter? cachedResult)) {
            using GameStorage.Request db = gameStorage.Context();
            long guildId = db.GetGuildIdByCharacterId(characterId);
            if (guildId == 0) {
                cachedResult = InGameRankPacket.PersonalRank(GameRankingType.PersonalGuildTrophy, 0);
            } else {
                // Check cached rankings for this guild's rank
                IList<GuildTrophyRankInfo> rankings = GetCachedGuildTrophyRankings();
                GuildTrophyRankInfo? cached = rankings.FirstOrDefault(r => r.GuildId == guildId);
                cachedResult = InGameRankPacket.PersonalRank(GameRankingType.PersonalGuildTrophy, cached?.Rank ?? 0);
            }

            cache.Set(cacheKey, cachedResult, RankingCacheDuration);
        }

        return cachedResult!;
    }

    private IList<GuildTrophyRankInfo> GetCachedGuildTrophyRankings() {
        const string cacheKey = "GuildTrophyRankings";

        if (!cache.TryGetValue(cacheKey, out IList<GuildTrophyRankInfo>? rankings)) {
            using GameStorage.Request db = gameStorage.Context();
            rankings = db.GetGuildTrophyRankings();
            cache.Set(cacheKey, rankings, RankingCacheDuration);
        }

        return rankings!;
    }
    #endregion

    public ByteWriter MenteeList(long accountId, long characterId) {
        using GameStorage.Request db = gameStorage.Context();
        IList<long> list = db.GetMentorList(accountId, characterId);

        IList<PlayerInfo> players = new List<PlayerInfo>();
        foreach (long menteeId in list) {
            PlayerInfo? playerInfo = db.GetPlayerInfo(menteeId);
            if (playerInfo != null) {
                players.Add(playerInfo);
            }
        }

        return MentorPacket.MenteeList(players);
    }

    private static byte[] ZlibCompress(ByteWriter writer) {
        using var outputStream = new MemoryStream();
        using (var zlibStream = new ZLibStream(outputStream, CompressionLevel.Optimal, leaveOpen: true)) {
            zlibStream.Write(writer.Buffer, 0, writer.Length);
        }

        return outputStream.ToArray();
    }

}
