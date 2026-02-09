using System.Net;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Game.PacketHandlers;

public class ChannelHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.Channel;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required WorldClient World { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        short channel = packet.ReadShort();
        session.MigrationSave();
        try {
            var request = new MigrateOutRequest {
                AccountId = session.AccountId,
                CharacterId = session.CharacterId,
                MachineId = session.MachineId.ToString(),
                Server = Server.World.Service.Server.Game,
                Channel = channel,
            };

            MigrateOutResponse response = World.MigrateOut(request);
            var endpoint = new IPEndPoint(IPAddress.Parse(response.IpAddress), response.Port);
            session.Send(MigrationPacket.GameToGame(endpoint, response.Token, session.Field?.MapId ?? 0));
            session.State = SessionState.ChangeChannel;
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to migrate to channel {Channel}", channel);

            session.Send(NoticePacket.MessageBox(new InterfaceText("Channel is unavailable, close the channel list and try again.")));

            // Update the client with the latest channel list.
            ChannelsResponse response = World.Channels(new ChannelsRequest());
            session.Send(ChannelPacket.Load(response.Channels));
        } finally {
            session.Disconnect();
        }
    }
}
