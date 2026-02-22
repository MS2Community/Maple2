using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Model.Game;

public class Home : IByteSerializable {
    private const byte HOME_PERMISSION_COUNT = 9;

    public long AccountId { get; init; }
    public long LastModified { get; set; }

    public byte Area { get; private set; }
    public byte Height { get; private set; }

    public byte PlannerArea { get; private set; }
    public byte PlannerHeight { get; private set; }
    public bool IsPlanner => Indoor.PlotMode is PlotMode.DecorPlanner or PlotMode.BlueprintPlanner;

    public int CurrentArchitectScore { get; set; }
    public int ArchitectScore { get; set; }

    // Interior Settings
    public HomeBackground Background { get; private set; }
    public HomeLighting Lighting { get; private set; }
    public HomeCamera Camera { get; private set; }
    public string? Passcode { get; set; }
    public readonly IDictionary<HomePermission, HomePermissionSetting> Permissions = new Dictionary<HomePermission, HomePermissionSetting>();

    public long DecorationExp { get; set; }
    public long DecorationLevel { get; set; }
    public long DecorationRewardTimestamp { get; set; }
    public List<int> InteriorRewardsClaimed { get; set; } = [];
    public List<HomeLayout> Layouts { get; set; } = [];
    public List<HomeLayout> Blueprints { get; set; } = [];

    private string message = string.Empty;
    public string Message {
        get => message;
        set {
            if (!string.IsNullOrWhiteSpace(value)) {
                message = value;
            }
        }
    }

    public PlotInfo Indoor { get; set; } = null!; // Required, when getting a home, this should be set always. Setting to null! to please the compiler.
    public PlotInfo? Outdoor { get; set; }

    public string Name => Outdoor?.Name ?? Indoor.Name;
    public int PlotMapId => Outdoor?.MapId ?? 0;
    public int PlotNumber => Outdoor?.Number ?? 0;
    public int ApartmentNumber => Outdoor?.ApartmentNumber ?? 0;
    public long PlotExpiryTime => Outdoor?.ExpiryTime ?? (string.IsNullOrEmpty(Name) ? 0 : Indoor.ExpiryTime); // If the name is empty, the plot is not setup yet.
    public PlotState State => Outdoor?.State ?? PlotState.Open;

    public bool IsHomeSetup => !string.IsNullOrEmpty(Name);

    public static DateTimeOffset HomeExpiryTime = new DateTimeOffset(2900, 12, 31, 0, 0, 0, TimeSpan.Zero);

    public void NewHomeDefaults(string characterName) {
        Indoor.Name = characterName;
        Indoor.ExpiryTime = HomeExpiryTime.ToUnixTimeSeconds();
        Message = "Thanks for visiting. Come back soon!";
        DecorationLevel = 1;
        Passcode = string.Empty;
    }

    public bool SetArea(int area) {
        if (Area == area) return false;
        Area = (byte) Math.Clamp(area, Constant.MinHomeArea, Constant.MaxHomeArea);
        return Area == area;
    }

    public bool SetHeight(int height) {
        if (Height == height) return false;
        Height = (byte) Math.Clamp(height, Constant.MinHomeHeight, Constant.MaxHomeHeight);
        return Height == height;
    }

    public bool SetPlannerArea(int area) {
        if (PlannerArea == area) return false;
        PlannerArea = (byte) Math.Clamp(area, Constant.MinHomeArea, Constant.MaxHomeArea);
        return PlannerArea == area;
    }

    public bool SetPlannerHeight(int height) {
        if (PlannerHeight == height) return false;
        PlannerHeight = (byte) Math.Clamp(height, Constant.MinHomeHeight, Constant.MaxHomeHeight);
        return PlannerHeight == height;
    }

    public bool SetBackground(HomeBackground background) {
        if (Background == background || !System.Enum.IsDefined(background)) {
            return false;
        }

        Background = background;
        return true;
    }

    public bool SetLighting(HomeLighting lighting) {
        if (Lighting == lighting || !System.Enum.IsDefined(lighting)) {
            return false;
        }

        Lighting = lighting;
        return true;
    }

    public bool SetCamera(HomeCamera camera) {
        if (Camera == camera || !System.Enum.IsDefined(camera)) {
            return false;
        }

        Camera = camera;
        return true;
    }

    public void EnterPlanner(PlotMode mode) {
        PlannerArea = Area;
        PlannerHeight = Height;
        Indoor.PlotMode = mode;
    }

    public Vector3 CalculateSafePosition(List<PlotCube> plotCubes) {
        int area = IsPlanner ? PlannerArea : Area;

        // plots start at 0,0 and are built towards negative x and y
        int dimension = -1 * (area - 1);

        // find the blocks in most negative x,y direction, with the highest z value
        int height = 0;
        if (plotCubes.Count > 0) {
            List<PlotCube> cubes = plotCubes.Where(cube => cube.Position.X == dimension && cube.Position.Y == dimension).ToList();
            if (cubes.Count > 0) {
                height = cubes.Max(cube => cube.Position.Z);
            }
        }

        dimension *= VectorExtensions.BLOCK_SIZE;

        height++; // add 1 to height to be on top of the block
        height *= VectorExtensions.BLOCK_SIZE;
        return new Vector3(dimension, dimension, height);
    }

    public void GainExp(long exp, IReadOnlyDictionary<int, MasteryUgcHousingTable.Entry> masteryTable) {
        if (exp <= 0 || DecorationLevel >= Constant.HomeDecorationMaxLevel) {
            return;
        }

        while (masteryTable.TryGetValue((int) DecorationLevel + 1, out MasteryUgcHousingTable.Entry? entry) && DecorationExp + exp >= entry.Exp) {
            exp -= entry.Exp - DecorationExp;
            DecorationLevel++;
        }

        DecorationExp += exp;
    }

    public void WriteTo(IByteWriter writer) {
        writer.WriteLong(AccountId);
        writer.WriteUnicodeString(Indoor.Name);
        writer.WriteUnicodeString(Message);
        writer.WriteByte();
        writer.WriteInt(CurrentArchitectScore);
        writer.WriteInt(ArchitectScore);
        writer.WriteInt(PlotMapId);
        writer.WriteInt(PlotNumber);
        writer.Write<PlotMode>(Indoor.PlotMode);
        writer.WriteByte(Area);
        writer.WriteByte(Height);
        writer.Write<HomeBackground>(Background);
        writer.Write<HomeLighting>(Lighting);
        writer.Write<HomeCamera>(Camera);

        writer.WriteByte(HOME_PERMISSION_COUNT);
        for (byte i = 0; i < HOME_PERMISSION_COUNT; i++) {
            bool enabled = Permissions.TryGetValue((HomePermission) i, out HomePermissionSetting setting);
            writer.WriteBool(enabled);
            if (enabled) {
                writer.Write<HomePermissionSetting>(setting);
            }
        }

        writer.WriteByte((byte) Layouts.Count);
        foreach (HomeLayout layout in Layouts) {
            writer.WriteClass<HomeLayout>(layout);
        }
        writer.WriteByte((byte) Blueprints.Count);
        foreach (HomeLayout blueprint in Blueprints) {
            writer.WriteClass<HomeLayout>(blueprint);
        }
    }
}

public class HomeLayout : IByteSerializable {
    public long Uid { get; private set; }
    public int Id { get; private set; }
    public string Name { get; private set; }
    public byte Area { get; private set; }
    public byte Height { get; private set; }
    public HomeBackground Background { get; init; }
    public HomeLighting Lighting { get; init; }
    public HomeCamera Camera { get; init; }
    public DateTimeOffset Timestamp { get; private set; }
    public List<PlotCube> Cubes { get; set; }

    public HomeLayout(long uid, int layoutId, string layoutName, byte area, byte height, DateTimeOffset timestamp, List<PlotCube> plotCubes) {
        Uid = uid;
        Id = layoutId;
        Name = layoutName;
        Area = area;
        Height = height;
        Timestamp = timestamp;
        Cubes = plotCubes;
    }

    public HomeLayout(int layoutId, string layoutName, byte area, byte height, DateTimeOffset timestamp, List<PlotCube> cubes) {
        Id = layoutId;
        Name = layoutName;
        Area = area;
        Height = height;
        Timestamp = timestamp;
        Cubes = cubes;
    }

    public void WriteTo(IByteWriter pWriter) {
        pWriter.WriteInt(Id);
        pWriter.WriteUnicodeString(Name);
        pWriter.WriteLong(Timestamp.ToUnixTimeSeconds());
    }
}
