using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game.Dungeon;

public class DungeonMission : IByteSerializable {
    public readonly DungeonMissionMetadata Metadata;
    public int Id => Metadata.Id;
    public short Score { get; private set; }
    public short Counter { get; private set; }

    public DungeonMission(DungeonMissionMetadata metadata) {
        Metadata = metadata;
    }

    public bool Update(int counter = 1) {
        if (Counter >= Metadata.ApplyCount) {
            return false;
        }

        Counter += (short) counter;
        float percentage = (float) Counter / Metadata.ApplyCount;
        Score = (short) (percentage * Metadata.MaxScore);
        return true;
    }

    public void Complete() {
        Counter = Metadata.ApplyCount;
        Score = Metadata.MaxScore;
    }

    public void WriteTo(IByteWriter writer) {
        writer.WriteInt(Id);
        writer.WriteShort(Score);
        writer.WriteShort(Counter);
    }
}
