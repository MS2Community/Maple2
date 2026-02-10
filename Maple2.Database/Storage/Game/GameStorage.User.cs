using System.Text.Json;
using Maple2.Database.Extensions;
using Maple2.Database.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Config;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using Account = Maple2.Model.Game.Account;
using Character = Maple2.Model.Game.Character;
using SkillMacro = Maple2.Model.Game.SkillMacro;
using SkillBook = Maple2.Model.Game.SkillBook;
using SkillTab = Maple2.Model.Game.SkillTab;
using SkillPoint = Maple2.Model.Game.SkillPoint;
using Wardrobe = Maple2.Model.Game.Wardrobe;
using GameEventUserValue = Maple2.Model.Game.GameEventUserValue;
using Home = Maple2.Model.Game.Home;
using HomeLayout = Maple2.Database.Model.HomeLayout;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request : IPlayerInfoProvider {
        public Account? GetAccount(long accountId) {
            return Context.Account.Find(accountId);
        }

        public Account? GetAccount(string username) {
            return Context.Account
                .FirstOrDefault(account => account.Username == username);
        }

        public Account? GetAccountByCharacterName(string name) {
            long accountId = Context.Character.Where(character => character.Name.ToLower() == name.ToLower())
                .Select(character => character.AccountId)
                .FirstOrDefault();

            return accountId == 0 ? null : GetAccount(accountId);
        }

        public bool VerifyPassword(long accountId, string password) {
            Model.Account? account = Context.Account.Find(accountId);
#if DEBUG
            if (string.IsNullOrEmpty(account?.Password)) {
                return true;
            }
#endif
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            return account != null && BCrypt.Net.BCrypt.Verify(password, account.Password);
        }

        public bool UpdateMachineId(long accountId, Guid machineId) {
            Model.Account? account = Context.Account.Find(accountId);
            if (account == null) {
                return false;
            }
            account.MachineId = machineId;
            Context.Account.Update(account);

            return Context.TrySaveChanges();
        }

        public (Account?, IList<Character>?) ListCharacters(long accountId) {
            Model.Account? model = Context.Account
                .Include(account => account.Characters)
                .FirstOrDefault(account => account.Id == accountId);
            if (model == null) {
                return (null, null);
            }

            IList<Character>? characters = model.Characters?.Select<Model.Character, Character>(c => c).ToList();
            if (characters != null) {
                foreach (Character character in characters) {
                    character.AchievementInfo = GetAchievementInfo(accountId, character.Id);
                }
            }

            return (model, characters);
        }

        public void SetAllCharacterToOffline() {
            Context.Database.ExecuteSqlRaw("UPDATE `character` SET Channel = -1");
        }

        //  If accountId is specified, only characters for the account will be returned.
        public Character? GetCharacter(long characterId, long accountId = -1) {
            if (accountId < 0) {
                Character? characterFind = Context.Character.Find(characterId);
                if (characterFind != null) {
                    characterFind.AchievementInfo = GetAchievementInfo(accountId, characterId);
                    characterFind.MarriageInfo = GetMarriageInfo(characterId);
                }
                return characterFind;
            }

            // Limit character fetching to those owned by account.
            Character? character = Context.Character.FirstOrDefault(character =>
                character.Id == characterId && character.AccountId == accountId);
            if (character != null) {
                character.AchievementInfo = GetAchievementInfo(accountId, characterId);
                character.MarriageInfo = GetMarriageInfo(characterId);
                Account? accountFind = Context.Account.Find(accountId);
                character.PremiumTime = accountFind?.PremiumTime ?? 0;
            }
            return character;
        }

        public long GetCharacterId(string name) {
            return Context.Character.Where(character => character.Name.ToLower() == name.ToLower())
                .Select(character => character.Id)
                .FirstOrDefault();
        }

        public PlayerInfo? GetPlayerInfo(long characterId) {
            var result = (from character in Context.Character where character.Id == characterId
                          join account in Context.Account on character.AccountId equals account.Id
                          join indoor in Context.UgcMap on
                              new {
                                  OwnerId = character.AccountId,
                                  Indoor = true,
                              } equals new {
                                  indoor.OwnerId,
                                  indoor.Indoor,
                              }
                          join outdoor in Context.UgcMap on
                              new {
                                  OwnerId = character.AccountId,
                                  Indoor = false,
                              } equals new {
                                  outdoor.OwnerId,
                                  outdoor.Indoor,
                              } into plot
                          from outdoor in plot.DefaultIfEmpty()
                          select new {
                              character,
                              indoor,
                              outdoor,
                              account.PremiumTime,
                              account.Permissions,
                          })
                .FirstOrDefault();
            if (result == null) {
                return null;
            }

            Tuple<long, string> guild = Context.GuildMember
                .Where(member => member.CharacterId == characterId)
                .Join(Context.Guild, member => member.GuildId, guild => guild.Id,
                    (member, guild) => new Tuple<long, string>(guild.Id, guild.Name))
                .FirstOrDefault() ?? new Tuple<long, string>(0, string.Empty);

            AchievementInfo achievementInfo = GetAchievementInfo(result.character.AccountId, result.character.Id);
            IList<long> clubs = ListClubs(result.character.Id);
            return BuildPlayerInfo(result.character, result.Permissions, result.indoor, result.outdoor, achievementInfo, guild.Item1, guild.Item2, result.PremiumTime, clubs);
        }

        public Home? GetHome(long ownerId) {
            Model.Home? model = Context.Home.Find(ownerId);
            if (model == null) {
                return null;
            }

            Home home = model;
            UgcMap[] ugcMaps = Context.UgcMap
                .Where(map => map.OwnerId == ownerId)
                .ToArray();
            PlotInfo? indoor = ToPlotInfo(ugcMaps.FirstOrDefault(map => map.Indoor));
            if (indoor == null) {
                Logger.LogError("Home does not have a indoor entry: {OwnerId}", ownerId);
                return null;
            }

            foreach (long layoutUid in model.Layouts) {
                HomeLayout? layout = GetHomeLayout(layoutUid);
                if (layout is null) {
                    Logger.LogError("Home layout not found: {LayoutUid}", layoutUid);
                    continue;
                }

                Maple2.Model.Game.HomeLayout? homeLayoutModel = ToHomeLayout(layout);
                if (homeLayoutModel == null) {
                    Logger.LogError("Failed to convert HomeLayout: {LayoutUid}", layoutUid);
                    continue;
                }

                home.Layouts.Add(homeLayoutModel);
            }

            foreach (long layoutUid in model.Blueprints) {
                HomeLayout? layout = GetHomeLayout(layoutUid);
                if (layout is null) {
                    Logger.LogError("Home layout not found: {LayoutUid}", layoutUid);
                    continue;
                }

                Maple2.Model.Game.HomeLayout? homeLayoutModel = ToHomeLayout(layout);
                if (homeLayoutModel == null) {
                    Logger.LogError("Failed to convert HomeLayout: {LayoutUid}", layoutUid);
                    continue;
                }

                home.Blueprints.Add(homeLayoutModel);
            }

            home.Indoor = indoor;
            home.Outdoor = ToPlotInfo(ugcMaps.FirstOrDefault(map => !map.Indoor));
            return home;
        }

        public (DateTime CharacterLastModified, DateTime AccountLastModified, DateTime UnlockLastModified)? GetLastModifiedTimestamps(long characterId) {
            var result = Context.Character.Where(character => character.Id == characterId)
                .Join(Context.Account, character => character.AccountId, account => account.Id, (character, account) => new {
                    character,
                    account,
                })
                .Join(Context.CharacterUnlock, @t => @t.character.Id, unlock => unlock.CharacterId, (@t, unlock) => new {
                    CharacterLastModified = @t.character.LastModified,
                    AccountLastModified = @t.account.LastModified,
                    UnlockLastModified = unlock.LastModified,
                })
                .AsNoTracking()
                .FirstOrDefault();
            if (result == null) {
                return null;
            }
            return (result.CharacterLastModified, result.AccountLastModified, result.UnlockLastModified);
        }

        // We pass in objectId only for Player initialization.
        public Player? LoadPlayer(long accountId, long characterId, int objectId, short channel) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            Model.Account? account = Context.Account.Find(accountId);
            if (account == null) {
                return null;
            }

            Model.Character? character = Context.Character.FirstOrDefault(character =>
                character.Id == characterId && character.AccountId == accountId);
            if (character == null) {
                return null;
            }

            account.Online = true;
            character.Channel = channel;

            Context.Account.Update(account);
            Context.Character.Update(character);
            Context.SaveChanges();

            Tuple<long, string> guild = Context.GuildMember
                .Where(member => member.CharacterId == characterId)
                .Join(Context.Guild, member => member.GuildId, guild => guild.Id,
                    (member, guild) => new Tuple<long, string>(guild.Id, guild.Name))
                .FirstOrDefault() ?? new Tuple<long, string>(0, string.Empty);

            List<Tuple<long, string>> clubs = Context.ClubMember
                .Where(member => member.CharacterId == characterId)
                .Join(Context.Club, member => member.ClubId, club => club.Id,
                    (member, club) => new Tuple<long, string>(club.Id, club.Name))
                .ToList();

            Home? home = GetHome(accountId);
            if (home == null) {
                return null;
            }

            var player = new Player(account, character, objectId) {
                Currency = new Currency {
                    Meret = account.Currency.Meret,
                    GameMeret = account.Currency.GameMeret,
                    Meso = character.Currency.Meso,
                    EventMeret = character.Currency.EventMeret,
                    ValorToken = character.Currency.ValorToken,
                    Treva = character.Currency.Treva,
                    Rue = character.Currency.Rue,
                    HaviFruit = character.Currency.HaviFruit,
                    ReverseCoin = character.Currency.ReverseCoin,
                    MentorToken = character.Currency.MentorToken,
                    MenteeToken = character.Currency.MenteeToken,
                    StarPoint = character.Currency.StarPoint,
                    MesoToken = account.Currency.MesoToken,
                },
                Unlock = Context.CharacterUnlock.Find(characterId),
                Home = home,
                Character = {
                    GuildId = guild.Item1,
                    GuildName = guild.Item2,
                    ClubIds = clubs.Select(club => club.Item1).ToList(),
                    AchievementInfo = GetAchievementInfo(accountId, characterId),
                    MarriageInfo = GetMarriageInfo(characterId),
                    PremiumTime = account.PremiumTime,
                },
            };

            return player;
        }

        public bool SavePlayer(Player player) {
            Logger.LogInformation("> Begin Save... {ContextId}:{CharacterId}", Context.ContextId, player.Character.Id);

            Model.Account account = player.Account;
            account.Currency = new AccountCurrency {
                Meret = player.Currency.Meret,
                GameMeret = player.Currency.GameMeret,
                MesoToken = player.Currency.MesoToken,
            };

            Model.Character character = player.Character;
            character.Currency = new CharacterCurrency {
                Meso = player.Currency.Meso,
                EventMeret = player.Currency.EventMeret,
                ValorToken = player.Currency.ValorToken,
                Treva = player.Currency.Treva,
                Rue = player.Currency.Rue,
                HaviFruit = player.Currency.HaviFruit,
                ReverseCoin = player.Currency.ReverseCoin,
                MentorToken = player.Currency.MentorToken,
                MenteeToken = player.Currency.MenteeToken,
                StarPoint = player.Currency.StarPoint,
            };

            Model.Account? dbAccount = Context.Account.Find(account.Id);
            if (dbAccount == null) {
                return false;
            }
            account.Password = dbAccount.Password;

            Context.Update(account);
            Context.Update(character);

            CharacterUnlock unlock = player.Unlock;
            unlock.CharacterId = character.Id;
            Context.Update(unlock);

            bool saved = false;
            int attempt = 0;
            const int maxAttempts = 5;

            while (!saved && attempt < maxAttempts) {
                try {
                    attempt++;
                    Context.SaveChanges();
                    saved = true;
                } catch (DbUpdateConcurrencyException ex) {
                    Logger.LogWarning("> Concurrency conflict (attempt {Attempt}) for CharacterId={CharacterId}", attempt, player.Character.Id);
                    foreach (EntityEntry entry in ex.Entries) {
                        string entityName = entry.Metadata.ClrType.Name;
                        if (entry.Entity is not Model.Account && entry.Entity is not Model.Character && entry.Entity is not CharacterUnlock) {
                            // Intentionally re-throw for unsupported entity types as fail-fast behavior during development.
                            // SavePlayer only handles concurrency conflicts for Account, Character, and CharacterUnlock.
                            // If other entities are unexpectedly involved, this indicates a logic error that should be caught immediately.
                            Logger.LogInformation("  Unsupported concurrency entity {EntityName}, rethrowing.", entityName);
                            throw;
                        }

                        PropertyValues? databaseValues = entry.GetDatabaseValues();
                        if (databaseValues == null) {
                            Logger.LogInformation("  Entity {EntityName} appears deleted in DB. Aborting save.", entityName);
                            return false;
                        }
                        PropertyValues proposedValues = entry.CurrentValues;

                        Logger.LogWarning("  Diff for {EntityName}:", entityName);
                        foreach (IProperty property in proposedValues.Properties) {
                            if (property.IsConcurrencyToken) {
                                object? originalValue = entry.OriginalValues[property];
                                object? currentValue = proposedValues[property];
                                object? databaseValue2 = databaseValues[property];
                                Logger.LogError("    {PropertyName}: original='{S}' current='{FormatValue1}' db='{S1}' <concurrency token>", property.Name, FormatValue(originalValue), FormatValue(currentValue), FormatValue(databaseValue2));
                                continue;
                            }
                            if (property.Name.Equals("Password", StringComparison.OrdinalIgnoreCase)) {
                                continue;
                            }

                            object? proposedValue = proposedValues[property];
                            object? databaseValue = databaseValues[property];

                            // Handle CreationTime as immutable: always trust database value and suppress logging
                            if (property.Name.Equals("CreationTime", StringComparison.OrdinalIgnoreCase)) {
                                if (proposedValue is DateTime propCt && databaseValue is DateTime dbCt) {
                                    // If they differ only by fractional seconds / timezone, normalize by taking db value
                                    if (propCt != dbCt) {
                                        proposedValues[property] = dbCt;
                                    }
                                } else if (databaseValue != null) {
                                    proposedValues[property] = databaseValue; // non-DateTime edge case
                                }
                                continue; // don't log CreationTime differences
                            }

                            if (property.Name.Contains("CreationTime", StringComparison.OrdinalIgnoreCase) &&
                                proposedValue is DateTime pvDt && pvDt == default &&
                                databaseValue is DateTime dbDt && dbDt != default) {
                                proposedValues[property] = databaseValue;
                                continue;
                            }

                            if (IsJsonStructurallyEqual(property.Name, proposedValue, databaseValue)) {
                                // Logger.LogWarning($"    {property.Name}: proposed and db are structurally equal JSON. proposed='{FormatValue(proposedValue)}' db='{FormatValue(databaseValue)}'");
                                continue;
                            }

                            if (!Equals(proposedValue, databaseValue)) {
                                Logger.LogInformation("    {PropertyName}: proposed='{S}' db='{FormatValue1}'", property.Name, FormatValue(proposedValue), FormatValue(databaseValue));
                            }
                        }

                        entry.OriginalValues.SetValues(databaseValues);
                    }
                } catch (Exception ex) {
                    Logger.LogError("> Save failed (non-concurrency) CharacterId={CharacterId} attempt={Attempt}\n{Exception}", player.Character.Id, attempt, ex);
                    return false;
                }
            }
            if (!saved) {
                Logger.LogError("> Save failed after {MaxAttempts} attempts CharacterId={CharacterId}", maxAttempts, player.Character.Id);
                return false;
            }

            // get updated values after save
            (DateTime CharacterLastModified, DateTime AccountLastModified, DateTime UnlockLastModified)? newPlayer = GetLastModifiedTimestamps(character.Id);
            if (newPlayer == null) {
                Logger.LogError("> Save succeeded but failed to fetch updated timestamps CharacterId={CharacterId}", player.Character.Id);
                return false;
            }
            player.Account.LastModified = newPlayer.Value.AccountLastModified;
            player.Character.LastModified = newPlayer.Value.CharacterLastModified;
            player.Unlock.LastModified = newPlayer.Value.UnlockLastModified;

            Logger.LogInformation("> Save complete {ContextId}:{CharacterId}", Context.ContextId, player.Character.Id);
            return true;
        }

        // Added helper methods for JSON diff suppression & formatting
        private static readonly HashSet<string> JsonNoiseProperties = new(StringComparer.OrdinalIgnoreCase) {
            "Cooldown",
            "Currency",
            "Experience",
            "Mastery",
            "Profile",
        };

        private static bool IsJsonStructurallyEqual(string propertyName, object? proposed, object? database) {
            if (!JsonNoiseProperties.Contains(propertyName)) return false;
            if (proposed == null && database == null) return true;
            if (proposed == null || database == null) return false;
            try {
                string p = JsonSerializer.Serialize(proposed);
                string d = JsonSerializer.Serialize(database);
                return string.Equals(p, d, StringComparison.Ordinal);
            } catch { return false; }
        }

        private static string FormatValue(object? value) {
            if (value == null) return "<null>";
            if (value is DateTime dt) return dt.ToString("O");
            Type t = value.GetType();
            if (t.IsPrimitive || value is string) return value.ToString() ?? string.Empty;
            return t.Name;
        }

        public bool SaveCharacter(Character character) {
            Context.Character.Update(character);
            return Context.TrySaveChanges();
        }

        public (IList<KeyBind>? KeyBinds, IList<QuickSlot[]>? HotBars, List<SkillMacro>?, List<Wardrobe>?, List<int>? FavoriteStickers, List<long>? FavoriteDesigners,
            IDictionary<LapenshardSlot, int>? Lapenshards, int InstantRevivalCount, int ExplorationProgress, IDictionary<AttributePointSource, int>?,
            IDictionary<BasicAttribute, int>?, SkillPoint? SkillPoint, IDictionary<int, int>? GatheringCounts, IDictionary<int, int>? GuideRecords, SkillBook?) LoadCharacterConfig(long characterId) {
            CharacterConfig? config = Context.CharacterConfig.Find(characterId);
            if (config == null) {
                return (null, null, null, null, null, null, null, 0, 0, null, null, null, null, null, null);
            }

            SkillBook? skillBook = config.SkillBook == null ? null : new SkillBook {
                MaxSkillTabs = config.SkillBook.MaxSkillTabs,
                ActiveSkillTabId = config.SkillBook.ActiveSkillTabId,
                SkillTabs = Context.SkillTab.Where(tab => tab.CharacterId == characterId)
                    .Select<Model.SkillTab, SkillTab>(tab => tab)
                    .ToList(),
            };

            Dictionary<GameEventUserValueType, GameEventUserValue> eventValues = Context.GameEventUserValue.Where(value => value.CharacterId == characterId)
                .Select<Model.GameEventUserValue, GameEventUserValue>(value => value)
                .ToDictionary(value => value.Type, value => value);

            var skillPoint = new SkillPoint();
            if (config.SkillPoint != null) {
                foreach (Model.SkillPoint point in config.SkillPoint) {
                    skillPoint[point.Source][point.Rank] = point.Points;
                }
            }

            return (
                config.KeyBinds,
                config.HotBars,
                config.SkillMacros?.Select<Model.SkillMacro, SkillMacro>(macro => macro).ToList(),
                config.Wardrobes?.Select<Model.Wardrobe, Wardrobe>(wardrobe => wardrobe).ToList(),
                config.FavoriteStickers?.Select(stickers => stickers).ToList(),
                config.FavoriteDesigners?.Select(designer => designer).ToList(),
                config.Lapenshards,
                config.InstantRevivalCount,
                config.ExplorationProgress,
                config.StatPoints,
                config.StatAllocation,
                skillPoint,
                config.GatheringCounts,
                config.GuideRecords,
                skillBook
            );
        }

        public bool SaveCharacterConfig(
            long characterId,
            IList<KeyBind> keyBinds,
            IList<QuickSlot[]> hotBars,
            IEnumerable<SkillMacro> skillMacros,
            IEnumerable<Wardrobe> wardrobes,
            IList<int> favoriteStickers,
            IList<long> favoriteDesigners,
            IDictionary<LapenshardSlot, int> lapenshards,
            int instantRevivalCount,
            int explorationProgress,
            StatAttributes.PointAllocation allocation,
            StatAttributes.PointSources statSources,
            SkillPoint skillPoint,
            IDictionary<int, int> gatheringCounts,
            IDictionary<int, int> guideRecords,
            SkillBook skillBook) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            CharacterConfig? config = Context.CharacterConfig.Find(characterId);
            if (config == null) {
                return false;
            }

            config.KeyBinds = keyBinds;
            config.HotBars = hotBars;
            config.SkillMacros = skillMacros.Select<SkillMacro, Model.SkillMacro>(macro => macro).ToList();
            config.Wardrobes = wardrobes.Select<Wardrobe, Model.Wardrobe>(wardrobe => wardrobe).ToList();
            config.FavoriteStickers = favoriteStickers;
            config.FavoriteDesigners = favoriteDesigners;
            config.Lapenshards = lapenshards;
            config.InstantRevivalCount = instantRevivalCount;
            config.ExplorationProgress = explorationProgress;
            config.StatAllocation = allocation.Attributes.ToDictionary(
                attribute => attribute,
                attribute => allocation[attribute]);
            config.StatPoints = statSources.Points;
            config.SkillPoint = skillPoint.Points.SelectMany(point => point.Value.Ranks.Select(rankPoint => new Model.SkillPoint {
                Source = point.Key,
                Rank = rankPoint.Key,
                Points = rankPoint.Value,
            }))
                .ToList();
            config.GatheringCounts = gatheringCounts;
            config.GuideRecords = guideRecords;
            config.SkillBook = new Model.SkillBook {
                MaxSkillTabs = skillBook.MaxSkillTabs,
                ActiveSkillTabId = skillBook.ActiveSkillTabId,
            };
            Context.CharacterConfig.Update(config);

            foreach (SkillTab skillTab in skillBook.SkillTabs) {
                Model.SkillTab model = skillTab;
                model.CharacterId = characterId;
                Context.SkillTab.Update(model);
            }

            return Context.TrySaveChanges();
        }

        #region Create
        public Account CreateAccount(Account account, string password) {
            Model.Account model = account;
            model.Id = 0;
            model.Password = BCrypt.Net.BCrypt.HashPassword(password, 13);
#if DEBUG
            model.Currency = new AccountCurrency {
                Meret = 9_999_999,
            };
            model.Permissions = AdminPermissions.Admin.ToString();
#endif
            Context.Account.Add(model);
            Context.SaveChanges(); // Exception if failed.

            Context.Home.Add(new Home {
                AccountId = model.Id,
            });
            Context.UgcMap.Add(new UgcMap {
                OwnerId = model.Id,
                MapId = Constant.DefaultHomeMapId,
                Indoor = true,
                Number = Constant.DefaultHomeNumber,
            });
            Context.SaveChanges(); // Exception if failed.

            return model;
        }

        public Character? CreateCharacter(Character character) {
            Model.Character model = character;
            model.Id = 0;
            model.Channel = -1;
#if DEBUG
            model.Currency = new CharacterCurrency {
                Meso = 999999999,
            };
#endif
            Context.Character.Add(model);
            return Context.TrySaveChanges() ? model : null;
        }

        public bool InitNewCharacter(long characterId, Unlock unlock) {
            CharacterUnlock model = unlock;
            model.CharacterId = characterId;
            Context.CharacterUnlock.Add(model);

            SkillTab? defaultTab = CreateSkillTab(characterId, new SkillTab("Build 1") {
                Id = characterId,
            });
            if (defaultTab == null) {
                return false;
            }

            var config = new CharacterConfig {
                CharacterId = characterId,
                SkillBook = new Model.SkillBook {
                    MaxSkillTabs = 1,
                    ActiveSkillTabId = defaultTab.Id,
                },
            };
            Context.CharacterConfig.Add(config);

            return Context.TrySaveChanges();
        }

        public SkillTab? CreateSkillTab(long characterId, SkillTab skillTab) {
            Model.SkillTab model = skillTab;
            model.CharacterId = characterId;
            Context.SkillTab.Add(model);
            return Context.TrySaveChanges() ? model : null;
        }
        #endregion

        #region Delete
        public bool UpdateDelete(long accountId, long characterId, long time) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            Model.Character? model = Context.Character.FirstOrDefault(character =>
                character.Id == characterId && character.AccountId == accountId);
            if (model == null) {
                return false;
            }

            model.DeleteTime = time.FromEpochSeconds();
            Context.Update(model);
            return Context.TrySaveChanges();
        }

        public bool DeleteCharacter(long accountId, long characterId) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            Model.Character? character = Context.Character.FirstOrDefault(character =>
                character.Id == characterId && character.AccountId == accountId);
            if (character == null) {
                return false;
            }

            Context.Remove(character);
            return Context.TrySaveChanges();
        }
        #endregion
    }

}
