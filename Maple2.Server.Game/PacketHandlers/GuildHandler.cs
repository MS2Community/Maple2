using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Maple2.Tools.Extensions;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Game.PacketHandlers;

public class GuildHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.Guild;

    private enum Command : byte {
        Create = 1,
        Disband = 2,
        Invite = 3,
        RespondInvite = 5,
        Leave = 7,
        Expel = 8,
        UpdateMemberRank = 10,
        UpdateMemberMessage = 13,
        CheckIn = 15,
        UpdateLeader = 61,
        UpdateNotice = 62,
        UpdateEmblem = 63,
        IncreaseCapacity = 64,
        UpdateRank = 65,
        UpdateFocus = 66,
        SendMail = 69,
        SendApplication = 80,
        CancelApplication = 81,
        RespondApplication = 82,
        ListApplications = 83,
        ListAppliedGuilds = 84,
        SearchGuilds = 85,
        SearchGuildName = 86,
        UseBuff = 88,
        UsePersonalBuff = 89,
        UpgradeBuff = 90,
        StartArcade = 96,
        EnterArcade = 97,
        UpgradeHouseRank = 98,
        UpgradeHouseTheme = 99,
        EnterHouse = 100,
        SendGift = 106,
        UpdateGiftLog = 109,
        Donate = 110,
        UpgradeNpc = 111,
        CreateGuildEvent = 112,
        StartGuildEvent = 113,
        JoinGuildEvent = 117,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required WorldClient World { private get; init; }
    public required TableMetadataStorage TableMetadata { private get; init; }
    public required BanWordStorage BanWordStorage { private get; init; }

    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Create:
                HandleCreate(session, packet);
                return;
            case Command.Disband:
                HandleDisband(session);
                return;
            case Command.Invite:
                HandleInvite(session, packet);
                return;
            case Command.RespondInvite:
                HandleRespondInvite(session, packet);
                return;
            case Command.Leave:
                HandleLeave(session);
                return;
            case Command.Expel:
                HandleExpel(session, packet);
                return;
            case Command.UpdateMemberRank:
                HandleUpdateMemberRank(session, packet);
                return;
            case Command.UpdateMemberMessage:
                HandleUpdateMemberMessage(session, packet);
                return;
            case Command.CheckIn:
                HandleCheckIn(session);
                return;
            case Command.UpdateLeader:
                HandleUpdateLeader(session, packet);
                return;
            case Command.UpdateNotice:
                HandleUpdateNotice(session, packet);
                return;
            case Command.IncreaseCapacity:
                HandleIncreaseCapacity(session);
                return;
            case Command.UpdateRank:
                HandleUpdateRank(session, packet);
                return;
            case Command.UpdateFocus:
                HandleUpdateFocus(session, packet);
                return;
            case Command.SendMail:
                HandleSendMail(session, packet);
                return;
            case Command.SendApplication:
                HandleSendApplication(session, packet);
                return;
            case Command.CancelApplication:
                HandleCancelApplication(session, packet);
                return;
            case Command.RespondApplication:
                HandleRespondApplication(session, packet);
                return;
            case Command.ListApplications:
                HandleListApplications(session);
                return;
            case Command.ListAppliedGuilds:
                HandleListAppliedGuilds(session);
                return;
            case Command.SearchGuilds:
                HandleSearchGuilds(session, packet);
                return;
            case Command.SearchGuildName:
                HandleSearchGuildName(session, packet);
                return;
            case Command.UseBuff:
                HandleUseBuff(session, packet);
                return;
            case Command.UsePersonalBuff:
                HandleUsePersonalBuff(session, packet);
                return;
            case Command.UpgradeBuff:
                HandleUpgradeBuff(session, packet);
                return;
            case Command.StartArcade:
                HandleStartArcade(session, packet);
                return;
            case Command.EnterArcade:
                HandleEnterArcade(session, packet);
                return;
            case Command.UpgradeHouseRank:
                HandleUpgradeHouseRank(session, packet);
                return;
            case Command.UpgradeHouseTheme:
                HandleUpgradeHouseTheme(session, packet);
                return;
            case Command.EnterHouse:
                HandleEnterHouse(session);
                return;
            case Command.SendGift:
                HandleSendGift(session, packet);
                return;
            case Command.UpdateGiftLog:
                HandleUpdateGiftLog(session);
                return;
            case Command.Donate:
                HandleDonate(session, packet);
                return;
            case Command.UpgradeNpc:
                HandleUpgradeNpc(session, packet);
                return;
            case Command.CreateGuildEvent:
                HandleCreateGuildEvent(session);
                return;
            case Command.StartGuildEvent:
                HandleStartGuildEvent(session);
                return;
            case Command.JoinGuildEvent:
                HandleJoinGuildEvent(session);
                return;
        }
    }

    private void HandleCreate(GameSession session, IByteReader packet) {
        string guildName = packet.ReadUnicodeString();
        if (session.Guild.Guild != null) {
            return; // Already in a guild.
        }

        if (string.IsNullOrWhiteSpace(guildName)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_name_value));
            return;
        }
        if (guildName.Length is < Constant.GuildNameLengthMin or > Constant.GuildNameLengthMax) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_name_value));
            return;
        }

        if (BanWordStorage.ContainsBannedWord(guildName)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_name_value));
            return;
        }

        if (!NameValidator.ValidName(guildName)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_name_value));
            return;
        }

        if (session.Player.Value.Character.Level < Constant.GuildCreateMinLevel) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_not_enough_level));
            return;
        }
        if (session.Currency.CanAddMeso(-Constant.GuildCreatePrice) != -Constant.GuildCreatePrice) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_no_money));
            return;
        }

        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                Create = new GuildRequest.Types.Create {
                    GuildName = guildName,
                },
            };
            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }
            if (response.InfoCase != GuildResponse.InfoOneofCase.Guild) {
                session.Send(GuildPacket.Error(GuildError.s_guild_err_null_guild));
                return;
            }

            session.Guild.SetGuild(response.Guild);
            session.Currency.Meso -= Constant.GuildCreatePrice;

            session.Guild.Load();
            session.Send(GuildPacket.Created(guildName));
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to create guild: {Name}", guildName);
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        }
    }

    private void HandleDisband(GameSession session) {
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        if (session.Guild.Guild.LeaderCharacterId != session.CharacterId) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_no_master));
            return;
        }

        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                Disband = new GuildRequest.Types.Disband {
                    GuildId = session.Guild.Id,
                },
            };
            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.Disbanded());
            session.Guild.RemoveGuild();
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to disband guild");
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        }
    }

    private void HandleInvite(GameSession session, IByteReader packet) {
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        string playerName = packet.ReadUnicodeString();
        using GameStorage.Request db = session.GameStorage.Context();
        long characterId = db.GetCharacterId(playerName);
        if (characterId == 0) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_wait_inviting));
            return;
        }

        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                Invite = new GuildRequest.Types.Invite {
                    GuildId = session.Guild.Id,
                    ReceiverId = characterId,
                },
            };
            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.Invited(playerName));
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to invite {Name} to guild", playerName);
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        }
    }

    private void HandleRespondInvite(GameSession session, IByteReader packet) {
        var invite = packet.ReadClass<GuildInvite>();
        bool accepted = packet.ReadBool();

        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                RespondInvite = new GuildRequest.Types.RespondInvite {
                    GuildId = invite.GuildId,
                    Accepted = accepted,
                },
            };
            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.InviteReply(invite, accepted));
            switch (response.InfoCase) {
                case GuildResponse.InfoOneofCase.GuildId: // Reject
                    break;
                case GuildResponse.InfoOneofCase.Guild: // Accept
                    session.Guild.SetGuild(response.Guild);
                    session.Guild.Load();
                    break;
            }
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to respond to guild invite");
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        }
    }

    private void HandleLeave(GameSession session) {
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                Leave = new GuildRequest.Types.Leave {
                    GuildId = session.Guild.Id,
                },
            };
            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.Leave());
            session.Guild.RemoveGuild();
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to leave guild");
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        }
    }

    private void HandleExpel(GameSession session, IByteReader packet) {
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        string playerName = packet.ReadUnicodeString();
        GuildMember? member = session.Guild.GetMember(playerName);
        if (member == null) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_null_member));
            return;
        }

        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                Expel = new GuildRequest.Types.Expel {
                    GuildId = session.Guild.Id,
                    ReceiverId = member.CharacterId,
                },
            };
            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.Expelled(playerName));
        } catch (RpcException) { /* ignored */ }
    }

    private void HandleUpdateMemberRank(GameSession session, IByteReader packet) {
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        string playerName = packet.ReadUnicodeString();
        byte rankId = packet.ReadByte();

        if (!session.Guild.HasPermission(session.CharacterId, GuildPermission.EditRank)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_no_authority));
            return;
        }

        GuildMember? member = session.Guild.GetMember(playerName);
        if (member == null) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_null_member));
            return;
        }

        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                UpdateMember = new GuildRequest.Types.UpdateMember {
                    GuildId = session.Guild.Id,
                    CharacterId = member.CharacterId,
                    Rank = rankId,
                },
            };

            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.UpdateMemberRank(playerName, rankId));
        } catch (RpcException) { /* ignored */ }
    }

    private void HandleUpdateMemberMessage(GameSession session, IByteReader packet) {
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        string message = packet.ReadUnicodeString();
        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                UpdateMember = new GuildRequest.Types.UpdateMember {
                    GuildId = session.Guild.Id,
                    CharacterId = session.CharacterId,
                    Message = message,
                },
            };

            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.UpdateMemberMessage(message));
        } catch (RpcException) { /* ignored */ }
    }

    private void HandleCheckIn(GameSession session) {
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        GuildMember? self = session.Guild.GetMember(session.CharacterId);
        if (self == null) {
            return;
        }

        // Check that player has not already checked in today.
        DateTimeOffset today = DateTimeOffset.UtcNow.Date;
        if (self.CheckinTime >= today.ToUnixTimeSeconds()) {
            return;
        }

        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                CheckIn = new GuildRequest.Types.CheckIn {
                    GuildId = session.Guild.Id,
                },
            };

            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.CheckedIn());
            session.Exp.AddExp(ExpType.guildUserExp, session.Guild.Properties.CheckInPlayerExpRate);
            Item? guildCoin = session.Field?.ItemDrop.CreateItem(Constant.GuildCoinId, Constant.GuildCoinRarity, session.Guild.Properties.CheckInCoin);
            if (guildCoin != null) {
                session.Item.Inventory.Add(guildCoin, true);
            }
        } catch (RpcException) { /* ignored */ }
    }

    private void HandleUpdateLeader(GameSession session, IByteReader packet) {
        string leaderName = packet.ReadUnicodeString();
        // Only leader is allowed to change leader
        if (session.CharacterId != session.Guild.LeaderId) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_no_master));
            return;
        }

        GuildMember? newLeader = session.Guild.GetMember(leaderName);
        if (newLeader == null) {
            return;
        }

        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                UpdateLeader = new GuildRequest.Types.UpdateLeader {
                    GuildId = session.Guild.Id,
                    LeaderId = newLeader.CharacterId,
                },
            };

            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.UpdateLeader(leaderName));
        } catch (RpcException) { /* ignored */ }
    }

    private void HandleUpdateNotice(GameSession session, IByteReader packet) {
        packet.ReadBool();
        string notice = packet.ReadUnicodeString();

        if (!session.Guild.HasPermission(session.CharacterId, GuildPermission.EditNotice)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_no_authority));
            return;
        }

        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                UpdateNotice = new GuildRequest.Types.UpdateNotice {
                    GuildId = session.Guild.Id,
                    Message = notice,
                },
            };

            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.UpdateNotice(notice));
        } catch (RpcException) { /* ignored */ }
    }

    private void HandleIncreaseCapacity(GameSession session) {

    }

    private void HandleUpdateRank(GameSession session, IByteReader packet) {
        if (session.Guild.Guild == null) {
            return;
        }

        byte rankId = packet.ReadByte();
        var rank = packet.ReadClass<GuildRank>();
        rank.Id = rankId;

        if (!session.Guild.HasPermission(session.CharacterId, GuildPermission.EditRank)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_no_authority));
            return;
        }
        if (rankId >= session.Guild.Guild.Ranks.Count) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_invalid_grade_index));
            return;
        }
        if (string.IsNullOrWhiteSpace(rank.Name) || rank.Name.Length > Constant.GuildNameLengthMax) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_invalid_grade_data));
            return;
        }

        session.Guild.Guild.Ranks[rankId].Name = rank.Name;
        session.Guild.Guild.Ranks[rankId].Permission = rank.Permission;

        using GameStorage.Request db = session.GameStorage.Context();
        if (!db.SaveGuild(session.Guild.Guild)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
            return;
        }

        foreach (GuildMember member in session.Guild.Guild.Members.Values) {
            if (!session.FindSession(member.CharacterId, out GameSession? other) || other.Guild.Guild == null) {
                continue;
            }
            if (rankId < other.Guild.Guild.Ranks.Count) {
                other.Guild.Guild.Ranks[rankId].Name = rank.Name;
                other.Guild.Guild.Ranks[rankId].Permission = rank.Permission;
            }
            other.Send(GuildPacket.NotifyUpdateRank(new InterfaceText("s_guild_change_grade_sucess", false), rank));
        }

        session.Send(GuildPacket.UpdateRank(rank));
    }

    private void HandleUpdateFocus(GameSession session, IByteReader packet) {
        packet.ReadByte();
        var focus = packet.Read<GuildFocus>();
    }

    private void HandleSendMail(GameSession session, IByteReader packet) {
        string title = packet.ReadUnicodeString();
        string content = packet.ReadUnicodeString();
    }

    private void HandleSendApplication(GameSession session, IByteReader packet) {
        long guildId = packet.ReadLong();
        if (session.Guild.Guild != null) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_has_guild));
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        Guild? guild = db.GetGuild(guildId);
        if (guild == null) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_null_guild));
            return;
        }

        GuildApplication? application = db.CreateGuildApplication(session.PlayerInfo, guildId, session.CharacterId);
        if (application == null) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
            return;
        }

        session.Send(GuildPacket.SendApplication(application.Id, guild.Name));

        foreach (GuildMember member in guild.Members.Values) {
            if (!session.FindSession(member.CharacterId, out GameSession? guildSession)) {
                continue;
            }

            GuildRank? rank = guild.Ranks.FirstOrDefault(x => x.Id == member.Rank);
            if (rank == null) {
                continue;
            }

            if (!rank.Permission.HasFlag(GuildPermission.InviteMembers)) {
                continue;
            }

            guildSession.Send(GuildPacket.ReceiveApplication(application));
        }
    }

    private void HandleCancelApplication(GameSession session, IByteReader packet) {
        long applicationId = packet.ReadLong();
        using GameStorage.Request db = session.GameStorage.Context();
        GuildApplication? application = db.GetGuildApplication(session.PlayerInfo, applicationId);
        if (application == null || application.Applicant.CharacterId != session.CharacterId) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
            return;
        }
        if (!db.DeleteGuildApplication(applicationId)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
            return;
        }

        session.Send(GuildPacket.CancelApplication(applicationId, application.Guild.Name));
    }

    private void HandleRespondApplication(GameSession session, IByteReader packet) {
        static GuildMember CloneGuildMember(GuildMember member) {
            return new GuildMember {
                GuildId = member.GuildId,
                Info = member.Info.Clone(),
                Message = member.Message,
                Rank = member.Rank,
                WeeklyContribution = member.WeeklyContribution,
                TotalContribution = member.TotalContribution,
                DailyDonationCount = member.DailyDonationCount,
                JoinTime = member.JoinTime,
                CheckinTime = member.CheckinTime,
                DonationTime = member.DonationTime,
            };
        }

        long applicationId = packet.ReadLong();
        bool accepted = packet.ReadBool();
        if (session.Guild.Guild == null) {
            return;
        }
        if (!session.Guild.HasPermission(session.CharacterId, GuildPermission.InviteMembers)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_no_authority));
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        GuildApplication? application = db.GetGuildApplication(session.PlayerInfo, applicationId);
        if (application == null || application.Guild.Id != session.Guild.Id) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
            return;
        }

        if (accepted) {
            GuildMember? newMember = db.CreateGuildMember(session.Guild.Id, application.Applicant);
            if (newMember == null) {
                session.Send(GuildPacket.Error(GuildError.s_guild_err_fail_addmember));
                return;
            }

            if (!db.DeleteGuildApplication(applicationId)) {
                session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
                return;
            }

            GuildMember[] currentMembers = session.Guild.Guild.Members.Values.ToArray();
            session.Guild.Guild.Members.TryAdd(newMember.CharacterId, newMember);

            // Refresh the guild master's member list immediately even if self session lookup fails.
            session.Send(GuildPacket.Joined(session.PlayerName, CloneGuildMember(newMember)));

            foreach (GuildMember member in currentMembers) {
                if (member.CharacterId == session.CharacterId) {
                    continue;
                }
                if (!session.FindSession(member.CharacterId, out GameSession? other)) {
                    continue;
                }

                other.Guild.AddMember(session.PlayerName, CloneGuildMember(newMember));
            }

            if (session.FindSession(newMember.CharacterId, out GameSession? applicantSession)) {
                if (applicantSession.Guild.Guild == null) {
                    GuildInfoResponse guildInfo = applicantSession.World.GuildInfo(new GuildInfoRequest {
                        GuildId = session.Guild.Id,
                    });
                    if (guildInfo.Guild != null) {
                        applicantSession.Guild.SetGuild(guildInfo.Guild);
                    }
                }

                if (applicantSession.Guild.Guild != null && applicantSession.Guild.GetMember(newMember.CharacterId) == null) {
                    applicantSession.Guild.AddMember(session.PlayerName, CloneGuildMember(newMember));
                }
                applicantSession.Guild.Load();
                applicantSession.Send(GuildPacket.ListAppliedGuilds(db.GetGuildApplicationsByApplicant(applicantSession.PlayerInfo, applicantSession.CharacterId)));
            }

            session.Send(GuildPacket.ListApplications(db.GetGuildApplications(session.PlayerInfo, session.Guild.Id)));
        } else {
            if (!db.DeleteGuildApplication(applicationId)) {
                session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
                return;
            }

            session.Send(GuildPacket.ListApplications(db.GetGuildApplications(session.PlayerInfo, session.Guild.Id)));
            if (session.FindSession(application.Applicant.CharacterId, out GameSession? applicantSession)) {
                applicantSession.Send(GuildPacket.ListAppliedGuilds(db.GetGuildApplicationsByApplicant(applicantSession.PlayerInfo, applicantSession.CharacterId)));
            }
        }

        session.Send(GuildPacket.RespondApplication(applicationId, application.Guild.Name, accepted));
    }

    private void HandleListApplications(GameSession session) {
        using GameStorage.Request db = session.GameStorage.Context();

        if (session.Guild.Guild == null) {
            session.Send(GuildPacket.ListApplications(Array.Empty<GuildApplication>()));
            return;
        }

        session.Send(GuildPacket.ListApplications(
            db.GetGuildApplications(session.PlayerInfo, session.Guild.Id)
        ));
    }
    private void HandleListAppliedGuilds(GameSession session) {
        using GameStorage.Request db = session.GameStorage.Context();

        session.Send(GuildPacket.ListAppliedGuilds(
            db.GetGuildApplicationsByApplicant(session.PlayerInfo, session.CharacterId)
        ));
    }
    private void HandleSearchGuilds(GameSession session, IByteReader packet) {
        var focus = packet.Read<GuildFocus>();
        packet.ReadInt();

        using GameStorage.Request db = session.GameStorage.Context();
        session.Send(GuildPacket.ListGuilds(db.SearchGuilds(session.PlayerInfo, focus: focus)));
    }

    private void HandleSearchGuildName(GameSession session, IByteReader packet) {
        string guildName = packet.ReadUnicodeString();

        using GameStorage.Request db = session.GameStorage.Context();
        session.Send(GuildPacket.ListGuilds(db.SearchGuilds(session.PlayerInfo, guildName: guildName)));
    }

    private void HandleUseBuff(GameSession session, IByteReader packet) {
        int buffId = packet.ReadInt();
    }

    private void HandleUsePersonalBuff(GameSession session, IByteReader packet) {
        int buffId = packet.ReadInt();
    }

    private void HandleUpgradeBuff(GameSession session, IByteReader packet) {
        int buffId = packet.ReadInt();
    }

    private void HandleStartArcade(GameSession session, IByteReader packet) {
        int index = packet.ReadInt();
    }

    private void HandleEnterArcade(GameSession session, IByteReader packet) {
        int index = packet.ReadInt();
    }

    private void HandleUpgradeHouseRank(GameSession session, IByteReader packet) {
        int rank = packet.ReadInt();
    }

    private void HandleUpgradeHouseTheme(GameSession session, IByteReader packet) {
        int theme = packet.ReadInt();
    }

    private void HandleEnterHouse(GameSession session) {
        if (session.Guild.Guild is null) {
            return;
        }
        TableMetadata.GuildTable.Houses.TryGetValue(session.Guild.Guild.HouseRank, out IReadOnlyDictionary<int, GuildTable.House>? houseRank);

        if (houseRank is null) {
            return;
        }

        houseRank.TryGetValue(session.Guild.Guild.HouseTheme, out GuildTable.House? house);

        if (house is null) {
            return;
        }

        session.Migrate(house.MapId, session.Guild.Id);
    }

    private void HandleSendGift(GameSession session, IByteReader packet) {
        var gift = packet.Read<RewardItem>();
        string playerName = packet.ReadUnicodeString();
    }

    private void HandleUpdateGiftLog(GameSession session) {

    }

    private void HandleDonate(GameSession session, IByteReader packet) {
        int count = packet.ReadInt();
    }

    private void HandleUpgradeNpc(GameSession session, IByteReader packet) {
        var type = packet.Read<GuildNpcType>();
    }

    private void HandleCreateGuildEvent(GameSession session) {

    }

    private void HandleStartGuildEvent(GameSession session) {

    }

    private void HandleJoinGuildEvent(GameSession session) {

    }
}
