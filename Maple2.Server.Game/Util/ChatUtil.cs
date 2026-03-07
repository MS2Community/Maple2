using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Util;

public static class ChatUtil {
    public static void SendEnchantAnnouncement(GameSession session, Item item) {
        if (session.ServerTableMetadata.ConstantsTable.enchantSuccessBroadcastingLevel > item.Enchant?.Enchants) {
            return;
        }

        // Saving the item to ensure the latest changes can be seen when broadcasted
        GameStorage.Request db = session.GameStorage.Context();
        db.SaveItems(session.CharacterId, item);

        const int unknownMode = 3;
        string message = string.Join(",", unknownMode, item.Uid, (int) AnnounceType.s_itemenchant_success_notice, session.PlayerName);

        ChatResponse? chatResponse = session.World.Chat(new ChatRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Name = session.PlayerName,
            Message = message,
            ItemUids = { item.Uid },
            SystemNotice = new ChatRequest.Types.SystemNotice(),
        });
    }
}
