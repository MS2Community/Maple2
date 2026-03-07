using System.Text.Json.Serialization;

namespace Maple2.Model.Metadata;

public class ServerTableMetadata {
    public required string Name { get; set; }
    public required ServerTable Table { get; set; }

    protected bool Equals(ServerTableMetadata other) {
        return Name == other.Name && Table.Equals(other.Table);
    }

    public override bool Equals(object? obj) {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;

        return Equals((ServerTableMetadata) obj);
    }

    public override int GetHashCode() {
        return HashCode.Combine(Name, Table);
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "!")]
[JsonDerivedType(typeof(InstanceFieldTable), typeDiscriminator: "instancefield")]
[JsonDerivedType(typeof(ScriptConditionTable), typeDiscriminator: "*scriptCondition")]
[JsonDerivedType(typeof(ScriptFunctionTable), typeDiscriminator: "*scriptFunction")]
[JsonDerivedType(typeof(ScriptEventConditionTable), typeDiscriminator: "*scriptEventCondition")]
[JsonDerivedType(typeof(JobConditionTable), typeDiscriminator: "jobConditionTable")]
[JsonDerivedType(typeof(BonusGameTable), typeDiscriminator: "bonusGame")]
[JsonDerivedType(typeof(GlobalDropItemBoxTable), typeDiscriminator: "globalItemDrop")]
[JsonDerivedType(typeof(UserStatTable), typeDiscriminator: "userStat*")]
[JsonDerivedType(typeof(IndividualDropItemTable), typeDiscriminator: "individualItemDrop")]
[JsonDerivedType(typeof(PrestigeExpTable), typeDiscriminator: "prestigeExpTable")]
[JsonDerivedType(typeof(PrestigeIdExpTable), typeDiscriminator: "prestigeIdExpTable")]
[JsonDerivedType(typeof(TimeEventTable), typeDiscriminator: "timeEvent")]
[JsonDerivedType(typeof(GameEventTable), typeDiscriminator: "gameEvent")]
[JsonDerivedType(typeof(OxQuizTable), typeDiscriminator: "oxQuiz")]
[JsonDerivedType(typeof(ItemMergeTable), typeDiscriminator: "itemMerge")]
[JsonDerivedType(typeof(ShopTable), typeDiscriminator: "shop")]
[JsonDerivedType(typeof(ShopItemTable), typeDiscriminator: "shopItem")]
[JsonDerivedType(typeof(BeautyShopTable), typeDiscriminator: "beautyShop")]
[JsonDerivedType(typeof(MeretMarketTable), typeDiscriminator: "meretMarket")]
[JsonDerivedType(typeof(FishTable), typeDiscriminator: "fish")]
[JsonDerivedType(typeof(CombineSpawnTable), typeDiscriminator: "combineSpawn")]
[JsonDerivedType(typeof(EnchantOptionTable), typeDiscriminator: "enchantOption")]
[JsonDerivedType(typeof(UnlimitedEnchantOptionTable), typeDiscriminator: "unlimitedEnchantOption")]
[JsonDerivedType(typeof(ConstantsTable), typeDiscriminator: "constants")]
public abstract record ServerTable;
