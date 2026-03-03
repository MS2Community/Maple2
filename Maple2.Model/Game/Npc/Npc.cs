using Maple2.Model.Metadata;

namespace Maple2.Model.Game;

public class Npc {
    public readonly NpcMetadata Metadata;
    public readonly IReadOnlyDictionary<string, AnimationSequenceMetadata> Animations;

    public int Id => Metadata.Id;

    public bool IsBoss => Metadata.Basic.Friendly == 0 && Metadata.Basic.Class >= 3;

    public Npc(NpcMetadata metadata, AnimationMetadata? animation, float constLastSightRadius, float constLastSightHeightUp, float constLastSightHeightDown) {
        float lastSightRadius = metadata.Distance.LastSightRadius == 0 ? constLastSightRadius : metadata.Distance.LastSightRadius;
        float lastSightHeightUp = metadata.Distance.LastSightHeightUp == 0 ? constLastSightHeightUp : metadata.Distance.LastSightHeightUp;
        float lastSightHeightDown = metadata.Distance.LastSightHeightDown == 0 ? constLastSightHeightDown : metadata.Distance.LastSightHeightDown;
        Metadata = new NpcMetadata(metadata, lastSightRadius, lastSightHeightUp, lastSightHeightDown);
        Animations = animation?.Sequences ?? new Dictionary<string, AnimationSequenceMetadata>();
    }
}
