using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Packets;

public static class LegionBattlePacket {
    private enum Mode : byte {
        Load = 0,
        Update = 1,
    }

    public static ByteWriter Load(IReadOnlyDictionary<int, FieldBossMetadata> bosses) {
        ByteWriter pWriter = Packet.Of(SendOp.LegionBattle);
        pWriter.Write<Mode>(Mode.Load);
        pWriter.WriteShort((short) bosses.Count);
        foreach ((int _, FieldBossMetadata metadata) in bosses) {
            pWriter.WriteInt(metadata.Id);
            pWriter.WriteInt(metadata.NpcIds.Length > 0 ? metadata.NpcIds[0] : 0);
            pWriter.WriteLong(FieldBossUtil.ComputeNextSpawnTimestamp(metadata));
        }
        return pWriter;
    }

    public static ByteWriter Update(int eventId, int npcId, long nextSpawnTimestamp) {
        ByteWriter pWriter = Packet.Of(SendOp.LegionBattle);
        pWriter.Write<Mode>(Mode.Update);
        pWriter.WriteInt(eventId);
        pWriter.WriteInt(npcId);
        pWriter.WriteLong(nextSpawnTimestamp);
        return pWriter;
    }
}
