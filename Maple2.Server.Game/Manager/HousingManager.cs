using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Game.Manager;

public class HousingManager {
    private readonly GameSession session;
    private readonly TableMetadataStorage tableMetadata;
    private Home Home => session.Player.Value.Home;

    private readonly ILogger logger = Log.Logger.ForContext<HousingManager>();

    public ItemBlueprint? StagedItemBlueprint = null;

    public HousingManager(GameSession session, TableMetadataStorage tableMetadata) {
        this.session = session;
        this.tableMetadata = tableMetadata;
    }

    public void SetPlot(PlotInfo? plot) {
        Home.Outdoor = plot;

        session.Field?.Broadcast(CubePacket.UpdateProfile(session.Player));
    }

    public void SetName(string name) {
        if (session.Field is null) return;
        if (name.Length > Constant.HomeNameMaxLength) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_center));
            return;
        }

        Home.Indoor.Name = name;
        PlotInfo? plot = Home.Outdoor;
        using GameStorage.Request db = session.GameStorage.Context();
        if (plot != null) {
            plot.Name = name;
            if (!db.SavePlotInfoName(plot.Id, name)) {
                session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
                return;
            }
            session.World.UpdateFieldPlot(new FieldPlotRequest {
                MapId = session.Field.MapId,
                PlotNumber = plot.Number,
                IgnoreChannel = GameServer.GetChannel(),
                UpdatePlot = new FieldPlotRequest.Types.UpdatePlot(),
            });
        } else {
            if (!db.SavePlotInfo(Home.Indoor)) {
                session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
                return;
            }
        }

        session.Field.Broadcast(CubePacket.SetHomeName(Home));
        session.Field.Broadcast(CubePacket.UpdateProfile(session.Player));
    }

    public void SetHomeMessage(string message) {
        if (message.Length > Constant.HomeMessageMaxLength) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_center));
            return;
        }

        Home.Message = message;
        if (!SaveHome()) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
            return;
        }

        session.Field?.Broadcast(CubePacket.SetHomeMessage(Home.Message));
    }

    public void SetPasscode(string passcode) {
        if (passcode.Length != 0 && (passcode.Length != Constant.HomePasscodeLength || !uint.TryParse(passcode, out _))) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_center));
            return;
        }

        Home.Passcode = passcode;
        using GameStorage.Request db = session.GameStorage.Context();
        if (!SaveHome()) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
            return;
        }

        session.Send(CubePacket.SetPasscode());
    }

    public bool SaveHome() {
        using GameStorage.Request db = session.GameStorage.Context();
        return db.SaveHome(Home);
    }

    public bool SavePlots() {
        using GameStorage.Request db = session.GameStorage.Context();
        return db.SavePlotInfo(Home.Indoor);
    }

    // Retrieves plot directly from field which includes cube data.
    public Plot? GetFieldPlot() {
        if (session.Field == null) {
            return null;
        }

        Plot? plot;
        if (session.Field.MapId == Home.Indoor.MapId) {
            session.Field.Plots.TryGetValue(Home.Indoor.Number, out plot);
            return plot;
        }

        if (Home.Outdoor == null) {
            return null;
        }

        session.Field.Plots.TryGetValue(Home.Outdoor.Number, out plot);
        return plot;
    }

    public Plot? GetIndoorPlot() {
        if (session.Field is not HomeFieldManager homeField) {
            return null;
        }

        if (session.AccountId != homeField.OwnerId || session.Field.MapId != Home.Indoor.MapId) return null;

        session.Field.Plots.TryGetValue(Home.Indoor.Number, out Plot? plot);
        return plot;
    }

    public bool SaveFieldPlot(int number) {
        if (session.Field?.Plots.TryGetValue(number, out Plot? plot) != true) {
            return false;
        }

        return true;
    }

    public bool BuyPlot(int plotNumber) {
        PlotInfo? plotInfo = Home.Outdoor;
        if (plotInfo != null) {
            session.Send(plotInfo.Number == plotNumber
                ? CubePacket.Error(UgcMapError.s_ugcmap_my_house)
                : CubePacket.Error(UgcMapError.s_ugcmap_cant_buy_more_than_two_house));
            return false;
        }

        if (session.Field == null || !session.Field.Plots.TryGetValue(plotNumber, out Plot? plot)) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_not_a_buyable));
            return false;
        }

        if (plot.OwnerId != 0 || plot.State != PlotState.Open) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_already_owned));
            return false;
        }

        UgcMapGroup.Cost contract = plot.Metadata.ContractCost;

        int costAmount = contract.Amount;
        GameEvent? saleEvent = session.FindEvent(GameEventType.UGCMapContractSale).FirstOrDefault();
        if (saleEvent != null) {
            float discount = float.Parse(saleEvent.Metadata.Value1) / 10000f;
            costAmount = (int) Math.Ceiling(costAmount * (1 - discount));
        }

        if (!CheckAndRemoveCost(session, contract.ItemId, costAmount)) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_not_enough_money));
            return false;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        plotInfo = db.BuyPlot(session.PlayerName, session.AccountId, plot, TimeSpan.FromDays(contract.Days));
        if (plotInfo == null) {
            logger.Warning("Failed to buy plot: {PlotId}, {OwnerId}", plot.Id, plot.OwnerId);
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
            return false;
        }

        session.ConditionUpdate(ConditionType.buy_house);
        if (session.Field.UpdatePlotInfo(plotInfo) != true) {
            logger.Warning("Failed to update map plot in field: {PlotId}", plotInfo.Id);
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_system_error));
            return false;
        }

        session.Field.Broadcast(CubePacket.BuyPlot(plotInfo));
        SetPlot(plotInfo);
        session.World.UpdateFieldPlot(new FieldPlotRequest {
            IgnoreChannel = GameServer.GetChannel(),
            MapId = session.Field.MapId,
            PlotNumber = plotInfo.Number,
            UpdatePlot = new FieldPlotRequest.Types.UpdatePlot(),
        });
        return true;
    }

    public PlotInfo? ForfeitPlot() {
        PlotInfo? plotInfo = Home.Outdoor;
        if (plotInfo == null) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_dont_have_ownership));
            return null;
        }

        if (!session.FindField(plotInfo.MapId, out FieldManager? fieldManager)) {
            // Should we instantiate a field manager instead of giving an error?
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_system_error));
            return null;
        }

        if (DateTime.UtcNow - plotInfo.ExpiryTime.FromEpochSeconds() > TimeSpan.FromDays(3)) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_not_extension_date));
            return null;
        }

        if (!fieldManager.Plots.TryGetValue(plotInfo.Number, out Plot? plot)) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_system_error));
            return null;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        plotInfo = db.ForfeitPlot(session.AccountId, plot);
        if (plotInfo == null) {
            logger.Warning("Failed to forfeit plot: {PlotId}, {OwnerId}", plot.Id, plot.OwnerId);
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
            return null;
        }

        if (fieldManager.UpdatePlotInfo(plotInfo) != true) {
            logger.Warning("Failed to update map plot in field: {PlotId}", plotInfo.Id);
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_system_error));
            return null;
        }

        fieldManager.Broadcast(CubePacket.ForfeitPlot(plotInfo));
        SetPlot(null);
        session.World.UpdateFieldPlot(new FieldPlotRequest {
            IgnoreChannel = GameServer.GetChannel(),
            MapId = fieldManager.MapId,
            PlotNumber = plotInfo.Number,
            UpdatePlot = new FieldPlotRequest.Types.UpdatePlot {
                AccountId = session.AccountId,
                Forfeit = true,
            },
        });
        return plotInfo;
    }

    public void ExtendPlot() {
        if (Home.Outdoor == null) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_dont_have_ownership));
            return;
        }

        if (Home.Outdoor.State != PlotState.Taken || Home.Outdoor.ExpiryTime <= DateTime.UtcNow.ToEpochSeconds()) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_expired_salable_group));
            return;
        }

        if (Home.Outdoor.ExpiryTime.FromEpochSeconds() - DateTime.UtcNow > TimeSpan.FromDays(30)) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_not_extension_date));
            return;
        }

        UgcMapGroup.Cost extension = Home.Outdoor.Metadata.ExtensionCost;

        int costAmount = extension.Amount;
        GameEvent? saleEvent = session.FindEvent(GameEventType.UGCMapExtensionSale).FirstOrDefault();
        if (saleEvent != null) {
            float discount = float.Parse(saleEvent.Metadata.Value1) / 10000f;
            costAmount = (int) Math.Ceiling(costAmount * (1 - discount));
        }

        if (!CheckAndRemoveCost(session, extension.ItemId, costAmount)) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_need_extension_pay));
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        PlotInfo? plotInfo = db.ExtendPlot(Home.Outdoor, TimeSpan.FromDays(extension.Days));
        if (plotInfo == null) {
            logger.Warning("Failed to extend plot: {PlotId}, {OwnerId}", Home.Outdoor.Id, Home.Outdoor.OwnerId);
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
            return;
        }

        session.ConditionUpdate(ConditionType.extend_house);
        if (session.Field?.UpdatePlotInfo(plotInfo) != true) {
            logger.Warning("Failed to update map plot in field: {PlotId}", Home.Outdoor.Id);
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_system_error));
            return;
        }

        session.Field.Broadcast(CubePacket.ExtendPlot(plotInfo));
        SetPlot(plotInfo);
        session.World.UpdateFieldPlot(new FieldPlotRequest {
            MapId = session.Field.MapId,
            PlotNumber = plotInfo.Number,
            IgnoreChannel = GameServer.GetChannel(),
            UpdatePlot = new FieldPlotRequest.Types.UpdatePlot(),
        });
    }

    private static bool CheckAndRemoveCost(GameSession session, int itemId, int cost) {
        switch (itemId) {
            case >= 90000001 and <= 90000003:
                if (session.Currency.Meso < cost) {
                    return false;
                }

                session.Currency.Meso -= cost;
                return true;
            case 90000004 or 90000011 or 90000015 or 90000016:
                if (session.Currency.Meret < cost) {
                    return false;
                }

                session.Currency.Meret -= cost;
                return true;
        }

        return false;
    }

    public void Save(GameStorage.Request db) {
        db.SaveHome(Home);
        db.SavePlotInfo(Home.Indoor);
    }

    public void InitNewHome(string characterName, ExportedUgcMapMetadata? template) {
        Home.NewHomeDefaults(characterName);

        using GameStorage.Request db = session.GameStorage.Context();
        if (template is null) {
            Home.SetArea(10);
            Home.SetHeight(4);

            db.SaveHome(Home);
            db.SavePlotInfo(Home.Indoor);
            return;
        }

        Home.SetArea(template.IndoorSize[0]);
        Home.SetHeight(template.IndoorSize[2]);

        List<PlotCube> plotCubes = [];
        foreach (ExportedUgcMapMetadata.Cube cube in template.Cubes) {
            if (!session.ItemMetadata.TryGet(cube.ItemId, out ItemMetadata? itemMetadata) || itemMetadata.Install is null || itemMetadata.Housing is null) {
                logger.Error("Failed to get item metadata for cube {cubeId}.", cube.ItemId);
                continue;
            }

            long itemUid = session.Item.Furnishing.AddCube(cube.ItemId);
            if (itemUid == 0) {
                logger.Error("Failed to add cube {cubeId} to storage.", cube.ItemId);
                continue;
            }

            session.FunctionCubeMetadata.TryGet(itemMetadata.Install.ObjectCubeId, out FunctionCubeMetadata? functionCubeMetadata);
            PlotCube? plotCube = CreateCube(Home.Indoor, itemMetadata, functionCubeMetadata, null, template.BaseCubePosition + cube.OffsetPosition, cube.Rotation);
            if (plotCube is null) {
                logger.Error("Failed to create cube {cubeId} at position {position}.", cube.ItemId, template.BaseCubePosition + cube.OffsetPosition);
                continue;
            }

            session.Item.Furnishing.TryAddCube(itemUid, plotCube);

            plotCubes.Add(plotCube);
        }

        db.SaveHome(Home);
        db.SavePlotInfo(Home.Indoor);
        db.SaveCubes(Home.Indoor, plotCubes);
    }

    private readonly Dictionary<HousingCategory, int> decorationGoal = new() {
        { HousingCategory.Bed, 1 },
        { HousingCategory.Table, 1 },
        { HousingCategory.SofasChairs, 2 },
        { HousingCategory.Storage, 1 },
        { HousingCategory.WallDecoration, 1 },
        { HousingCategory.WallTiles, 3 },
        { HousingCategory.Bathroom, 1 },
        { HousingCategory.Lighting, 1 },
        { HousingCategory.Electronics, 1 },
        { HousingCategory.Fences, 2 },
        { HousingCategory.NaturalTerrain, 4 },
    };

    public void InteriorCheckIn(Plot plot) {
        if (session.Field is null) return;

        if (Home.DecorationRewardTimestamp != 0) {
            return;
        }

        Dictionary<HousingCategory, int> decorationCurrent = plot.Cubes.Values
            .Where(plotCube => plotCube.Metadata.Housing != null)
            .GroupBy(plotCube => plotCube.Metadata.Housing!.HousingCategory)
            .ToDictionary(grouping => grouping.Key, grouping => grouping.Count());

        int decorationScore = 0;

        foreach (KeyValuePair<HousingCategory, int> goal in decorationGoal) {
            if (!decorationCurrent.TryGetValue(goal.Key, out int current)) {
                continue;
            }

            if (current < goal.Value) {
                continue;
            }

            switch (goal.Key) {
                case HousingCategory.Bed:
                case HousingCategory.SofasChairs:
                case HousingCategory.WallDecoration:
                case HousingCategory.WallTiles:
                case HousingCategory.Bathroom:
                case HousingCategory.Lighting:
                case HousingCategory.Fences:
                case HousingCategory.NaturalTerrain:
                    decorationScore += 100;
                    break;
                case HousingCategory.Table:
                case HousingCategory.Storage:
                    decorationScore += 50;
                    break;
                case HousingCategory.Electronics:
                    decorationScore += 200;
                    break;
                default:
                    continue;
            }
        }

        int housingPoint = decorationScore switch {
            < 300 => 100,
            < 500 => 300,
            < 700 => 500,
            < 1100 => 700,
            _ => 1100,
        };

        tableMetadata.UgcHousingPointRewardTable.Entries.TryGetValue(housingPoint, out UgcHousingPointRewardTable.Entry? reward);
        if (reward is null) {
            logger.Error("Failed to get reward for decoration score {DecorationScore}.", housingPoint);
            return;
        }

        ICollection<Item> items = session.Field.ItemDrop.GetIndividualDropItems(session, decorationScore, reward.IndividualDropBoxId);
        foreach (Item item in items) {
            if (!session.Item.Inventory.Add(item, true)) {
                session.Item.MailItem(item);
            }
        }

        Home.GainExp(decorationScore, tableMetadata.MasteryUgcHousingTable.Entries);
        Home.DecorationRewardTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        session.Send(CubePacket.DesignRankReward(Home));
    }

    public void InteriorReward(byte rewardId) {
        if (Home.InteriorRewardsClaimed.Contains(rewardId)) {
            return;
        }

        tableMetadata.MasteryUgcHousingTable.Entries.TryGetValue(rewardId, out MasteryUgcHousingTable.Entry? reward);
        if (reward == null) {
            return;
        }

        Item? rewardItem = session.Field?.ItemDrop.CreateItem(reward.RewardJobItemId);
        if (rewardItem == null) {
            return;
        }

        if (!session.Item.Inventory.Add(rewardItem, true)) {
            session.Item.MailItem(rewardItem);
        }

        Home.InteriorRewardsClaimed.Add(rewardId);
        session.Send(CubePacket.DesignRankReward(Home));
    }

    private PlotCube? CreateCube(PlotInfo plot, ItemMetadata itemMetadata, FunctionCubeMetadata? functionCubeMetadata, UgcItemLook? template, Vector3 position, float rotation) {
        var result = new PlotCube(itemMetadata, 0, template) {
            Type = PlotCube.CubeType.Construction,
            Position = position,
            Rotation = rotation,
        };

        using GameStorage.Request db = session.GameStorage.Context();

        if (functionCubeMetadata is not null) {
            result.Interact = new InteractCube(position, functionCubeMetadata);
            if (result.Interact.Nurturing is not null && result.Interact.Metadata.Nurturing is not null) {
                Nurturing? nurturing = db.GetNurturing(session.AccountId, result.ItemId, result.Interact.Metadata.Nurturing);
                if (nurturing is null) {
                    nurturing = db.CreateNurturing(session.AccountId, result.Interact.Metadata.Nurturing, result.Interact.Metadata.Id);
                    if (nurturing is null) {
                        logger.Error("Failed to create Nurturing for {AccountId}, ItemId {ItemId}", session.AccountId, result.ItemId);
                        return null;
                    }
                }
            }
        }

        result = db.CreateCube(plot, result);
        if (result is null) {
            logger.Error("Failed to create cube for plot {PlotNumber} at position {Position}.", plot.Number, position);
            return null;
        }

        return result;
    }

    private void DeleteCube(PlotCube cube) {
        using GameStorage.Request db = session.GameStorage.Context();
        if (db.DeleteCube(cube)) return;
        logger.Error("Failed to delete cube {CubeId} at position {Position}.", cube.Id, cube.Position);
    }

    public bool TryPlaceCube(HeldCube cube, Plot plot, ItemMetadata itemMetadata, in Vector3B position, float rotation,
                             [NotNullWhen(true)] out PlotCube? result, bool isReplace = false) {
        result = null;
        if (session.Field is null || itemMetadata.Install is null) return false;

        tableMetadata.FurnishingShopTable.Entries.TryGetValue(cube.ItemId, out FurnishingShopTable.Entry? shopEntry);

        switch (plot.PlotMode) {
            case PlotMode.BlueprintPlanner when itemMetadata.Housing!.IsNotAllowedInBlueprint:
                session.Send(CubePacket.Error(UgcMapError.s_err_cannot_install_blueprint));
                return false;
            case PlotMode.BlueprintPlanner when itemMetadata.Housing!.HousingCategory is HousingCategory.Farming or HousingCategory.Ranching:
                session.Send(CubePacket.Error(UgcMapError.s_err_cannot_install_nurturing_in_design_home));
                return false;
            case PlotMode.BlueprintPlanner when cube.ItemId is Constant.InteriorPortalCubeId:
                session.Send(CubePacket.Error(UgcMapError.s_err_cannot_install_magic_portal));
                return false;
            case PlotMode.BlueprintPlanner when shopEntry is null:
                session.Send(CubePacket.Error(UgcMapError.s_err_cannot_buy_limited_item_more));
                return false;
        }

        float groundHeight = 0;
        bool outdoor = plot.MapId is not Constant.DefaultHomeMapId;
        if (outdoor) {
            FieldSellableTile? groundTile = session.Field.AccelerationStructure?.FirstSellableTile(position, (x) => x.SellableGroup == plot.Number);
            if (groundTile is null) {
                session.Send(CubePacket.Error(UgcMapError.s_ugcmap_cant_create_on_place));
                return false;
            }

            groundHeight = groundTile.Position.Z / Constant.BlockSize;
        }

        if (!outdoor && itemMetadata.Install.IndoorPortal) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_cant_be_created));
            return false;
        }

        // can only place one indoor portal per plot
        if (outdoor && plot.Cubes.Any(x => x.Value.Metadata.Install is { IndoorPortal: true }) && itemMetadata.Install.IndoorPortal) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_cant_create_on_place));
            return false;
        }

        bool isSolidCube = itemMetadata.Install!.IsSolidCube;
        bool isOnGround = Math.Abs(position.Z - groundHeight) < 0.1;
        bool allowWaterOnGround = itemMetadata.Install.MapAttribute is MapAttribute.water && Constant.AllowWaterOnGround;

        // If the cube is not a solid cube and it's replacing ground, it's not allowed.
        if ((!isSolidCube && isOnGround) && !allowWaterOnGround) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_cant_create_on_place));
            return false;
        }

        // Cannot overlap cubes if not replacing
        if (plot.Cubes.ContainsKey(position) && !isReplace) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_cant_create_on_place));
            return false;
        }

        if (isReplace && plot.Cubes.ContainsKey(position)) {
            TryRemoveCube(plot, position, out _);
        }

        if (plot.MapId is Constant.DefaultHomeMapId && IsCoordOutsideArea(position)) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_area_limit));
            return false;
        }

        // Only check height on outside plots since we already check if it's inside the area when getting the ground height
        if (outdoor && position.Z - groundHeight > plot.Metadata.Limit.Height) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_area_limit));
            return false;
        }

        session.FunctionCubeMetadata.TryGet(itemMetadata.Install.ObjectCubeId, out FunctionCubeMetadata? functionCubeMetadata);

        if (plot.IsPlanner) {
            if (shopEntry is null && cube.Id == 0) {
                session.Send(CubePacket.Error(UgcMapError.s_ugcmap_cant_additionalbuy));
                return false;
            }
            Item? existingItem = session.Item.Furnishing.GetItem(cube.ItemId);
            int existingCubeCount = plot.Cubes.Count(x => x.Value.ItemId == cube.ItemId);
            if (shopEntry is null && (existingItem is null || existingCubeCount >= existingItem.Amount)) {
                session.Send(CubePacket.Error(UgcMapError.s_err_cannot_buy_limited_item_more));
                return false;
            }

            result = new PlotCube(itemMetadata, FurnishingManager.NextCubeId(), cube.Template) {
                Position = position,
                Rotation = rotation,
                Type = PlotCube.CubeType.Construction,
            };

            if (functionCubeMetadata is not null) {
                result.Interact = new InteractCube(position, functionCubeMetadata);
            }

            plot.Cubes.Add(position, result);
            return true;
        }

        result = CreateCube(plot, itemMetadata, functionCubeMetadata, cube.Template, position, rotation);
        if (result is null) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_db));
            return false;
        }

        if (!session.Item.Furnishing.TryAddCube(cube.Id, result)) {
            long itemUid = session.Item.Furnishing.AddCube(cube);
            if (itemUid == 0) {
                DeleteCube(result);
                session.Send(CubePacket.Error(UgcMapError.s_ugcmap_not_for_sale));
                return false;
            }

            if (shopEntry is null) {
                DeleteCube(result);
                session.Send(CubePacket.Error(UgcMapError.s_err_cannot_buy_limited_item_more));
                return false;
            }

            if (!session.Item.Furnishing.PurchaseCube(shopEntry)) {
                DeleteCube(result);
                return false;
            }

            session.Send(CubePacket.PurchaseCube(session.Player.ObjectId));
            // Now that we have purchased the cube, it must be placeable.
            if (!session.Item.Furnishing.TryAddCube(itemUid, result)) {
                DeleteCube(result);
                session.Send(CubePacket.Error(UgcMapError.s_ugcmap_not_owned_item));
                return false;
            }
        }

        if (functionCubeMetadata is not null) {
            session.Field.AddFieldFunctionInteract(result);
        }

        if (outdoor && itemMetadata.Install.IndoorPortal) {
            FieldPortal? fieldPortal = session.Field.SpawnFieldToHomePortal(result, plot.OwnerId);
            if (fieldPortal is null) {
                DeleteCube(result);
                session.Send(CubePacket.Error(UgcMapError.s_ugcmap_not_owned_item));
                return false;
            }
            session.Field.Broadcast(PortalPacket.Add(fieldPortal));
        }

        plot.Cubes.Add(position, result);
        return true;
    }

    public bool TryRemoveCube(Plot plot, in Vector3B position, [NotNullWhen(true)] out PlotCube? cube) {
        if (!plot.Cubes.Remove(position, out cube)) {
            session.Send(CubePacket.Error(UgcMapError.s_ugcmap_no_cube_to_remove));
            return false;
        }

        if (cube.Interact?.PortalSettings is not null) {
            session.Field?.RemovePortal(cube.Interact.PortalSettings.PortalObjectId);
        }

        if (cube.Interact is not null) {
            session.Field?.RemoveFieldFunctionInteract(cube.Interact.Id);
        }

        if (plot.MapId is not Constant.DefaultHomeMapId && cube.Metadata.Install is { IndoorPortal: true }) {
            FieldPortal? portal = session.Field?.GetPortals().FirstOrDefault(x => x.HomeId == plot.OwnerId);
            if (portal is not null) {
                session.Field?.RemovePortal(portal.ObjectId);
            }
        }

        if (plot.IsPlanner) {
            return true;
        }

        if (!session.Item.Furnishing.RetrieveCube(cube.Id)) {
            logger.Error("Failed to deposit cube {CubeId} back into storage.", cube.Id);
            return false;
        }

        DeleteCube(cube);

        return true;
    }

    public bool IsCoordOutsideArea(Vector3B position) {
        int height = Home.IsPlanner ? Home.PlannerHeight : Home.Height;
        int area = Home.IsPlanner ? Home.PlannerArea : Home.Area;

        // Check if the position is outside the planar area bounds
        if (position.X > 0 || position.Y > 0 || position.Z < 0) {
            return true;
        }

        area *= -1;
        if (position.X <= area || position.Y <= area) {
            return true;
        }

        // Check if the position is outside the height bounds
        return position.Z > height;
    }

    public bool RequestLayout(HomeLayout layout, out (Dictionary<FurnishingCurrencyType, long> cubeCosts, int cubeCount) result) {
        Dictionary<int, int> groupedCubes = layout.Cubes.GroupBy(plotCube => plotCube.ItemId)
            .ToDictionary(grouping => grouping.Key, grouping => grouping.Count()); // Dictionary<item id, count>
        int cubeCount = 0;
        Dictionary<FurnishingCurrencyType, long> cubeCosts = new() {
            { FurnishingCurrencyType.Meso, 0 },
            { FurnishingCurrencyType.Meret, 0 },
        };

        foreach ((int id, int amount) in groupedCubes) {
            tableMetadata.FurnishingShopTable.Entries.TryGetValue(id, out FurnishingShopTable.Entry? shopEntry);

            Item? item = session.Item.Furnishing.GetItem(id);
            if (item is null) {
                if (shopEntry is null) {
                    continue;
                }
                cubeCosts[shopEntry.FurnishingTokenType] += shopEntry.Price * amount;
                cubeCount += amount;
                continue;
            }

            if (item.Amount >= amount) {
                continue;
            }

            if (shopEntry is null) {
                continue;
            }

            int missingCubes = amount - item.Amount;
            cubeCosts[shopEntry.FurnishingTokenType] += shopEntry.Price * missingCubes;
            cubeCount += missingCubes;
        }

        result = (cubeCosts, cubeCount);
        return true;
    }

    public void ApplyLayout(Plot plot, HomeLayout layout, bool isBlueprint = false) {
        if (session.Field is null) return;
        if (plot.IsPlanner) {
            Home.SetPlannerArea(layout.Area);
            Home.SetPlannerHeight(layout.Height);
        } else {
            Home.SetArea(layout.Area);
            Home.SetHeight(layout.Height);
        }

        session.Field.Broadcast(CubePacket.UpdateHomeAreaAndHeight(Home.Area, Home.Height));
        if (isBlueprint && plot.IsPlanner) {
            // If it's planner, only send packets don't save properties
            session.Field.Broadcast(CubePacket.SetBackground(layout.Background));
            session.Field.Broadcast(CubePacket.SetLighting(layout.Lighting));
            session.Field.Broadcast(CubePacket.SetCamera(layout.Camera));
        } else if (isBlueprint) {
            if (session.Player.Value.Home.SetBackground(layout.Background)) {
                session.Field.Broadcast(CubePacket.SetBackground(layout.Background));
            }

            if (session.Player.Value.Home.SetLighting(layout.Lighting)) {
                session.Field.Broadcast(CubePacket.SetLighting(layout.Lighting));
            }

            if (session.Player.Value.Home.SetCamera(layout.Camera)) {
                session.Field.Broadcast(CubePacket.SetCamera(layout.Camera));
            }
        }

        foreach (PlotCube cube in layout.Cubes) {
            Item? item = session.Item.Furnishing.GetItem(cube.ItemId);
            cube.Id = item?.Uid ?? 0;
            ItemMetadata? metadata = null;
            if (item is null) {
                if (session.ItemMetadata.TryGet(cube.ItemId, out ItemMetadata? newMetadata)) {
                    metadata = newMetadata;
                }
            } else {
                metadata = item.Metadata;
            }
            if (metadata is null) {
                logger.Error("Failed to get item metadata for cube {CubeId} when applying layout {LayoutId}.", cube.ItemId, layout.Id);
                continue;
            }
            if (!TryPlaceCube(cube, plot, metadata, cube.Position, cube.Rotation, out PlotCube? plotCube)) {
                continue;
            }

            ByteWriter sendPacket;
            if (plotCube.Position.Z == 0) {
                sendPacket = CubePacket.ReplaceCube(session.Player.ObjectId, plotCube);
            } else {
                sendPacket = CubePacket.PlaceCube(session.Player.ObjectId, plot, plotCube);
            }

            if (plotCube.Interact is not null) {
                if (cube.Interact?.PortalSettings is not null) {
                    plotCube.Interact.PortalSettings = cube.Interact.PortalSettings;
                }
            }

            session.Field.Broadcast(sendPacket);
        }

        List<PlotCube> cubePortals = plot.Cubes.Values
            .Where(x => x.ItemId is Constant.InteriorPortalCubeId && x.Interact?.PortalSettings is not null)
            .ToList();

        foreach (PlotCube cubePortal in cubePortals) {
            FieldPortal? fieldPortal = session.Field.SpawnCubePortal(cubePortal);
            if (fieldPortal is null) {
                continue;
            }
            session.Field.Broadcast(PortalPacket.Add(fieldPortal));
        }

        Vector3 position = Home.CalculateSafePosition(plot.Cubes.Values.ToList());
        foreach (FieldPlayer fieldPlayer in session.Field.Players.Values) {
            fieldPlayer.Session.Send(PortalPacket.MoveByPortal(fieldPlayer, position, default));
        }

        session.Item.Furnishing.SendStorageCount();
        session.Housing.SaveHome();
        session.Field.Broadcast(NoticePacket.Message(StringCode.s_ugcmap_package_automatic_creation_completed, NoticePacket.Flags.Message | NoticePacket.Flags.Alert));
    }
}
