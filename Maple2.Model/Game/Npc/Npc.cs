using Maple2.Model.Metadata;

namespace Maple2.Model.Game;

public class Npc {
    public readonly NpcMetadata Metadata;
    public readonly IReadOnlyDictionary<string, AnimationSequenceMetadata> Animations;

    public int Id => Metadata.Id;

    public bool IsBoss => Metadata.Basic.Friendly == 0 && Metadata.Basic.Class >= 3;

    public Npc(NpcMetadata metadata, AnimationMetadata? animation, float constLastSightRadius, float constLastSightHeightUp, float constLastSightHeightDown) {
        if (metadata.Distance.LastSightRadius == 0) {
            Metadata = new NpcMetadata(metadata, constLastSightRadius);
        } else if (metadata.Distance.LastSightRadius == 0 && metadata.Distance.LastSightHeightUp == 0) {
            Metadata = new NpcMetadata(metadata, constLastSightRadius, constLastSightHeightUp);
        } else if (metadata.Distance.LastSightRadius == 0 && metadata.Distance.LastSightHeightUp == 0 && metadata.Distance.LastSightHeightDown == 0) {
            Metadata = new NpcMetadata(metadata, constLastSightRadius, constLastSightHeightUp, constLastSightHeightDown);
        } else {
            Metadata = metadata;
        }
        Animations = animation?.Sequences ?? new Dictionary<string, AnimationSequenceMetadata>();
    }
}
