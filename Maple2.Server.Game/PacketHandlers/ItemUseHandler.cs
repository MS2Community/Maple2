using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using static Maple2.Model.Error.CharacterCreateError;

namespace Maple2.Server.Game.PacketHandlers;

public class ItemUseHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestItemUse;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required GameStorage GameStorage { private get; init; }
    public required TableMetadataStorage TableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        long itemUid = packet.ReadLong();

        Item? item = session.Item.Inventory.Get(itemUid);
        if (item == null) {
            Logger.Warning("RequestItemUse for invalid item:{ItemUid}", itemUid);
            return;
        }

        if (item.Metadata.Limit.RequireVip && session.Player.Value.Account.PremiumTime < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) {
            return;
        }

        if (item.Metadata.Function?.OnlyShadowWorld == true && session.Field.Metadata.Property.Continent != Continent.ShadowWorld) {
            return;
        }

        if (session.Survival.TryUseGoldPassActivationItem(item)) {
            return;
        }

        if (session.Survival.TryUsePassExpItem(item)) {
            return;
        }

        if (session.Survival.TryUseSkinItem(item)) {
            return;
        }

        switch (item.Metadata.Function?.Type) {
            case ItemFunction.BlueprintImport:
                HandleBlueprintImport(session, item);
                break;
            case ItemFunction.StoryBook:
                HandleStoryBook(session, item);
                break;
            case ItemFunction.ChatEmoticonAdd:
                HandleChatSticker(session, item);
                break;
            case ItemFunction.EnchantScroll:
                HandleEnchantScroll(session, item);
                break;
            case ItemFunction.ItemRemakeScroll:
                HandleItemRemakeScroll(session, item);
                break;
            case ItemFunction.ItemSocketScroll:
                HandleItemSocketScroll(session, item);
                break;
            case ItemFunction.TitleScroll:
                HandleTitleScroll(session, item);
                break;
            case ItemFunction.OpenCoupleEffectBox:
                HandleBuddyBadgeBox(session, packet, item);
                break;
            case ItemFunction.ExpendCharacterSlot:
                HandleExpandCharacterSlot(session, item);
                break;
            case ItemFunction.ChangeCharName:
                HandleChangeCharacterName(session, packet, item);
                break;
            case ItemFunction.VIPCoupon:
                HandlePremiumClubCoupon(session, item);
                break;
            case ItemFunction.SelectItemBox:
                HandleSelectItemBox(session, packet, item);
                break;
            case ItemFunction.OpenItemBox:
            case ItemFunction.OpenItemBoxWithKey:
                HandleOpenItemBox(session, item);
                break;
            case ItemFunction.OpenGachaBox:
                HandleOpenGacha(session, packet, item);
                break;
            case ItemFunction.OpenItemBoxLullu:
                HandleOpenItemBoxLullu(session, packet, item);
                break;
            case ItemFunction.OpenItemBoxLulluSimple:
                HandleOpenItemBoxLulluSimple(session, packet, item);
                break;
            case ItemFunction.ItemRePackingScroll:
                HandleItemRepackingScroll(session, item);
                break;
            case ItemFunction.ItemChangeBeauty:
                HandleItemChangeBeauty(session, item);
                break;
            case ItemFunction.InstallBillBoard:
                HandleInstallBillBoard(session, packet, item);
                break;
            case ItemFunction.OpenMassive:
                HandleOpenMassive(session, packet, item);
                break;
            case ItemFunction.DefenseGuard:
                HandleDefenseGuard(session, item);
                break;
            case ItemFunction.LevelPotion:
                HandleLevelPotion(session, item);
                break;
            case ItemFunction.HongBao:
                HandleHongBao(session, item);
                break;
            case ItemFunction.AddAdditionalEffect:
                HandleAddAdditionalEffect(session, item);
                break;
            case ItemFunction.ExpandInven:
                HandleExpandInventory(session, item);
                break;
            case ItemFunction.QuestScroll:
                HandleQuestScroll(session, item);
                break;
            case ItemFunction.CallAirTaxi:
                HandleCallAirTaxi(session, packet, item);
                break;
            default:
                Logger.Warning("Unhandled item function: {Name}", item.Metadata.Function?.Type);
                return;
        }
    }

    private void HandleBlueprintImport(GameSession session, Item item) {
        if (item.Blueprint is null) {
            Logger.Error("Item {ItemUid} is missing blueprint", item.Uid);
            return;
        }

        Plot? plot = session.Housing.GetIndoorPlot();
        if (plot is null) {
            session.Send(NoticePacket.Message(StringCode.s_ugcmap_not_use_blueprint_item, NoticePacket.Flags.Message | NoticePacket.Flags.Alert));
            return;
        }

        if (plot.Cubes.Count != 0) {
            session.Send(NoticePacket.Message(StringCode.s_err_ugcmap_package_clear_indoor_first, NoticePacket.Flags.Message | NoticePacket.Flags.Alert));
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        HomeLayout? layout = db.GetHomeLayout(item.Blueprint.BlueprintUid);
        if (layout == null) {
            return;
        }

        if (!session.Housing.RequestLayout(layout, out (Dictionary<FurnishingCurrencyType, long> cubeCosts, int cubeCount) result)) {
            return;
        }

        session.Housing.StagedItemBlueprint = item.Blueprint;
        session.Send(CubePacket.BuyCubes(result.cubeCosts, result.cubeCount));
    }

    private static void HandleStoryBook(GameSession session, Item item) {
        if (!int.TryParse(item.Metadata.Function?.Parameters, out int storyBookId)) {
            return;
        }

        session.ConditionUpdate(ConditionType.openStoryBook, codeLong: storyBookId);
        session.Send(StoryBookPacket.Load(storyBookId));
    }

    private static void HandleChatSticker(GameSession session, Item item) {
        Dictionary<string, string> parameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);

        if (!parameters.ContainsKey("id") || !int.TryParse(parameters["id"], out int stickerSetId)) {
            session.Send(ChatStickerPacket.Error(ChatStickerError.s_msg_chat_emoticon_add_failed));
            return;
        }

        int duration = 0;
        if (parameters.TryGetValue("durationSec", out string? durationString)) {
            int.TryParse(durationString, out duration);
        }

        long existingTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (session.Player.Value.Unlock.StickerSets.TryGetValue(stickerSetId, out long set)) {
            existingTime = set;
            if (existingTime == long.MaxValue) {
                session.Send(ChatStickerPacket.Error(ChatStickerError.s_msg_chat_emoticon_add_failed_already_exist));
                return;
            }
        }

        long newTime = existingTime + duration;
        if (duration == 0) {
            newTime = long.MaxValue;
        }

        if (newTime <= existingTime) {
            session.Send(ChatStickerPacket.Error(ChatStickerError.s_msg_chat_emoticon_add_failed_already_exist));
            return;
        }

        if (!session.Item.Inventory.Consume(item.Uid, 1)) {
            return;
        }

        session.Player.Value.Unlock.StickerSets[stickerSetId] = newTime;

        session.Send(ChatStickerPacket.Add(item, new ChatSticker(stickerSetId, session.Player.Value.Unlock.StickerSets[stickerSetId])));
    }

    private void HandleEnchantScroll(GameSession session, Item item) {
        if (!int.TryParse(item.Metadata.Function?.Parameters, out int enchantId)) {
            session.Send(EnchantScrollPacket.Error(EnchantScrollError.s_enchantscroll_invalid_scroll));
            return;
        }

        if (!TableMetadata.EnchantScrollTable.Entries.TryGetValue(enchantId, out EnchantScrollMetadata? metadata)) {
            session.Send(EnchantScrollPacket.Error(EnchantScrollError.s_enchantscroll_invalid_scroll));
            return;
        }

        session.Send(EnchantScrollPacket.UseScroll(item, metadata));
    }

    private static void HandleItemRemakeScroll(GameSession session, Item item) {
        session.Send(ChangeAttributesScrollPacket.UseScroll(item));
    }

    private void HandleItemSocketScroll(GameSession session, Item item) {
        if (!int.TryParse(item.Metadata.Function?.Parameters, out int scrollId)) {
            session.Send(ItemSocketScrollPacket.Error(ItemSocketScrollError.s_itemsocket_scroll_error_server_default));
            return;
        }

        if (!TableMetadata.ItemSocketScrollTable.Entries.TryGetValue(scrollId, out ItemSocketScrollMetadata? metadata)) {
            session.Send(ItemSocketScrollPacket.Error(ItemSocketScrollError.s_itemsocket_scroll_error_server_default));
            return;
        }

        session.Send(ItemSocketScrollPacket.UseScroll(item, metadata));
    }

    private static void HandleTitleScroll(GameSession session, Item item) {
        if (!int.TryParse(item.Metadata.Function?.Parameters, out int titleId)) {
            return;
        }

        if (session.Player.Value.Unlock.Titles.Contains(titleId)) {
            session.Send(ChatPacket.Alert(StringCode.s_title_scroll_duplicate_err));
            return;
        }

        if (session.Item.Inventory.Consume(item.Uid, 1)) {
            session.Send(UserEnvPacket.AddTitle(titleId));
            session.Player.Value.Unlock.Titles.Add(titleId);
        }
    }

    private void HandleBuddyBadgeBox(GameSession session, IByteReader packet, Item item) {
        string targetUser = packet.ReadUnicodeString();

        if (targetUser == session.PlayerName) {
            session.Send(NoticePacket.MessageBox(StringCode.s_couple_effect_error_openbox_myself_char));
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        long characterId = db.GetCharacterId(targetUser);
        if (!session.PlayerInfo.GetOrFetch(characterId, out PlayerInfo? receiverInfo)) {
            session.Send(NoticePacket.MessageBox((StringCode.s_couple_effect_error_openbox_charname)));
            return;
        }

        if (receiverInfo.AccountId == session.Player.Value.Character.AccountId) {
            session.Send(NoticePacket.MessageBox(StringCode.s_couple_effect_error_openbox_myself_account));
            return;
        }

        int[] buddyBadgeBoxParams = item.Metadata.Function?.Parameters.Split(',').Select(int.Parse).ToArray() ?? [];
        if (buddyBadgeBoxParams.Length != 2) {
            session.Send(NoticePacket.MessageBox(StringCode.s_couple_effect_error_openbox_unknown));
            Logger.Error("Invalid buddy badge box parameters: {Parameters}", item.Metadata.Function?.Parameters);
            return;
        }

        Item? selfBadge = session.Field?.ItemDrop.CreateItem(buddyBadgeBoxParams[0], buddyBadgeBoxParams[1]);
        if (selfBadge == null) {
            session.Send(NoticePacket.MessageBox(StringCode.s_couple_effect_error_openbox_unknown));
            Logger.Error("Failed to create buddy badge box item: {Parameters}", buddyBadgeBoxParams[0]);
            return;
        }
        selfBadge.CoupleInfo = new ItemCoupleInfo(receiverInfo.CharacterId, receiverInfo.Name, true);

        if (!session.Item.Inventory.CanAdd(selfBadge)) {
            session.Send(NoticePacket.MessageBox(StringCode.s_gem_error_inventory_full));
            return;
        }

        if (!session.Item.Inventory.Consume(item.Uid, 1)) {
            Logger.Error("Failed to use buddy badge box: {ItemUid}", item.Uid);
            return;
        }

        var receiverMail = new Mail() {
            ReceiverId = receiverInfo.CharacterId,
            Type = MailType.System,
            ContentArgs = [
                ("str", $"{session.PlayerName}"),
            ],
        };

        receiverMail.SetTitle(StringCode.s_couple_effect_mail_title_receiver);
        receiverMail.SetContent(StringCode.s_couple_effect_mail_content_receiver);
        receiverMail.SetSenderName(StringCode.s_couple_effect_mail_sender);

        receiverMail = db.CreateMail(receiverMail);
        if (receiverMail == null) {
            session.Send(NoticePacket.MessageBox((StringCode.s_couple_effect_error_openbox_unknown)));
            throw new InvalidOperationException($"Failed to create buddy badge mail for receiver character id: {receiverInfo.CharacterId}");
        }

        Item? receiverItem = session.Field?.ItemDrop.CreateItem(buddyBadgeBoxParams[0], buddyBadgeBoxParams[1]);
        if (receiverItem == null) {
            throw new InvalidOperationException($"Failed to create buddy badge item: {buddyBadgeBoxParams[0]}");
        }
        receiverItem.CoupleInfo = new ItemCoupleInfo(session.Player.Value.Character.Id, session.PlayerName);
        receiverItem = db.CreateItem(receiverMail.Id, receiverItem);
        if (receiverItem == null) {
            throw new InvalidOperationException($"Failed to create buddy badge: {buddyBadgeBoxParams[0]}");
        }

        receiverMail.Items.Add(receiverItem);

        try {
            session.World.MailNotification(new MailNotificationRequest {
                CharacterId = receiverInfo.CharacterId,
                MailId = receiverMail.Id,
            });
        } catch { /* ignored */
        }

        session.Item.Inventory.Add(selfBadge, true);
        session.Send(NoticePacket.MessageBox(new InterfaceText(StringCode.s_couple_effect_mail_send_partner, receiverInfo.Name)));
    }

    private static void HandleExpandCharacterSlot(GameSession session, Item item) {
        if (session.Player.Value.Account.MaxCharacters >= Constant.ServerMaxCharacters) {
            session.Send(ItemUsePacket.MaxCharacterSlots());
            return;
        }

        if (session.Item.Inventory.Consume(item.Uid, 1)) {
            session.Player.Value.Account.MaxCharacters++;
            session.Send(ItemUsePacket.CharacterSlotAdded());
        }
    }

    private void HandleChangeCharacterName(GameSession session, IByteReader packet, Item item) {
        string newName = packet.ReadUnicodeString();

        if (newName.Length < Constant.CharacterNameLengthMin) {
            session.Send(CharacterListPacket.CreateError(s_char_err_name));
            return;
        }

        if (newName.Length > Constant.CharacterNameLengthMax) {
            session.Send(CharacterListPacket.CreateError(s_char_err_system));
            return;
        }

        using GameStorage.Request db = GameStorage.Context();
        long existingId = db.GetCharacterId(newName);
        if (existingId != default) {
            session.Send(CharacterListPacket.CreateError(s_char_err_already_taken));
            return;
        }

        if (!session.Item.Inventory.Consume(item.Uid, 1)) {
            return;
        }

        session.Player.Value.Character.Name = newName;
        // TODO: Update name on clubs, party(?), group chat(?)
        session.PlayerInfo.SendUpdate(new PlayerUpdateRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Name = newName,
        });
        session.Send(CharacterListPacket.NameChanged(session.CharacterId, newName));
    }

    private static void HandlePremiumClubCoupon(GameSession session, Item item) {
        Dictionary<string, string> parameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);

        if (!parameters.ContainsKey("period") || !int.TryParse(parameters["period"], out int hours)) {
            return;
        }

        if (!session.Item.Inventory.Consume(item.Uid, 1)) {
            return;
        }

        session.Config.UpdatePremiumTime(hours);
    }

    private static void HandleSelectItemBox(GameSession session, IByteReader packet, Item item) {
        if (!int.TryParse(packet.ReadUnicodeString(), out int index)) {
            return;
        }
        session.ItemBox.Open(item, index: index);
        session.ItemBox.Reset();
    }

    private static void HandleOpenItemBox(GameSession session, Item item) {
        ItemBoxError error = session.ItemBox.Open(item);
        session.Send(ItemBoxPacket.Open(item.Id, session.ItemBox.BoxCount, error));
        session.ItemBox.Reset();
    }

    private static void HandleOpenGacha(GameSession session, IByteReader packet, Item item) {
        string amountString = packet.ReadUnicodeString();
        ItemBoxError error = session.ItemBox.Open(item, amountString == "multi" ? 10 : 1);
        session.Send(ItemBoxPacket.Open(item.Id, session.ItemBox.BoxCount, error));
        session.ItemBox.Reset();
    }

    private static void HandleOpenItemBoxLullu(GameSession session, IByteReader packet, Item item) {
        string amountString = packet.ReadUnicodeString();
        ItemBoxError error = session.ItemBox.OpenLulluBox(item, amountString.Contains("multi") ? 10 : 1, autoPay: amountString.Contains("autoPay"));
        session.Send(ItemBoxPacket.Open(item.Id, session.ItemBox.BoxCount, error));
        session.ItemBox.Reset();
    }

    private static void HandleOpenItemBoxLulluSimple(GameSession session, IByteReader packet, Item item) {
        string amountString = packet.ReadUnicodeString();
        ItemBoxError error = session.ItemBox.OpenLulluBoxSimple(item, amountString == "multi" ? 10 : 1);
        session.Send(ItemBoxPacket.Open(item.Id, session.ItemBox.BoxCount, error));
        session.ItemBox.Reset();
    }

    private static void HandleItemRepackingScroll(GameSession session, Item item) {
        session.Send(ItemRepackPacket.Open(item.Uid));
    }

    private static void HandleItemChangeBeauty(GameSession session, Item item) {
        session.Send(ItemUsePacket.BeautyCoupon(session.Player.Value.ObjectId, item.Uid));
    }

    private static void HandleInstallBillBoard(GameSession session, IByteReader packet, Item item) {
        string[] fieldParameters = packet.ReadUnicodeString().Split("'");
        if (fieldParameters.Length < 3 || session.Field == null) {
            return;
        }

        Dictionary<string, string> functionParameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);
        if (!functionParameters.ContainsKey("interactID") || !int.TryParse(functionParameters["interactID"], out int interactId) ||
            !functionParameters.ContainsKey("durationSec") || !int.TryParse(functionParameters["durationSec"], out int durationSec) ||
            !functionParameters.ContainsKey("model") || !functionParameters.ContainsKey("normal") || !functionParameters.ContainsKey("reactable")) {
            return;
        }

        string globalId = Guid.NewGuid().ToString();
        var interactMesh = new Ms2InteractMesh(interactId, session.Player.Position, session.Player.Rotation);
        var billboard = new InteractBillBoardObject("BillBoard_" + globalId, interactMesh, session.Player.Value.Character) {
            Title = fieldParameters[0],
            Description = fieldParameters[1],
            PublicHouse = fieldParameters[2].Equals("1"),
            CreationTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ExpirationTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + durationSec,
            Model = functionParameters["model"],
            Asset = functionParameters.TryGetValue("asset", out string? parameter) ? parameter : string.Empty,
            NormalState = functionParameters["normal"],
            Reactable = functionParameters["reactable"],
            Scale = functionParameters.TryGetValue("scale", out string? scaleString) && !float.TryParse(scaleString, out float scale) ? scale : 1f,
        };

        FieldInteract? fieldInteract = session.Field.AddInteract(interactMesh, billboard);
        if (fieldInteract == null) {
            return;
        }
        session.ConditionUpdate(ConditionType.install_billboard, targetLong: session.Field.MapId);
        session.Field.Broadcast(InteractObjectPacket.Add(fieldInteract.Object));
        session.Item.Inventory.Consume(item.Uid, 1);
    }

    private static void HandleOpenMassive(GameSession session, IByteReader packet, Item item) {
        if (session.Field == null) {
            return;
        }
        string password = packet.ReadUnicodeString();
        Dictionary<string, string> parameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);

        // TODO: Handle maxCount and weddingParty
        if (!parameters.ContainsKey("fieldID") || !int.TryParse(parameters["fieldID"], out int fieldId) ||
            !parameters.ContainsKey("portalDurationTick") || !int.TryParse(parameters["portalDurationTick"], out int portalDurationTick)) {
            return;
        }

        if (!session.Item.Inventory.Consume(item.Uid, 1)) {
            throw new InvalidOperationException($"Failed to consume item: {item.Uid}");
        }
        session.Send(PlayerHostPacket.StartMiniGame(session.PlayerName, fieldId));
        FieldPortal portal = session.Field.SpawnEventPortal(session.Player, fieldId, portalDurationTick, password);
        session.Field.UsePortal(session, portal.Value.Id, password);
    }

    private static void HandleDefenseGuard(GameSession session, Item item) {
        int[] npcParameters = item.Metadata.Function?.Parameters.Split(',').Select(int.Parse).ToArray() ?? [];
        if (npcParameters.Length < 3) {
            return;
        }

        int npcId = npcParameters[0];
        int lifeSpanSeconds = npcParameters[1];
        int distance = npcParameters[2];

        if (!session.NpcMetadata.TryGet(npcId, out NpcMetadata? npcMetadata)) {
            return;
        }

        Vector3 position = session.Player.Transform.Position + session.Player.Transform.FrontAxis * distance;
        // TODO: Do we check Z?
        FieldNpc? fieldNpc = session.Field?.SpawnNpc(npcMetadata, position, session.Player.Rotation);
        if (fieldNpc == null || session.Field is null) {
            return;
        }
        session.Field.Broadcast(FieldPacket.AddNpc(fieldNpc));
        session.Field.Broadcast(ProxyObjectPacket.AddNpc(fieldNpc));
        session.Field.RemoveNpc(fieldNpc.ObjectId, TimeSpan.FromSeconds(lifeSpanSeconds));
        session.Item.Inventory.Consume(item.Uid, 1);
    }

    private static void HandleLevelPotion(GameSession session, Item item) {
        Dictionary<string, string> xmlParameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);
        if (!xmlParameters.ContainsKey("targetLevel") || !short.TryParse(xmlParameters["targetLevel"], out short level)) {
            return;
        }

        int lastClearEpicQuestId = 0;
        if (xmlParameters.TryGetValue("lastClearEpic", out string? lastEpicQuestId) && !int.TryParse(lastEpicQuestId, out lastClearEpicQuestId)) {
            lastClearEpicQuestId = 0;
        }

        int currentLevel = session.Player.Value.Character.Level;
        session.Player.Value.Character.Level = level;
        session.Field?.Broadcast(LevelUpPacket.LevelUp(session.Player));
        session.Stats.Refresh();

        session.Quest.LevelPotion(level - 1, lastClearEpicQuestId);

        for (int i = currentLevel; i < level; i++) {
            session.ConditionUpdate(ConditionType.level, targetLong: i);
        }

        session.PlayerInfo.SendUpdate(new PlayerUpdateRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Level = level,
            Async = true,
        });

        session.Item.Inventory.Consume(item.Uid, 1);
    }

    private static void HandleHongBao(GameSession session, Item item) {
        Dictionary<string, string> xmlParameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);
        if (!xmlParameters.ContainsKey("itemId") || !int.TryParse(xmlParameters["itemId"], out int itemId) ||
            !xmlParameters.ContainsKey("totalCount") || !int.TryParse(xmlParameters["totalCount"], out int totalCount) ||
            !xmlParameters.ContainsKey("totalUser") || !int.TryParse(xmlParameters["totalUser"], out int totalUser) ||
            !xmlParameters.ContainsKey("durationSec") || !int.TryParse(xmlParameters["durationSec"], out int durationSec)) {
            return;
        }
        session.Item.Inventory.Consume(item.Uid, 1);
        session.Field?.AddHongBao(session.Player, item.Id, itemId, totalUser, durationSec, totalCount);
    }

    private void HandleAddAdditionalEffect(GameSession session, Item item) {
        if (session.Field is null) return;
        int[] parameters = item.Metadata.Function?.Parameters.Split(',').Select(int.Parse).ToArray() ?? [];
        if (parameters.Length < 2) {
            return;
        }

        int effectId = parameters[0];
        short effectLevel = (short) parameters[1];
        session.Player.Buffs.AddBuff(session.Player, session.Player, effectId, effectLevel, session.Field.FieldTick, notifyField: true);
        session.Item.Inventory.Consume(item.Uid, 1);
    }

    private void HandleExpandInventory(GameSession session, Item item) {
        string[] parameters = item.Metadata.Function?.Parameters.Split(',') ?? [];
        if (parameters.Length < 2) {
            return;
        }

        int amount = int.Parse(parameters[0]);
        InventoryType inventoryType = parameters[1] switch {
            "game" => InventoryType.Gear,
            "mastery" => InventoryType.LifeSkill,
            "summon" => InventoryType.Mount,
            "misc" => InventoryType.Misc,
            "skin" => InventoryType.Outfit,
            "gem" => InventoryType.Gemstone,
            "material" => InventoryType.Catalyst,
            "quest" => InventoryType.Quest,
            "life" => InventoryType.FishingMusic,
            "coin" => InventoryType.Currency,
            "pet" => InventoryType.Pets,
            "activeSkill" => InventoryType.Consumable,
            "badge" => InventoryType.Badge,
            "piece" => InventoryType.Fragment,
            _ => InventoryType.Misc,
        };

        if (!session.Item.Inventory.Expand(inventoryType, amount)) {
            session.Send(ItemUsePacket.MaxInventory());
            return;
        }

        session.Send(ItemUsePacket.ExpandInventory());
        session.Item.Inventory.Consume(item.Uid, 1);
    }

    private void HandleQuestScroll(GameSession session, Item item) {
        Dictionary<string, string> xmlParameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);
        if (!xmlParameters.ContainsKey("questID")) {
            Logger.Warning("QuestScroll item {ItemId} is missing questID parameter", item.Id);
            return;
        }

        int[] questIds = xmlParameters["questID"].Split(',').Select(int.Parse).ToArray();
        foreach (int questId in questIds) {
            if (!session.QuestMetadata.TryGet(questId, out QuestMetadata? metadata)) {
                Logger.Warning("QuestScroll item {ItemId} has invalid questID {QuestId}", item.Id, questId);
                return;
            }
            if (!session.Quest.CanStart(metadata)) {
                Logger.Warning("QuestScroll item {ItemId} has questID {QuestId} that cannot be started", item.Id, questId);
                return;
            }
        }

        foreach (int questId in questIds) {
            session.Quest.Start(questId);
        }

        session.Item.Inventory.Consume(item.Uid, 1);
        session.Send(ItemUsePacket.QuestScroll(item.Id));
    }

    private void HandleCallAirTaxi(GameSession session, IByteReader packet, Item item) {
        if (session.Field is null) return;
        string mapIdString = packet.ReadUnicodeString();

        if (!int.TryParse(mapIdString, out int mapId)) {
            Logger.Warning("Invalid map ID: {MapId}", mapIdString);
            return;
        }

        if (!TaxiHandler.CheckMapCashCall(session, mapId, session.Field.MapMetadata)) {
            return;
        }

        if (!session.Item.Inventory.Consume(item.Uid, 1)) {
            return;
        }

        session.Send(session.PrepareField(mapId)
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(MigrationError.s_move_err_default));
    }
}
