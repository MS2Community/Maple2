using Maple2.Database.Context;
using Maple2.File.Flat;
using Maple2.File.Flat.maplestory2library;
using Maple2.File.Flat.physxmodellibrary;
using Maple2.File.Flat.standardmodellibrary;
using Maple2.File.Ingest.Helpers;
using Maple2.File.Parser.MapXBlock;
using Maple2.Model.Common;
using Maple2.Model.Game.Field;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;
using Maple2.Tools.VectorMath;
using System.IO.Compression;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata.FieldEntity;

namespace Maple2.File.Ingest.Mapper;

public class MapDataMapper : TypeMapper<MapDataMetadata> {
    private const float BLOCK_SIZE = (float) Constant.BlockSize;
    private const float HALF_BLOCK = 0.5f * BLOCK_SIZE;

    private readonly HashSet<string> xBlocks;
    private readonly XBlockParser parser;

    private readonly StatsTracker mapByteStats = new StatsTracker();
    private readonly StatsTracker mapGridByteStats = new StatsTracker();
    private readonly StatsTracker mapGridBytePercentStats = new StatsTracker();
    private readonly StatsTracker mapXStats = new StatsTracker();
    private readonly StatsTracker mapYStats = new StatsTracker();
    private readonly StatsTracker mapZStats = new StatsTracker();
    private readonly StatsTracker alignedStats = new StatsTracker();
    private readonly StatsTracker alignedTrimmedStats = new StatsTracker();
    private readonly StatsTracker unalignedStats = new StatsTracker();
    private readonly HashSet<string> invalidLlids = [];
    private readonly HashSet<uint> missingLlids = [];

    public MapDataMapper(MetadataContext db, XBlockParser parser) {
        xBlocks = db.MapMetadata.Select(metadata => metadata.XBlock).ToHashSet();

        this.parser = parser;
    }

    private class StatsTracker {

        public ulong MinValue = ulong.MaxValue;
        public ulong MaxValue;
        public ulong AvgValue {
            get {
                if (Entries == 0) return 0;
                return TotalValue / Entries;
            }
        }
        public ulong Entries;
        public ulong TotalValue;

        public StatsTracker() { }

        public void AddValue(ulong value) {
            ++Entries;
            TotalValue += value;
            MinValue = ulong.Min(MinValue, value);
            MaxValue = ulong.Max(MaxValue, value);
        }
    }

    private FieldAccelerationStructure ParseMapEntities(IEnumerable<IMapEntity> entities) {
        var gridAlignedEntities = new Dictionary<Vector3S, List<FieldEntity>>();
        var unalignedEntities = new List<FieldEntity>();
        var minIndex = new Vector3S(short.MaxValue, short.MaxValue, short.MaxValue);
        var maxIndex = new Vector3S(short.MinValue, short.MinValue, short.MinValue);
        var transform = new Transform();

        int vibrateObjectId = 0;

        foreach (IMapEntity entity in entities) {

            if (entity is not IPlaceable placeable) {
                continue;
            }

            transform.Transformation = Matrix4x4.Identity;
            transform.Position = placeable.Position;
            transform.RotationAnglesDegrees = placeable.Rotation;
            transform.Scale = placeable.Scale;

            Vector3 position = (1 / BLOCK_SIZE) * (placeable.Position - new Vector3(0, 0, HALF_BLOCK)); // offset to round to nearest
            var nearestCubeIndex = new Vector3S((short) Math.Floor(position.X + 0.5f), (short) Math.Floor(position.Y + 0.5f), (short) Math.Floor(position.Z + 0.5f));
            Vector3 voxelPosition = BLOCK_SIZE * new Vector3(nearestCubeIndex.X, nearestCubeIndex.Y, nearestCubeIndex.Z);
            var entityBounds = new BoundingBox3();

            bool isHexId = entity.EntityId.Length == 32;

            for (int i = 0; isHexId && i < entity.EntityId.Length; ++i) {
                isHexId = entity.EntityId[i].IsHexDigit();
            }

            FieldEntity? fieldEntity;
            var entityId = new FieldEntityId(0, 0, string.Empty);

            if (isHexId) {
                entityId = FieldEntityId.FromString(entity.EntityId);
            }

            switch (entity) {
                /*
                    PhysXProp                               | WhiteboxCube
                    PhysXProp, MS2MapProperties, MS2Vibrate | PhysXCube, DoesMakeTok
                    MS2MapProperties                        | PhysXCube
                    MS2MapProperties, MS2Vibrate            | None
                    MS2MapProperties, MS2Breakable          | PhysXCube, NxCube, BothCube, OnlyNxCube
                    MS2Breakable                            | NxCube
                 */
                case IMS2Breakable breakable:
                    continue; // intentionally skip breakables. these are dynamic so should be handled at run time
                case IPhysXWhitebox whitebox:
                    entityBounds.Min = -new Vector3(whitebox.ShapeDimensions.X, whitebox.ShapeDimensions.Y, 0.5f * whitebox.ShapeDimensions.Z);
                    entityBounds.Max = new Vector3(whitebox.ShapeDimensions.X, whitebox.ShapeDimensions.Y, 0.5f * whitebox.ShapeDimensions.Z);
                    fieldEntity = new FieldBoxColliderEntity(
                        Id: entityId,
                        Position: placeable.Position - new Vector3(0, 0, 0.5f * whitebox.ShapeDimensions.Z),
                        Rotation: placeable.Rotation,
                        Scale: placeable.Scale,
                        Bounds: BoundingBox3.Transform(entityBounds, transform.Transformation),
                        Size: whitebox.ShapeDimensions,
                        IsWhiteBox: true,
                        IsFluid: false,
                        MapAttribute: MapAttribute.none);
                    break;
                case IMesh mesh:
                    if (entity is IMS2Vibrate { Enabled: true } vibrate) {
                        entityBounds.Min = -new Vector3(HALF_BLOCK, HALF_BLOCK, 0);
                        entityBounds.Max = new Vector3(HALF_BLOCK, HALF_BLOCK, BLOCK_SIZE);

                        fieldEntity = new FieldVibrateEntity(
                            Id: entityId,
                            Position: placeable.Position,
                            Rotation: placeable.Rotation,
                            Scale: placeable.Scale,
                            Bounds: BoundingBox3.Transform(entityBounds, transform.Transformation),
                            BreakDefense: vibrate.brokenDefence,
                            BreakTick: vibrate.brokenTick,
                            VibrateIndex: vibrateObjectId++);

                        break;
                    }

                    if (entity is IMS2CubeProp cube && cube.CubeSalableGroup != 0) {
                        entityBounds.Min = -new Vector3(HALF_BLOCK, HALF_BLOCK, 0);
                        entityBounds.Max = new Vector3(HALF_BLOCK, HALF_BLOCK, BLOCK_SIZE);
                        fieldEntity = new FieldSellableTile(
                            Id: entityId,
                            Position: placeable.Position,
                            Rotation: placeable.Rotation,
                            Scale: placeable.Scale,
                            Bounds: BoundingBox3.Transform(entityBounds, transform.Transformation),
                            SellableGroup: cube.CubeSalableGroup
                        );

                        break;
                    }

                    bool isFluid = false;
                    var attribute = MapAttribute.none;
                    if (entity is IMS2MapProperties meshMapProperties) {
                        if (meshMapProperties.DisableCollision) {
                            continue;
                        }

                        isFluid = meshMapProperties.CubeType == "Fluid";
                        attribute = Enum.TryParse(meshMapProperties.MapAttribute, out MapAttribute mapAttribute) ? mapAttribute : MapAttribute.none;
                        if (meshMapProperties.GeneratePhysX) {
                            var meshPhysXDimension = new Vector3(BLOCK_SIZE, BLOCK_SIZE, BLOCK_SIZE);

                            if (meshMapProperties.GeneratePhysXDimension != Vector3.Zero) {
                                meshPhysXDimension = meshMapProperties.GeneratePhysXDimension;
                            }

                            entityBounds.Min = -new Vector3(0.5f * meshPhysXDimension.X, 0.5f * meshPhysXDimension.Y, 0);
                            entityBounds.Max = new Vector3(0.5f * meshPhysXDimension.X, 0.5f * meshPhysXDimension.Y, meshPhysXDimension.Z);

                            fieldEntity = new FieldBoxColliderEntity(
                                Id: entityId,
                                Position: placeable.Position,
                                Rotation: placeable.Rotation,
                                Scale: placeable.Scale,
                                Bounds: BoundingBox3.Transform(entityBounds, transform.Transformation),
                                Size: meshPhysXDimension,
                                IsWhiteBox: false,
                                IsFluid: isFluid,
                                MapAttribute: attribute);

                            break;
                        }
                    }

                    if (mesh.NifAsset.Length < 9 || !mesh.NifAsset[..9].Equals("urn:llid:", StringComparison.CurrentCultureIgnoreCase)) {
                        if (invalidLlids.Add(mesh.NifAsset)) {
                            Console.WriteLine($"Non llid NifAsset: '{mesh.NifAsset}'");
                        }

                        continue;
                    }

                    // require length of "urn:llid:XXXXXXXX"
                    if (mesh.NifAsset.Length < 9 + 8) {
                        continue;
                    }

                    uint llid = Convert.ToUInt32(mesh.NifAsset.Substring(mesh.NifAsset.LastIndexOf(':') + 1, 8), 16);

                    if (!NifParserHelper.nifBounds.TryGetValue(llid, out entityBounds)) {
                        if (missingLlids.Add(llid)) {
                            Console.WriteLine($"NIF with LLID {llid:X} not found");
                        }

                        continue;
                    }

                    if (isFluid && mesh is IMS2MapProperties fluidMapProperties) {
                        fieldEntity = new FieldFluidEntity(
                            Id: entityId,
                            Position: placeable.Position,
                            Rotation: placeable.Rotation,
                            Scale: placeable.Scale,
                            LiquidType: Enum.TryParse(fluidMapProperties.MapAttribute, out LiquidType liquidType) ? liquidType : LiquidType.none,
                            Bounds: BoundingBox3.Transform(entityBounds, transform.Transformation),
                            MeshLlid: llid,
                            IsShallow: false,
                            IsSurface: true,
                            MapAttribute: attribute);
                        break;
                    }

                    fieldEntity = new FieldMeshColliderEntity(
                        Id: entityId,
                        Position: placeable.Position,
                        Rotation: placeable.Rotation,
                        Scale: placeable.Scale,
                        Bounds: BoundingBox3.Transform(entityBounds, transform.Transformation),
                        MeshLlid: llid,
                        MapAttribute: attribute);
                    break;
                case IMS2MapProperties mapProperties: // GeneratePhysX
                    if (mapProperties.DisableCollision) {
                        continue;
                    }

                    if (!mapProperties.GeneratePhysX) {
                        continue;
                    }

                    var physXDimension = new Vector3(BLOCK_SIZE, BLOCK_SIZE, BLOCK_SIZE);

                    if (mapProperties.GeneratePhysXDimension != Vector3.Zero) {
                        physXDimension = mapProperties.GeneratePhysXDimension;
                    }

                    entityBounds.Min = -new Vector3(0.5f * physXDimension.X, 0.5f * physXDimension.Y, 0);
                    entityBounds.Max = new Vector3(0.5f * physXDimension.X, 0.5f * physXDimension.Y, physXDimension.Z);

                    fieldEntity = new FieldBoxColliderEntity(
                        Id: entityId,
                        Position: placeable.Position,
                        Rotation: placeable.Rotation,
                        Scale: placeable.Scale,
                        Bounds: BoundingBox3.Transform(entityBounds, transform.Transformation),
                        Size: physXDimension,
                        IsWhiteBox: false,
                        IsFluid: false,
                        MapAttribute: MapAttribute.none);
                    break;
                default:
                    continue;
            }

            entityBounds = BoundingBox3.Transform(entityBounds, transform.Transformation);

            var cellBounds = new BoundingBox3(
                min: voxelPosition - new Vector3(HALF_BLOCK, HALF_BLOCK, 0),
                max: voxelPosition + new Vector3(HALF_BLOCK, HALF_BLOCK, BLOCK_SIZE));

            if (!cellBounds.Contains(entityBounds, 1e-5f)) {
                // put in list for aabb tree
                unalignedEntities.Add(fieldEntity);

                continue;
            }

            // grid aligned
            minIndex = new Vector3S(Math.Min(minIndex.X, nearestCubeIndex.X), Math.Min(minIndex.Y, nearestCubeIndex.Y), Math.Min(minIndex.Z, nearestCubeIndex.Z));
            maxIndex = new Vector3S(Math.Max(maxIndex.X, nearestCubeIndex.X), Math.Max(maxIndex.Y, nearestCubeIndex.Y), Math.Max(maxIndex.Z, nearestCubeIndex.Z));

            if (!gridAlignedEntities.TryGetValue(nearestCubeIndex, out List<FieldEntity>? cellEntities)) {
                cellEntities = new List<FieldEntity>();
                gridAlignedEntities.Add(nearestCubeIndex, cellEntities);
            }

            cellEntities.Add(fieldEntity);
        }

        maxIndex += new Vector3S(0, 0, 1); // make room for potential spawn tiles

        var fieldData = new FieldAccelerationStructure();

        fieldData.AddEntities(gridAlignedEntities, minIndex, maxIndex, unalignedEntities, vibrateObjectId);

        return fieldData;
    }

    private static byte[] GetEmptyMap() {
        var mapData = new FieldAccelerationStructure();

        var writer = new ByteWriter();

        writer.WriteClass(mapData);

        return writer.ToArray();
    }

    protected override IEnumerable<MapDataMetadata> Map() {
        return parser.Parallel().Select(ParseMap).OrderBy(x => x.XBlock).ThenBy(x => x.Data.Length);
    }

    private MapDataMetadata ParseMap((string xblock, IEnumerable<IMapEntity> entities) map) {
        string xblock = map.xblock.ToLower();
        if (!xBlocks.Contains(xblock)) {
            return new MapDataMetadata(xblock, GetEmptyMap());
        }

        FieldAccelerationStructure mapData = ParseMapEntities(map.entities);

        var writer = new ByteWriter();

        writer.WriteClass(mapData);

        byte[] data = writer.ToArray();
        var dataStream = new MemoryStream();

        using (var dstream = new DeflateStream(dataStream, CompressionLevel.SmallestSize)) {
            dstream.Write(data, 0, data.Length);
        }

        data = dataStream.ToArray();

        lock (this) {
            mapByteStats.AddValue((ulong) data.LongLength);
            mapGridByteStats.AddValue(mapData.GridBytesWritten);
            mapGridBytePercentStats.AddValue((ulong) (10000 * (float) mapData.GridBytesWritten / data.LongLength));
            mapXStats.AddValue((ulong) mapData.GridSize.X);
            mapYStats.AddValue((ulong) mapData.GridSize.Y);
            mapZStats.AddValue((ulong) mapData.GridSize.Z);
            alignedStats.AddValue((ulong) mapData.AlignedEntities.Length);
            alignedTrimmedStats.AddValue((ulong) mapData.AlignedTrimmedEntities.Length);
            unalignedStats.AddValue((ulong) mapData.UnalignedEntities.Length);
        }

        return new MapDataMetadata(xblock, data);
    }

    public void ReportStats() {
        Console.WriteLine($"Total maps parsed: {mapByteStats.Entries.ToString().ColorBlue()}");
        Console.WriteLine($"Total bytes: {mapByteStats.TotalValue.ToString().ColorBlue()} ");
        Console.WriteLine($"Average map bytes: {mapByteStats.AvgValue.ToString().ColorBlue()} ");
        Console.WriteLine($"Largest map bytes: {mapByteStats.MaxValue.ToString().ColorBlue()} ");
        Console.WriteLine($"Largest map dimensions: {$"< {mapXStats.MaxValue}, {mapYStats.MaxValue}, {mapZStats.MaxValue} >".ColorBlue()} ");
        Console.WriteLine($"Average map dimensions: {$"< {mapXStats.AvgValue}, {mapYStats.AvgValue}, {mapZStats.AvgValue} >".ColorBlue()} ");
        Console.WriteLine($"Largest aligned entities: {alignedStats.MaxValue.ToString().ColorBlue()} ");
        Console.WriteLine($"Average aligned entities: {alignedStats.AvgValue.ToString().ColorBlue()} ");
        Console.WriteLine($"Largest trimmed aligned entities: {alignedTrimmedStats.MaxValue.ToString().ColorBlue()} ");
        Console.WriteLine($"Average trimmed aligned entities: {alignedTrimmedStats.AvgValue.ToString().ColorBlue()} ");
        Console.WriteLine($"Largest unaligned entities: {unalignedStats.MaxValue.ToString().ColorBlue()} ");
        Console.WriteLine($"Average unaligned entities: {unalignedStats.AvgValue.ToString().ColorBlue()} ");
    }
}
