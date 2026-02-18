using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game;

public class SoldUgcMarketItem : IByteSerializable {
    public long Id { get; init; }
    public long Price { get; init; }
    public long Profit { get; init; }
    public required string Name { get; init; }
    public long SoldTime { get; init; }
    public long AccountId { get; init; }


    public void WriteTo(IByteWriter writer) {
        writer.WriteLong(Id);
        writer.WriteLong();
        writer.WriteUnicodeString(Name);
        writer.WriteInt();
        writer.WriteInt();
        writer.WriteLong();
        writer.WriteLong();
        writer.WriteUnicodeString();
        writer.WriteUnicodeString();
        writer.WriteInt();
        writer.WriteLong(Price);
        writer.WriteLong(SoldTime);
        writer.WriteLong(Profit);
    }
}
