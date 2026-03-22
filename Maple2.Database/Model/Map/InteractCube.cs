using System.Diagnostics.CodeAnalysis;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Database.Model;

internal record InteractCube(
    string Id,
    int ObjectCode,
    CubePortalSettings? PortalSettings,
    CubeNoticeSettings? NoticeSettings) {

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator InteractCube?(Maple2.Model.Game.InteractCube? other) {
        return other == null ? null : new InteractCube(
            other.Id,
            other.ObjectCode,
            other.PortalSettings,
            other.NoticeSettings ?? (other.TriggerSettings == null ? null : new Maple2.Model.Game.CubeNoticeSettings {
                Notice = other.TriggerSettings.ScriptXml,
                Distance = other.TriggerSettings.Distance,
            }));
    }

    // Use explicit Convert() here because we need metadata to construct InteractCube.
    public Maple2.Model.Game.InteractCube Convert(FunctionCubeMetadata metadata, CubeNoticeSettings? noticeSettings, CubePortalSettings? portalSettings) {
        Maple2.Model.Game.InteractCube result = new Maple2.Model.Game.InteractCube(Id, metadata, portalSettings, noticeSettings);
        return result;
    }
}

internal record CubePortalSettings(
    string PortalName,
    PortalActionType Method,
    CubePortalDestination Destination,
    string DestinationTarget) {

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator CubePortalSettings?(Maple2.Model.Game.CubePortalSettings? other) {
        return other == null ? null : new CubePortalSettings(
            other.PortalName,
            other.Method,
            other.Destination,
            other.DestinationTarget);
    }

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator Maple2.Model.Game.CubePortalSettings?(CubePortalSettings? other) {
        return other == null ? null : new Maple2.Model.Game.CubePortalSettings {
            PortalName = other.PortalName,
            Method = other.Method,
            Destination = other.Destination,
            DestinationTarget = other.DestinationTarget,
        };
    }
}

internal record CubeNoticeSettings(
    string Notice,
    byte Distance) {

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator CubeNoticeSettings?(Maple2.Model.Game.CubeNoticeSettings? other) {
        return other == null ? null : new CubeNoticeSettings(
            other.Notice,
            other.Distance);
    }

    [return: NotNullIfNotNull(nameof(other))]
    public static implicit operator Maple2.Model.Game.CubeNoticeSettings?(CubeNoticeSettings? other) {
        return other == null ? null : new Maple2.Model.Game.CubeNoticeSettings {
            Notice = other.Notice,
            Distance = other.Distance,
        };
    }
}
