using System.Net;
using Grpc.Core;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using static Maple2.Model.Error.MigrationError;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Game.PacketHandlers;

public class QuitHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.RequestQuit;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required WorldClient World { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        bool quitGame = packet.ReadBool();

        // Reset map to return map
        if (session.Player.Value.Character.ReturnMaps.Peek() is not 0) {
            session.Player.Value.Character.MapId = session.Player.Value.Character.ReturnMaps.Peek();
        }

        // Fully close client
        if (quitGame) {
            session.Disconnect();
            return;
        }

        session.MigrationSave();
        try {
            var request = new MigrateOutRequest {
                AccountId = session.AccountId,
                CharacterId = session.CharacterId,
                MachineId = session.MachineId.ToString(),
                Server = Server.World.Service.Server.Login,
            };

            MigrateOutResponse response = World.MigrateOut(request);
            var endpoint = new IPEndPoint(IPAddress.Parse(response.IpAddress), response.Port);
            session.Send(MigrationPacket.GameToLogin(endpoint, response.Token));
            // Do NOT disconnect here — let the client close the TCP connection after
            // receiving the migration packet. Calling Disconnect() immediately would
            // set disconnecting=1, causing SendWorker to drop the queued packet.
            // The natural TCP close will trigger the full Dispose chain (leave field,
            // update PlayerInfo, save state, etc.).
        } catch (RpcException ex) {
            Logger.Error(ex, "MigrateOut failed for account={AccountId} char={CharacterId}",
                session.AccountId, session.CharacterId);
            session.Send(MigrationPacket.GameToLoginError(s_move_err_default));
            session.Disconnect();
        }
    }
}
