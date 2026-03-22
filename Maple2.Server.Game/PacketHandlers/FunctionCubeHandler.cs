using System.Linq;
using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
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
using Serilog;

namespace Maple2.Server.Game.PacketHandlers;

public class FunctionCubeHandler : FieldPacketHandler {
    private static readonly ILogger FunctionCubeLogger = Log.Logger.ForContext<FunctionCubeHandler>();


    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required FunctionCubeMetadataStorage FunctionCubeMetadataStorage { private get; init; }
    // ReSharper restore All
    #endregion

    public override RecvOp OpCode => RecvOp.FunctionCube;

    private enum Command : byte {
        Use = 4,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Use:
                HandleUseCube(session, packet);
                break;
        }
    }

    private void HandleUseCube(GameSession session, IByteReader packet) {
        string interactId = packet.ReadUnicodeString();
        byte unk = packet.ReadByte();

        if (!interactId.Contains('_')) {
            Logger.Error("Unexpected interactId {0}", interactId);
            return;
        }

        FieldFunctionInteract? fieldInteract = session.Field?.TryGetFieldFunctionInteract(interactId);
        if (fieldInteract is null) {
            Logger.Error("Interact cube not found for interactId {0}", interactId);
            return;
        }

        PlotCube? plotCube = session.Field?.Plots.Values
            .SelectMany(plot => plot.Cubes.Values)
            .FirstOrDefault(cube => cube.Id == fieldInteract.CubeId);

        FunctionCubeLogger.Information("FunctionCube.Use interactId={InteractId} unk={Unk} cubeId={CubeId} objectCode={ObjectCode} controlType={ControlType} state={State} plotItemId={PlotItemId}",
            interactId, unk, fieldInteract.CubeId, fieldInteract.InteractCube.Metadata.Id, fieldInteract.Value.ControlType, fieldInteract.InteractCube.State, plotCube?.ItemId ?? 0);

        if (plotCube is not null && HousingFunctionFurnitureRegistry.TryHandleUse(session, plotCube, fieldInteract)) {
            FunctionCubeLogger.Information("Housing furniture registry handled interactId={InteractId} plotItemId={PlotItemId}", interactId, plotCube.ItemId);
            return;
        }

        switch (fieldInteract.Value.ControlType) {
            case InteractCubeControlType.Nurturing:
                if (fieldInteract.InteractCube.Nurturing is not null) {
                    HandleNurturing(session, fieldInteract);
                }
                break;
            case InteractCubeControlType.Farming:
            case InteractCubeControlType.Breeding:
                FieldFunctionInteract? fieldFunctionInteract = session.Field?.TryGetFieldFunctionInteract(fieldInteract.InteractCube.Id);
                if (fieldFunctionInteract != null && fieldFunctionInteract.Use()) {
                    session.Send(FunctionCubePacket.UpdateFunctionCube(fieldFunctionInteract.InteractCube));

                    session.Mastery.Gather(fieldFunctionInteract);
                }
                break;
            case InteractCubeControlType.SpawnNPC:
            case InteractCubeControlType.InstallNPC:
                HandleSpawnNpcFurniture(session, fieldInteract);
                break;
            default:
                if (fieldInteract.InteractCube.State is InteractCubeState.InUse && fieldInteract.InteractCube.InteractingCharacterId != session.CharacterId) {
                    return;
                }

                bool isInDefaultState = fieldInteract.InteractCube.State == fieldInteract.InteractCube.Metadata.DefaultState;
                fieldInteract.InteractCube.State = isInDefaultState ? InteractCubeState.InUse : fieldInteract.InteractCube.Metadata.DefaultState;
                fieldInteract.InteractCube.InteractingCharacterId = isInDefaultState ? session.CharacterId : 0;
                session.Field?.Broadcast(FunctionCubePacket.UpdateFunctionCube(fieldInteract.InteractCube));
                session.Field?.Broadcast(FunctionCubePacket.UseFurniture(session.CharacterId, fieldInteract.InteractCube));
                break;
        }
    }


    private void HandleSpawnNpcFurniture(GameSession session, FieldFunctionInteract fieldInteract) {
        if (session.Field is null) {
            return;
        }

        FieldFunctionInteract? currentInteract = session.Field.TryGetFieldFunctionInteract(fieldInteract.InteractCube.Id);
        if (currentInteract == null) {
            return;
        }

        if (currentInteract.InteractCube.State is InteractCubeState.InUse && currentInteract.InteractCube.InteractingCharacterId != session.CharacterId) {
            return;
        }

        PlotCube? plotCube = session.Field.Plots.Values
            .SelectMany(plot => plot.Cubes.Values)
            .FirstOrDefault(cube => cube.Id == currentInteract.CubeId);
        if (plotCube is null) {
            Logger.Warning("Furniture cube runtime not found for interact cube {InteractId}", currentInteract.InteractCube.Id);
            return;
        }

        currentInteract.InteractCube.State = InteractCubeState.InUse;
        currentInteract.InteractCube.InteractingCharacterId = session.CharacterId;
        session.Field.Broadcast(FunctionCubePacket.UpdateFunctionCube(currentInteract.InteractCube));
        session.Field.Broadcast(FunctionCubePacket.UseFurniture(session.CharacterId, currentInteract.InteractCube));

        HousingFunctionFurnitureRegistry.Materialize(session.Field, plotCube);
    }

    private void HandleNurturing(GameSession session, FieldFunctionInteract fieldCube) {
        if (session.Field is null) return;

        if (fieldCube.Value.Nurturing is null) {
            return;
        }

        Plot? plot = session.Housing.GetFieldPlot();
        if (plot is null) {
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        Nurturing? dbNurturing = db.GetNurturing(plot.OwnerId, fieldCube.Value.Id, fieldCube.Value.Nurturing);
        if (dbNurturing is null) {
            Logger.Error("Nurturing not found for account id {0} and interact id {1}", session.AccountId, fieldCube.Value.Id);
            return;
        }

        if (dbNurturing.ClaimedGiftForStage > dbNurturing.NurturingMetadata.RequiredGrowth.Length) {
            // fully grown
            return;
        }

        // always update object reference
        fieldCube.InteractCube.Nurturing = dbNurturing;
        Nurturing? nurturing = fieldCube.InteractCube.Nurturing;

        if (session.AccountId != plot.OwnerId) {
            HandlePlayNurturing(session, plot, fieldCube, nurturing, db);
            return;
        }

        FunctionCubeMetadata.NurturingData.Growth requiredGrowth = nurturing.NurturingMetadata.RequiredGrowth.First(x => x.Stage == nurturing.ClaimedGiftForStage);
        // Claim reward
        if (nurturing.Exp >= requiredGrowth.Exp) {
            nurturing.ClaimedGiftForStage++;

            Item? rewardStageItem = session.Field.ItemDrop.CreateItem(requiredGrowth.Reward.ItemId, rarity: requiredGrowth.Reward.Rarity, amount: requiredGrowth.Reward.Amount);
            if (rewardStageItem is null) {
                Logger.Error("Failed to create the reward item for account {0} and item {1}", session.AccountId, fieldCube.InteractCube.Metadata.Id);
                return;
            }
            // drop the item
            session.Field.DropItem(fieldCube.Position, fieldCube.Rotation, rewardStageItem, owner: session.Player, characterId: session.CharacterId);
            db.UpdateNurturing(session.AccountId, fieldCube.InteractCube);

            session.Field.Broadcast(FunctionCubePacket.UpdateFunctionCube(fieldCube.InteractCube));
            return;
        }

        RewardItem feedItem = nurturing.NurturingMetadata.Feed;
        Item? hasItem = session.Item.Inventory.Find(feedItem.ItemId, feedItem.Rarity).FirstOrDefault();
        if (hasItem is null) {
            session.Send(NoticePacket.Message("You don't have the required item to feed this pet.  TODO: Find correct string id")); // TODO: Find correct string id
            return;
        }

        session.Item.Inventory.Consume(hasItem.Uid, amount: 1);

        RewardItem rewardFeedItem = nurturing.NurturingMetadata.RewardFeed;
        Item? rewardItem = session.Field.ItemDrop.CreateItem(rewardFeedItem.ItemId, rarity: rewardFeedItem.Rarity, amount: rewardFeedItem.Amount);
        if (rewardItem is null) {
            Logger.Error("Failed to create the reward item for account {0} and item {1}", session.AccountId, fieldCube.InteractCube.Metadata.Id);
            return;
        }

        // drop the item
        session.Field.DropItem(fieldCube.Position, fieldCube.Rotation, rewardItem, owner: session.Player, characterId: session.CharacterId);
        nurturing.Feed();
        db.UpdateNurturing(session.AccountId, fieldCube.InteractCube);

        session.Field.Broadcast(FunctionCubePacket.UpdateFunctionCube(fieldCube.InteractCube));
        session.Send(FunctionCubePacket.Feed(rewardItem.Uid, fieldCube.CubeId, fieldCube.InteractCube));
    }

    private void HandlePlayNurturing(GameSession session, Plot plot, FieldFunctionInteract cube, Nurturing nurturing, GameStorage.Request db) {
        if (session.Field is null) return;

        if (nurturing.PlayedBy.Contains(session.AccountId)) {
            session.Send(NoticePacket.Message("You already played with this pet today. TODO: Find correct string id")); // TODO: Find correct string id
            return;
        }

        if (db.CountNurturingForAccount(cube.InteractCube.Metadata.Id, session.AccountId) >= Constant.NurturingPlayMaxCount) {
            session.Send(NoticePacket.Message("You have already played with the maximum number of pets today. TODO: Find correct string id")); // TODO: Find correct string id
            return;
        }

        if (!nurturing.Play(session.AccountId)) {
            return;
        }

        RewardItem rewardPlay = nurturing.NurturingMetadata.Feed;
        Item? rewardItem = session.Field.ItemDrop.CreateItem(rewardPlay.ItemId, rarity: rewardPlay.Rarity, amount: rewardPlay.Amount);
        if (rewardItem is null) {
            Logger.Error("Failed to create the reward item for account {0} and item {1}", session.AccountId, cube.Value.Id);
            return;
        }

        // drop the item
        session.Field.DropItem(cube.Position, cube.Rotation, rewardItem, owner: session.Player, characterId: session.CharacterId);

        db.UpdateNurturing(plot.OwnerId, cube.InteractCube);

        session.Field.Broadcast(FunctionCubePacket.UpdateFunctionCube(cube.InteractCube));

        Mail? mail = CreateOwnerMail(session, plot.OwnerId, nurturing.NurturingMetadata);

        if (mail == null) {
            Logger.Error("Failed to create mail for account {0} and item {1}", session.AccountId, cube.Value.Id);
            return;
        }

        try {
            session.World.MailNotification(new MailNotificationRequest {
                AccountId = plot.OwnerId,
                MailId = mail.Id,
            });
        } catch { /* ignored */
        }
    }

    private Mail? CreateOwnerMail(GameSession session, long ownerId, FunctionCubeMetadata.NurturingData nurturing) {
        using GameStorage.Request db = session.GameStorage.Context();

        string contentId = nurturing.QuestTag switch {
            "NurturingPumpkinDevil" => "18101804",
            "NurturingGhostCats" => "19101804",
            _ => "",
        };

        if (string.IsNullOrEmpty(contentId)) {
            Logger.Warning("Unknown event tag {0} for nurturing mail", nurturing.QuestTag);
            return null;
        }

        var mail = new Mail {
            ReceiverId = ownerId,
            Type = MailType.System,
            Content = contentId,
            SenderName = session.PlayerName,
        };

        mail = db.CreateMail(mail);
        if (mail is null) {
            Logger.Error("Failed to create mail for account {0}", session.AccountId);
            return null;
        }

        RewardItem rewardPlay = nurturing.RewardFeed;
        Item? rewardItem = session.Field?.ItemDrop.CreateItem(rewardPlay.ItemId, rarity: rewardPlay.Rarity, amount: rewardPlay.Amount);
        if (rewardItem is null) {
            Logger.Error("Failed to create the reward item for account {0} and item {1}", session.AccountId, rewardPlay.ItemId);
            return null;
        }

        Item? item = db.CreateItem(mail.Id, rewardItem);
        if (item is null) {
            Logger.Error("Failed to create item for mail {0} and item {1}", mail.Id, rewardPlay.ItemId);
            return null;
        }

        mail.Items.Add(item);
        return mail;
    }
}
