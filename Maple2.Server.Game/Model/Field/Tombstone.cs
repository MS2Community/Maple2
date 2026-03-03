using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using Maple2.Tools;

namespace Maple2.Server.Game.Model;

public class Tombstone : IByteSerializable {
    public readonly FieldPlayer Owner;
    public int ObjectId => Owner.ObjectId;
    private byte hitsRemaining;
    public byte HitsRemaining {
        get => hitsRemaining;
        set {
            if (hitsRemaining == 0) {
                return;
            }
            hitsRemaining = Math.Clamp(value, (byte) 0, TotalHitCount);

            Owner.Field.Broadcast(RevivalPacket.Tombstone(this));
        }
    }
    public byte TotalHitCount { get; }
    public int Unknown1 { get; } = 1;
    public bool Unknown2 { get; }

    private ConstantsTable Constants => Owner.Session.ServerTableMetadata.ConstantsTable;

    public Tombstone(FieldPlayer owner, int totalDeaths) {
        Owner = owner;
        TotalHitCount = (byte) Math.Min(totalDeaths * Constants.hitPerDeadCount,
            Constants.hitPerDeadCount * Constants.maxDeadCount);
        hitsRemaining = TotalHitCount;
    }
    public void WriteTo(IByteWriter writer) {
        writer.WriteInt(ObjectId);
        writer.WriteByte(HitsRemaining);
        writer.WriteByte(TotalHitCount);
        writer.WriteInt(Unknown1); // 1 if hit by a user?
        writer.WriteBool(Unknown2); // true if revived by pet
    }
}
