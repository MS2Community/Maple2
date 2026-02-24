// using Maple2.Model.Game;
// using Maple2.PacketLib.Tools;
// using Maple2.Server.Core.Constants;
// using Maple2.Server.Core.PacketHandlers;
// using Maple2.Server.Game.Model;
// using Maple2.Server.Game.Packets;
// using Maple2.Server.Game.Session;
//
// namespace Maple2.Server.Game.PacketHandlers;
//
// public class PlayerHostHandler : PacketHandler<GameSession> {
//     public override RecvOp OpCode => RecvOp.PlayerHost;
//
//     private enum Command : byte {
//         ClaimHongBao = 1,
//     }
//
//     public override void Handle(GameSession session, IByteReader packet) {
//         var command = packet.Read<Command>();
//
//         switch (command) {
//             case Command.ClaimHongBao:
//                 HandleClaimHongBao(session, packet);
//                 return;
//         }
//     }
//
//     private void HandleClaimHongBao(GameSession session, IByteReader packet) {
//         if (session.Field is null) return;
//         int hongBaoId = packet.ReadInt();
//         if (!session.Field.TryGetHongBao(hongBaoId, out HongBao? hongBao)) {
//             session.Send(PlayerHostPacket.InactiveHongBao());
//             return;
//         }
//
//         Item? item = hongBao.Claim(session.Player);
//         if (item == null) {
//             return;
//         }
//
//         session.Send(PlayerHostPacket.GiftHongBao(session.Player, hongBao, item.Amount));
//         if (!session.Item.Inventory.Add(item, true)) {
//             session.Item.MailItem(item);
//         }
//     }
// }
