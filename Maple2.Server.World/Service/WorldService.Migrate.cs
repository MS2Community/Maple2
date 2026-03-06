using System.Net;
using System.Security.Cryptography;
using Grpc.Core;
using Maple2.Model.Error;
using Maple2.Server.Core.Constants;
using Microsoft.Extensions.Caching.Memory;

namespace Maple2.Server.World.Service;

public partial class WorldService {
    private readonly record struct TokenEntry(Server Server, long AccountId, long CharacterId, Guid MachineId, int Channel, int MapId, int PortalId, int RoomId, long OwnerId, MigrationType Type);

    // Duration for which a token remains valid.
    private static readonly TimeSpan AuthExpiry = TimeSpan.FromSeconds(30);

    private readonly IMemoryCache tokenCache;

    public override Task<MigrateOutResponse> MigrateOut(MigrateOutRequest request, ServerCallContext context) {
        ulong token = UniqueToken();

        switch (request.Server) {
            case Server.Login:
                var loginEntry = new TokenEntry(request.Server, request.AccountId, request.CharacterId, new Guid(request.MachineId), 0, 0, 0, 0, 0, MigrationType.Normal);
                worldServer.MarkMigrating(request.CharacterId, 45);
                tokenCache.Set(token, loginEntry, AuthExpiry);
                return Task.FromResult(new MigrateOutResponse {
                    IpAddress = Target.LoginIp.ToString(),
                    Port = Target.LoginPort,
                    Token = token,
                });
            case Server.Game:
                if (channelClients.Count == 0) {
                    throw new RpcException(new Status(StatusCode.Unavailable, $"No available game channels"));
                }

                // Channel selection priority:
                // 1) If the Game server specifies a channel, keep it (even for instanced content).
                //    This avoids routing players to a dedicated/invalid instanced channel (often 0),
                //    which can desync presence and cause "fake offline" (client shows 65535).
                // 2) Otherwise, if instanced content is requested, use an instanced channel if available.
                // 3) Fallback to the first available channel.

                int channel;
                if (request.HasChannel && channelClients.TryGetActiveEndpoint(request.Channel, out _)) {
                    channel = request.Channel;
                } else if (request.InstancedContent && channelClients.TryGetInstancedChannelId(out channel)) {
                    if (!channelClients.TryGetActiveEndpoint(channel, out _)) {
                        throw new RpcException(new Status(StatusCode.Unavailable, "No available instanced game channel"));
                    }
                } else {
                    channel = channelClients.FirstChannel();
                    if (channel == -1) {
                        throw new RpcException(new Status(StatusCode.Unavailable, "No available game channels"));
                    }
                }
                if (!channelClients.TryGetActiveEndpoint(channel, out IPEndPoint? endpoint)) {
                    throw new RpcException(new Status(StatusCode.Unavailable, $"Channel {channel} not found"));
                }

                var gameEntry = new TokenEntry(request.Server, request.AccountId, request.CharacterId, new Guid(request.MachineId), channel, request.MapId, request.PortalId, request.RoomId, request.OwnerId, request.Type);
                tokenCache.Set(token, gameEntry, AuthExpiry);
                return Task.FromResult(new MigrateOutResponse {
                    IpAddress = endpoint.Address.ToString(),
                    Port = endpoint.Port,
                    Token = token,
                    Channel = channel,
                });
            default:
                throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid server: {request.Server}"));
        }
    }

    public override Task<MigrateInResponse> MigrateIn(MigrateInRequest request, ServerCallContext context) {
        if (!tokenCache.TryGetValue(request.Token, out TokenEntry data)) {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid token"));
        }

        if (data.AccountId != request.AccountId) {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Invalid token for account"));
        }
        if (data.MachineId != new Guid(request.MachineId)) {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Mismatched machineId for account"));
        }

        tokenCache.Remove(request.Token);
        worldServer.ClearMigrating(data.CharacterId);
        return Task.FromResult(new MigrateInResponse {
            CharacterId = data.CharacterId,
            Channel = data.Channel,
            MapId = data.MapId,
            PortalId = data.PortalId,
            OwnerId = data.OwnerId,
            RoomId = data.RoomId,
            Type = data.Type,
        });
    }

    // Generates a 64-bit token that does not exist in cache.
    private ulong UniqueToken() {
        ulong token;
        do {
            token = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));
        } while (tokenCache.TryGetValue(token, out _));

        return token;
    }

    public override Task<FieldResponse> Field(FieldRequest request, ServerCallContext context) {
        switch (request.FieldCase) {
            case FieldRequest.FieldOneofCase.CreateDungeon:
                return Task.FromResult(CreateDungeon(request));
            case FieldRequest.FieldOneofCase.DestroyDungeon:
                return Task.FromResult(DestroyDungeon(request));
            default:
                return Task.FromResult(new FieldResponse { Error = (int) MigrationError.ok });
        }
    }

    private FieldResponse CreateDungeon(FieldRequest request) {
        if (!channelClients.TryGetClient(channelClients.FirstChannel(), out Channel.Service.Channel.ChannelClient? client)) {
            return new FieldResponse {
                Error = (int) MigrationError.s_move_err_no_server,
            };
        }

        return client.Field(request);
    }

    private FieldResponse DestroyDungeon(FieldRequest request) {
        if (!channelClients.TryGetClient(channelClients.FirstChannel(), out Channel.Service.Channel.ChannelClient? client)) {
            return new FieldResponse {
                Error = (int) MigrationError.s_move_err_no_server,
            };
        }

        return client.Field(request);
    }
}
