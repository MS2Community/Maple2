using Grpc.Core;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.Channel.Service;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using WorldGuildInfoRequest = Maple2.Server.World.Service.GuildInfoRequest;
using WorldGuildInfoResponse = Maple2.Server.World.Service.GuildInfoResponse;

namespace Maple2.Server.Game.Service;

public partial class ChannelService {
    public override Task<GuildResponse> Guild(GuildRequest request, ServerCallContext context) {
        switch (request.GuildCase) {
            case GuildRequest.GuildOneofCase.Invite:
                return Task.FromResult(GuildInvite(request.GuildId, request.ReceiverIds, request.Invite));
            case GuildRequest.GuildOneofCase.InviteReply:
                return Task.FromResult(GuildInviteReply(request.ReceiverIds, request.InviteReply));
            case GuildRequest.GuildOneofCase.AddMember:
                return Task.FromResult(AddGuildMember(request.GuildId, request.ReceiverIds, request.AddMember));
            case GuildRequest.GuildOneofCase.RemoveMember:
                return Task.FromResult(RemoveGuildMember(request.ReceiverIds, request.RemoveMember));
            case GuildRequest.GuildOneofCase.UpdateMember:
                return Task.FromResult(UpdateGuildMember(request.ReceiverIds, request.UpdateMember));
            case GuildRequest.GuildOneofCase.UpdateContribution:
                return Task.FromResult(UpdateGuildContribution(request.ReceiverIds, request.UpdateContribution));
            case GuildRequest.GuildOneofCase.UpdateLeader:
                return Task.FromResult(UpdateGuildLeader(request.ReceiverIds, request.UpdateLeader));
            case GuildRequest.GuildOneofCase.UpdateNotice:
                return Task.FromResult(UpdateGuildNotice(request.ReceiverIds, request.UpdateNotice));
            case GuildRequest.GuildOneofCase.UpdateEmblem:
                return Task.FromResult(UpdateGuildEmblem(request.ReceiverIds, request.UpdateEmblem));
            case GuildRequest.GuildOneofCase.UpdatePoster:
                return Task.FromResult(UpdateGuildPoster(request.ReceiverIds, request.UpdatePoster));
            default:
                return Task.FromResult(new GuildResponse { Error = (int) GuildError.s_guild_err_none });
        }
    }

    private GuildResponse GuildInvite(long guildId, IEnumerable<long> receiverIds, GuildRequest.Types.Invite invite) {
        // There really should only be 1 receiver here, but whatever.
        foreach (long receiverId in receiverIds) {
            if (!server.GetSession(receiverId, out GameSession? session)) {
                return new GuildResponse { Error = (int) GuildError.s_guild_err_wait_inviting };
            }
            if (session.Buddy.IsBlocked(invite.SenderId)) {
                return new GuildResponse { Error = (int) GuildError.s_guild_err_blocked };
            }
            if (session.Guild.Guild != null) {
                return new GuildResponse { Error = (int) GuildError.s_guild_err_has_guild };
            }

            var guildInvite = new GuildInvite {
                GuildId = guildId,
                GuildName = invite.GuildName,
                SenderName = invite.SenderName,
                ReceiverName = session.PlayerName,
            };
            session.Send(GuildPacket.InviteInfo(guildInvite));
        }

        return new GuildResponse();
    }

    private GuildResponse GuildInviteReply(IEnumerable<long> receiverIds, GuildRequest.Types.InviteReply reply) {
        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            session.Send(GuildPacket.NotifyInvite(reply.Name, (GuildInvite.Response) reply.Reply));
        }

        return new GuildResponse();
    }

    private GuildResponse AddGuildMember(long guildId, IEnumerable<long> receiverIds, GuildRequest.Types.AddMember add) {
        if (!playerInfos.GetOrFetch(add.CharacterId, out PlayerInfo? info)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_fail_addmember };
        }

        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            var member = new GuildMember {
                GuildId = guildId,
                Info = info.Clone(),
                Rank = (byte) add.Rank,
                JoinTime = add.JoinTime,
            };

            if (session.CharacterId == add.CharacterId && session.Guild.Guild == null) {
                WorldGuildInfoResponse response = session.World.GuildInfo(new WorldGuildInfoRequest {
                    GuildId = guildId,
                });

                if (response.Guild != null) {
                    session.Guild.SetGuild(response.Guild);
                    session.Guild.Load();
                }
            }

            session.Guild.AddMember(add.RequestorName, member);
        }

        return new GuildResponse();
    }

    private GuildResponse RemoveGuildMember(IEnumerable<long> receiverIds, GuildRequest.Types.RemoveMember remove) {
        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            if (characterId == remove.CharacterId) {
                session.Send(GuildPacket.NotifyExpel(remove.RequestorName));
                session.Guild.RemoveGuild();
            } else {
                session.Guild.RemoveMember(remove.CharacterId, remove.RequestorName);
            }
        }

        return new GuildResponse();
    }

    private GuildResponse UpdateGuildMember(IEnumerable<long> receiverIds, GuildRequest.Types.UpdateMember update) {
        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            if (update.HasRank) {
                session.Guild.UpdateMemberRank(update.RequestorId, update.CharacterId, (byte) update.Rank);
            }
            if (update.HasMessage) {
                session.Guild.UpdateMemberMessage(update.CharacterId, update.Message);
            }
            if (update.HasContribution || update.HasCheckInTime) {
                session.Guild.UpdateMemberContribution(update.CharacterId, update.CheckInTime, update.Contribution);
            }
        }

        return new GuildResponse();
    }

    private GuildResponse UpdateGuildContribution(IEnumerable<long> receiverIds, GuildRequest.Types.UpdateContribution guild) {
        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            session.Guild.UpdateGuildExpFunds(guild.ContributorId, guild.GuildExp, guild.GuildFund);
        }

        return new GuildResponse();
    }

    private GuildResponse UpdateGuildLeader(IEnumerable<long> receiverIds, GuildRequest.Types.UpdateLeader update) {
        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            session.Guild.UpdateLeader(update.OldLeaderId, update.NewLeaderId);
        }

        return new GuildResponse();
    }

    private GuildResponse UpdateGuildNotice(IEnumerable<long> receiverIds, GuildRequest.Types.UpdateNotice update) {
        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            session.Guild.UpdateNotice(update.RequestorName, update.Message);
        }

        return new GuildResponse();
    }

    private GuildResponse UpdateGuildEmblem(IEnumerable<long> receiverIds, GuildRequest.Types.UpdateEmblem update) {
        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            session.Guild.UpdateEmblem(update.RequestorName, update.Emblem);
        }

        return new GuildResponse();
    }

    private GuildResponse UpdateGuildPoster(IEnumerable<long> receiverIds, GuildRequest.Types.UpdatePoster update) {
        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            session.Guild.AddOrUpdatePoster(new GuildPoster {
                Id = update.Id,
                Picture = update.Picture,
                OwnerId = update.OwnerId,
                OwnerName = update.OwnerName,
                ResourceId = update.ResourceId,
            });
        }

        return new GuildResponse();
    }
}
