using Grpc.Core;
using Maple2.Server.World.Containers;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace Maple2.Server.World.Service;

public partial class WorldService : World.WorldBase {
    private readonly ChannelClientLookup channelClients;
    private readonly WorldServer worldServer;
    private readonly PlayerInfoLookup playerLookup;
    private readonly GuildLookup guildLookup;
    private readonly PartyLookup partyLookup;
    private readonly ClubLookup clubLookup;
    private readonly PartySearchLookup partySearchLookup;
    private readonly GroupChatLookup groupChatLookup;
    private readonly BlackMarketLookup blackMarketLookup;
    private readonly GlobalPortalLookup globalPortalLookup;
    private readonly FieldBossLookup fieldBossLookup;
    private readonly PlayerConfigLookUp playerConfigLookUp;
    private readonly ILogger logger = Log.Logger.ForContext<WorldService>();

    public WorldService(
        IMemoryCache tokenCache, WorldServer worldServer, ChannelClientLookup channelClients,
        PlayerInfoLookup playerLookup, GuildLookup guildLookup, PartyLookup partyLookup,
        PartySearchLookup partySearchLookup, GroupChatLookup groupChatLookup, BlackMarketLookup blackMarketLookup,
        ClubLookup clubLookup, GlobalPortalLookup globalPortalLookup, FieldBossLookup fieldBossLookup, PlayerConfigLookUp playerConfigLookUp
        ) {
        this.tokenCache = tokenCache;
        this.worldServer = worldServer;
        this.channelClients = channelClients;
        this.playerLookup = playerLookup;
        this.guildLookup = guildLookup;
        this.partyLookup = partyLookup;
        this.partySearchLookup = partySearchLookup;
        this.groupChatLookup = groupChatLookup;
        this.clubLookup = clubLookup;
        this.blackMarketLookup = blackMarketLookup;
        this.globalPortalLookup = globalPortalLookup;
        this.fieldBossLookup = fieldBossLookup;
        this.playerConfigLookUp = playerConfigLookUp;
    }

    public override Task<ChannelsResponse> Channels(ChannelsRequest request, ServerCallContext context) {
        var response = new ChannelsResponse();
        response.Channels.AddRange(channelClients.Keys);
        return Task.FromResult(response);
    }
}
