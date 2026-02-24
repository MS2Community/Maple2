using System.Numerics;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Tools;
using Maple2.Tools.Collision;

namespace Maple2.Model.Game;

public interface ITriggerObject : IByteSerializable {
    public int Id { get; }
    public bool Visible { get; }
}

public abstract class TriggerObject<T> : ITriggerObject where T : Ms2Trigger {
    public readonly T Metadata;

    public int Id => Metadata.TriggerId;
    public bool Visible { get; set; }

    public TriggerObject(T metadata) {
        Metadata = metadata;
    }

    public virtual void WriteTo(IByteWriter writer) {
        writer.WriteInt(Id);
        writer.WriteBool(Visible);
    }
}

public class TriggerObjectSound(Ms2TriggerSound metadata) : TriggerObject<Ms2TriggerSound>(metadata);

public class TriggerObjectMesh : TriggerObject<Ms2TriggerMesh> {
    public bool MinimapVisible { get; init; }
    public int Fade { get; set; }
    public float Scale { get; set; }

    public TriggerObjectMesh(Ms2TriggerMesh metadata) : base(metadata) {
        MinimapVisible = metadata.MinimapInvisible;
        Scale = metadata.Scale;
        Visible = metadata.Visible;
    }

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteBool(MinimapVisible);
        writer.WriteInt(Fade); // Fade 10 = 1s?
        writer.WriteUnicodeString();
        writer.WriteFloat(Scale);
    }
}

public class TriggerObjectActor(Ms2TriggerActor metadata) : TriggerObject<Ms2TriggerActor>(metadata) {
    public string SequenceName { get; set; } = string.Empty;

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteUnicodeString(SequenceName);
    }
}

public class TriggerObjectRope(Ms2TriggerRope metadata) : TriggerObject<Ms2TriggerRope>(metadata) {
    public bool Animate { get; set; }
    public int Delay { get; set; }

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteBool(Animate);
        writer.WriteInt(Delay);
    }
}

public class TriggerObjectLadder(Ms2TriggerLadder metadata) : TriggerObject<Ms2TriggerLadder>(metadata) {
    public bool Animate { get; set; }
    public int Delay { get; set; }

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteBool(Animate);
        writer.WriteInt(Delay);
    }
}

public class TriggerObjectEffect(Ms2TriggerEffect metadata) : TriggerObject<Ms2TriggerEffect>(metadata) {
    public bool UnknownBool { get; set; }
    public int UnknownInt { get; set; }

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteBool(UnknownBool);
        writer.WriteInt(UnknownInt);
    }
}

public class TriggerObjectCube(Ms2TriggerCube metadata) : TriggerObject<Ms2TriggerCube>(metadata);

public class TriggerObjectCamera(Ms2TriggerCamera metadata) : TriggerObject<Ms2TriggerCamera>(metadata);

public class TriggerObjectAgent(Ms2TriggerAgent metadata) : TriggerObject<Ms2TriggerAgent>(metadata);

public class TriggerBox {
    // Some extra height to compensate for entity height
    private const float EXTRA_HEIGHT = 10f;

    // Extra to check for entity size
    private const float EXTRA_WIDTH = 10f;

    public readonly Ms2TriggerBox Metadata;

    public int Id => Metadata.TriggerId;

    private readonly Prism box;

    public TriggerBox(Ms2TriggerBox metadata) {
        Metadata = metadata;

        var min = new Vector2(metadata.Position.X - metadata.Dimensions.X / 2 - EXTRA_WIDTH, metadata.Position.Y - metadata.Dimensions.Y / 2 - EXTRA_WIDTH);
        var max = new Vector2(metadata.Position.X + metadata.Dimensions.X / 2 + EXTRA_WIDTH, metadata.Position.Y + metadata.Dimensions.Y / 2 + EXTRA_WIDTH);
        box = new Prism(new BoundingBox(min, max), metadata.Position.Z - metadata.Dimensions.Z / 2 - EXTRA_HEIGHT, metadata.Dimensions.Z + EXTRA_HEIGHT);
    }

    public bool Contains(in Vector3 point) => box.Contains(point);

    public bool Intersects(in IPrism shape) => box.Intersects(shape);

    public override string ToString() {
        return $"Id:{Id}\n- {box}";
    }
}
