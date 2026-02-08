using System;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Core.Packets;
using Maple2.Server.Global.Service;
using Maple2.Server.Login.Session;
using GlobalClient = Maple2.Server.Global.Service.Global.GlobalClient;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Login.PacketHandlers;

public class LoginHandler : PacketHandler<LoginSession> {
    public override RecvOp OpCode => RecvOp.ResponseLogin;

    private enum Command : byte {
        ServerList = 1,
        CharacterList = 2,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required GlobalClient Global { private get; init; }
    public required WorldClient World { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(LoginSession session, IByteReader packet) {
        var command = packet.Read<Command>();

        string user = packet.ReadUnicodeString();
        string pass = packet.ReadUnicodeString();
        packet.ReadShort(); // 1
        var machineId = packet.Read<Guid>();

        Logger.Debug("Logging in with user:{User}", user);
        LoginResponse response = Global.Login(new LoginRequest {
            Username = user,
            Password = pass,
            ClientIp = session.ExtractIp(),
            MachineId = machineId.ToString(),
        });

        if (response.Code != LoginResponse.Types.Code.Ok) {
            if (response.Code == LoginResponse.Types.Code.Restricted) {
                session.Send(LoginResultPacket.Restricted(response.Message, response.AccountId, response.BanStart, response.BanExpiry));
            } else {
                session.Send(LoginResultPacket.Error((byte) response.Code, response.Message, response.AccountId));
            }
            session.Disconnect();
            return;
        }

        // Account is already logged into login server.
        if (session.Server.GetSession(response.AccountId, out LoginSession? existing) && existing != session) {
            existing.Disconnect();
            session.Send(LoginResultPacket.Error((byte) LoginResponse.Types.Code.AlreadyLogin, "", response.AccountId));
            return;
        }

        PlayerInfoResponse? playerInfo = World.AccountInfo(new PlayerInfoRequest {
            AccountId = response.AccountId,
        });

        if (playerInfo is not null && playerInfo.Channel != -1 && playerInfo.CharacterId > 0) {
            DisconnectResponse? disconnectResponse = World.Disconnect(new DisconnectRequest {
                CharacterId = playerInfo.CharacterId,
                Force = true,
            });
            if (disconnectResponse is null || !disconnectResponse.Success) {
                Logger.Error("Failed to disconnect character: {CharacterId}", playerInfo.CharacterId);
            } else {
                Logger.Debug("Disconnected character: {CharacterId}", playerInfo.CharacterId);
            }

            session.Send(LoginResultPacket.Error((byte) LoginResponse.Types.Code.AlreadyLogin, "", response.AccountId));
            return;
        }

        try {
            switch (command) {
                case Command.ServerList:
                    session.ListServers();
                    return;
                case Command.CharacterList:
                    session.Init(response.AccountId, machineId);

                    session.Send(LoginResultPacket.Success(response.AccountId));
                    session.Send(UgcPacket.SetEndpoint(Target.WebUri));

                    session.ListCharacters();
                    session.Send(GameEventPacket.Load(session.Server.GetEvents().ToArray()));
                    return;
                default:
                    Logger.Error("Invalid type: {Type}", command);
                    break;
            }
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to login");
        }

        // Disconnect by default if anything goes wrong.
        session.Disconnect();
    }
}
