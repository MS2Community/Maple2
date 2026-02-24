using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Game.Manager;

public class MasteryManager {

    private readonly GameSession session;
    private Mastery Mastery => session.Player.Value.Character.Mastery;
    private IDictionary<int, int> gatheringCounts => session.Config.GatheringCounts;

    public MasteryManager(GameSession session) {
        this.session = session;
    }

    public int this[MasteryType type] {
        get => type switch {
            MasteryType.Fishing => Mastery.Fishing,
            MasteryType.Music => Mastery.Instrument,
            MasteryType.Mining => Mastery.Mining,
            MasteryType.Gathering => Mastery.Foraging,
            MasteryType.Breeding => Mastery.Ranching,
            MasteryType.Farming => Mastery.Farming,
            MasteryType.Blacksmithing => Mastery.Smithing,
            MasteryType.Engraving => Mastery.Handicrafts,
            MasteryType.Alchemist => Mastery.Alchemy,
            MasteryType.Cooking => Mastery.Cooking,
            MasteryType.PetTaming => Mastery.PetTaming,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Invalid mastery type."),
        };
        set {
            short startLevel = GetLevel(type);
            int startValue = this[type];
            switch (type) {
                case MasteryType.Fishing:
                    Mastery.Fishing = Math.Clamp(value, Mastery.Fishing, Constant.FishingMasteryMax);
                    break;
                case MasteryType.Music:
                    Mastery.Instrument = Math.Clamp(value, Mastery.Instrument, Constant.PerformanceMasteryMax);
                    break;
                case MasteryType.Mining:
                    Mastery.Mining = Math.Clamp(value, Mastery.Mining, Constant.MiningMasteryMax);
                    break;
                case MasteryType.Gathering:
                    Mastery.Foraging = Math.Clamp(value, Mastery.Foraging, Constant.ForagingMasteryMax);
                    break;
                case MasteryType.Breeding:
                    Mastery.Ranching = Math.Clamp(value, Mastery.Ranching, Constant.RanchingMasteryMax);
                    break;
                case MasteryType.Farming:
                    Mastery.Farming = Math.Clamp(value, Mastery.Farming, Constant.FarmingMasteryMax);
                    break;
                case MasteryType.Blacksmithing:
                    Mastery.Smithing = Math.Clamp(value, Mastery.Smithing, Constant.SmithingMasteryMax);
                    break;
                case MasteryType.Engraving:
                    Mastery.Handicrafts = Math.Clamp(value, Mastery.Handicrafts, Constant.HandicraftsMasteryMax);
                    break;
                case MasteryType.Alchemist:
                    Mastery.Alchemy = Math.Clamp(value, Mastery.Alchemy, Constant.AlchemyMasteryMax);
                    break;
                case MasteryType.Cooking:
                    Mastery.Cooking = Math.Clamp(value, Mastery.Cooking, Constant.CookingMasteryMax);
                    break;
                case MasteryType.PetTaming:
                    Mastery.PetTaming = Math.Clamp(value, Mastery.PetTaming, Constant.PetTamingMasteryMax);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "Invalid mastery type.");
            }

            session.Send(MasteryPacket.UpdateMastery(type, session.Mastery[type]));
            int currentLevel = GetLevel(type);
            int deltaLevel = currentLevel - startLevel + (startValue == 0 ? 1 : 0);
            int deltaExp = value - startValue;
            Log.Logger.Debug("[Mastery] {type} changed from {startValue} to {value} (Level {startLevel} -> {currentLevel}), ΔLevel: {deltaLevel}, ΔExp: {deltaExp}", type, startValue, value, startLevel, currentLevel, deltaLevel, deltaExp);

            HandleMasteryLevelChange(type, currentLevel, deltaLevel);
            HandleMasteryExpIncrease(type, deltaExp);
        }

    }

    /// <summary>
    /// Handles the change of mastery level for a specified <see cref="MasteryType"/>.
    /// Updates the corresponding condition based on the mastery type and level changes.
    /// </summary>
    /// <param name="type">The type of mastery whos level has been increased.</param>
    /// <param name="currentLevel">The new current level of the mastery after the increase.</param>
    /// <param name="deltaLevel">The delta by which the mastery level has changed.</param>
    private void HandleMasteryLevelChange(MasteryType type, int currentLevel, int deltaLevel) {
        if (deltaLevel == 0) {
            return;
        }

        if (deltaLevel < 0) {
            session.ConditionUpdate(ConditionType.set_mastery_grade, codeLong: (int) type);
            return;
        }

        switch (type) {
            case MasteryType.Fishing:
                session.ConditionUpdate(ConditionType.fisher_grade, codeLong: currentLevel);
                return;
            case MasteryType.Music:
                session.ConditionUpdate(ConditionType.music_play_grade, counter: deltaLevel);
                return;
            default:
                session.ConditionUpdate(ConditionType.mastery_grade, codeLong: (int) type);
                return;
        }
    }

    /// <summary>
    /// Handles the increase of mastery experience for a specified MasteryType.
    /// Updates relevant conditions based on the mastery type and the amount of experience gained.
    /// </summary>
    /// <param name="type">The type of mastery for which experience is being increased.</param>
    /// <param name="deltaExp">The amount of experience that has been gained for the mastery.</param>
    private void HandleMasteryExpIncrease(MasteryType type, int deltaExp) {
        switch (type) {
            case MasteryType.Music:
                session.ConditionUpdate(ConditionType.music_play_instrument_mastery, counter: deltaExp, codeLong: session.Instrument?.Value.Category ?? 0);
                return;
            default:
                return;
        }
    }

    public short GetLevel(MasteryType type) {
        if (!session.TableMetadata.MasteryRewardTable.Entries.TryGetValue(type, out IReadOnlyDictionary<int, MasteryRewardTable.Entry>? masteryRewardEntries)) {
            return 1;
        }

        return (short) Math.Max(1, masteryRewardEntries.OrderByDescending(mastery => mastery.Key).FirstOrDefault(mastery => session.Mastery[type] >= mastery.Value.Value).Key);
    }

    public void Gather(FieldInteract fieldInteract) {
        if (GatherCommon(fieldInteract.Value.Item.RecipeId, fieldInteract.Position, fieldInteract.Rotation)) {
            session.Send(InteractObjectPacket.Interact(fieldInteract, decreaseAmount: 1));
        } else {
            session.Send(InteractObjectPacket.Interact(fieldInteract, GatherResult.Fail, decreaseAmount: 0));
        }
    }

    public void Gather(FieldFunctionInteract fieldFunctionInteract) {
        if (GatherCommon(fieldFunctionInteract.Value.RecipeId, fieldFunctionInteract.Position, fieldFunctionInteract.Rotation)) {
            session.Send(FunctionCubePacket.SuccessLifeSkill(session.CharacterId, fieldFunctionInteract.InteractCube));
        } else {
            session.Send(FunctionCubePacket.FailLifeSkill(session.CharacterId, fieldFunctionInteract.InteractCube));
        }
    }

    private bool GatherCommon(int recipeId, Vector3 position, Vector3 rotation) {
        if (!session.TableMetadata.MasteryRecipeTable.Entries.TryGetValue(recipeId, out MasteryRecipeTable.Entry? recipeMetadata)) {
            session.Send(MasteryPacket.Error(MasteryError.s_mastery_error_unknown));
            return false;
        }

        if (recipeMetadata.RequiredMastery > this[recipeMetadata.Type]) {
            session.Send(MasteryPacket.Error(MasteryError.s_mastery_error_lack_mastery));
            return false;
        }
        float successRate = GatherSuccessRate(recipeMetadata);

        BeforeGather(recipeMetadata);
        if (Random.Shared.NextDouble() > (successRate / 100)) {
            return false;
        }

        Gather(recipeMetadata, position, rotation);
        if (!recipeMetadata.NoRewardExp) {
            session.Exp.AddExp(ExpType.gathering);
        }
        switch (recipeMetadata.Type) {
            case MasteryType.Farming:
            case MasteryType.Mining:
            case MasteryType.Gathering:
            case MasteryType.Breeding:
                short playerLevel = GetLevel(recipeMetadata.Type);
                // no mastery given - recipe level is more than 3 levels below player's mastery level
                int masteryFactorCount = session.TableMetadata.MasteryDifferentialFactorTable.Entries.Values.Count(m => m.Factor > 0);
                if (playerLevel - recipeMetadata.RewardMastery >= masteryFactorCount) {
                    return true;
                }
                break;
            default:
                break;
        }
        this[recipeMetadata.Type] += recipeMetadata.RewardMastery;
        return true;
    }

    public void Gather(MasteryRecipeTable.Entry recipeMetadata, Vector3 position, Vector3 rotation) {
        foreach (ItemComponent itemReward in recipeMetadata.RewardItems) {
            Item? item = session.Field?.ItemDrop.CreateItem(itemReward.ItemId, itemReward.Rarity, itemReward.Amount);
            if (item == null || session.Field is null) {
                continue;
            }
            session.Field.DropItem(position, rotation, item, characterId: session.CharacterId);
        }

        AfterGather(recipeMetadata, 1);
    }

    public float GatherSuccessRate(MasteryRecipeTable.Entry recipeMetadata) {
        if (!gatheringCounts.TryGetValue(recipeMetadata.Id, out int currentCount)) {
            gatheringCounts[recipeMetadata.Id] = 0;
        }

        int myHome = 1; // For outside gathering, it should be always 1
        if (session.Field is HomeFieldManager homeField) {
            myHome = homeField.OwnerId == session.AccountId ? 1 : 0;
        }
        return Lua.CalcGatheringObjectSuccessRate(currentCount, recipeMetadata.HighRateLimitCount, recipeMetadata.NormalRateLimitCount, myHome);
    }

    private void AfterGather(MasteryRecipeTable.Entry recipeMetadata, int gatheringAmount) {
        switch (recipeMetadata.Type) {
            case MasteryType.Breeding:
            case MasteryType.Farming:
                if (session.Field is HomeFieldManager homeField && homeField.OwnerId != session.AccountId) {
                    session.ConditionUpdate(ConditionType.mastery_harvest_otherhouse, counter: gatheringAmount, codeLong: recipeMetadata.Id);
                }
                if (recipeMetadata.Type == MasteryType.Farming) {
                    session.ConditionUpdate(ConditionType.mastery_farming, counter: gatheringAmount, codeLong: recipeMetadata.Id);
                }
                session.ConditionUpdate(ConditionType.mastery_harvest, counter: gatheringAmount, codeLong: recipeMetadata.Id);
                break;
            case MasteryType.Gathering:
            case MasteryType.Mining:
                session.ConditionUpdate(ConditionType.mastery_gathering, counter: gatheringAmount, codeLong: recipeMetadata.Id);
                break;
        }

        gatheringCounts[recipeMetadata.Id] += 1;
    }

    public void BeforeGather(MasteryRecipeTable.Entry recipeMetadata) {
        switch (recipeMetadata.Type) {
            case MasteryType.Farming:
            case MasteryType.Breeding:
                session.ConditionUpdate(ConditionType.mastery_harvest_try, codeLong: recipeMetadata.Id);
                session.ConditionUpdate(ConditionType.mastery_farming_try, codeLong: recipeMetadata.Id);
                break;
            case MasteryType.Gathering:
            case MasteryType.Mining:
                session.ConditionUpdate(ConditionType.mastery_gathering_try, codeLong: recipeMetadata.Id);
                break;
        }
    }
}
