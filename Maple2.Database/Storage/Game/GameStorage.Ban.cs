namespace Maple2.Database.Storage;

using Maple2.Database.Model;
using Microsoft.EntityFrameworkCore;

public partial class GameStorage {
    public partial class Request {
        public long CreateBan(long accountId, string reason, TimeSpan? duration = null, string? details = null, string? ipAddress = null, Guid? machineId = null) {
            var ban = new Ban {
                AccountId = accountId,
                IpAddress = ipAddress,
                MachineId = machineId,
                Reason = reason,
                Details = details,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = duration == null ? DateTime.UtcNow.AddYears(99) : DateTime.UtcNow.Add(duration.Value),
            };
            Context.Ban.Add(ban);
            Context.SaveChanges();
            return ban.Id;
        }

        private IQueryable<Ban> ActiveBansQuery() {
            DateTime now = DateTime.UtcNow;
            return Context.Ban.AsNoTracking().Where(b => b.ExpiresAt > now);
        }

        private Ban? GetActiveAccountBan(long accountId) =>
            ActiveBansQuery()
                .Where(b => b.AccountId == accountId)
                .OrderByDescending(b => b.ExpiresAt)
                .FirstOrDefault();
        private Ban? GetActiveHardwareBan(Guid machineId) =>
            ActiveBansQuery()
                .Where(b => b.MachineId == machineId)
                .OrderByDescending(b => b.ExpiresAt)
                .FirstOrDefault();
        private Ban? GetActiveIpBan(string ipAddress) =>
            ActiveBansQuery()
                .Where(b => b.IpAddress == ipAddress)
                .OrderByDescending(b => b.ExpiresAt)
                .FirstOrDefault();

        // Precedence: Account > Hardware > IP
        public (bool IsBanned, Ban? Ban) GetBanStatus(long? accountId, string? ipAddress, Guid? machineId) {
            if (accountId.HasValue) {
                Ban? acc = GetActiveAccountBan(accountId.Value);
                if (acc != null) return (true, acc);
            }
            if (machineId.HasValue) {
                Ban? hw = GetActiveHardwareBan(machineId.Value);
                if (hw != null) return (true, hw);
            }
            if (!string.IsNullOrWhiteSpace(ipAddress)) {
                Ban? ip = GetActiveIpBan(ipAddress);
                if (ip != null) return (true, ip);
            }

            return (false, null);
        }

        // List all active bans tied to accountId (account-level, ip, hardware)
        public IEnumerable<(long Id, string Reason, DateTime ExpiresAt, string? Details, string? IpAddress, Guid? MachineId)> ListActiveBans(long accountId) {
            foreach (Ban b in ActiveBansQuery().Where(b => b.AccountId == accountId)) {
                yield return (b.Id, b.Reason, b.ExpiresAt, b.Details, b.IpAddress, b.MachineId);
            }
        }

        public int RemoveBansByIds(IEnumerable<long> banIds) {
            var ids = banIds.Distinct().ToArray();
            if (ids.Length == 0) return 0;
            var bans = Context.Ban.Where(b => ids.Contains(b.Id)).ToList();
            if (bans.Count == 0) return 0;
            Context.Ban.RemoveRange(bans);
            Context.SaveChanges();
            return bans.Count;
        }
    }
}
