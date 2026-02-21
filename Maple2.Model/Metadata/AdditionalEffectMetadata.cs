using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

public record AdditionalEffectMetadata(
    int Id,
    short Level,
    BeginCondition Condition,
    AdditionalEffectMetadataProperty Property,
    AdditionalEffectMetadataConsume Consume,
    AdditionalEffectMetadataReflect Reflect,
    AdditionalEffectMetadataUpdate Update,
    AdditionalEffectMetadataStatus Status,
    AdditionalEffectMetadataRecovery? Recovery,
    AdditionalEffectMetadataDot Dot,
    AdditionalEffectMetadataShield? Shield,
    AdditionalEffectMetadataInvokeEffect? InvokeEffect,
    AdditionalEffectMetadataModifyOverlapCount[] ModifyOverlapCount,
    SkillEffectMetadata[] Skills,
    SkillEffectMetadata[] TickSkills);

public record AdditionalEffectMetadataProperty(
    BuffType Type, // 1,2,3
    BuffSubType SubType, // 0,1,2,4,6,8,16,32,64,128,256,512,1024
    BuffCategory Category, // 0,1,2,4,6,7,8,9,99,1007,2001
    BuffEventType EventType, // 0,1,2,3,4
    int Group,
    int DurationTick,
    int IntervalTick,
    int DelayTick,
    int MaxCount,
    bool UseInGameTime,
    bool KeepOnDeath,
    bool RemoveOnLogout,
    bool RemoveOnLeaveField,
    bool RemoveOnPvpZone,
    bool KeepOnEnterPvpZone,
    bool CasterIndividualBuff,
    int Exp,
    int CooldownTime,
    int RideId,
    int ImmuneBreak,
    int Stun,
    BuffKeepCondition KeepCondition,
    BuffResetCondition ResetCondition,
    BuffDotCondition DotCondition,
    int ClearOnDistanceFromCaster);

public record AdditionalEffectMetadataConsume(
    float HpRate,
    float SpRate);

public record AdditionalEffectMetadataReflect(
    float Rate,
    int EffectId,
    short EffectLevel,
    int Count,
    int PhysicalRateLimit,
    int MagicalRateLimit,
    IReadOnlyDictionary<BasicAttribute, long> Values,
    IReadOnlyDictionary<BasicAttribute, float> Rates);

public record AdditionalEffectMetadataUpdate(
    AdditionalEffectMetadataUpdate.CancelEffect? Cancel,
    int[] ImmuneIds,
    BuffCategory[] ImmuneCategories,
    int[] ResetCooldown,
    AdditionalEffectMetadataUpdate.ModifyDuration[] Duration
) {
    public record CancelEffect(bool CheckSameCaster, bool PassiveEffect, int[] Ids, BuffCategory[] Categories);

    public record ModifyDuration(int Id, float Rate, float Value);
}

public record AdditionalEffectMetadataStatus(
    IReadOnlyDictionary<BasicAttribute, long> Values,
    IReadOnlyDictionary<BasicAttribute, float> Rates,
    IReadOnlyDictionary<SpecialAttribute, float> SpecialValues,
    IReadOnlyDictionary<SpecialAttribute, float> SpecialRates,
    IReadOnlyDictionary<BasicAttribute, float> Resistances,
    bool DeathResistanceHp,
    AdditionalEffectMetadataStatus.CompulsionEvent? Compulsion,
    AdditionalEffectMetadataStatus.StatConversion? Conversion,
    int ImmuneBreak,
    bool Invincible) {

    public record CompulsionEvent(BuffCompulsionEventType Type, float Rate, int[] SkillIds);

    public record StatConversion(BasicAttribute BaseAttribute, BasicAttribute ResultAttribute, float Rate);
}

public record AdditionalEffectMetadataRecovery(
    float RecoveryRate,
    long HpValue,
    float HpRate,
    float HpConsumeRate,
    long SpValue,
    float SpRate,
    float SpConsumeRate,
    long EpValue,
    float EpRate,
    bool DisableCrit);

public record AdditionalEffectMetadataDot(
    AdditionalEffectMetadataDot.DotDamage? Damage,
    AdditionalEffectMetadataDot.DotBuff? Buff
) {
    public record DotDamage(
        AttackType Type,
        Element Element,
        bool UseGrade,
        float Rate,
        int HpValue,
        int SpValue,
        int EpValue,
        float DamageByTargetMaxHp,
        float RecoverHpByDamage,
        bool IsConstDamage, // Use 'Value' to determine damage
        bool NotKill);

    public record DotBuff(
        DotTargetType Target, // 0,1
        int Id,
        short Level);
}

public record AdditionalEffectMetadataShield(
    long HpValue,
    float HpByTargetMaxHp);

public record AdditionalEffectMetadataInvokeEffect(
    InvokeEffectType[] Types,
    float[] Values,
    float[] Rates,
    int EffectId,
    int EffectGroupId,
    int SkillId,
    int SkillGroupId);

public record AdditionalEffectMetadataModifyOverlapCount(
    int Id,
    int OffsetCount);
