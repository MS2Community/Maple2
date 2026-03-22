using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game;

public class CubePortalSettings : IByteSerializable {
    public string PortalName { get; set; }
    public PortalActionType Method { get; set; }
    public CubePortalDestination Destination { get; set; }
    public string DestinationTarget { get; set; }
    public int PortalObjectId { get; set; }

    public CubePortalSettings() {
        PortalName = string.Empty;
        DestinationTarget = string.Empty;
    }

    public CubePortalSettings(Vector3B position) {
        PortalName = $"Portal_{Math.Abs(position.X):D2}.{Math.Abs(position.Y):D2}.{Math.Abs(position.Z):D2}";
        DestinationTarget = string.Empty;
    }

    public void WriteTo(IByteWriter writer) {
        writer.WriteUnicodeString(PortalName);
        writer.WriteByte((byte) Method);
        writer.Write<CubePortalDestination>(Destination);
        writer.WriteUnicodeString(DestinationTarget);
    }
}

public class CubeNoticeSettings : IByteSerializable {
    public string Notice { get; set; } = string.Empty;
    public byte Distance { get; set; } = 1;

    public void WriteTo(IByteWriter writer) {
        writer.WriteUnicodeString(Notice);
        writer.WriteByte(Distance);
    }
}


public class CubeTriggerSettings : IByteSerializable {
    public string ScriptXml { get; set; } = string.Empty;
    public byte Distance { get; set; } = 1;

    public void WriteTo(IByteWriter writer) {
        writer.WriteUnicodeString(ScriptXml);
        writer.WriteByte(Distance);
    }
}
