using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Serilog;

namespace Maple2.Server.Game.PacketHandlers;

public class HomeActionHandler : FieldPacketHandler {
    private static readonly ILogger HomeActionLogger = Log.Logger.ForContext<HomeActionHandler>();

    public override RecvOp OpCode => RecvOp.HomeAction;

    private enum Command : byte {
        Smite = 1,
        Kick = 2,
        Survey = 5,
        ChangePortalSettings = 6,
        UpdateBallCoord = 7,
        ChangeNoticeSettings = 12,
        SendConfigurableSettings = 13,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Survey:
                HandleSurvey(session, packet);
                break;
            case Command.UpdateBallCoord:
                HandleUpdateBallCoord(session, packet);
                break;
            case Command.ChangePortalSettings:
                HandleChangePortalSettings(session, packet);
                break;
            case Command.ChangeNoticeSettings:
                HandleChangeNoticeSettings(session, packet);
                break;
            case Command.SendConfigurableSettings:
                HandleConfigurableSettings(session, packet);
                break;
        }
    }

    private static void HandleSurvey(GameSession session, IByteReader packet) {
        if (session.Field is not HomeFieldManager homeField) {
            return;
        }
        packet.ReadByte();
        packet.ReadLong(); // character id
        long surveyId = packet.ReadLong();
        byte responseIndex = packet.ReadByte();

        string playerName = session.PlayerName;

        HomeSurvey? homeSurvey = homeField.HomeSurvey;
        if (homeSurvey == null || homeSurvey.Id != surveyId) {
            return;
        }

        KeyValuePair<string, List<string>> option = homeSurvey.Options.ElementAtOrDefault(responseIndex);
        if (option.Key == null) {
            return;
        }

        if (!homeSurvey.Started || homeSurvey.Ended || option.Value.Contains(playerName) || !homeSurvey.AvailableCharacters.Contains(playerName)) {
            return;
        }

        homeSurvey.AvailableCharacters.Remove(playerName);
        option.Value.Add(playerName);

        session.Send(HomeActionPacket.SurveyAnswer(playerName));
        homeSurvey.Answers++;

        if (homeSurvey.Answers < homeSurvey.MaxAnswers) {
            return;
        }

        session.Field.Broadcast(HomeActionPacket.SurveyEnd(homeSurvey));
        homeSurvey.End();
    }

    private enum BallUpdateCommand : byte {
        Move = 2,
        Hit = 3,
    };

    private void HandleUpdateBallCoord(GameSession session, IByteReader packet) {
        BallUpdateCommand mode = packet.Read<BallUpdateCommand>();
        int objectId = packet.ReadInt();
        Vector3 coord = packet.Read<Vector3>();
        Vector3 velocity = packet.Read<Vector3>();

        FieldGuideObject? guideObject = session.GuideObject;
        if (guideObject?.Value is not BallGuideObject || guideObject.ObjectId != objectId) {
            Logger.Warning("Ball not found");
            return;
        }

        guideObject.Position = coord;

        switch (mode) {
            case BallUpdateCommand.Move:
                Vector3 velocity2 = packet.Read<Vector3>();

                session.Field?.Broadcast(HomeActionPacket.UpdateBall(guideObject, velocity, velocity2), session);
                break;
            case BallUpdateCommand.Hit:
                session.Field?.Broadcast(HomeActionPacket.HitBall(guideObject, velocity));
                break;
        }
    }

    private void HandleChangePortalSettings(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        packet.ReadByte();
        Vector3B coord = packet.Read<Vector3B>();

        Plot? plot = session.Housing.GetFieldPlot();
        if (plot == null) {
            return;
        }

        if (!plot.Cubes.TryGetValue(coord, out PlotCube? cube)) {
            Logger.Warning("Cube not found at {0}", coord);
            return;
        }

        if (cube.Interact?.PortalSettings is null) {
            Logger.Warning("Cube does not have portal settings at {0}", coord);
            return;
        }

        cube.Interact.PortalSettings.PortalName = packet.ReadUnicodeString();
        cube.Interact.PortalSettings.Method = (PortalActionType) packet.ReadByte();
        cube.Interact.PortalSettings.Destination = (CubePortalDestination) packet.ReadByte();
        cube.Interact.PortalSettings.DestinationTarget = packet.ReadUnicodeString();

        foreach (FieldPortal portal in session.Field.GetPortals()) {
            session.Field.RemovePortal(portal.ObjectId);
        }

        List<PlotCube> cubePortals = plot.Cubes.Values
            .Where(x => x.ItemId is Constant.InteriorPortalCubeId && x.Interact?.PortalSettings is not null)
            .ToList();

        foreach (PlotCube cubePortal in cubePortals) {
            FieldPortal? fieldPortal = session.Field.SpawnCubePortal(cubePortal);
            if (fieldPortal == null) {
                continue;
            }
            session.Field.Broadcast(PortalPacket.Add(fieldPortal));
        }

        session.Housing.SaveHome();
    }

    private void HandleChangeNoticeSettings(GameSession session, IByteReader packet) {
        bool editing = packet.ReadBool();
        Vector3B coord = packet.Read<Vector3B>();

        Plot? plot = session.Housing.GetFieldPlot();
        if (plot == null) {
            return;
        }

        if (!plot.Cubes.TryGetValue(coord, out PlotCube? cube)) {
            Logger.Warning("Cube not found at {0}", coord);
            return;
        }

        if (cube.Interact?.NoticeSettings is null) {
            Logger.Warning("Cube does not have notice settings at {0}", coord);
            return;
        }

        cube.Interact.NoticeSettings.Notice = packet.ReadUnicodeString();
        cube.Interact.NoticeSettings.Distance = packet.ReadByte();

        session.Field?.Broadcast(HomeActionPacket.SendCubeNoticeSettings(cube, editing: false));

        if (HousingFunctionFurnitureRegistry.IsSmartComputer(cube)) {
            string result = HousingFunctionFurnitureRegistry.ApplyComputerScript(session, cube);
            session.Send(HomeActionPacket.HostAlarm(result));
        }

        session.Housing.SaveHome();
    }

    private void HandleConfigurableSettings(GameSession session, IByteReader packet) {
        var coord = packet.Read<Vector3B>();

        Plot? plot = session.Housing.GetFieldPlot();
        if (plot == null) {
            return;
        }

        if (!plot.Cubes.TryGetValue(coord, out PlotCube? cube)) {
            Logger.Warning("Cube not found at {0}", coord);
            return;
        }

        HomeActionLogger.Information("HomeAction.SendConfigurableSettings coord={Coord} cubeId={CubeId} itemId={ItemId} hasNotice={HasNotice} hasPortal={HasPortal} isSmartComputer={IsSmartComputer}",
            coord, cube.Id, cube.ItemId, cube.Interact?.NoticeSettings is not null, cube.Interact?.PortalSettings is not null, HousingFunctionFurnitureRegistry.IsSmartComputer(cube));

        if (HousingFunctionFurnitureRegistry.IsSmartComputer(cube)) {
            if (!HousingFunctionFurnitureRegistry.OpenSmartComputerEditor(session, cube)) {
                Logger.Warning("Failed to open smart computer editor at {0}", coord);
            }
            return;
        }

        if (cube.Interact?.PortalSettings is not null) {
            List<string> otherPortalsNames = plot.Cubes.Values
                .Where(x => x.ItemId is Constant.InteriorPortalCubeId && x.Id != cube.Id)
                .Select(x => x.Interact!.PortalSettings?.PortalName ?? string.Empty)
                .ToList();

            session.Send(HomeActionPacket.SendCubePortalSettings(cube, otherPortalsNames));
        } else if (cube.Interact?.NoticeSettings is not null) {
            session.Send(HomeActionPacket.SendCubeNoticeSettings(cube, editing: true));
        } else {
            Logger.Warning("Cube is not configurable at {0}", coord);
        }
    }
}
