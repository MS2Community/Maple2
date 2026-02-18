using Maple2.Database.Model;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        public DateTime GetLastDailyReset() {
            ServerInfo? dailyReset = Context.ServerInfo.Find("DailyReset");
            return dailyReset?.LastModified ?? CreateServerInfo("DailyReset");
        }

        public DateTime GetLastWeeklyReset() {
            ServerInfo? weeklyReset = Context.ServerInfo.Find("WeeklyReset");
            return weeklyReset?.LastModified ?? CreateServerInfo("WeeklyReset");
        }

        public DateTime GetLastMonthlyReset() {
            ServerInfo? monthlyReset = Context.ServerInfo.Find("MonthlyReset");
            return monthlyReset?.LastModified ?? CreateServerInfo("MonthlyReset");
        }

        private DateTime CreateServerInfo(string key) {
            var model = new ServerInfo {
                Key = key,
                LastModified = DateTime.Now,
            };
            Context.ServerInfo.Add(model);
            Context.SaveChanges();

            return model.LastModified;
        }

        public void DailyReset() {
            lock (Context) {
                ServerInfo serverInfo = Context.ServerInfo.Find("DailyReset")!;
                serverInfo.LastModified = DateTime.Now;
                Context.Update(serverInfo);
                Context.SaveChanges();

                Context.Database.ExecuteSqlRaw("UPDATE `account` SET `PrestigeExp` = `PrestigeCurrentExp`");
                Context.Database.ExecuteSqlRaw("UPDATE `account` SET `PrestigeLevelsGained` = DEFAULT");
                Context.Database.ExecuteSqlRaw("UPDATE `account` SET `PremiumRewardsClaimed` = DEFAULT");
                Context.Database.ExecuteSqlRaw("UPDATE `character-config` SET `GatheringCounts` = DEFAULT");
                Context.Database.ExecuteSqlRaw("UPDATE `character-config` SET `InstantRevivalCount` = 0");
                Context.Database.ExecuteSqlRaw("UPDATE `nurturing` SET `PlayedBy` = '[]'");
                Context.Database.ExecuteSqlRaw("UPDATE `home` SET `DecorationRewardTimestamp` = 0");
                Context.Database.ExecuteSqlRaw("UPDATE `character-shop-data` SET `RestockCount` = 0 WHERE `Interval` = 1");
                // Reset shop item stock purchased for daily shops
                Context.Database.ExecuteSqlRaw("UPDATE `character-shop-item-data` SET `StockPurchased` = 0 WHERE `ShopId` IN (SELECT `ShopId` FROM `character-shop-data` WHERE `Interval` = 1)");
                Context.Database.ExecuteSqlRaw("UPDATE `account` SET `MarketLimits` = JSON_SET(MarketLimits, '$.MesoListed', 0)");
                // Dungeon daily clears
                Context.Database.ExecuteSqlRaw("UPDATE `dungeon-record` SET `CurrentSubClears` = 0, `ExtraCurrentSubClears` = 0");
            }
        }

        public void WeeklyReset() {
            lock (Context) {
                ServerInfo? serverInfo = Context.ServerInfo.Find("WeeklyReset");
                if (serverInfo == null) {
                    serverInfo = new ServerInfo { Key = "WeeklyReset", LastModified = DateTime.Now };
                    Context.ServerInfo.Add(serverInfo);
                } else {
                    serverInfo.LastModified = DateTime.Now;
                    Context.Update(serverInfo);
                }
                Context.SaveChanges();

                Context.Database.ExecuteSqlRaw("UPDATE `guild-member` SET `WeeklyContribution` = 0");
                Context.Database.ExecuteSqlRaw("UPDATE `account` SET `PrestigeRewardsClaimed` = DEFAULT");
                Context.Database.ExecuteSqlRaw("UPDATE `character-shop-data` SET `RestockCount` = 0 WHERE `Interval` = 2");
                // Reset shop item stock purchased for weekly shops
                Context.Database.ExecuteSqlRaw("UPDATE `character-shop-item-data` SET `StockPurchased` = 0 WHERE `ShopId` IN (SELECT `ShopId` FROM `character-shop-data` WHERE `Interval` = 2)");
                // Dungeon weekly clears
                Context.Database.ExecuteSqlRaw("UPDATE `dungeon-record` SET `CurrentClears` = 0, `ExtraCurrentClears` = 0");
            }
        }

        public void MonthlyReset() {
            lock (Context) {
                ServerInfo? serverInfo = Context.ServerInfo.Find("MonthlyReset");
                if (serverInfo == null) {
                    serverInfo = new ServerInfo { Key = "MonthlyReset", LastModified = DateTime.Now };
                    Context.ServerInfo.Add(serverInfo);
                } else {
                    serverInfo.LastModified = DateTime.Now;
                    Context.Update(serverInfo);
                }
                Context.SaveChanges();

                Context.Database.ExecuteSqlRaw("UPDATE `account` SET `MarketLimits` = JSON_SET(MarketLimits, '$.MesoPurchased', 0)");
            }
        }
    }
}
