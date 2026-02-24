namespace Maple2.Model.Game.Shop;

public class CharacterShopItemData {
    public int ShopId { get; init; }
    public int ShopItemId { get; init; }
    public int StockPurchased { get; set; }
    public required Item Item { get; set; }
}
