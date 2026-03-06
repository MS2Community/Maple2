using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maple2.Server.Game;
using Maple2.Server.Game.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maple2.Server.Game.Service;

/// <summary>
/// Periodically saves online player state so progress isn't lost if the server restarts.
/// 
/// Why here (Server.Game):
/// - The database/storage layer doesn't know which players are online.
/// - GameServer owns the live sessions, so it can safely iterate and call SessionSave().
/// </summary>
public sealed class AutoSaveService : BackgroundService {
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(60);

    private readonly GameServer gameServer;
    private readonly ILogger<AutoSaveService> logger;

    public AutoSaveService(GameServer gameServer, ILogger<AutoSaveService> logger) {
        this.gameServer = gameServer;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        // Small startup delay so we don't compete with initial login/boot work.
        try {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        } catch (OperationCanceledException) {
            return;
        }

        while (!stoppingToken.IsCancellationRequested) {
            try {
                int saved = 0;

                // Snapshot current sessions to avoid issues if the collection changes mid-iteration.
                GameSession[] sessions = gameServer.GetSessions().ToArray();
                foreach (GameSession session in sessions) {
                    if (stoppingToken.IsCancellationRequested) break;
                    if (session.Player == null) continue;

                    // SessionSave() is internally locked and already checks for null Player.
                    session.SessionSave();
                    saved++;
                }

                if (saved > 0) {
                    logger.LogInformation("[AutoSave] Saved {Count} online session(s).", saved);
                }
            } catch (OperationCanceledException) {
                // Normal shutdown.
            } catch (Exception ex) {
                logger.LogError(ex, "[AutoSave] Unexpected error while saving sessions.");
            }

            try {
                await Task.Delay(SaveInterval, stoppingToken);
            } catch (OperationCanceledException) {
                break;
            }
        }
    }
}
