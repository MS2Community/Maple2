using Grpc.Core;
using Maple2.Database.Model;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Enum; // added for BanType reference
using Serilog;
using ILogger = Serilog.ILogger;

// TODO: Move this to a Global server
// ReSharper disable once CheckNamespace
namespace Maple2.Server.Global.Service;

public partial class GlobalService : Global.GlobalBase {
    private readonly GameStorage gameStorage;

    private readonly ILogger logger = Log.Logger.ForContext<GlobalService>();

    public GlobalService(GameStorage gameStorage) {
        this.gameStorage = gameStorage;
    }

    public override Task<LoginResponse> Login(LoginRequest request, ServerCallContext context) {

        if (string.IsNullOrWhiteSpace(request.Username)) {
            return Task.FromResult(new LoginResponse {
                Code = LoginResponse.Types.Code.ErrorId,
                Message = "Invalid Id",
            });
        }

        if (string.IsNullOrWhiteSpace(request.Password)) {
            return Task.FromResult(new LoginResponse {
                Code = LoginResponse.Types.Code.ErrorPassword,
                Message = "Invalid Password.",
            });
        }

        // Normalize username
        string username = request.Username.Trim().ToLower();
        string password = request.Password.Trim();
        var machineId = new Guid(request.MachineId);
        string clientIp = request.ClientIp;

        using GameStorage.Request db = gameStorage.Context();

        // Hardware and ip ban pre-check (no account context yet, prevents auto-registration for banned hardware)
        (bool IsBanned, Ban? Ban) hwStatus = db.GetBanStatus(null, clientIp, machineId);
        if (hwStatus is { IsBanned: true, Ban: not null }) {
            return Task.FromResult(new LoginResponse {
                Code = LoginResponse.Types.Code.Restricted,
                Message = hwStatus.Ban.Reason,
                AccountId = 0,
                BanStart = new DateTimeOffset(hwStatus.Ban.CreatedAt).ToUnixTimeSeconds(),
                BanExpiry = new DateTimeOffset(hwStatus.Ban.ExpiresAt).ToUnixTimeSeconds(),
            });
        }

        Account? account = db.GetAccount(username);
        if (account is null) {
            if (Constant.AutoRegister) {
                account = new Account {
                    Username = username,
                    MachineId = machineId,
                };

                db.BeginTransaction();
                account = db.CreateAccount(account, password);
                if (account == null) {
                    throw new RpcException(new Status(StatusCode.Internal, "Failed to create account."));
                }

                db.Commit();
                return Task.FromResult(new LoginResponse { AccountId = account.Id });
            }

            return Task.FromResult(new LoginResponse {
                Code = LoginResponse.Types.Code.ErrorId,
                Message = "Account not found",
            });
        }

        bool isPasswordValid = db.VerifyPassword(account.Id, password);
        if (!isPasswordValid) {
            return Task.FromResult(new LoginResponse {
                Code = LoginResponse.Types.Code.ErrorPassword,
                Message = "Incorrect Password.",
            });
        }

        if (account.MachineId == null) {
            db.UpdateMachineId(account.Id, machineId);
        }
        else if (account.MachineId != machineId) {
            logger.Warning("MachineId mismatch for account {AccountId}", account.Id);
            if (Constant.BlockLoginWithMismatchedMachineId) {
                return Task.FromResult(new LoginResponse {
                    Code = LoginResponse.Types.Code.BlockNexonSn,
                    Message = "MachineId mismatch",
                });
            }
        }

        (bool IsBanned, Ban? Ban) status = db.GetBanStatus(account.Id, clientIp, machineId);
        if (status is { IsBanned: true, Ban: not null }) {
            return Task.FromResult(new LoginResponse {
                Code = LoginResponse.Types.Code.Restricted,
                Message = status.Ban.Reason,
                AccountId = account.Id,
                BanStart = new DateTimeOffset(status.Ban.CreatedAt).ToUnixTimeSeconds(),
                BanExpiry = new DateTimeOffset(status.Ban.ExpiresAt).ToUnixTimeSeconds(),
            });
        }

        return Task.FromResult(new LoginResponse { AccountId = account.Id });
    }
}
