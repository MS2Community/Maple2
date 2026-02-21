using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Server.Game.Model.Skill;

public class HealDamageRecord : IByteSerializable {
    public readonly IActor Caster;
    public readonly IActor Target;
    public readonly int OwnerId;
    public readonly int HpAmount;
    public readonly int SpAmount;
    public readonly int EpAmount;

    public HealDamageRecord(IActor caster, IActor target, int ownerId, AdditionalEffectMetadataRecovery recovery) {
        Caster = caster;
        Target = target;
        OwnerId = ownerId;

        float multiplier = 1f;
        if (!recovery.DisableCrit && caster.Stats.GetCriticalRate() == DamageType.Critical) {
            multiplier = 1.5f;
        }

        HpAmount = (int) (recovery.HpValue + recovery.HpRate * target.Stats.Values[BasicAttribute.Health].Total
                                           + recovery.RecoveryRate * caster.Stats.Values[BasicAttribute.MagicalAtk].Current * multiplier);
        SpAmount = (int) (recovery.SpValue + recovery.SpRate * target.Stats.Values[BasicAttribute.Spirit].Total * multiplier);
        EpAmount = (int) (recovery.EpValue + recovery.EpRate * target.Stats.Values[BasicAttribute.Stamina].Total * multiplier);
    }

    public void WriteTo(IByteWriter writer) {
        writer.WriteInt(Caster.ObjectId);
        writer.WriteInt(Target.ObjectId);
        writer.WriteInt(OwnerId);
        writer.WriteInt(HpAmount);
        writer.WriteInt(SpAmount);
        writer.WriteInt(EpAmount);
    }
}
