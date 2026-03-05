using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.PacketHandlers;

public class BeautyHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Beauty;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required ItemMetadataStorage ItemMetadata { private get; init; }
    public required TableMetadataStorage TableMetadata { private get; init; }
    public required NpcMetadataStorage NpcMetadata { private get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    private enum Command : byte {
        Shop = 0,
        CreateBeauty = 3,
        Unknown4 = 4, // UpdateEar?
        UpdateBeauty = 5,
        UpdateSkin = 6,
        RandomHair = 7,
        Warp = 10,
        ConfirmRandomHair = 12,
        SaveHair = 16,
        AddSlots = 17,
        DeleteHair = 18,
        AskAddSlots = 19,
        ApplySavedHair = 21,
        GearDye = 22,
        Voucher = 23,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Shop:
                HandleShop(session, packet);
                return;
            case Command.CreateBeauty:
                HandleCreateBeauty(session, packet);
                return;
            case Command.Unknown4:
                packet.ReadLong();
                return;
            case Command.UpdateBeauty:
                HandleUpdateBeauty(session, packet);
                return;
            case Command.UpdateSkin:
                HandleUpdateSkin(session, packet);
                return;
            case Command.RandomHair:
                HandleRandomHair(session, packet);
                return;
            case Command.Warp:
                HandleWarp(session, packet);
                return;
            case Command.ConfirmRandomHair:
                HandleConfirmRandomHair(session, packet);
                return;
            case Command.SaveHair:
                HandleSaveHair(session, packet);
                return;
            case Command.AddSlots:
                HandleAddSlots(session, packet);
                return;
            case Command.DeleteHair:
                HandleDeleteHair(session, packet);
                return;
            case Command.AskAddSlots:
                HandleAskAddSlots(session, packet);
                return;
            case Command.ApplySavedHair:
                HandleApplySavedHair(session, packet);
                return;
            case Command.GearDye:
                HandleGearDye(session, packet);
                return;
            case Command.Voucher:
                HandleVoucher(session, packet);
                return;
        }
    }

    private void HandleShop(GameSession session, IByteReader packet) {
        int npcId = packet.ReadInt();
        var shopType = (BeautyShopType) packet.ReadByte();

        if (!NpcMetadata.TryGet(npcId, out NpcMetadata? metadata)) {
            return;
        }

        if (!session.ServerTableMetadata.BeautyShopTable.Entries.TryGetValue(metadata.Basic.ShopId, out BeautyShopMetadata? shopMetadata)) {
            // TODO: Error?
            return;
        }

        // filter out non-usable genders
        List<BeautyShopItem> entries = FilterItemsByGender(session.Player.Value.Character.Gender, shopMetadata.Items).ToList();

        // Add date-based items
        if (shopMetadata.ItemGroups.Length > 0) {
            BeautyShopItemGroup? group = shopMetadata.ItemGroups
                .Where(group => group.StartTime < DateTime.Now.ToEpochSeconds())
                .OrderByDescending(group => group.StartTime)
                .FirstOrDefault();
            if (group != null) {
                entries.AddRange(FilterItemsByGender(session.Player.Value.Character.Gender, group.Items));
            }
        }

        session.BeautyShop = new BeautyShop(shopMetadata, entries.ToArray()) {
            Type = shopType,
        };

        switch (session.BeautyShop.Type) {
            case BeautyShopType.Modify:
                switch (session.BeautyShop.Metadata.Category) {
                    case BeautyShopCategory.Mirror:
                        // No packet for mirror
                        return;
                    case BeautyShopCategory.Dye:
                        session.Send(BeautyPacket.DyeShop(session.BeautyShop));
                        break;
                    case BeautyShopCategory.Skin:
                        session.Send(BeautyPacket.BeautyShop(session.BeautyShop));
                        break;
                }
                break;
            case BeautyShopType.Save:
                session.Send(BeautyPacket.SaveShop(session.BeautyShop));
                session.Beauty.Load();
                break;
            case BeautyShopType.Default:
            case BeautyShopType.Random:
                session.Send(BeautyPacket.BeautyShop(session.BeautyShop));
                break;
            default:
                Logger.Error("Unknown beauty shop category: {Category}", session.BeautyShop.Metadata.Category);
                break;
        }
        return;

        IEnumerable<BeautyShopItem> FilterItemsByGender(Gender gender, BeautyShopItem[] items) {
            foreach (BeautyShopItem entry in items) {
                if (!ItemMetadata.TryGet(entry.Id, out ItemMetadata? itemMetadata)) {
                    continue;
                }

                if (itemMetadata.Limit.Gender == gender ||
                    itemMetadata.Limit.Gender == Gender.All) {
                    yield return entry;
                }
            }
        }
    }

    private void HandleCreateBeauty(GameSession session, IByteReader packet) {
        byte index = packet.ReadByte();
        bool useVoucher = packet.ReadBool();
        int itemId = packet.ReadInt();

        if (session.BeautyShop == null) {
            return;
        }

        BeautyShopItem? entry = session.BeautyShop.Items.FirstOrDefault(entry => entry.Id == itemId);
        if (entry == null) {
            return;
        }

        if (useVoucher) {
            if (!PayWithVoucher(session, session.BeautyShop)) {
                return;
            }
        } else {
            if (!PayWithCurrency(session, entry.Cost)) {
                return;
            }
        }

        if (ModifyBeauty(session, packet, entry.Id)) {
            session.ConditionUpdate(ConditionType.beauty_add, codeLong: itemId);
        }
    }

    private void HandleUpdateBeauty(GameSession session, IByteReader packet) {
        byte index = packet.ReadByte();
        bool useVoucher = packet.ReadBool();
        long uid = packet.ReadLong();

        if (session.BeautyShop == null) {
            return;
        }
        BeautyShop shop = session.BeautyShop;

        Item? cosmetic = session.Item.Equips.Get(uid);
        if (cosmetic == null) {
            return;
        }

        // Only for cap rotation/positioning. Used in mirror npc and also when changing hair while hat is equipped.
        if (cosmetic.Type.IsHat) {
            var appearance = packet.ReadClass<CapAppearance>();
            cosmetic.Appearance = appearance;
            session.Field?.Broadcast(ItemUpdatePacket.Update(session.Player, cosmetic));
            return;
        }

        if (useVoucher) {
            if (!PayWithVoucher(session, shop)) {
                return;
            }
        } else {
            if (!PayWithCurrency(session, shop.Metadata.StyleCostMetadata)) {
                return;
            }
        }

        EquipColor? startColor = cosmetic.Appearance?.Color;
        if (ModifyBeauty(session, packet, cosmetic.Id) && startColor != null) {
            Item newCosmetic = session.Item.Equips.Get(cosmetic.Metadata.SlotNames.First())!;
            if (!Equals(newCosmetic.Appearance?.Color, startColor)) {
                session.ConditionUpdate(ConditionType.beauty_change_color, codeLong: cosmetic.Id);
            }
        }
    }

    private void HandleUpdateSkin(GameSession session, IByteReader packet) {
        byte index = packet.ReadByte();
        var skinColor = packet.Read<SkinColor>();
        bool useVoucher = packet.ReadBool();

        if (session.BeautyShop == null) {
            return;
        }

        if (useVoucher) {
            if (!PayWithVoucher(session, session.BeautyShop)) {
                return;
            }
        } else {
            if (!PayWithCurrency(session, session.BeautyShop.Metadata.StyleCostMetadata)) {
                return;
            }
        }

        session.Player.Value.Character.SkinColor = skinColor;
        session.Field?.Broadcast(UserSkinColorPacket.Update(session.Player, skinColor));
    }

    private void HandleRandomHair(GameSession session, IByteReader packet) {
        int shopId = packet.ReadInt();
        bool useVoucher = packet.ReadBool();

        if (session.BeautyShop == null || session.BeautyShop.Id != shopId) {
            return;
        }
        BeautyShop shop = session.BeautyShop;

        if (useVoucher) {
            if (!PayWithVoucher(session, shop)) {
                return;
            }
        } else {
            if (!PayWithCurrency(session, shop.Metadata.StyleCostMetadata)) {
                return;
            }
        }

        WeightedSet<BeautyShopItem> weightedSet = new();
        foreach (BeautyShopItem item in shop.Items) {
            weightedSet.Add(item, item.Weight);
        }

        BeautyShopItem entry = weightedSet.Get();

        if (!ItemMetadata.TryGet(entry.Id, out ItemMetadata? itemMetadata)) {
            return;
        }
        DefaultHairMetadata defaultHairMetadata = itemMetadata.DefaultHairs[Random.Shared.Next(itemMetadata.DefaultHairs.Length)];

        // Grab random hair from default hair metadata
        double frontLength = Random.Shared.NextDouble() * (defaultHairMetadata.MaxScale - defaultHairMetadata.MinScale + defaultHairMetadata.MinScale);
        double backLength = Random.Shared.NextDouble() * (defaultHairMetadata.MaxScale - defaultHairMetadata.MinScale + defaultHairMetadata.MinScale);

        // Get random color
        if (!TableMetadata.ColorPaletteTable.Entries.TryGetValue(Constant.HairPaletteId, out IReadOnlyDictionary<int, ColorPaletteTable.Entry>? palette)) {
            return;
        }

        int colorIndex = Random.Shared.Next(palette.Count);
        ColorPaletteTable.Entry? colorEntry = palette.Values.ElementAtOrDefault(colorIndex);
        if (colorEntry == null) {
            return;
        }

        var hairAppearance = new HairAppearance(new EquipColor(colorEntry.Primary, colorEntry.Secondary, colorEntry.Tertiary, Constant.HairPaletteId, colorIndex),
            (float) backLength, defaultHairMetadata.BackPosition, defaultHairMetadata.BackRotation, (float) frontLength, defaultHairMetadata.FrontPosition,
            defaultHairMetadata.FrontRotation);

        Item? newHair = session.Field?.ItemDrop.CreateItem(entry.Id);
        if (newHair == null) {
            return;
        }
        newHair.Appearance = hairAppearance;
        newHair.Group = ItemGroup.Outfit;

        using GameStorage.Request db = session.GameStorage.Context();
        newHair = db.CreateItem(session.CharacterId, newHair);
        if (newHair == null) {
            return;
        }

        // Save old hair
        Item? prevHair = session.Item.Equips.Get(EquipSlot.HR);
        if (prevHair == null) {
            Logger.Error("Player {PlayerId} has no previous hair to save for random hair", session.Player.Value.Character.Id);
            return;
        }
        session.Beauty.SavePreviousHair(prevHair);

        if (!session.Item.Equips.EquipCosmetic(newHair, EquipSlot.HR)) {
            Logger.Error("Player {PlayerId} failed to equip random hair {HairId}", session.Player.Value.Character.Id, newHair.Id);
        }
        session.Send(BeautyPacket.RandomHair(prevHair.Id, newHair.Id));
        session.ConditionUpdate(ConditionType.beauty_random, codeLong: newHair.Id);
    }
    private void HandleWarp(GameSession session, IByteReader packet) {
        short type = packet.ReadShort();
        int mapId = type switch {
            1 => Constants.BeautyHairShopGotoFieldID,
            3 => Constants.BeautyFaceShopGotoFieldID,
            5 => Constants.BeautyColorShopGotoFieldID,
            _ => 0,
        };
        int portalId = type switch {
            1 => Constants.BeautyHairShopGotoPortalID,
            3 => Constants.BeautyFaceShopGotoPortalID,
            5 => Constants.BeautyColorShopGotoPortalID,
            _ => 0,
        };

        session.Send(session.PrepareField(mapId, portalId: portalId)
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(MigrationError.s_move_err_default));
    }

    private void HandleConfirmRandomHair(GameSession session, IByteReader packet) {
        bool newHairSelected = packet.ReadBool();
        int voucherItemId = 0;
        if (session.BeautyShop == null) {
            return;
        }
        if (!newHairSelected) {
            session.Beauty.SelectPreviousHair();
            Item? voucher = session.Field?.ItemDrop.CreateItem(session.BeautyShop.Metadata.ReturnCouponId);
            if (voucher != null && !session.Item.Inventory.Add(voucher, true)) {
                session.Item.MailItem(voucher);
                voucherItemId = voucher.Id;
            }
        }

        session.Send(BeautyPacket.RandomHairResult(voucherItemId));
        session.Beauty.ClearPreviousHair();
    }

    private static void HandleSaveHair(GameSession session, IByteReader packet) {
        long uid = packet.ReadLong();
        if (session.BeautyShop == null) {
            return;
        }

        session.Beauty.AddHair(uid);
    }

    private void HandleAddSlots(GameSession session, IByteReader packet) {
        long uid = packet.ReadLong();
    }

    private void HandleDeleteHair(GameSession session, IByteReader packet) {
        long uid = packet.ReadLong();
        bool delete = packet.ReadBool();

        if (session.BeautyShop == null) {
            return;
        }

        if (delete) {
            session.Beauty.RemoveHair(uid);
        }
    }

    private void HandleAskAddSlots(GameSession session, IByteReader packet) { }

    private static void HandleApplySavedHair(GameSession session, IByteReader packet) {
        long uid = packet.ReadLong();
        byte index = packet.ReadByte();

        if (session.BeautyShop == null) {
            return;
        }

        if (!PayWithCurrency(session, session.BeautyShop.Metadata.StyleCostMetadata)) {
            return;
        }

        session.Beauty.EquipSavedCosmetic(uid);
    }

    private void HandleGearDye(GameSession session, IByteReader packet) {
        byte count = packet.ReadByte();
        if (session.BeautyShop == null) {
            return;
        }
        for (byte i = 0; i < count; i++) {
            bool isValid = packet.ReadBool();
            if (!isValid) {
                continue;
            }

            packet.ReadByte();
            bool useVoucher = packet.ReadBool();
            packet.ReadByte();
            packet.ReadInt();
            packet.ReadLong();
            long itemUid = packet.ReadLong();
            int itemId = packet.ReadInt();

            Item? item = session.Item.Equips.Get(itemUid);
            if (item == null) {
                return;
            }

            if (useVoucher) {
                if (!PayWithVoucher(session, session.BeautyShop)) {
                    return;
                }
            } else {
                if (!PayWithCurrency(session, session.BeautyShop.Metadata.ColorCostMetadata)) {
                    return;
                }
            }

            item.Appearance = item.EquipSlot() == EquipSlot.CP ? packet.ReadClass<CapAppearance>() : packet.ReadClass<ItemAppearance>();
            session.Field?.Broadcast(ItemUpdatePacket.Update(session.Player, item));
        }
    }

    private void HandleVoucher(GameSession session, IByteReader packet) {
        long uid = packet.ReadLong();

        Item? voucher = session.Item.Inventory.Get(uid);
        if (voucher == null || voucher.Metadata.Function?.Type != ItemFunction.ItemChangeBeauty) {
            return;
        }

        if (!int.TryParse(voucher.Metadata.Function.Parameters, out int shopId)) {
            return;
        }

        if (!session.ServerTableMetadata.BeautyShopTable.Entries.TryGetValue(shopId, out BeautyShopMetadata? shopMetadata) ||
            shopMetadata.Items.Length == 0) {
            return;
        }

        // Shop all must be the same type (hair, face, etc). Will not work correctly if mixed. Assumes all items are the same type, so we'll just check the first one.
        if (!ItemMetadata.TryGet(shopMetadata.Items.First().Id, out ItemMetadata? itemEntry)) {
            return;
        }

        var shop = new BeautyShop(shopMetadata, shopMetadata.Items);

        session.BeautyShop = shop;
        session.Send(BeautyPacket.BeautyShop(shop));
    }

    private static bool PayWithVoucher(GameSession session, BeautyShop shop) {
        ItemTag voucherTag = shop.Metadata.CouponTag;

        if (voucherTag == ItemTag.None) {
            return false;
        }

        var ingredient = new IngredientInfo(voucherTag, 1);
        if (!session.Item.Inventory.Consume([ingredient])) {
            session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert, StringCode.s_err_invalid_item));
            return false;
        }

        session.Send(BeautyPacket.Voucher(shop.Metadata.CouponId, 1));
        return true;
    }

    private static bool PayWithCurrency(GameSession session, BeautyShopCostMetadata cost) {
        switch (cost.CurrencyType) {
            case ShopCurrencyType.Meso:
                if (session.Currency.CanAddMeso(-cost.Price) != -cost.Price) {
                    session.Send(BeautyPacket.Error(BeautyError.lack_currency));
                    return false;
                }
                session.Currency.Meso -= cost.Price;
                break;
            case ShopCurrencyType.Meret:
            case ShopCurrencyType.EventMeret: // TODO: EventMeret?
            case ShopCurrencyType.GameMeret:
                if (session.Currency.CanAddMeret(-cost.Price) != -cost.Price) {
                    session.Send(BeautyPacket.Error(BeautyError.s_err_lack_merat_ask));
                    return false;
                }

                session.Currency.Meret -= cost.Price;
                break;
            case ShopCurrencyType.Item:
                var ingredient = new ItemComponent(cost.PaymentItemId, -1, cost.Price, ItemTag.None);
                if (!session.Item.Inventory.ConsumeItemComponents([ingredient])) {
                    session.Send(BeautyPacket.Error(BeautyError.lack_currency));
                    return false;
                }
                break;
            default:
                CurrencyType currencyType = cost.CurrencyType switch {
                    ShopCurrencyType.ValorToken => CurrencyType.ValorToken,
                    ShopCurrencyType.Treva => CurrencyType.Treva,
                    ShopCurrencyType.Rue => CurrencyType.Rue,
                    ShopCurrencyType.HaviFruit => CurrencyType.HaviFruit,
                    ShopCurrencyType.StarPoint => CurrencyType.StarPoint,
                    ShopCurrencyType.MenteeToken => CurrencyType.MenteeToken,
                    ShopCurrencyType.MentorToken => CurrencyType.MentorToken,
                    ShopCurrencyType.MesoToken => CurrencyType.MesoToken,
                    ShopCurrencyType.ReverseCoin => CurrencyType.ReverseCoin,
                    _ => CurrencyType.None,

                };
                if (currencyType == CurrencyType.None || session.Currency[currencyType] < cost.Price) {
                    session.Send(BeautyPacket.Error(BeautyError.lack_currency));
                    return false;
                }

                session.Currency[currencyType] -= cost.Price;
                break;
        }
        return true;
    }

    private static bool ModifyBeauty(GameSession session, IByteReader packet, int itemId) {
        Item? newCosmetic = session.Field?.ItemDrop.CreateItem(itemId, 1, 1);
        if (newCosmetic == null) {
            return false;
        }

        if (newCosmetic.Type.IsHair) {
            newCosmetic.Appearance = packet.ReadClass<HairAppearance>();
        } else if (newCosmetic.Type.IsDecal) {
            newCosmetic.Appearance = packet.ReadClass<DecalAppearance>();
        } else if (newCosmetic.Type.IsHat) {
            newCosmetic.Appearance = packet.ReadClass<CapAppearance>();
        } else {
            newCosmetic.Appearance = packet.ReadClass<ItemAppearance>();
        }

        using GameStorage.Request db = session.GameStorage.Context();
        newCosmetic = db.CreateItem(session.CharacterId, newCosmetic);
        if (newCosmetic == null) {
            return false;
        }

        if (!session.Item.Equips.EquipCosmetic(newCosmetic, newCosmetic.Metadata.SlotNames.First())) {
            db.SaveItems(0, newCosmetic);
            return false;
        }

        return true;
    }
}
