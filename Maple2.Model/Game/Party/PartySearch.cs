using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game.Party;

public class PartySearch : IByteSerializable {
    public long Id { get; init; }
    public int PartyId { get; init; }
    public string Name { get; init; }
    public int Size { get; init; }
    public long CreationTime;
    public bool NoApproval;
    public int MemberCount;
    public long LeaderAccountId;
    public long LeaderCharacterId;
    public required string LeaderName { get; set; }

    public PartySearch(long id, string name, int size) {
        Id = id;
        Name = name;
        Size = size;
        CreationTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public void WriteTo(IByteWriter writer) {
        writer.WriteLong(Id);
        writer.WriteInt(PartyId);
        writer.WriteInt(); // Unknown
        writer.WriteInt(); // Unknown
        writer.WriteUnicodeString(Name);
        writer.WriteBool(NoApproval);
        writer.WriteInt(MemberCount);
        writer.WriteInt(Size);
        writer.WriteLong(LeaderAccountId);
        writer.WriteLong(LeaderCharacterId);
        writer.WriteUnicodeString(LeaderName);
        writer.WriteLong(CreationTime);
    }
}
