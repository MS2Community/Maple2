using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Error;
using Maple2.Model.Game.Club;
using Maple2.Model.Game.Party;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;
using ClubResponse = Maple2.Server.World.Service.ClubResponse;

namespace Maple2.Server.Game.PacketHandlers;

public class ClubHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Club;

    private enum Command : byte {
        Create = 1,
        StagedInvite = 3,
        Invite = 6,
        RespondInvite = 8,
        Leave = 10,
        Buff = 13,
        Rename = 14,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required WorldClient World { private get; init; }
    public required BanWordStorage BanWordStorage { private get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata {  private get; init; }
    // ReSharper restore All
    #endregion

    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Create:
                HandleCreate(session, packet);
                break;
            case Command.StagedInvite:
                HandleStagedInvite(session, packet);
                break;
            case Command.Invite:
                HandleInvite(session, packet);
                break;
            case Command.RespondInvite:
                HandleRespondInvite(session, packet);
                break;
            case Command.Leave:
                HandleLeave(session, packet);
                break;
            case Command.Buff:
                HandleBuff(session, packet);
                break;
            case Command.Rename:
                HandleRename(session, packet);
                break;
        }
    }

    private void HandleCreate(GameSession session, IByteReader packet) {
        string clubName = packet.ReadUnicodeString();

        if (string.IsNullOrWhiteSpace(clubName)) {
            session.Send(ClubPacket.Error(ClubError.s_club_err_name_value));
            return;
        }
        if (clubName.Length < Constants.ClubNameLengthMin || clubName.Length > Constants.ClubNameLengthMax) {
            session.Send(ClubPacket.Error(ClubError.s_club_err_name_value));
            return;
        }

        if (BanWordStorage.ContainsBannedWord(clubName)) {
            session.Send(ClubPacket.Error(ClubError.s_club_err_name_value));
            return;
        }

        if (!NameValidator.ValidName(clubName)) {
            session.Send(ClubPacket.Error(ClubError.s_club_err_name_value));
            return;
        }

        // Grabbing party. Clubs can only be created by party leaders.
        Party? party = session.Party.Party;
        if (party is null || party.LeaderCharacterId != session.Player.Value.Character.Id) {
            return;
        }

        try {
            var request = new ClubRequest {
                RequestorId = session.CharacterId,
                Create = new ClubRequest.Types.Create {
                    ClubName = clubName,
                },
            };
            ClubResponse response = World.Club(request);
            var error = (ClubError) response.Error;
            if (error != ClubError.none) {
                session.Send(ClubPacket.Error(error));
            }
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to create guild: {Name}", clubName);
            session.Send(ClubPacket.Error(ClubError.s_club_err_unknown));
        }
    }

    private void HandleStagedInvite(GameSession session, IByteReader packet) {
        long clubId = packet.ReadLong();
        var reply = packet.Read<Maple2.Model.Enum.ClubResponse>();

        ClubResponse response = World.Club(new ClubRequest {
            NewClubInvite = new ClubRequest.Types.NewClubInvite {
                ClubId = clubId,
                ReceiverId = session.CharacterId,
                Reply = (int) reply,
            },
        });

        var error = (ClubError) response.Error;
        if (error != ClubError.none) {
            session.Send(ClubPacket.Error(error));
        }
    }

    private void HandleInvite(GameSession session, IByteReader packet) {
        long clubId = packet.ReadLong();
        string name = packet.ReadUnicodeString();

        if (!session.Clubs.TryGetValue(clubId, out ClubManager? club) || club.Club == null) {
            session.Send(ClubPacket.Error(ClubError.s_club_err_null_club));
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        long characterid = db.GetCharacterId(name);
        if (characterid == 0) {
            session.Send(ClubPacket.Error(ClubError.s_club_err_null_invite_member));
            return;
        }

        ClubResponse response = World.Club(new ClubRequest {
            RequestorId = session.CharacterId,
            Invite = new ClubRequest.Types.Invite {
                ClubId = clubId,
                ReceiverId = characterid,
            },
        });

        if (response.Error != 0) {
            session.Send(ClubPacket.Error((ClubError) response.Error));
            return;
        }

        session.Send(ClubPacket.Invited(club.Id, name));
    }

    private void HandleRespondInvite(GameSession session, IByteReader packet) {
        long clubId = packet.ReadLong();
        string clubName = packet.ReadUnicodeString();
        string inviterName = packet.ReadUnicodeString();
        string invitee = packet.ReadUnicodeString();
        bool accept = packet.ReadBool();

        if (session.PlayerName != invitee) {
            return;
        }

        if (session.Clubs.Count >= Constant.ClubMaxCount) {
            session.Send(ClubPacket.Error(ClubError.s_club_err_full_club_member));
            return;
        }

        ClubResponse response = World.Club(new ClubRequest {
            RequestorId = session.CharacterId,
            RespondInvite = new ClubRequest.Types.RespondInvite {
                ClubId = clubId,
                Accept = accept,
            },
        });

        if (response.Error != 0) {
            session.Send(ClubPacket.Error((ClubError) response.Error));
            return;
        }

        if (accept) {
            var club = ClubManager.Create(response.Club, session);
            if (club == null) {
                Logger.Error("Failed to join club: {Name}", clubName);
                return;
            }

            session.Send(ClubPacket.AcceptInvite(new ClubInvite {
                ClubId = clubId,
                Name = clubName,
                LeaderName = inviterName,
                Invitee = invitee,
            }));
            if (session.Clubs.TryAdd(response.Club.Id, club)) {
                session.Send(ClubPacket.Join(club.Club.Members[session.CharacterId], club.Club.Name));
                club.Load();
            }
        }
    }

    private void HandleLeave(GameSession session, IByteReader packet) {
        long clubId = packet.ReadLong();

        if (!session.Clubs.ContainsKey(clubId)) {
            session.Send(ClubPacket.Error(ClubError.s_club_err_null_club));
            return;
        }

        ClubResponse response = World.Club(new ClubRequest {
            RequestorId = session.CharacterId,
            Leave = new ClubRequest.Types.Leave {
                ClubId = clubId,
            },
        });

        if (response.Error != 0) {
            session.Send(ClubPacket.Error((ClubError) response.Error));
        }
    }

    private void HandleBuff(GameSession session, IByteReader packet) {
        long clubId = packet.ReadLong();
        int buffId = packet.ReadInt();
        int buffLevel = packet.ReadInt();
    }

    private void HandleRename(GameSession session, IByteReader packet) {
        long clubId = packet.ReadLong();
        string newName = packet.ReadUnicodeString();

        if (string.IsNullOrWhiteSpace(newName)) {
            session.Send(ClubPacket.Error(ClubError.s_club_err_name_value));
            return;
        }
        if (newName.Length < Constants.ClubNameLengthMin || newName.Length > Constants.ClubNameLengthMax) {
            session.Send(ClubPacket.Error(ClubError.s_club_err_name_value));
            return;
        }

        if (BanWordStorage.ContainsBannedWord(newName)) {
            session.Send(ClubPacket.Error(ClubError.s_club_err_name_value));
            return;
        }

        if (!NameValidator.ValidName(newName)) {
            session.Send(ClubPacket.Error(ClubError.s_club_err_name_value));
            return;
        }

        if (!session.Clubs.ContainsKey(clubId)) {
            return;
        }

        ClubResponse response = World.Club(new ClubRequest {
            RequestorId = session.CharacterId,
            Rename = new ClubRequest.Types.Rename {
                ClubId = clubId,
                Name = newName,
            },
        });

        if (response.Error != 0) {
            session.Send(ClubPacket.Error((ClubError) response.Error));
        }
    }
}
