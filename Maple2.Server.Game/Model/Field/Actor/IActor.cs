using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Model.Skill;
using Maple2.Tools.Collision;

namespace Maple2.Server.Game.Model;

public interface IActor : IFieldEntity {
    public NpcMetadataStorage NpcMetadata { get; init; }
    public BuffManager Buffs { get; }

    public StatsManager Stats { get; }
    public AnimationManager Animation { get; }
    public SkillState SkillState { get; init; }

    public bool IsDead { get; }
    public IPrism Shape { get; }
    public SkillQueue ActiveSkills { get; init; }
    public virtual void ApplyEffects(SkillEffectMetadata[] effects, IActor caster, IActor owner, EventConditionType type = EventConditionType.Activate, int skillId = 0, int buffId = 0, params IActor[] targets) { }
    public virtual void ApplyEffects(SkillEffectMetadata[] effects, IActor caster, DamageRecord record, params IActor[] targets) { }
    public virtual void ApplyEffect(IActor caster, IActor owner, SkillEffectMetadata effect, long startTick, EventConditionType type = EventConditionType.Activate, int skillId = 0, int buffId = 0, bool notifyField = true) { }
    public virtual void ApplyDamage(IActor caster, DamageRecord damage, SkillMetadataAttack attack) { }
    public virtual IActor GetTarget(SkillTargetType targetType, IActor caster, IActor target, IActor owner) { return this; }
    public virtual IActor GetOwner(SkillTargetType targetType, IActor caster, IActor target, IActor owner) { return this; }

    public virtual void TargetAttack(SkillRecord record) { }

    public virtual SkillRecord? CastSkill(int id, short level, long uid, int castTick, in Vector3 position = default, in Vector3 direction = default, in Vector3 rotation = default, float rotateZ = 0f, byte motionPoint = 0) { return null; }
    public virtual void KeyframeEvent(string keyName) { }
}

public interface IActor<out T> : IActor {
    public T Value { get; }
}
