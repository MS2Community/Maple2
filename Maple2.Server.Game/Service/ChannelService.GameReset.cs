using Grpc.Core;

namespace Maple2.Server.Game.Service;

public partial class ChannelService {
    public override Task<GameResetResponse> GameReset(GameResetRequest request, ServerCallContext context) {
        switch (request.ResetCase) {
            case GameResetRequest.ResetOneofCase.Daily:
                return Task.FromResult(Daily());
            case GameResetRequest.ResetOneofCase.Weekly:
                return Task.FromResult(Weekly());
            case GameResetRequest.ResetOneofCase.Monthly:
                return Task.FromResult(Monthly());
            default:
                return Task.FromResult(new GameResetResponse());
        }
    }

    private GameResetResponse Daily() {
        server.DailyReset();
        return new GameResetResponse();
    }

    private GameResetResponse Weekly() {
        server.WeeklyReset();
        return new GameResetResponse();
    }

    private GameResetResponse Monthly() {
        server.MonthlyReset();
        return new GameResetResponse();
    }
}

