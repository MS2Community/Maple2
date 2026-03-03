using Maple2.Database.Storage;
using Maple2.Server.Game.Util.Sync;
using Serilog;

namespace Maple2.Server.Game.Service;

public partial class ChannelService : Channel.Service.Channel.ChannelBase {
    private readonly GameServer server;
    private readonly PlayerInfoStorage playerInfos;
    private readonly GameStorage gameStorage;
    private readonly TableMetadataStorage tableMetadata;
    private readonly ServerTableMetadataStorage serverTableMetadata;
    private readonly ILogger logger = Log.Logger.ForContext<ChannelService>();

    public ChannelService(GameServer server, PlayerInfoStorage playerInfos, GameStorage gameStorage, ServerTableMetadataStorage serverTableMetadata, TableMetadataStorage tableMetadata, ItemMetadataStorage itemMetadata) {
        this.server = server;
        this.playerInfos = playerInfos;
        this.gameStorage = gameStorage;
        this.serverTableMetadata = serverTableMetadata;
        this.tableMetadata = tableMetadata;
    }
}
