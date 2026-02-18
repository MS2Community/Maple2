using System.Diagnostics;
using Maple2.Database.Extensions;
using Maple2.Database.Model;
using Maple2.Model.Common;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Z.EntityFramework.Plus;
using Home = Maple2.Model.Game.Home;
using InteractCube = Maple2.Model.Game.InteractCube;
using HomeLayout = Maple2.Model.Game.HomeLayout;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        public IList<Plot> LoadPlotsForMap(int mapId, long ownerId = -1) {
            IQueryable<UgcMap> query;
            if (ownerId >= 0) {
                query = Context.UgcMap.Include(map => map.Cubes)
                    .Where(map => map.MapId == mapId && map.OwnerId == ownerId);
            } else {
                query = Context.UgcMap.Include(map => map.Cubes)
                    .Where(map => map.MapId == mapId && map.MapId != Constant.DefaultHomeMapId);
            }

            return query.AsEnumerable()
                .ToList() // ToList before Select so 'This MySqlConnection is already in use.' exception doesn't occur.
                .Select(ToPlot)
                .Where(plot => plot != null)
                .ToList()!;
        }

        public IList<PlotCube> LoadCubesForOwner(long ownerId) {
            List<PlotCube> plotCubes = Context.UgcMap.Where(map => map.OwnerId == ownerId)
                .Join(Context.UgcMapCube, ugcMap => ugcMap.Id, cube => cube.UgcMapId, (ugcMap, cube) => cube)
                .AsEnumerable()
                .Select(ToPlotCube)
                .Where(cube => cube != null)
                .ToList()!;
            foreach (PlotCube cube in plotCubes) {
                if (cube.Interact?.Metadata.Nurturing is null) continue;

                cube.Interact!.Nurturing = GetNurturing(ownerId, cube.ItemId, cube.Interact.Metadata.Nurturing);
            }

            return plotCubes;
        }

        public PlotInfo? BuyPlot(string characterName, long ownerId, PlotInfo plot, TimeSpan days) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            UgcMap? ugcMap = Context.UgcMap.FirstOrDefault(map => map.Id == plot.Id && !map.Indoor);
            if (ugcMap == null) {
                return null;
            }

            Debug.Assert(ugcMap.MapId == plot.MapId && ugcMap.Number == plot.Number && ugcMap.ApartmentNumber == plot.ApartmentNumber);
            if (ugcMap.OwnerId != 0 || ugcMap.ExpiryTime >= DateTime.Now) {
                return null;
            }

            ugcMap.OwnerId = ownerId;
            ugcMap.ExpiryTime = DateTime.UtcNow + days;
            ugcMap.Name = characterName;
            Context.UgcMap.Update(ugcMap);
            Context.UgcMapCube.Where(cube => cube.UgcMapId == ugcMap.Id).Delete();

            return Context.TrySaveChanges() ? ToPlotInfo(ugcMap) : null;
        }

        public PlotInfo? ExtendPlot(PlotInfo plot, TimeSpan days) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            UgcMap? model = Context.UgcMap.Find(plot.Id);
            if (model == null) {
                return null;
            }

            Debug.Assert(model.MapId == plot.MapId && model.Number == plot.Number && model.ApartmentNumber == plot.ApartmentNumber);
            if (model.ExpiryTime < DateTime.Now) {
                return null;
            }

            model.ExpiryTime += days;
            Context.UgcMap.Update(model);

            return Context.TrySaveChanges() ? ToPlotInfo(model) : null;
        }

        public PlotInfo? ForfeitPlot(long ownerId, PlotInfo plot) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            UgcMap? model = Context.UgcMap.Find(plot.Id);
            if (model == null || model.OwnerId != ownerId) {
                return null;
            }

            Debug.Assert(model.MapId == plot.MapId && model.Number == plot.Number && model.ApartmentNumber == plot.ApartmentNumber);
            if (model.ExpiryTime < DateTime.Now) {
                return null;
            }

            model.OwnerId = 0;
            model.Name = string.Empty;
            model.ExpiryTime = DateTimeOffset.UtcNow;
            Context.UgcMapCube.Where(cube => cube.UgcMapId == model.Id).Delete();
            Context.UgcMap.Update(model);

            return Context.TrySaveChanges() ? ToPlotInfo(model) : null;
        }

        public PlotInfo? GetSoonestPlotFromExpire() {
            IQueryable<UgcMap> maps = Context.UgcMap.Where(map => map.ExpiryTime > DateTimeOffset.MinValue && !map.Indoor);
            foreach (UgcMap map in maps) {
                if (map.OwnerId == 0) {
                    map.ExpiryTime = map.ExpiryTime.Add(Constant.UgcHomeSaleWaitingTime);
                }
            }
            UgcMap? model = maps.OrderBy(map => map.ExpiryTime).FirstOrDefault();

            return model == null ? null : ToPlotInfo(model);
        }

        public List<PlotInfo> GetPlotsToExpire() {
            // Get all plots with expiry time <= now and owner >= 0 (not unowned or pending).
            List<UgcMap> expired = Context.UgcMap
                .Where(map => map.OwnerId >= 0 && map.ExpiryTime <= DateTimeOffset.UtcNow && map.ExpiryTime > DateTimeOffset.MinValue && !map.Indoor)
                .ToList();

            return expired.Select(ToPlotInfo).Where(p => p != null).ToList()!;
        }

        public void SetPlotPending(long plotId) {
            // Set expiry time to now
            UgcMap? ugcMap = Context.UgcMap.Find(plotId);
            if (ugcMap == null) return;

            ugcMap.ExpiryTime = DateTimeOffset.UtcNow;
            ugcMap.OwnerId = 0;
            ugcMap.Name = string.Empty;

            Context.UgcMap.Update(ugcMap);
            Context.TrySaveChanges();
        }

        public void SetPlotOpen(long plotId) {
            UgcMap? model = Context.UgcMap.Find(plotId);
            if (model == null) return;

            model.ExpiryTime = DateTimeOffset.MinValue;

            Context.UgcMap.Update(model);
            Context.TrySaveChanges();
        }

        public Plot? GetOutdoorPlotInfo(int plotNumber, int mapId) {
            UgcMap? model = Context.UgcMap.Include(x => x.Cubes).SingleOrDefault(map => map.MapId == mapId && map.Number == plotNumber && !map.Indoor);
            if (model == null) {
                return null;
            }

            return ToPlot(model);
        }

        public bool SaveHome(Home home) {
            Model.Home model = home;
            Context.Home.Update(model);
            if (!Context.TrySaveChanges()) {
                return false;
            }

            home.LastModified = model.LastModified.ToEpochSeconds();
            return true;
        }

        public bool SavePlotInfo(PlotInfo plotInfo) {
            UgcMap? model = Context.UgcMap.Find(plotInfo.Id);
            if (model == null) {
                return false;
            }

            model.OwnerId = plotInfo.OwnerId;
            model.MapId = plotInfo.MapId;
            model.Number = plotInfo.Number;
            model.ApartmentNumber = plotInfo.ApartmentNumber;
            model.ExpiryTime = plotInfo.ExpiryTime.FromEpochSeconds();
            model.Name = plotInfo.Name;
            Context.UgcMap.Update(model);

            return Context.TrySaveChanges();
        }

        public bool SavePlotInfoName(long plotInfoId, string name) {
            UgcMap? model = Context.UgcMap.Find(plotInfoId);
            if (model == null) {
                return false;
            }

            model.Name = name;
            Context.UgcMap.Update(model);

            return Context.TrySaveChanges();
        }

        public ICollection<PlotCube>? SaveCubes(PlotInfo plotInfo, IEnumerable<PlotCube> cubes) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            var results = new List<UgcMapCube>();
            var updated = new HashSet<long>();
            foreach (PlotCube cube in cubes) {
                UgcMapCube model = cube;
                model.UgcMapId = plotInfo.Id;
                if (model.Id >= Constant.FurnishingBaseId) {
                    model.Id = 0; // This needs to be auto-generated.
                    results.Add(model);
                    Context.UgcMapCube.Add(model);
                } else {
                    updated.Add(model.Id);
                    results.Add(model);
                    Context.UgcMapCube.Update(model);
                }
            }
            foreach (UgcMapCube cube in Context.UgcMapCube.Where(cube => cube.UgcMapId == plotInfo.Id)) {
                if (!updated.Contains(cube.Id)) {
                    Context.UgcMapCube.Remove(cube);
                }
            }

            if (!Context.TrySaveChanges()) {
                return null;
            }

            PlotCube[] plotCubes = results
                .Select(ToPlotCube)
                .Where(cube => cube != null)
                .ToArray()!;
            foreach (PlotCube cube in plotCubes) {
                if (cube.Interact?.Metadata.Nurturing is null) continue;

                cube.Interact!.Nurturing = GetNurturing(plotInfo.OwnerId, cube.ItemId, cube.Interact.Metadata.Nurturing);
            }

            return plotCubes;
        }

        public PlotCube? CreateCube(PlotInfo plotInfo, PlotCube cube) {
            try {
                Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

                UgcMapCube model = cube;
                model.UgcMapId = plotInfo.Id;

                Context.UgcMapCube.Add(model);

                if (!Context.TrySaveChanges()) {
                    return null;
                }

                PlotCube? plotCube = ToPlotCube(model);
                if (plotCube?.Interact?.Metadata.Nurturing is not null) {
                    plotCube.Interact.Nurturing = GetNurturing(plotInfo.OwnerId, cube.ItemId, plotCube.Interact.Metadata.Nurturing);
                }

                return plotCube;
            } catch (Exception e) {
                Logger.LogError(e, "Failed to create cube for plot {PlotId} with item {ItemId}", plotInfo.Id, cube.ItemId);
                return null;
            }
        }

        public bool DeleteCube(PlotCube cube) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            UgcMapCube? model = Context.UgcMapCube.Find(cube.Id);
            if (model == null) {
                return false;
            }

            Context.UgcMapCube.Remove(model);
            return Context.TrySaveChanges();
        }

        public PlotCube? GetCube(long cubeId) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            UgcMapCube? model = Context.UgcMapCube.Find(cubeId);
            if (model == null) {
                return null;
            }

            return ToPlotCube(model);
        }

        public bool InitUgcMap(IEnumerable<UgcMapMetadata> maps) {
            HashSet<(int MapId, int Number)> existing = Context.UgcMap
                .Select(m => new { m.MapId, m.Number })
                .AsEnumerable()
                .Select(m => (m.MapId, m.Number))
                .ToHashSet();

            foreach (UgcMapMetadata map in maps) {
                if (map.Id == Constant.DefaultHomeMapId) {
                    continue;
                }

                foreach (UgcMapGroup group in map.Plots.Values) {
                    if (existing.Contains((map.Id, group.Number))) {
                        continue;
                    }

                    Context.UgcMap.Add(new UgcMap {
                        MapId = map.Id,
                        Number = group.Number,
                        ApartmentNumber = group.ApartmentNumber,
                    });
                }
            }

            return Context.TrySaveChanges();
        }

        private Plot? ToPlot(UgcMap? ugcMap) {
            if (ugcMap == null || !game.mapMetadata.TryGetUgc(ugcMap.MapId, out UgcMapMetadata? metadata)) {
                return null;
            }

            if (!metadata.Plots.TryGetValue(ugcMap.Number, out UgcMapGroup? group)) {
                return null;
            }

            if (!ValidateAndFixHomeExpiryTime(ugcMap)) {
                return null;
            }

            var plot = new Plot(group) {
                Id = ugcMap.Id,
                OwnerId = ugcMap.OwnerId,
                MapId = ugcMap.MapId,
                Number = ugcMap.Number,
                ApartmentNumber = 0,
                Name = ugcMap.Name,
                ExpiryTime = ugcMap.ExpiryTime.ToUnixTimeSeconds(),
            };

            if (ugcMap.Cubes == null) return plot;

            foreach (UgcMapCube cube in ugcMap.Cubes) {
                PlotCube? plotCube = ToPlotCube(cube);
                if (plotCube == null) {
                    continue;
                }

                if (plotCube.Interact?.Metadata.Nurturing is not null) {
                    plotCube.Interact.Nurturing = GetNurturing(ugcMap.OwnerId, cube.Interact!.ObjectCode, plotCube.Interact.Metadata.Nurturing);
                }

                plot.Cubes.Add(plotCube.Position, plotCube);
            }

            return plot;
        }

        private PlotInfo? ToPlotInfo(UgcMap? ugcMap) {
            if (ugcMap == null || !game.mapMetadata.TryGetUgc(ugcMap.MapId, out UgcMapMetadata? metadata)) {
                return null;
            }

            if (!metadata.Plots.TryGetValue(ugcMap.Number, out UgcMapGroup? group)) {
                return null;
            }

            if (!ValidateAndFixHomeExpiryTime(ugcMap)) {
                return null;
            }

            return new PlotInfo(group) {
                Id = ugcMap.Id,
                OwnerId = ugcMap.OwnerId,
                MapId = ugcMap.MapId,
                Number = ugcMap.Number,
                Name = ugcMap.Name,
                ApartmentNumber = 0,
                ExpiryTime = ugcMap.ExpiryTime.ToUnixTimeSeconds(),
            };
        }

        private HomeLayout? ToHomeLayout(Model.HomeLayout? model) {
            if (model == null) {
                return null;
            }

            List<PlotCube> cubes = model.Cubes.Select(ToPlotCube)
                .Where(cube => cube != null)
                .ToList()!;

            return new HomeLayout(model.Uid, model.Id, model.Name, model.Area, model.Height, model.Timestamp, cubes);
        }

        // Converts model to interact cube if possible, otherwise returns null.
        private InteractCube? ToInteractCube(Model.InteractCube? model) {
            if (model == null) {
                return null;
            }

            return game.functionCubeMetadata.TryGet(model.ObjectCode, out FunctionCubeMetadata? metadata) ? model.Convert(metadata, model.NoticeSettings, model.PortalSettings) : null;
        }

        private PlotCube? ToPlotCube(HomeLayoutCube? model) {
            if (model == null) {
                return null;
            }

            if (!game.itemMetadata.TryGet(model.ItemId, out ItemMetadata? itemMetadata)) {
                return null;
            }

            return new PlotCube(itemMetadata, model.Id, model.Template) {
                Position = new Vector3B(model.X, model.Y, model.Z),
                Rotation = model.Rotation,
                Interact = ToInteractCube(model.Interact),
                Type = PlotCube.CubeType.Construction,
            };
        }

        private PlotCube? ToPlotCube(UgcMapCube? model) {
            if (model == null) {
                return null;
            }

            if (!game.itemMetadata.TryGet(model.ItemId, out ItemMetadata? itemMetadata)) {
                return null;
            }

            return new PlotCube(itemMetadata, model.Id, model.Template) {
                Position = new Vector3B(model.X, model.Y, model.Z),
                Rotation = model.Rotation,
                Interact = ToInteractCube(model.Interact),
                Type = PlotCube.CubeType.Construction,
            };
        }

        private bool ValidateAndFixHomeExpiryTime(UgcMap ugcMap) {
            if (!string.IsNullOrEmpty(ugcMap.Name) && ugcMap.MapId is Constant.DefaultHomeMapId && ugcMap.ExpiryTime != Home.HomeExpiryTime) {
                Logger.LogError("Plot {Id} for {OwnerId} is initialized but it's ExpiryTime is not set to Home.HomeExpiryTime. Why?", ugcMap.Id, ugcMap.OwnerId);

                ugcMap.ExpiryTime = Home.HomeExpiryTime;
                Context.UgcMap.Update(ugcMap);
                return Context.TrySaveChanges();
            }
            return true;
        }
    }
}
