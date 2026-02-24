using System.Net;
using Grpc.Core;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Game.Party;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Maple2.Tools.Extensions;
using Serilog;
using MigrationType = Maple2.Server.World.Service.MigrationType;

namespace Maple2.Server.Game.Manager;

public class DungeonManager {
    private readonly GameSession session;

    public IDictionary<int, DungeonRecord> Records { get; set; }
    private IDictionary<int, DungeonEnterLimit> EnterLimits => session.Player.Value.Character.DungeonEnterLimits;
    private IDictionary<int, DungeonRankReward> RankRewards => session.Player.Value.Unlock.DungeonRankRewards;

    public DungeonFieldManager? Lobby { get; private set; }
    public DungeonRoomMetadata? Metadata;
    public DungeonRoomRecord? DungeonRoomRecord => Lobby?.DungeonRoomRecord;
    public DungeonUserRecord? UserRecord;
    private int LobbyRoomId { get; set; }

    private Party? Party => session.Party.Party;

    private readonly ILogger logger = Log.Logger.ForContext<DungeonManager>();

    public DungeonManager(GameSession session) {
        this.session = session;
        Records = new Dictionary<int, DungeonRecord>();
        Init();
    }

    private void Init() {
        using GameStorage.Request db = session.GameStorage.Context();
        Records = db.GetDungeonRecords(session.CharacterId);

        foreach (DungeonRoomMetadata metadata in session.TableMetadata.DungeonRoomTable.Entries.Values) {
            if (!Records.TryGetValue(metadata.Id, out DungeonRecord? record)) {
                record = new DungeonRecord(metadata.Id);
                record = db.CreateDungeonRecord(record, session.CharacterId);
                if (record == null) {
                    logger.Error("Failed to create dungeon record for dungeonId {dungeonId}", metadata.Id);
                    continue;
                }
                Records.Add(metadata.Id, record);
            }

            DungeonEnterLimit limit = GetEnterLimit(metadata);
            EnterLimits[metadata.Id] = limit;
        }
    }

    public void Load() {
        session.Send(DungeonRoomPacket.Load(Records));
        session.Send(DungeonRoomPacket.RankRewards(RankRewards));
        session.Send(FieldEntrancePacket.Load(EnterLimits));
    }

    public void ResetDailyClears() {
        foreach (DungeonRecord record in Records.Values) {
            record.UnionSubClears = 0;
            record.ExtraSubClears = 0;
        }
        using GameStorage.Request db = session.GameStorage.Context();
        db.SaveDungeonRecords(session.CharacterId, Records.Values.ToArray());
        session.Send(DungeonRoomPacket.Load(Records));
    }

    public void ResetWeeklyClears() {
        foreach (DungeonRecord record in Records.Values) {
            record.UnionClears = 0;
            record.ExtraClears = 0;
        }
        using GameStorage.Request db = session.GameStorage.Context();
        db.SaveDungeonRecords(session.CharacterId, Records.Values.ToArray());
        session.Send(DungeonRoomPacket.Load(Records));
    }

    public void LoadField() {
        if (Lobby == null || session.Field is not DungeonFieldManager) {
            return;
        }

        if (session.Field.FieldInstance.Type == InstanceType.DungeonLobby) {
            session.Send(DungeonWaitingPacket.Set(Lobby.DungeonId, Lobby.Size));
        } else {
            // DEBUG
            /*session.Send(RoomStageDungeonPacket.Set(Metadata.Id));
            ByteWriter pWriter = Packet.Of(SendOp.DungeonMission);
            pWriter.WriteByte(0);
            pWriter.WriteInt(1);
            pWriter.WriteInt(23023005);
            pWriter.WriteShort(1);
            pWriter.WriteShort(); // counter
            session.Send(pWriter);*/
        }
    }

    public void UpdateDungeonEnterLimit() {
        bool updated = false;
        foreach (int dungeonId in EnterLimits.Keys) {
            if (!session.TableMetadata.DungeonRoomTable.Entries.TryGetValue(dungeonId, out DungeonRoomMetadata? metadata)) {
                continue;
            }

            DungeonEnterLimit newLimit = GetEnterLimit(metadata);
            if (EnterLimits[dungeonId] != newLimit) {
                EnterLimits[dungeonId] = newLimit;
                updated = true;
            }
        }

        if (updated) {
            session.PlayerInfo.SendUpdate(new PlayerUpdateRequest {
                AccountId = session.AccountId,
                CharacterId = session.CharacterId,
                DungeonEnterLimits = {
                    EnterLimits.Select(dungeon => new DungeonEnterLimitUpdate {
                        DungeonId = dungeon.Key,
                        Limit = (int) dungeon.Value,
                    })
                },
                Async = true,
            });
            session.Send(FieldEntrancePacket.Load(EnterLimits));
        }
    }

    private DungeonEnterLimit GetEnterLimit(DungeonRoomMetadata metadata) {
        DungeonRoomLimitMetadata limitMetadataMetadata = metadata.Limit;
        if (limitMetadataMetadata.MinLevel > session.Player.Value.Character.Level) {
            return DungeonEnterLimit.MinLevel;
        }

        if (limitMetadataMetadata.RequiredAchievementId > 0 && !session.Achievement.HasAchievement(limitMetadataMetadata.RequiredAchievementId)) {
            return DungeonEnterLimit.Achievement;
        }

        if (limitMetadataMetadata.VipOnly && !session.Config.IsPremiumClubActive()) {
            return DungeonEnterLimit.Vip;
        }

        if (limitMetadataMetadata.GearScore > 0 && session.Stats.Values.GearScore < limitMetadataMetadata.GearScore) {
            return DungeonEnterLimit.Gearscore;
        }

        if (limitMetadataMetadata.ClearDungeonIds.Length > 0) {
            foreach (int clearDungeonId in limitMetadataMetadata.ClearDungeonIds) {
                DungeonRecord clearDungeonRecord = GetRecord(clearDungeonId);
                if (clearDungeonRecord.ClearTimestamp == 0) {
                    return DungeonEnterLimit.DungeonClear;
                }
            }
        }

        if (limitMetadataMetadata.Buffs.Length > 0 && limitMetadataMetadata.Buffs.Any(buffId => !session.Buffs.HasBuff(buffId))) {
            return DungeonEnterLimit.Buff;
        }

        if (limitMetadataMetadata.EquippedRecommendedWeapon) {
            if (!session.Item.Equips.Gear.TryGetValue(EquipSlot.RH, out Item? item)) {
                return DungeonEnterLimit.RecommendedWeapon;
            }
            switch (session.Player.Value.Character.Job.Code()) {
                case JobCode.Newbie:
                    if (!item.Type.IsBludgeon) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.Knight:
                    if (!item.Type.IsLongsword) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.Berserker:
                    if (!item.Type.IsGreatsword) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.Wizard:
                    if (!item.Type.IsStaff) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.Priest:
                    if (!item.Type.IsScepter) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.Archer:
                    if (!item.Type.IsBow) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.HeavyGunner:
                    if (!item.Type.IsCannon) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.Assassin:
                    if (!item.Type.IsThrowingStar) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.Thief:
                    if (!item.Type.IsDagger) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.RuneBlader:
                    if (!item.Type.IsBlade) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.Striker:
                    if (!item.Type.IsKnuckle) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.SoulBinder:
                    if (!item.Type.IsOrb) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
            }
        }

        if (GetRecord(metadata.Id).Flag.HasFlag(DungeonRecordFlag.Veteran)) {
            return DungeonEnterLimit.Veteran;
        }

        return DungeonEnterLimit.Rookie;
    }

    public void SetDungeon(DungeonFieldManager field) {
        Lobby = field;
        LobbyRoomId = field.RoomId;
        Metadata = field.DungeonMetadata;
        UserRecord = new DungeonUserRecord(field.DungeonId, session.CharacterId);

        foreach (int missionId in Metadata.UserMissions) {
            //TODO
        }

        field.DungeonRoomRecord.UserResults.TryAdd(session.CharacterId, UserRecord);
    }

    public void CreateDungeonRoom(int dungeonId, bool withParty) {
        int size = 1;
        if (!session.TableMetadata.DungeonRoomTable.Entries.TryGetValue(dungeonId, out DungeonRoomMetadata? metadata)) {
            session.Send(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_notOpenTimeDungeon));
            return;
        }

        if (EnterLimits.TryGetValue(dungeonId, out DungeonEnterLimit limit) && limit != DungeonEnterLimit.Veteran && limit != DungeonEnterLimit.Rookie) {
            session.Send(FieldEntrancePacket.Error(dungeonId, limit));
            return;
        }

        if (withParty) {
            if (Party == null) {
                session.Send(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_error_invalidPartyOID));
                return;
            }
            if (Party.LeaderCharacterId != session.CharacterId) {
                session.Send(DungeonRoomPacket.Error(DungeonRoomError.s_room_party_err_not_chief));
                return;
            }
            size = Party.Members.Count;
        }

        try {
            FieldResponse response = session.World.Field(new FieldRequest {
                CreateDungeon = new FieldRequest.Types.CreateDungeon {
                    DungeonId = dungeonId,
                    Size = size,
                    PartyId = session.Party.Id,
                },
            });
            if ((MigrationError) response.Error != MigrationError.ok) {
                session.Send(MigrationPacket.GameToGameError((MigrationError) response.Error));
                return;
            }

            LobbyRoomId = response.RoomId;
        } catch (RpcException) {
            session.Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_no_server));
            return;
        }

        if (withParty) {
            var request = new PartyRequest {
                RequestorId = session.CharacterId,
                SetDungeon = new PartyRequest.Types.SetDungeon {
                    PartyId = Party!.Id,
                    DungeonId = dungeonId,
                    DungeonRoomId = LobbyRoomId,
                    Set = true,
                },
            };

            try {
                session.World.Party(request);
            } catch (RpcException) { }
        }

        Metadata = metadata;
        MigrateToDungeon();
    }

    public void EnterLobby() {
        if (Party == null) {
            session.Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_NotFoundParty));
            return;
        }

        MigrateToDungeon();
    }

    public void EnterInitField() {
        if (Lobby == null || Metadata == null || Lobby.RoomFields.IsEmpty) {
            logger.Error("Field is null, cannot enter dungeon");
            return;
        }

        if (!Lobby.RoomFields.TryGetValue(Metadata.FieldIds[0], out DungeonFieldManager? firstField)) {
            logger.Error("First field is null, cannot enter dungeon");
            return;
        }

        if (firstField.DungeonRoomRecord.StartTick == 0) {
            firstField.DungeonRoomRecord.StartTick = Lobby.FieldTick;
        }
        session.Send(session.PrepareField(firstField.MapId, roomId: firstField.RoomId) ? FieldEnterPacket.Request(session.Player) :
            FieldEnterPacket.Error(MigrationError.s_move_err_default));
    }

    public void SetDungeon(int dungeonId, int roomId, bool set) {
        if (set) {
            if (!session.TableMetadata.DungeonRoomTable.Entries.TryGetValue(dungeonId, out DungeonRoomMetadata? metadata)) {
                logger.Error("Dungeon metadata not found for dungeonId {dungeonId}", dungeonId);
                return;
            }

            Metadata = metadata;
            LobbyRoomId = roomId;
        } else {
            Metadata = null;
            LobbyRoomId = 0;
        }
    }

    public void CompleteDungeon(long clearTimestamp) {
        if (Lobby == null || Metadata == null || UserRecord == null) {
            return;
        }
        DungeonRoomRewardMetadata metadata = Metadata.Reward;
        UserRecord.IsDungeonSuccess = Lobby.DungeonRoomRecord.State == DungeonState.Clear;
        UserRecord.TotalSeconds = (int) (Lobby.DungeonRoomRecord.EndTick - Lobby.DungeonRoomRecord.StartTick) / 1000;

        CalculateRewards(clearTimestamp);

        session.Send(DungeonRewardPacket.Dungeon(UserRecord));
    }

    private void CalculateRewards(long clearTimeStamp) {
        if (Lobby == null || Metadata == null || UserRecord == null) {
            return;
        }
        DungeonRecord record = GetRecord(Metadata.Id);
        List<int> rewardIds = [];

        if (Metadata.Reward.UnionRewardId > 0) {
            if (!session.TableMetadata.DungeonRoomTable.Entries.TryGetValue(Metadata.Reward.UnionRewardId, out DungeonRoomMetadata? unionMetadata)) {
                return;
            }
            DungeonRecord unionRecord = GetRecord(Metadata.Reward.UnionRewardId);
            // Ensure player can get rewards
            if (GetDailyClearCount(unionRecord) < unionMetadata.Reward.SubRewardCount && GetWeeklyClearCount(unionRecord) < unionMetadata.Reward.Count) {
                UpdateUnionRecord(unionRecord, unionMetadata, clearTimeStamp);
                UserRecord.Flag |= DungeonBonusFlag.Clear;
            }

            if (session.TableMetadata.DungeonConfigTable.UnitedWeeklyRewards.TryGetValue(GetWeeklyClearCount(unionRecord), out int rewardId)) {
                UserRecord.Flag |= DungeonBonusFlag.UnitedWeekly;
                rewardIds.Add(rewardId);
            }

            UpdateDungeonRecord(record, Metadata, clearTimeStamp);

        } else {
            if (GetDailyClearCount(record) < Metadata.Reward.SubRewardCount && GetWeeklyClearCount(record) < Metadata.Reward.Count) {
                UpdateUnionRecord(record, Metadata, clearTimeStamp);
                UpdateDungeonRecord(record, Metadata, clearTimeStamp);
                UserRecord.Flag |= DungeonBonusFlag.Clear;
            }
        }

        // TODO: If dungeon was dungeon helper, add clear count on dungeon ID 1003

        if (UserRecord.Flag.HasFlag(DungeonBonusFlag.Clear)) {
            GetClearDungeonRewards();
        }

        foreach (int rewardId in rewardIds) {
            RewardRecord rewardRecord = session.GetRewardContent(rewardId);
            UserRecord.Add(rewardRecord);
        }

        return;

        void GetClearDungeonRewards() {
            DungeonRoomRewardMetadata rewardMetadata = Metadata!.Reward;
            int exp = (int) rewardMetadata.Exp;
            if (rewardMetadata.ExpRate > 0f) {
                exp = (int) (rewardMetadata.Exp * rewardMetadata.ExpRate);
            }
            UserRecord.Rewards[DungeonRewardType.Exp] = exp;
            UserRecord.Rewards[DungeonRewardType.Meso] = (int) rewardMetadata.Meso;
            session.Exp.AddExp(UserRecord.Rewards[DungeonRewardType.Exp]);
            session.Currency.Meso += UserRecord.Rewards[DungeonRewardType.Meso];
            session.Send(DungeonRoomPacket.Modify(DungeonRoomModify.GiveReward, Lobby.DungeonId));

            ICollection<Item> items = [];
            if (rewardMetadata.UnlimitedDropBoxIds.Length > 0) {
                foreach (int boxId in rewardMetadata.UnlimitedDropBoxIds) {
                    items = items.Concat(Lobby.ItemDrop.GetIndividualDropItems(boxId)).ToList();
                    items = items.Concat(Lobby.ItemDrop.GetGlobalDropItems(boxId, Metadata.Level)).ToList();
                }
            }

            foreach (Item item in items) {
                UserRecord.RewardItems.Add(item);
                if (!session.Item.Inventory.Add(item, true)) {
                    session.Item.MailItem(item);
                }
            }
        }
    }

    private void UpdateUnionRecord(DungeonRecord record, DungeonRoomMetadata metadata, long clearTimeStamp) {
        if (UserRecord == null) {
            return;
        }
        DateTime clearTime = clearTimeStamp.FromEpochSeconds();
        if (record.ClearTimestamp == 0) {
            record.ClearTimestamp = clearTimeStamp;
        }
        if (clearTimeStamp > record.UnionCooldownTimestamp) {
            record.UnionCooldownTimestamp = GetCooldownTime(metadata.CooldownType, metadata.CooldownValue, clearTime);
            record.UnionClears = 0;
            record.ExtraClears = 0;
        }

        if (clearTimeStamp > record.UnionSubCooldownTimestamp) {
            record.UnionSubCooldownTimestamp = new DateTime(clearTime.Year, clearTime.Month, clearTime.Day).AddDays(1).ToEpochSeconds();
            record.UnionSubClears = 0;
            record.ExtraSubClears = 0;
        }

        if (metadata.GroupType == DungeonGroupType.colosseum) {
            record.UnionClears = (byte) UserRecord.Round;
        } else {
            record.UnionClears++;
            record.UnionSubClears++;
        }

        if (record.UnionClears <= metadata.Reward.Count) {
            UserRecord.Flag |= DungeonBonusFlag.UnitedWeekly;
        }

        if (record.UnionSubClears <= metadata.Reward.SubRewardCount) {
            UserRecord.Flag |= DungeonBonusFlag.Clear;
        }
        session.Send(DungeonRoomPacket.Update(record));
    }

    private void UpdateDungeonRecord(DungeonRecord record, DungeonRoomMetadata metadata, long clearTimeStamp, bool updateUnion = false) {
        if (UserRecord == null) {
            return;
        }
        DateTime clearTime = clearTimeStamp.FromEpochSeconds();
        if (record.ClearTimestamp == 0) {
            record.ClearTimestamp = clearTimeStamp;
        }

        if (updateUnion) {
            UpdateUnionRecord(record, metadata, clearTimeStamp);
            return;
        }

        // TODO: Rank score
        if (metadata.GroupType == DungeonGroupType.colosseum) {
            record.TotalClears = UserRecord.Round;
        } else {
            record.TotalClears++;
        }
        record.CooldownTimestamp = GetCooldownTime(metadata.CooldownType, metadata.CooldownValue, clearTime);
        session.Send(DungeonRoomPacket.Update(record));
    }

    private int GetDailyClearCount(DungeonRecord record) {
        if (!session.TableMetadata.DungeonRoomTable.Entries.TryGetValue(record.DungeonId, out DungeonRoomMetadata? metadata)) {
            return 0;
        }

        if (metadata.Reward.UnionRewardId > 0) {
            DungeonRecord unionRecord = GetRecord(metadata.Reward.UnionRewardId);
            if (DateTime.Now.ToEpochSeconds() > unionRecord.UnionSubCooldownTimestamp) {
                return 0;
            }
            return unionRecord.UnionSubClears;
        }

        if (DateTime.Now.ToEpochSeconds() > record.UnionSubCooldownTimestamp) {
            return 0;
        }

        return record.UnionSubClears;
    }

    private int GetWeeklyClearCount(DungeonRecord record) {
        if (!session.TableMetadata.DungeonRoomTable.Entries.TryGetValue(record.DungeonId, out DungeonRoomMetadata? metadata)) {
            return 0;
        }

        if (metadata.Reward.UnionRewardId > 0) {
            DungeonRecord unionRecord = GetRecord(metadata.Reward.UnionRewardId);

            if (DateTime.Now.ToEpochSeconds() > unionRecord.UnionCooldownTimestamp) {
                return 0;
            }
            return unionRecord.UnionClears;
        }

        if (DateTime.Now.ToEpochSeconds() > record.UnionCooldownTimestamp) {
            return 0;
        }

        return record.UnionClears;
    }

    private long GetCooldownTime(DungeonCooldownType type, int cooldownValue, DateTime clearTime) {
        return type switch {
            DungeonCooldownType.nextDay => new DateTime(clearTime.Year, clearTime.Month, clearTime.Day).AddDays(1).ToEpochSeconds(),
            DungeonCooldownType.dayOfWeeks => DateTime.Now.NextDayOfWeek((DayOfWeek) cooldownValue).ToEpochSeconds(),
            _ => 0,
        };
    }

    public void Reset() {
        if (session.Party.Party == null || session.Party.Party.LeaderCharacterId != session.CharacterId) {
            return;
        }

        try {
            FieldResponse response = session.World.Field(new FieldRequest {
                DestroyDungeon = new FieldRequest.Types.DestroyDungeon {
                    RoomId = LobbyRoomId,
                },
            });
            if ((MigrationError) response.Error != MigrationError.ok) {
                session.Send(MigrationPacket.GameToGameError((MigrationError) response.Error));
                return;
            }
        } catch (RpcException) {
            session.Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_no_server));
            return;
        }

        var request = new PartyRequest {
            RequestorId = session.CharacterId,
            SetDungeon = new PartyRequest.Types.SetDungeon {
                PartyId = session.Party.Id,
                DungeonId = 0,
                DungeonRoomId = 0,
                Set = false,
            },
        };

        try {
            session.World.Party(request);
        } catch (RpcException) { }

    }

    private DungeonRecord GetRecord(int dungeonId) {
        if (!Records.TryGetValue(dungeonId, out DungeonRecord? record)) {
            Records[dungeonId] = record = new DungeonRecord(dungeonId);
        }
        return record;
    }

    private void MigrateToDungeon() {
        if (Metadata == null) {
            logger.Error("Dungeon metadata is null, cannot migrate to dungeon");
            return;
        }

        session.MigrationSave();
        try {
            var request = new MigrateOutRequest {
                AccountId = session.AccountId,
                CharacterId = session.CharacterId,
                MachineId = session.MachineId.ToString(),
                Server = Server.World.Service.Server.Game,
                MapId = Metadata.LobbyFieldId,
                RoomId = LobbyRoomId,
                Type = MigrationType.Dungeon,
            };

            MigrateOutResponse response = session.World.MigrateOut(request);
            var endpoint = new IPEndPoint(IPAddress.Parse(response.IpAddress), response.Port);
            session.Send(MigrationPacket.GameToGame(endpoint, response.Token, Metadata.LobbyFieldId));
            session.State = SessionState.ChangeMap;
        } catch (RpcException ex) {
            session.Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
            session.Send(NoticePacket.Disconnect(new InterfaceText(ex.Message)));
        } finally {
            session.Disconnect();
        }
    }

    public void Save(GameStorage.Request db) {
        db.SaveDungeonRecords(session.CharacterId, Records.Values.ToArray());
    }
}
