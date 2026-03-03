using System.Text.RegularExpressions;
using System.Xml;
using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Game.PacketHandlers;

public class UserChatHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.UserChat;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required WorldClient World { private get; init; }
    public required GameStorage GameStorage { private get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;

    public override void Handle(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        var type = packet.Read<ChatType>();
        string message = packet.ReadUnicodeString();
        string recipient = packet.ReadUnicodeString();
        long clubId = packet.ReadLong();
        ICollection<long> itemUids = LinkedItemUids(session, message);

        // Handle ChatCommands
        if (message.StartsWith('/')) {
            session.CommandHandler.Invoke(message[1..]);
            return;
        }

        switch (type) {
            case ChatType.Normal:
                HandleNormal(session, message, itemUids);
                break;
            case ChatType.WhisperTo:
                HandleWhisper(session, message, recipient, itemUids);
                break;
            case ChatType.Party:
                HandleParty(session, message, itemUids);
                break;
            case ChatType.Guild:
                HandleGuild(session, message, itemUids);
                break;
            case ChatType.World:
                HandleWorld(session, message, itemUids);
                break;
            case ChatType.Channel:
                HandleChannel(session, message, itemUids);
                break;
            case ChatType.Super:
                HandleSuper(session, message, itemUids);
                break;
            case ChatType.Club:
                HandleClub(session, message, clubId, itemUids);
                break;
            case ChatType.Wedding:
                HandleWedding(session, message, itemUids);
                break;
        }

        session.ConditionUpdate(ConditionType.chat, targetLong: session.Field.MapId);
    }

    private static void HandleNormal(GameSession session, string message, ICollection<long> itemUids) {
        if (itemUids.Count > 0) {
            List<Item> items = [];
            using GameStorage.Request db = session.GameStorage.Context();
            foreach (long itemUid in itemUids) {
                Item? item = db.GetItem(itemUid);
                if (item != null) {
                    items.Add(item);
                }
            }
            if (items.Count > 0) {
                session.Field?.Broadcast(MessengerBrowserPacket.Link(items.ToArray()));
            }
        }

        session.Field?.Broadcast(ChatPacket.Message(session.Player.Value, ChatType.Normal, message));
    }

    private void HandleWhisper(GameSession session, string message, string recipient, ICollection<long> itemUids) {
        if (recipient == session.PlayerName) {
            session.Send(ChatPacket.Alert(StringCode.s_whisper_err_myself));
            return;
        }

        using GameStorage.Request db = GameStorage.Context();
        long characterId = db.GetCharacterId(recipient);
        if (!session.PlayerInfo.GetOrFetch(characterId, out PlayerInfo? info)) {
            session.Send(ChatPacket.Alert(StringCode.s_whisper_err_target));
            return;
        }

        var request = new ChatRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Name = session.PlayerName,
            Message = message,
            ItemUids = { itemUids },
            Whisper = new ChatRequest.Types.Whisper { RecipientId = info.CharacterId, RecipientName = recipient },
        };

        try {
            World.Chat(request);
            session.Send(ChatPacket.Whisper(info.AccountId, info.CharacterId, info.Name, request.Message));
        } catch (RpcException ex) {
            switch (ex.StatusCode) {
                case StatusCode.NotFound:
                    session.Send(ChatPacket.Alert(StringCode.s_err_cannot_find_user));
                    return;
                case StatusCode.PermissionDenied:
                    session.Send(ChatPacket.WhisperReject(recipient));
                    return;
                case StatusCode.Unavailable:
                    return;
            }
        }
    }

    private void HandleParty(GameSession session, string message, ICollection<long> itemUids) {
        if (session.Party.Party == null) {
            return;
        }

        var request = new ChatRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Name = session.PlayerName,
            Message = message,
            ItemUids = { itemUids },
            Party = new ChatRequest.Types.Party {
                PartyId = session.Party.Id,
            },
        };

        try {
            World.Chat(request);
        } catch (RpcException) { }
    }

    private void HandleGuild(GameSession session, string message, ICollection<long> itemUids) {
        if (session.Guild.Guild == null) {
            return;
        }

        var request = new ChatRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Name = session.PlayerName,
            Message = message,
            ItemUids = { itemUids },
            Guild = new ChatRequest.Types.Guild {
                GuildId = session.Guild.Id,
            },
        };

        try {
            World.Chat(request);
        } catch (RpcException) { }
    }

    private void HandleWorld(GameSession session, string message, ICollection<long> itemUids) {
        var voucher = session.Item.Inventory.Filter(item => item.Metadata.Property.Tag == ItemTag.FreeWorldChatCoupon).FirstOrDefault();
        if (voucher != null) {
            if (!session.Item.Inventory.Consume(voucher.Uid, 1)) {
                session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert | NoticePacket.Flags.Message, StringCode.s_err_invalid_item));
                return;
            }
            session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert | NoticePacket.Flags.Message, StringCode.s_worldchat_use_coupon));
        } else {
            int meretCost = Constants.MeratConsumeWorldChat;
            if (session.FindEvent(GameEventType.SaleChat).FirstOrDefault()?.Metadata.Data is SaleChat gameEvent) {
                meretCost -= (int) (meretCost * Convert.ToSingle(gameEvent.WorldChatDiscount) / 10000);
            }

            if (session.Currency.Meret < meretCost) {
                session.Send(ChatPacket.Alert(StringCode.s_err_lack_merat));
                return;
            }
            session.Currency.Meret -= meretCost;
        }

        var request = new ChatRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Name = session.PlayerName,
            Message = message,
            ItemUids = { itemUids },
            World = new ChatRequest.Types.World(),
        };

        try {
            World.Chat(request);
        } catch (RpcException) { }
    }

    private void HandleChannel(GameSession session, string message, ICollection<long> itemUids) {
        var voucher = session.Item.Inventory.Filter(item => item.Metadata.Property.Tag == ItemTag.FreeChannelChatCoupon).FirstOrDefault();
        if (voucher != null) {
            if (!session.Item.Inventory.Consume(voucher.Uid, 1)) {
                session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert | NoticePacket.Flags.Message, StringCode.s_err_invalid_item));
                return;
            }
            session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert | NoticePacket.Flags.Message, StringCode.s_channelchat_use_coupon));
        } else {
            int meretCost = Constants.MeratConsumeChannelChat;
            if (session.FindEvent(GameEventType.SaleChat).FirstOrDefault()?.Metadata.Data is SaleChat gameEvent) {
                meretCost -= (int) (meretCost * Convert.ToSingle(gameEvent.ChannelChatDiscount) / 10000);
            }

            if (session.Currency.Meret < meretCost) {
                session.Send(ChatPacket.Alert(StringCode.s_err_lack_merat));
                return;
            }
            session.Currency.Meret -= meretCost;
        }

        session.ChannelBroadcast(ChatPacket.Message(session.AccountId, session.CharacterId, session.PlayerName, ChatType.Channel, message));
    }

    private void HandleSuper(GameSession session, string message, ICollection<long> itemUids) {
        if (session.SuperChatId == 0) {
            return;
        }

        Item? superChatItem = session.Item.Inventory.Find(session.SuperChatItemId).FirstOrDefault();
        if (superChatItem == null) {
            session.SuperChatId = 0;
            session.SuperChatItemId = 0;
            session.Send(SuperChatPacket.Deselect(session.Player.ObjectId));
            session.Send(ChatPacket.Alert(StringCode.s_err_lack_super_coupon));
            return;
        }

        var request = new ChatRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Name = session.PlayerName,
            Message = message,
            ItemUids = { itemUids },
            Super = new ChatRequest.Types.Super { ItemId = session.SuperChatId },
        };

        if (session.Item.Inventory.Consume(superChatItem.Uid, 1)) {
            try {
                session.SuperChatId = 0;
                session.SuperChatItemId = 0;
                session.Send(SuperChatPacket.Deselect(session.Player.ObjectId));
                World.Chat(request);
            } catch (RpcException) { }
        }

    }

    private void HandleClub(GameSession session, string message, long clubId, ICollection<long> itemUids) {
        if (!session.Clubs.ContainsKey(clubId)) {
            session.Send(ChatPacket.Alert(StringCode.s_err_cannot_find_club));
            return;
        }

        var request = new ChatRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Name = session.PlayerName,
            Message = message,
            ItemUids = { itemUids },
            Club = new ChatRequest.Types.Club { ClubId = clubId },
        };

        try {
            World.Chat(request);
        } catch (RpcException) { }
    }

    private void HandleWedding(GameSession session, string message, ICollection<long> itemUids) {
        var request = new ChatRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Name = session.PlayerName,
            Message = message,
            ItemUids = { itemUids },
            Wedding = new ChatRequest.Types.Wedding { ItemId = 0 },
        };

        try {
            World.Chat(request);
        } catch (RpcException) { }
    }

    private static ICollection<long> LinkedItemUids(GameSession session, string message) {
        List<long> itemUids = [];

        MatchCollection matches = Regex.Matches(message, "<A HREF=\"event:[^\"]+\">[^<]+</A>");
        string[] links = matches.Select(match => match.Value).ToArray();
        foreach (string link in links) {
            string linkHtml = System.Net.WebUtility.HtmlDecode(link);
            XmlDocument xml = new();
            xml.LoadXml(linkHtml);

            // Select the 'A' node and get the 'HREF' attribute
            XmlNode? aNode = xml.SelectSingleNode("//A");

            string? hrefValue = aNode?.Attributes?["HREF"]?.Value;
            if (hrefValue == null) {
                continue;
            }

            // Remove "event:" and split by comma
            string eventValue = hrefValue.Substring("event:".Length);
            string[] parameters = eventValue.Split(',');

            switch (parameters[0]) {
                case "itemTooltip":
                    if (!long.TryParse(parameters[1], out long itemUid)) {
                        continue;
                    }
                    // Can only link items player owns.
                    Item? item = session.Item.Inventory.Get(itemUid) ?? session.Item.Equips.Get(itemUid);
                    if (item != null) {
                        itemUids.Add(item.Uid);
                    }
                    break;
                case "achieveTooltip":
                case "billDialog":
                case "searchFromMeratmarket":
                    break;
            }
        }
        return itemUids;
    }
}
