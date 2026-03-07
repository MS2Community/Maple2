using Grpc.Core;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Game.PacketHandlers;

public class PartyHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Party;

    private enum Command : byte {
        Invite = 1,
        InviteResponse = 2,
        Leave = 3,
        Kick = 4,
        SetLeader = 17,
        SearchPartyJoin = 23,
        SummonParty = 29,
        Unknown = 32,
        PartySearch = 33,
        CancelPartySearch = 34,
        VoteKick = 45,
        ReadyCheck = 46,
        VoteReply = 48,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required WorldClient World { private get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Invite:
                HandleInvite(session, packet);
                return;
            case Command.InviteResponse:
                HandleInviteResponse(session, packet);
                return;
            case Command.Leave:
                HandleLeave(session, packet);
                return;
            case Command.Kick:
                HandleKick(session, packet);
                return;
            case Command.SetLeader:
                HandleSetLeader(session, packet);
                return;
            case Command.SearchPartyJoin:
                HandleMatchPartyJoin(session, packet);
                return;
            case Command.SummonParty:
                HandleSummonParty(session);
                return;
            case Command.Unknown:
                HandleUnknown(session, packet);
                return;
            case Command.PartySearch:
                HandlePartySearch(session, packet);
                return;
            case Command.CancelPartySearch:
                HandleCancelPartySearch(session, packet);
                return;
            case Command.VoteKick:
                HandleVoteKick(session, packet);
                return;
            case Command.ReadyCheck:
                HandleReadyCheck(session);
                return;
            case Command.VoteReply:
                HandleVoteReply(session, packet);
                return;
        }

    }

    private void HandleInvite(GameSession session, IByteReader packet) {
        string playerName = packet.ReadUnicodeString();

        if (playerName == session.PlayerName.ToLower()) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_myself));
            return;
        }

        if (session.Party.Party == null) {
            // Create new party
            PartyResponse response = World.Party(new PartyRequest {
                RequestorId = session.CharacterId,
                Create = new PartyRequest.Types.Create { },
            });

            var error = (PartyError) response.Error;
            if (error != PartyError.none) {
                session.Send(PartyPacket.Error(error));
                return;
            }

            if (session.Party.SetParty(response.Party)) {
                session.Send(PartyPacket.Load(session.Party.Party!));
            }
        } else if (session.Party.Party.LeaderCharacterId != session.CharacterId) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_chief));
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        long characterId = db.GetCharacterId(playerName);
        if (characterId == 0) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_cannot_invite));
            return;
        }

        try {
            var request = new PartyRequest {
                RequestorId = session.CharacterId,
                Invite = new PartyRequest.Types.Invite {
                    PartyId = session.Party.Id,
                    ReceiverId = characterId,
                },
            };
            PartyResponse response = World.Party(request);
            var error = (PartyError) response.Error;
            if (error != PartyError.none) {
                session.Send(PartyPacket.Error(error, playerName));
            }
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to invite {Name} to party", playerName);
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_found));
        }
    }

    private void HandleInviteResponse(GameSession session, IByteReader packet) {
        string name = packet.ReadUnicodeString();
        PartyInviteResponse response = (PartyInviteResponse) packet.ReadByte();
        int partyId = packet.ReadInt();

        if (session.Party.Party != null) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_already));
            return;
        }

        PartyResponse partyResponse = World.Party(new PartyRequest {
            RequestorId = session.CharacterId,
            RespondInvite = new PartyRequest.Types.RespondInvite {
                PartyId = partyId,
                Reply = (int) response,
            },
        });
        var error = (PartyError) partyResponse.Error;
        if (error != PartyError.none) {
            session.Send(PartyPacket.Error(error));
            return;
        }

        if (response == PartyInviteResponse.Accept) {
            if (session.Party.SetParty(partyResponse.Party)) {
                session.Send(PartyPacket.Load(session.Party.Party!, true));
            }
        }
    }

    private void HandleLeave(GameSession session, IByteReader packet) {
        if (session.Party.Party == null) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_found));
            return;
        }

        PartyResponse response = World.Party(new PartyRequest {
            RequestorId = session.CharacterId,
            Leave = new PartyRequest.Types.Leave {
                PartyId = session.Party.Id,
            },
        });
    }

    private void HandleKick(GameSession session, IByteReader packet) {
        long targetCharacterId = packet.ReadLong();
        PartyResponse response = World.Party(new PartyRequest {
            RequestorId = session.CharacterId,
            Kick = new PartyRequest.Types.Kick {
                PartyId = session.Party.Id,
                ReceiverId = targetCharacterId,
            },
        });

        var error = (PartyError) response.Error;
        if (error != PartyError.none) {
            return;
        }
    }

    private void HandleSetLeader(GameSession session, IByteReader packet) {
        string targetName = packet.ReadUnicodeString();
        using GameStorage.Request db = session.GameStorage.Context();
        long targetCharacterId = db.GetCharacterId(targetName);
        if (targetCharacterId == 0) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_exist));
            return;
        }

        PartyResponse response = World.Party(new PartyRequest {
            RequestorId = session.CharacterId,
            UpdateLeader = new PartyRequest.Types.UpdateLeader {
                PartyId = session.Party.Id,
                CharacterId = targetCharacterId,
            },
        });
    }

    private void HandleMatchPartyJoin(GameSession session, IByteReader packet) {
        int partyId = packet.ReadInt();
        string leaderName = packet.ReadUnicodeString();
        long partySearchId = packet.ReadLong();

        if (session.Party.Party != null) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_already));
            return;
        }

        PartyResponse? response = World.Party(new PartyRequest {
            RequestorId = session.CharacterId,
            JoinByPartySearch = new PartyRequest.Types.JoinByPartySearch {
                PartyId = partyId,
                PartySearchId = partySearchId,
            },
        });

        if (response.Error != (int) PartyError.none) {
            session.Send(PartyPacket.Error((PartyError) response.Error));
            return;
        }

        if (session.Party.SetParty(response.Party)) {
            session.Send(PartyPacket.Load(session.Party.Party!, true));
        }
    }

    private void HandleSummonParty(GameSession session) {
        // This only reaches if player does not have a summon scroll
    }

    private void HandleUnknown(GameSession session, IByteReader packet) {
        // TODO: Implement
        byte unk1 = packet.ReadByte();
    }

    private void HandlePartySearch(GameSession session, IByteReader packet) {
        // TODO: Implement
        int dungeonId = packet.ReadInt();
    }

    private void HandleCancelPartySearch(GameSession session, IByteReader packet) {
        // TODO: Implement
        byte unk1 = packet.ReadByte();
        if (unk1 != 3) {
            int const_1 = packet.ReadInt();
            int unk2 = packet.ReadInt();
            byte unk3 = packet.ReadByte();
        }
    }

    private void HandleVoteKick(GameSession session, IByteReader packet) {
        long targetCharacterId = packet.ReadLong();
        if (session.Party.Party == null) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_found));
            return;
        }

        if (targetCharacterId == session.CharacterId) {
            return;
        }

        if (session.Party.GetMember(targetCharacterId) == null) {
            return;
        }

        if (session.Party.Party.LastVoteTime.FromEpochSeconds().AddSeconds(Constants.PartyVoteReadyDurationSeconds) >
            DateTime.Now && session.Party.Party.Vote != null) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_already_vote));
            return;
        }

        if (session.Party.Party.Members.Values.Count(member => member.Info.Online) < 2 ||
            session.Party.Party.Members.Count < 4) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_vote_need_more_people));
        }

        PartyResponse response = World.Party(new PartyRequest {
            RequestorId = session.CharacterId,
            VoteKick = new PartyRequest.Types.VoteKick {
                PartyId = session.Party.Id,
                TargetUserId = targetCharacterId,
            },
        });
    }

    private void HandleReadyCheck(GameSession session) {
        if (session.Party.Party == null) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_found));
            return;
        }

        if (session.Party.Party?.LeaderCharacterId != session.CharacterId) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_chief));
            return;
        }

        if (session.Party.Party.LastVoteTime.FromEpochSeconds().AddSeconds(Constants.PartyVoteReadyDurationSeconds) >
            DateTime.Now && session.Party.Party.Vote != null) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_already_vote));
            return;
        }

        if (session.Party.Party.Members.Values.Count(member => member.Info.Online) < 2) {
            return;
        }

        PartyResponse response = World.Party(new PartyRequest {
            RequestorId = session.CharacterId,
            ReadyCheck = new PartyRequest.Types.ReadyCheck {
                PartyId = session.Party.Id,
            },
        });

        var error = (PartyError) response.Error;
        if (error != PartyError.none) {
            session.Send(PartyPacket.Error(error));
        }
    }

    private void HandleVoteReply(GameSession session, IByteReader packet) {
        int counter = packet.ReadInt();
        bool isReady = packet.ReadBool();

        if (session.Party.Party == null) {
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_found));
            return;
        }

        PartyResponse response = World.Party(new PartyRequest {
            RequestorId = session.CharacterId,
            VoteReply = new PartyRequest.Types.VoteReply {
                PartyId = session.Party.Id,
                Reply = isReady,
            },
        });

        var error = (PartyError) response.Error;
        if (error != PartyError.none) {
            session.Send(PartyPacket.Error(error));
        }
    }
}
