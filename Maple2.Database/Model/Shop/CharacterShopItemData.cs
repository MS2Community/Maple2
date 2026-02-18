using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model.Shop;

internal class CharacterShopItemData {
    public int ShopId { get; set; }
    public int ShopItemId { get; set; }
    public long OwnerId { get; set; }
    public int StockPurchased { get; set; }
    public Item? Item { get; set; }

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator CharacterShopItemData?(Maple2.Model.Game.Shop.CharacterShopItemData? other) {
        return other == null ? null : new CharacterShopItemData {
            ShopId = other.ShopId,
            ShopItemId = other.ShopItemId,
            StockPurchased = other.StockPurchased,
            Item = other.Item,
        };
    }

    public static void Configure(EntityTypeBuilder<CharacterShopItemData> builder) {
        builder.ToTable("character-shop-item-data");
        builder.HasKey(info => new { info.ShopItemId, info.ShopId, info.OwnerId });
        builder.Property(data => data.Item).HasJsonConversion();
    }
}
