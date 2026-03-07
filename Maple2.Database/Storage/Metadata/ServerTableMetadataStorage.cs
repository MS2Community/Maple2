using Maple2.Database.Context;
using Maple2.Model.Common;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;

namespace Maple2.Database.Storage;

public class ServerTableMetadataStorage {
    private readonly Lazy<InstanceFieldTable> instanceFieldTable;
    private readonly Lazy<ScriptConditionTable> scriptConditionTable;
    private readonly Lazy<ScriptFunctionTable> scriptFunctionTable;
    private readonly Lazy<ScriptEventConditionTable> scriptEventConditionTable;
    private readonly Lazy<JobConditionTable> jobConditionTable;
    private readonly Lazy<BonusGameTable> bonusGameTable;
    private readonly Lazy<GlobalDropItemBoxTable> globalDropItemBoxTable;
    private readonly Lazy<UserStatTable> userStatTable;
    private readonly Lazy<IndividualDropItemTable> individualDropItemTable;
    private readonly Lazy<PrestigeExpTable> prestigeExpTable;
    private readonly Lazy<PrestigeIdExpTable> prestigeIdExpTable;
    private readonly Lazy<TimeEventTable> timeEventTable;
    private readonly Lazy<GameEventTable> gameEventTable;
    private readonly Lazy<OxQuizTable> oxQuizTable;
    private readonly Lazy<ItemMergeTable> itemMergeTable;
    private readonly Lazy<ShopTable> shopTable;
    private readonly Lazy<ShopItemTable> shopItemTable;
    private readonly Lazy<BeautyShopTable> beautyShopTable;
    private readonly Lazy<MeretMarketTable> meretMarketTable;
    private readonly Lazy<FishTable> fishTable;
    private readonly Lazy<CombineSpawnTable> combineSpawnTable;
    private readonly Lazy<EnchantOptionTable> enchantOptionTable;
    private readonly Lazy<UnlimitedEnchantOptionTable> unlimitedEnchantOptionTable;
    private readonly Lazy<ConstantsTable> constantsTable;

    public InstanceFieldTable InstanceFieldTable => instanceFieldTable.Value;
    public ScriptConditionTable ScriptConditionTable => scriptConditionTable.Value;
    public ScriptFunctionTable ScriptFunctionTable => scriptFunctionTable.Value;
    public ScriptEventConditionTable ScriptEventConditionTable => scriptEventConditionTable.Value;
    public JobConditionTable JobConditionTable => jobConditionTable.Value;
    public BonusGameTable BonusGameTable => bonusGameTable.Value;
    public GlobalDropItemBoxTable GlobalDropItemBoxTable => globalDropItemBoxTable.Value;
    public UserStatTable UserStatTable => userStatTable.Value;
    public IndividualDropItemTable IndividualDropItemTable => individualDropItemTable.Value;
    public PrestigeExpTable PrestigeExpTable => prestigeExpTable.Value;
    public PrestigeIdExpTable PrestigeIdExpTable => prestigeIdExpTable.Value;
    public TimeEventTable TimeEventTable => timeEventTable.Value;
    public GameEventTable GameEventTable => gameEventTable.Value;
    public OxQuizTable OxQuizTable => oxQuizTable.Value;
    public ItemMergeTable ItemMergeTable => itemMergeTable.Value;
    public ShopTable ShopTable => shopTable.Value;
    public ShopItemTable ShopItemTable => shopItemTable.Value;
    public BeautyShopTable BeautyShopTable => beautyShopTable.Value;
    public MeretMarketTable MeretMarketTable => meretMarketTable.Value;
    public FishTable FishTable => fishTable.Value;
    public CombineSpawnTable CombineSpawnTable => combineSpawnTable.Value;
    public EnchantOptionTable EnchantOptionTable => enchantOptionTable.Value;
    public UnlimitedEnchantOptionTable UnlimitedEnchantOptionTable => unlimitedEnchantOptionTable.Value;
    public ConstantsTable ConstantsTable => constantsTable.Value;

    public ServerTableMetadataStorage(MetadataContext context) {
        instanceFieldTable = Retrieve<InstanceFieldTable>(context, ServerTableNames.INSTANCE_FIELD);
        scriptConditionTable = Retrieve<ScriptConditionTable>(context, ServerTableNames.SCRIPT_CONDITION);
        scriptFunctionTable = Retrieve<ScriptFunctionTable>(context, ServerTableNames.SCRIPT_FUNCTION);
        scriptEventConditionTable = Retrieve<ScriptEventConditionTable>(context, ServerTableNames.SCRIPT_EVENT);
        jobConditionTable = Retrieve<JobConditionTable>(context, ServerTableNames.JOB_CONDITION);
        bonusGameTable = Retrieve<BonusGameTable>(context, ServerTableNames.BONUS_GAME);
        globalDropItemBoxTable = Retrieve<GlobalDropItemBoxTable>(context, ServerTableNames.GLOBAL_DROP_ITEM_BOX);
        userStatTable = Retrieve<UserStatTable>(context, ServerTableNames.USER_STAT);
        individualDropItemTable = Retrieve<IndividualDropItemTable>(context, ServerTableNames.INDIVIDUAL_DROP_ITEM);
        prestigeExpTable = Retrieve<PrestigeExpTable>(context, ServerTableNames.PRESTIGE_EXP);
        prestigeIdExpTable = Retrieve<PrestigeIdExpTable>(context, ServerTableNames.PRESTIGE_ID_EXP);
        timeEventTable = Retrieve<TimeEventTable>(context, ServerTableNames.TIME_EVENT);
        gameEventTable = Retrieve<GameEventTable>(context, ServerTableNames.GAME_EVENT);
        oxQuizTable = Retrieve<OxQuizTable>(context, ServerTableNames.OX_QUIZ);
        itemMergeTable = Retrieve<ItemMergeTable>(context, ServerTableNames.ITEM_MERGE);
        shopTable = Retrieve<ShopTable>(context, ServerTableNames.SHOP);
        shopItemTable = Retrieve<ShopItemTable>(context, ServerTableNames.SHOP_ITEM);
        beautyShopTable = Retrieve<BeautyShopTable>(context, ServerTableNames.BEAUTY_SHOP);
        meretMarketTable = Retrieve<MeretMarketTable>(context, ServerTableNames.MERET_MARKET);
        fishTable = Retrieve<FishTable>(context, ServerTableNames.FISH);
        combineSpawnTable = Retrieve<CombineSpawnTable>(context, ServerTableNames.COMBINE_SPAWN);
        enchantOptionTable = Retrieve<EnchantOptionTable>(context, ServerTableNames.ENCHANT_OPTION);
        unlimitedEnchantOptionTable = Retrieve<UnlimitedEnchantOptionTable>(context, ServerTableNames.UNLIMITED_ENCHANT_OPTION);
        constantsTable = Retrieve<ConstantsTable>(context, ServerTableNames.CONSTANTS);
    }

    public IEnumerable<GameEvent> GetGameEvents() {
        foreach ((int id, GameEventMetadata gameEvent) in GameEventTable.Entries) {
            if (gameEvent.EndTime < DateTime.Now) {
                continue;
            }

            yield return new GameEvent(gameEvent);
        }
    }

    private static Lazy<T> Retrieve<T>(MetadataContext context, string key) where T : ServerTable {
        var result = new Lazy<T>(() => {
            lock (context) {
                ServerTableMetadata? row = context.ServerTableMetadata.Find(key);
                if (row?.Table is not T result) {
                    throw new InvalidOperationException($"Row does not exist: {key}");
                }

                return result;
            }
        });

#if !DEBUG
        // No lazy loading for RELEASE build.
        _ = result.Value;
#endif
        return result;
    }
}
