using Maple2.Model.Metadata;

namespace Maple2.Model.Game;

public class Npc {
    public readonly NpcMetadata Metadata;
    public readonly IReadOnlyDictionary<string, AnimationSequenceMetadata> Animations;

    public int Id => Metadata.Id;

    public bool IsBoss => Metadata.Basic.Friendly == 0 && Metadata.Basic.Class >= 3;

    public Npc(NpcMetadata metadata, AnimationMetadata? animation) {
        Metadata = metadata;
        Animations = animation?.Sequences ?? new Dictionary<string, AnimationSequenceMetadata>();
    }
}
