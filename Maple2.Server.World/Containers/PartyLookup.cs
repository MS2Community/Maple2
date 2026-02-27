using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Storage;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;

namespace Maple2.Server.World.Containers;

public class PartyLookup : IDisposable {
    private readonly ChannelClientLookup channelClients;
    private readonly PlayerInfoLookup playerLookup;
    private readonly PartySearchLookup partySearchLookup;
    private ServerTableMetadataStorage serverTableMetadataStorage;


    private readonly ConcurrentDictionary<int, PartyManager> parties;
    private int nextPartyId = 1;

    public PartyLookup(ChannelClientLookup channelClients, PlayerInfoLookup playerLookup, PartySearchLookup partySearchLookup) {
        this.channelClients = channelClients;
        this.playerLookup = playerLookup;
        this.partySearchLookup = partySearchLookup;

        parties = new ConcurrentDictionary<int, PartyManager>();
    }

    public void InjectDependencies(ServerTableMetadataStorage serverTableMetadataStorage) {
        this.serverTableMetadataStorage = serverTableMetadataStorage;
    }

    public void Dispose() {
        foreach (PartyManager manager in parties.Values) {
            manager.Dispose();
        }

        if (partySearchLookup.TryGetByPartyId(nextPartyId, out PartySearchManager? partySearch)) {
            partySearch.Dispose();
        }
    }

    public bool TryGet(int partyId, [NotNullWhen(true)] out PartyManager? party) {
        return parties.TryGetValue(partyId, out party);
    }

    public bool TryGetByCharacter(long characterId, [NotNullWhen(true)] out PartyManager? party) {
        party = null;
        foreach (PartyManager manager in parties.Values) {
            if (manager.Party.Members.TryGetValue(characterId, out PartyMember? member)) {
                party = manager;
                return true;
            }
        }
        return false;
    }

    public PartyError Create(long leaderId, out int partyId) {
        partyId = nextPartyId++;

        PlayerInfo? leaderInfo = playerLookup.GetPlayerInfo(leaderId);
        if (leaderInfo == null) {
            return PartyError.s_party_err_not_found;
        }

        var party = new Party(partyId, leaderInfo.AccountId, leaderInfo.CharacterId, leaderInfo.Name);
        var manager = new PartyManager(party, serverTableMetadataStorage.ConstantsTable.PartyVoteReadyDurationSeconds) {
            ChannelClients = channelClients,
            PartyLookup = this,
        };

        if (!parties.TryAdd(partyId, manager)) {
            return PartyError.s_party_err_not_found;
        }

        return manager.Join(leaderInfo);
    }

    public PartyError Disband(int partyId) {
        if (!TryGet(partyId, out PartyManager? manager)) {
            return PartyError.s_party_err_not_found;
        }

        if (!parties.TryRemove(partyId, out manager)) {
            // Failed to remove party after validating.
            return PartyError.s_party_err_not_found;
        }
        manager.Dispose();

        return PartyError.none;
    }
}
