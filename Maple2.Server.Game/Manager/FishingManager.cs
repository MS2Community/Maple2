using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Manager;

public class FishingManager {
    private readonly GameSession session;
    private IDictionary<int, FishEntry> FishAlbum => session.Player.Value.Unlock.FishAlbum;
    private readonly TableMetadataStorage tableMetadata;
    private readonly ServerTableMetadataStorage serverTableMetadata;
    private FieldGuideObject? GuideObject {
        get => session.GuideObject;
        set => session.GuideObject = value;
    }
    private Item? rod;
    private bool fishFightGame;
    private FishTable.Fish? selectedFish;
    private FishingTile? selectedTile;
    private IDictionary<Vector3, FishingTile> tiles = new Dictionary<Vector3, FishingTile>();

    private ConstantsTable Constants => serverTableMetadata.ConstantsTable;

    private readonly ILogger logger = Log.ForContext<FishingManager>();

    public FishingManager(GameSession session, TableMetadataStorage tableMetadata, ServerTableMetadataStorage serverTableMetadata) {
        this.session = session;
        this.tableMetadata = tableMetadata;
        this.serverTableMetadata = serverTableMetadata;
    }

    public void Reset() {
        if (GuideObject == null) {
            return;
        }
        session.Send(FishingPacket.Stop());
        session.Field?.Broadcast(GuideObjectPacket.Remove(GuideObject));
        GuideObject = null;
        rod = null;
        fishFightGame = false;
        selectedTile = null;
        selectedFish = null;
        tiles.Clear();
        // Remove guide object?
    }

    public FishingError Prepare(long fishingRodUid) {
        if (session.GuideObject != null || session.Field?.AccelerationStructure == null) {
            return FishingError.s_fishing_error_system_error;
        }

        Item? rodItem = session.Item.Inventory.Get(fishingRodUid);
        if (rodItem == null || rodItem.Metadata.Function?.Type != ItemFunction.FishingRod) {
            return FishingError.s_fishing_error_invalid_item;
        }
        if (!int.TryParse(rodItem.Metadata.Function?.Parameters, out int rodCode) || !tableMetadata.FishingRodTable.Entries.TryGetValue(rodCode, out FishingRodTable.Entry? rodMetadata)) {
            return FishingError.s_fishing_error_invalid_item;
        }
        if (session.Mastery[MasteryType.Fishing] < rodMetadata.MinMastery) {
            return FishingError.s_fishing_error_fishingrod_mastery;
        }
        if (!serverTableMetadata.FishTable.Spots.TryGetValue(session.Field.MapId, out FishTable.Spot? spotMetadata)) {
            return FishingError.s_fishing_error_notexist_fish;
        }
        if (session.Mastery[MasteryType.Fishing] < spotMetadata.MinMastery) {
            return FishingError.s_fishing_error_lack_mastery;
        }

        tiles = GetFishingBlocks(rodMetadata);
        if (tiles.Count == 0) {
            return FishingError.s_fishing_error_notexist_fish;
        }
        rod = rodItem;
        // get the fishing block closest to player
        Vector3 guidePosition = tiles.Keys.MinBy(block =>
            Vector2.Distance(new Vector2(session.Player.Position.X, session.Player.Position.Y), new Vector2(block.X, block.Y)));
        // Move guide 1 block up so it sits on top of the fishing block
        guidePosition.Z += Constant.BlockSize;
        var guide = new FishingGuideObject(rodMetadata, spotMetadata);
        session.GuideObject = session.Field.SpawnGuideObject(session.Player, guide, guidePosition);

        session.Send(FishingPacket.LoadTiles(tiles.Values));
        session.Field.Broadcast(GuideObjectPacket.Create(session.GuideObject));
        session.Send(FishingPacket.Prepare(fishingRodUid));
        return FishingError.none;
    }

    private Dictionary<Vector3, FishingTile> GetFishingBlocks(FishingRodTable.Entry rod) {
        Dictionary<Vector3, FishingTile> tiles = new();
        if (session.Field?.AccelerationStructure == null) {
            return tiles;
        }
        Vector3 playerPosition = session.Player.Position;
        float direction = session.Player.Rotation.AlignRotation().Z;

        Vector3 position1 = Vector3.Zero;
        Vector3 position2 = Vector3.Zero;

        switch (direction) {
            case Constant.NorthEast:
                position1 = new Vector3(
                    playerPosition.X + (3 * Constant.BlockSize),
                    playerPosition.Y + (2 * Constant.BlockSize),
                    playerPosition.Z - (int) (Constant.BlockSize / 2)
                );
                position2 = new Vector3(
                    playerPosition.X + (1 * Constant.BlockSize),
                    playerPosition.Y - (2 * Constant.BlockSize),
                    playerPosition.Z - (3 * Constant.BlockSize)
                );
                break;
            case Constant.NorthWest:
                position1 = new Vector3(
                    playerPosition.X - (2 * Constant.BlockSize),
                    playerPosition.Y + (3 * Constant.BlockSize),
                    playerPosition.Z - (int) (Constant.BlockSize / 2)
                );
                position2 = new Vector3(
                    playerPosition.X + (2 * Constant.BlockSize),
                    playerPosition.Y + (1 * Constant.BlockSize),
                    playerPosition.Z - (3 * Constant.BlockSize)
                );
                break;
            case Constant.SouthWest:
                position1 = new Vector3(
                    playerPosition.X - (3 * Constant.BlockSize),
                    playerPosition.Y - (2 * Constant.BlockSize),
                    playerPosition.Z - (int) (Constant.BlockSize / 2)
                );
                position2 = new Vector3(
                    playerPosition.X - (1 * Constant.BlockSize),
                    playerPosition.Y + (2 * Constant.BlockSize),
                    playerPosition.Z - (3 * Constant.BlockSize)
                );
                break;
            case Constant.SouthEast:
                position1 = new Vector3(
                    playerPosition.X + (2 * Constant.BlockSize),
                    playerPosition.Y - (3 * Constant.BlockSize),
                    playerPosition.Z - (int) (Constant.BlockSize / 2)
                );
                position2 = new Vector3(
                    playerPosition.X - (2 * Constant.BlockSize),
                    playerPosition.Y - (1 * Constant.BlockSize),
                    playerPosition.Z - (3 * Constant.BlockSize)
                );
                break;
        }

        session.Field.AccelerationStructure.QueryFluids(
            new Vector3(
                Math.Min(position1.X, position2.X),
                Math.Min(position1.Y, position2.Y),
                Math.Min(position1.Z, position2.Z)),
            new Vector3(
                Math.Max(position1.X, position2.X),
                Math.Max(position1.Y, position2.Y),
                Math.Max(position1.Z, position2.Z)),
            (tile) => tiles.Add(tile.Position, new FishingTile(tile, rod.ReduceTime)));
        return tiles;
    }

    private WeightedSet<FishTable.Fish> GetFishes(FishTable.Spot spot, LiquidType liquidType) {
        if (liquidType == LiquidType.water && spot.LiquidTypes.Contains(LiquidType.seawater)) { // If spot is water and has seawater, it will be seawater
            liquidType = LiquidType.seawater;
        }
        var fishes = new WeightedSet<FishTable.Fish>();

        if (serverTableMetadata.FishTable.GlobalFishBoxes.TryGetValue(spot.GlobalFishBoxId, out FishTable.FishBox? globalFishBox)) {
            fishes = CombineBox(fishes, globalFishBox);
        }

        if (serverTableMetadata.FishTable.IndividualFishBoxes.TryGetValue(spot.IndividualFishBoxId, out FishTable.FishBox? individualFishBox)) {
            fishes = CombineBox(fishes, individualFishBox);
        }
        return fishes;

        WeightedSet<FishTable.Fish> CombineBox(WeightedSet<FishTable.Fish> fishes, FishTable.FishBox fishBox) {
            if (fishBox.Probability < Random.Shared.Next(0, 10000)) {
                return fishes;
            }
            foreach ((int fishid, int weight) in fishBox.Fishes) {
                if (!serverTableMetadata.FishTable.Fishes.TryGetValue(fishid, out FishTable.Fish? fish)) {
                    continue;
                }

                if (fish.FluidHabitat != liquidType) {
                    continue;
                }

                if (!fish.IgnoreSpotMastery && (spot.MinMastery > fish.Mastery || spot.MaxMastery < fish.Mastery)) {
                    continue;
                }

                fishes.Add(fish, weight);
            }
            return fishes;
        }
    }

    public FishingError Start(Vector3 position) {
        if (session.Field?.AccelerationStructure == null) {
            return FishingError.s_fishing_error_system_error;
        }

        if (GuideObject == null) {
            return FishingError.s_fishing_error_system_error;
        }

        if (rod?.Metadata.Function?.Type != ItemFunction.FishingRod) {
            return FishingError.s_fishing_error_invalid_item;
        }

        if (!int.TryParse(rod.Metadata.Function.Parameters, out int rodCode) || !tableMetadata.FishingRodTable.Entries.TryGetValue(rodCode, out FishingRodTable.Entry? rodMetadata)) {
            return FishingError.s_fishing_error_invalid_item;
        }

        FishingTile? block = tiles.FirstOrDefault(tile => tile.Key == position).Value;
        if (block == null) {
            return FishingError.s_fishing_error_system_error;
        }

        if (!serverTableMetadata.FishTable.Spots.TryGetValue(session.Field.MapId, out FishTable.Spot? spotMetadata)) {
            return FishingError.s_fishing_error_notexist_fish;
        }

        selectedTile = block;
        WeightedSet<FishTable.Fish> fishes = GetFishes(spotMetadata, selectedTile.LiquidType);
        if (fishes.Count == 0) {
            return FishingError.s_fishing_error_notexist_fish;
        }

        selectedFish = fishes.Get();

        int fishingTick = Constants.fisherBoreDuration;
        bool hasAutoFish = session.Player.Buffs.HasBuff(BuffEventType.AutoFish);

        // Fishing Success
        if (Random.Shared.Next(0, 10000) < selectedFish.BaitProbability) {
            if (!hasAutoFish && Random.Shared.Next(0, 10000) < Constants.fishFightingProp) {
                fishFightGame = true;
            }

            fishingTick -= rodMetadata.ReduceTime;
            fishingTick = Random.Shared.Next(fishingTick - fishingTick / 3, fishingTick); // Chance for early catch
        } else {
            fishingTick = Random.Shared.Next(fishingTick + 1, fishingTick * 2); // If tick is over bore duration, it will fail
        }

        session.Send(FishingPacket.Start((session.Field.FieldTick + fishingTick).Truncate32(), fishFightGame));

        return FishingError.none;
    }

    public FishingError Catch(bool success) {
        if (selectedTile == null || selectedFish == null || session.Field is null) {
            return FishingError.s_fishing_error_system_error;
        }

        if (!serverTableMetadata.FishTable.Spots.TryGetValue(session.Field.MapId, out FishTable.Spot? spotMetadata)) { // Should not happen.
            return FishingError.s_fishing_error_notexist_fish;
        }

        // determine fish size
        int fishSize = Random.Shared.NextDouble() switch {
            >= 0.0 and < 0.025 => Random.Shared.Next(selectedFish.BigSize.Max, selectedFish.BigSize.Max * 2), // prize fish
            >= 0.025 and < 0.03 => Random.Shared.Next(selectedFish.SmallSize.Min, selectedFish.SmallSize.Max),
            >= 0.03 and < 0.15 => Random.Shared.Next(selectedFish.SmallSize.Max, selectedFish.BigSize.Min),
            >= 0.15 => Random.Shared.Next(selectedFish.SmallSize.Min, selectedFish.SmallSize.Max),
            _ => Random.Shared.Next(selectedFish.SmallSize.Min, selectedFish.SmallSize.Max),
        };

        bool hasAutoFish = session.Player.Buffs.HasBuff(BuffEventType.AutoFish);

        if (success) {
            AddFish(fishSize, hasAutoFish);
            CatchItem(spotMetadata);
        } else {
            session.ConditionUpdate(ConditionType.fish_fail, codeLong: selectedFish.Id, targetLong: session.Field.MapId);
            session.Send(FishingPacket.CatchFish(selectedFish.Id, fishSize, hasAutoFish));
            fishFightGame = false;
        }

        return FishingError.none;
    }

    private void AddFish(int fishSize, bool hasAutoFish) {
        if (selectedFish == null || session.Field is null) {
            return;
        }

        bool prizeFish = fishSize >= selectedFish.BigSize.Max;
        int masteryExp = 0;
        var caughtFishType = CaughtFishType.Default;

        if (FishAlbum.TryGetValue(selectedFish.Id, out FishEntry? fishEntry)) {
            fishEntry.TotalCaught++;
            fishEntry.LargestSize = Math.Max(fishEntry.LargestSize, fishSize);

            if (fishEntry.TotalCaught % selectedFish.PointCount == 0) {
                masteryExp = selectedFish.MasteryExp;
            }

            if (prizeFish) {
                fishEntry.TotalPrizeFish++;
            }

            session.Send(FishingPacket.CatchFish(selectedFish.Id, fishSize, hasAutoFish, fishEntry));
        } else {
            fishEntry = new FishEntry(selectedFish.Id) {
                TotalCaught = 1,
                TotalPrizeFish = prizeFish ? 1 : 0,
                LargestSize = fishSize,
            };
            FishAlbum.Add(selectedFish.Id, fishEntry);
            session.ConditionUpdate(ConditionType.fish_collect, codeLong: selectedFish.Id, targetLong: session.Field.MapId);
            masteryExp = selectedFish.MasteryExp * 2; // double mastery exp if first catch
            caughtFishType = CaughtFishType.FirstKind;
            session.Send(FishingPacket.CatchFish(selectedFish.Id, fishSize, hasAutoFish, fishEntry));
        }

        if (fishSize >= selectedFish.BigSize.Min) {
            session.ConditionUpdate(ConditionType.fish_goldmedal, codeLong: selectedFish.Id, targetLong: session.Field.MapId);
        }
        if (prizeFish) {
            session.ConditionUpdate(ConditionType.fish_big, codeLong: selectedFish.Id, targetLong: session.Field.MapId);
            caughtFishType = CaughtFishType.Prize;
            masteryExp = selectedFish.MasteryExp * 2; // double mastery exp if prize fish
            session.Field.Broadcast(FishingPacket.PrizeFish(session.PlayerName, selectedFish.Id));
        }
        session.ConditionUpdate(ConditionType.fish, codeLong: selectedFish.Id, targetLong: session.Field.MapId);

        if (masteryExp == 0) {
            return;
        }

        session.Mastery[MasteryType.Fishing] += masteryExp;
        short masteryLevel = session.Mastery.GetLevel(MasteryType.Fishing);
        session.Send(FishingPacket.IncreaseMastery(selectedFish.Id, masteryLevel, masteryExp, caughtFishType));
    }

    private void CatchItem(FishTable.Spot spot) {
        if (session.Field is null) return;
        var items = new List<Item>();
        if (spot.GlobalDropBoxId > 0) {
            ICollection<Item> globalDropItems = session.Field.ItemDrop.GetGlobalDropItems(spot.GlobalDropBoxId, spot.SpotLevel);
            foreach (Item item in globalDropItems) {
                items.Add(item);
                if (!session.Item.Inventory.Add(item, true)) {
                    session.Item.MailItem(item);
                }
            }
        }

        if (spot.IndividualDropBoxId > 0) {
            ICollection<Item> individualDropItems = session.Field.ItemDrop.GetIndividualDropItems(spot.IndividualDropBoxId, spot.DropRank);
            foreach (Item item in individualDropItems) {
                items.Add(item);
                if (!session.Item.Inventory.Add(item, true)) {
                    session.Item.MailItem(item);
                }
            }
        }

        if (items.Count > 0) {
            session.Send(FishingPacket.CatchItem(items));
        }
    }

    public void FailMinigame() {
        fishFightGame = false;
    }

}
