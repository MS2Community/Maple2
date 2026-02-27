using System.Collections.Concurrent;
using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using Maple2.Server.Channel.Service;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.World.Containers;

public class PartyManager : IDisposable {
    public required ChannelClientLookup ChannelClients { get; init; }
    public required PartyLookup PartyLookup { get; init; }
    public readonly Party Party;
    private readonly ConcurrentDictionary<long, (string, DateTime)> pendingInvites;
    private readonly int partyVoteReadyDurationSeconds;

    public PartyManager(Party party, int partyVoteReadyDurationSeconds) {
        Party = party;
        pendingInvites = new ConcurrentDictionary<long, (string, DateTime)>();
        this.partyVoteReadyDurationSeconds = partyVoteReadyDurationSeconds;
    }

    public void Dispose() {
        Broadcast(new PartyRequest {
            Disband = new PartyRequest.Types.Disband(),
        });
    }

    public void Broadcast(PartyRequest request) {
        if (request.PartyId > 0 && request.PartyId != Party.Id) {
            throw new InvalidOperationException($"Broadcasting {request.PartyCase} for incorrect party: {request.PartyId} => {Party.Id}");
        }

        request.PartyId = Party.Id;
        foreach (IGrouping<short, PartyMember> group in Party.Members.Values.GroupBy(member => member.Info.Channel)) {
            if (!ChannelClients.TryGetClient(group.Key, out ChannelClient? client)) {
                continue;
            }

            request.ReceiverIds.Clear();
            request.ReceiverIds.AddRange(group.Select(member => member.Info.CharacterId));

            try {
                client.Party(request);
            } catch { }
        }
    }

    /// <summary>
    /// Differs from Broadcast as it only broadcasts to voters.
    /// </summary>
    private void BroadcastVote(PartyRequest request) {
        if (Party.Vote == null) {
            return;
        }
        if (request.PartyId > 0 && request.PartyId != Party.Id) {
            throw new InvalidOperationException($"Broadcasting {request.PartyCase} for incorrect party: {request.PartyId} => {Party.Id}");
        }

        request.PartyId = Party.Id;
        foreach (IGrouping<short, PartyMember> group in Party.Members.Values.Where(member => Party.Vote.Voters.Contains(member.CharacterId)).GroupBy(member => member.Info.Channel)) {
            if (!ChannelClients.TryGetClient(group.Key, out ChannelClient? client)) {
                continue;
            }

            request.ReceiverIds.Clear();
            request.ReceiverIds.AddRange(group.Select(member => member.Info.CharacterId).Where(member => Party.Vote.Voters.Contains(member)));

            try {
                client.Party(request);
            } catch { }
        }
    }

    public void FindNewLeader(long characterId) {
        PartyMember? newLeader = Party.Members.Values.FirstOrDefault(m => m.CharacterId != characterId && m.Info.Online);
        if (newLeader == null) {
            CheckForDisband();
            return;
        }
        UpdateLeader(characterId, newLeader.CharacterId);
    }

    public bool CheckForDisband() {
        if (Party.Members.Values.Count(member => member.Info.Online) < 2 ||
            Party.Members.Count <= 2) {
            PartyLookup.Disband(Party.Id);
            return true;
        }
        return false;
    }

    public PartyError Invite(long requestorId, PlayerInfo player) {
        if (!Party.Members.TryGetValue(requestorId, out PartyMember? requestor)) {
            return PartyError.s_party_err_not_exist;
        }
        bool isLeader = requestorId == Party.LeaderCharacterId;
        if (!isLeader) {
            return PartyError.s_party_err_not_chief;
        }
        if (Party.Members.Count >= Party.Capacity) {
            return PartyError.s_party_err_full;
        }
        if (Party.Members.ContainsKey(player.CharacterId)) {
            return PartyError.s_party_err_cannot_invite;
        }
        if (!ChannelClients.TryGetClient(player.Channel, out ChannelClient? client)) {
            return PartyError.s_party_err_cannot_invite;
        }

        try {
            pendingInvites[player.CharacterId] = (requestor.Name, DateTime.Now.AddSeconds(30));
            var request = new PartyRequest {
                PartyId = Party.Id,
                ReceiverIds = {
                    player.CharacterId,
                },
                Invite = new PartyRequest.Types.Invite {
                    SenderId = requestor.CharacterId,
                    SenderName = requestor.Name,
                },
            };

            PartyResponse response = client.Party(request);
            return (PartyError) response.Error;
        } catch (RpcException) {
            return PartyError.s_party_err_not_found;
        }
    }

    public PartyError Join(PlayerInfo info) {
        if (Party.Members.Count >= Party.Capacity) {
            return PartyError.s_party_err_full_limit_player;
        }
        if (Party.Members.ContainsKey(info.CharacterId)) {
            return PartyError.s_party_err_alreadyInvite;
        }

        var member = new PartyMember {
            PartyId = Party.Id,
            Info = info,
            JoinTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        Broadcast(new PartyRequest {
            AddMember = new PartyRequest.Types.AddMember {
                CharacterId = member.CharacterId,
                JoinTime = member.JoinTime,
            },
        });
        if (Party.Members.TryAdd(info.CharacterId, member) && Party.Search != null) {
            Party.Search.MemberCount++;
        }
        return PartyError.none;
    }

    public PartyError Kick(long requestorId, long characterId) {
        if (!Party.Members.TryGetValue(requestorId, out PartyMember? _)) {
            return PartyError.s_party_err_not_found;
        }
        if (requestorId != Party.LeaderCharacterId) {
            return PartyError.s_party_err_not_chief;
        }
        if (characterId == Party.LeaderCharacterId) {
            return PartyError.none;
        }
        if (!Party.Members.TryGetValue(characterId, out PartyMember? member)) {
            return PartyError.s_party_err_not_exist;
        }
        if (CheckForDisband()) {
            return PartyError.none;
        }

        Broadcast(new PartyRequest {
            RemoveMember = new PartyRequest.Types.RemoveMember {
                CharacterId = member.CharacterId,
                IsKicked = true,
            },
        });
        if (Party.Members.TryRemove(member.CharacterId, out _) && Party.Search != null) {
            Party.Search.MemberCount--;
        }
        return PartyError.none;
    }

    public PartyError Leave(long characterId) {
        if (!Party.Members.TryGetValue(characterId, out PartyMember? member)) {
            return PartyError.s_party_err_not_found;
        }

        if (characterId == Party.LeaderCharacterId) {
            FindNewLeader(characterId);
        }

        if (CheckForDisband()) {
            return PartyError.none;
        }

        Broadcast(new PartyRequest {
            RemoveMember = new PartyRequest.Types.RemoveMember {
                CharacterId = member.CharacterId,
            },
        });
        if (Party.Members.TryRemove(member.CharacterId, out _) && Party.Search != null) {
            Party.Search.MemberCount--;
        }
        return PartyError.none;
    }

    public PartyError UpdateLeader(long requestorId, long characterId) {
        if (!Party.Members.TryGetValue(requestorId, out PartyMember? requestor)) {
            return PartyError.s_party_err_not_found;
        }
        if (!Party.Members.TryGetValue(characterId, out PartyMember? member)) {
            return PartyError.s_party_err_not_found;
        }

        if (requestorId != Party.LeaderCharacterId) {
            return PartyError.s_party_err_not_chief;
        }

        Party.LeaderCharacterId = member.Info.CharacterId;
        Party.LeaderAccountId = member.Info.AccountId;
        Party.LeaderName = member.Info.Name;
        Broadcast(new PartyRequest {
            UpdateLeader = new PartyRequest.Types.UpdateLeader {
                CharacterId = characterId,
            },
        });

        if (Party.Search != null) {
            Party.Search.LeaderCharacterId = member.Info.CharacterId;
            Party.Search.LeaderAccountId = member.Info.AccountId;
            Party.Search.LeaderName = member.Info.Name;
        }

        return PartyError.none;
    }

    public void Disband(long requestorId) {
        int onlineCount = Party.Members.Values.Count(member => member.Info.Online);
        if (onlineCount == 0) {
            PartyLookup.Disband(Party.Id);
            return;
        }
        if (requestorId == Party.LeaderCharacterId) {
            FindNewLeader(requestorId);
        }
    }

    public string ConsumeInvite(long characterId) {
        foreach ((long id, (string name, DateTime expiryTime)) in pendingInvites) {
            // Remove any expired entries while iterating.
            if (expiryTime < DateTime.Now) {
                pendingInvites.Remove(id, out _);
                continue;
            }

            if (id == characterId) {
                pendingInvites.Remove(id, out _);
                return name;
            }
        }

        return string.Empty;
    }

    public PartyError StartReadyCheck(long requestorId) {
        if (!Party.Members.TryGetValue(requestorId, out PartyMember? requestor)) {
            return PartyError.s_party_err_not_found;
        }
        if (requestorId != Party.LeaderCharacterId) {
            return PartyError.s_party_err_not_chief;
        }

        Party.Vote = new PartyVote(PartyVoteType.ReadyCheck, Party.Members.Keys, requestorId);
        Broadcast(new PartyRequest {
            StartReadyCheck = new PartyRequest.Types.StartReadyCheck {
                CharacterId = requestorId,
            },
        });

        Task.Factory.StartNew(() => {
            Thread.Sleep(TimeSpan.FromSeconds(partyVoteReadyDurationSeconds));
            if (Party.Vote == null) {
                return;
            }
            int count = Party.Vote.Disapprovals.Count + Party.Vote.Approvals.Count;
            if (count >= Party.Vote.Voters.Count) {
                Party.Vote = null;
                return;
            }

            Broadcast(new PartyRequest {
                ExpiredVote = new PartyRequest.Types.ExpiredVote {
                    PartyId = Party.Id,
                },
            });

            Party.Vote = null;
        });

        return PartyError.none;
    }

    public PartyError ReadyCheckReply(long requestorId, bool reply) {
        if (Party.Vote == null) {
            return PartyError.s_party_err_not_found;
        }

        if (!Party.Members.TryGetValue(requestorId, out PartyMember? requestor)) {
            return PartyError.s_party_err_not_found;
        }

        if (Party.Vote.Approvals.Contains(requestorId) || Party.Vote.Disapprovals.Contains(requestorId)) {
            return PartyError.s_party_err_already_vote;
        }

        if (reply) {
            Party.Vote.Approvals.Add(requestorId);
        } else {
            Party.Vote.Disapprovals.Add(requestorId);
        }

        Broadcast(new PartyRequest {
            VoteReply = new PartyRequest.Types.VoteReply {
                CharacterId = requestorId,
                Reply = reply,
                PartyId = Party.Id,
            },
        });


        CheckEndVote();

        return PartyError.none;
    }

    private void CheckEndVote() {
        if (Party.Vote == null) {
            return;
        }

        switch (Party.Vote.Type) {
            case PartyVoteType.ReadyCheck:
                int votes = Party.Vote.Approvals.Count + Party.Vote.Disapprovals.Count;
                if (votes >= Party.Vote.Voters.Count) {
                    Broadcast(new PartyRequest {
                        EndVote = new PartyRequest.Types.EndVote {
                            PartyId = Party.Id,
                        },
                    });
                    Party.Vote = null;
                }
                break;
            case PartyVoteType.Kick:
                if (Party.Vote.Type == PartyVoteType.Kick &&
                    Party.Vote.Approvals.Count >= Party.Vote.VotesNeeded) {
                    Kick(Party.Vote.InitiatorId, Party.Vote.TargetMember!.CharacterId);

                    Broadcast(new PartyRequest {
                        EndVote = new PartyRequest.Types.EndVote {
                            PartyId = Party.Id,
                        },
                    });
                }
                break;
        }
    }

    public PartyError VoteKick(long requestorId, long targetId) {
        if (!Party.Members.ContainsKey(requestorId)) {
            return PartyError.s_party_err_not_found;
        }

        if (!Party.Members.TryGetValue(targetId, out PartyMember? target)) {
            return PartyError.s_party_err_not_found;
        }

        ICollection<long> voters = Party.Members.Keys.Where(member => member != targetId).ToList();
        Party.Vote = new PartyVote(PartyVoteType.Kick, voters, requestorId) {
            TargetMember = target,
        };

        BroadcastVote(new PartyRequest {
            PartyId = Party.Id,
            StartVoteKick = new PartyRequest.Types.StartVoteKick {
                CharacterId = requestorId,
                TargetId = targetId,
                ReceiverIds = { Party.Vote.Voters },
            },
        });

        Task.Factory.StartNew(() => {
            // TODO: The duration is wrong.
            Thread.Sleep(TimeSpan.FromSeconds(partyVoteReadyDurationSeconds));
            if (Party.Vote == null) {
                return;
            }

            Broadcast(new PartyRequest {
                ExpiredVote = new PartyRequest.Types.ExpiredVote {
                    PartyId = Party.Id,
                },
            });

            Party.Vote = null;
        });

        return PartyError.none;
    }

    public PartyError SetDungeon(long requestorId, int dungeonId, bool set, int dungeonRoomId = 0) {
        if (requestorId != Party.LeaderCharacterId) {
            return PartyError.s_party_err_not_chief;
        }

        Party.DungeonSet = set;
        Party.DungeonId = dungeonId;
        Party.DungeonLobbyRoomId = dungeonRoomId;
        Broadcast(new PartyRequest {
            SetDungeon = new PartyRequest.Types.SetDungeon {
                PartyId = Party.Id,
                DungeonId = dungeonId,
                DungeonRoomId = dungeonRoomId,
                Set = set,
            },
        });

        return PartyError.none;
    }
}
