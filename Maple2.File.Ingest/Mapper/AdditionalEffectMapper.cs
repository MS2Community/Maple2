using Maple2.File.IO;
using Maple2.File.Parser;
using Maple2.File.Parser.Xml.AdditionalEffect;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Tools.Extensions;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Maple2.File.Ingest.Mapper;

public class AdditionalEffectMapper : TypeMapper<AdditionalEffectMetadata> {
    private readonly AdditionalEffectParser parser;

    public AdditionalEffectMapper(M2dReader xmlReader) {
        parser = new AdditionalEffectParser(xmlReader);
    }

    protected override IEnumerable<AdditionalEffectMetadata> Map() {
        foreach ((int id, IList<AdditionalEffectData> datas) in parser.Parse()) {
            foreach (AdditionalEffectData data in datas) {
                yield return new AdditionalEffectMetadata(
                    Id: id,
                    Level: data.BasicProperty.level,
                    Condition: data.beginCondition.Convert(),
                    Property: new AdditionalEffectMetadataProperty(
                        Type: (BuffType) data.BasicProperty.buffType,
                        SubType: (BuffSubType) data.BasicProperty.buffSubType,
                        Category: (BuffCategory) data.BasicProperty.buffCategory,
                        EventType: (BuffEventType) data.BasicProperty.eventBuffType,
                        Group: data.BasicProperty.group,
                        DurationTick: data.BasicProperty.durationTick,
                        IntervalTick: data.BasicProperty.intervalTick,
                        DelayTick: data.BasicProperty.delayTick,
                        MaxCount: data.BasicProperty.maxBuffCount,
                        UseInGameTime: data.BasicProperty.useInGameTime,
                        KeepOnDeath: data.BasicProperty.deadKeepEffect,
                        RemoveOnLogout: data.BasicProperty.logoutClearEffect,
                        RemoveOnLeaveField: data.BasicProperty.leaveFieldClearEffect,
                        RemoveOnPvpZone: data.BasicProperty.clearEffectFromPVPZone,
                        KeepOnEnterPvpZone: data.BasicProperty.doNotClearEffectFromEnterPVPZone,
                        CasterIndividualBuff: data.BasicProperty.casterIndividualEffect,
                        Exp: data.ExpProperty.value,
                        CooldownTime: (int) TimeSpan.FromSeconds(float.TryParse(data.BasicProperty.coolDownTime, out float cooldown) ? cooldown : 0).TotalMilliseconds,
                        RideId: data.RideeProperty == null! ? 0 : data.RideeProperty.rideeID,
                        ImmuneBreak: data.MotionProperty.abnormalImmuneBreak,
                        Stun: data.MotionProperty.stun,
                        KeepCondition: (BuffKeepCondition) data.BasicProperty.keepCondition,
                        ResetCondition: (BuffResetCondition) data.BasicProperty.resetCondition,
                        DotCondition: (BuffDotCondition) data.BasicProperty.dotCondition,
                        ClearOnDistanceFromCaster: int.TryParse(data.BasicProperty.clearDistanceFromCaster, out int distance) ? distance : 0),
                    Consume: new AdditionalEffectMetadataConsume(
                        HpRate: data.ConsumeProperty.hpRate,
                        SpRate: data.ConsumeProperty.spRate),
                    Reflect: Convert(data.ReflectProperty),
                    Update: Convert(data),
                    Status: Convert(data.StatusProperty, data.OffensiveProperty, data.DefensiveProperty),
                    Recovery: Convert(data.RecoveryProperty),
                    Dot: new AdditionalEffectMetadataDot(
                        Damage: Convert(data.DotDamageProperty),
                        Buff: Convert(data.DotBuffProperty)),
                    Shield: Convert(data.ShieldProperty),
                    InvokeEffect: Convert(data.InvokeEffectProperty),
                    ModifyOverlapCount: data.ModifyOverlapCountProperty == null! ? [] : data.ModifyOverlapCountProperty.effectCodes
                            .Zip(data.ModifyOverlapCountProperty.offsetCounts)
                            .Select(tuple => new AdditionalEffectMetadataModifyOverlapCount(
                                Id: tuple.First,
                                OffsetCount: tuple.Second))
                            .ToArray(),
                    Skills: data.conditionSkill.Concat(data.splashSkill).Where(skill => !skill.activeByIntervalTick).Select(skill => skill.Convert()).ToArray(),
                    TickSkills: data.conditionSkill.Concat(data.splashSkill).Where(skill => skill.activeByIntervalTick).Select(skill => skill.Convert()).ToArray());
            }
        }
    }

    private static AdditionalEffectMetadataUpdate Convert(AdditionalEffectData data) {
        CancelEffectProperty cancel = data.CancelEffectProperty;
        AdditionalEffectMetadataUpdate.CancelEffect? cancelEffect = null;
        if (cancel.cancelEffectCodes.Length != 0 || cancel.cancelBuffCategories.Length != 0) {
            cancelEffect = new AdditionalEffectMetadataUpdate.CancelEffect(
                CheckSameCaster: cancel.cancelCheckSameCaster,
                PassiveEffect: cancel.cancelPassiveEffect,
                Ids: cancel.cancelEffectCodes,
                Categories: Array.ConvertAll(cancel.cancelBuffCategories, category => (BuffCategory) category));
        }

        ModifyEffectDurationProperty modify = data.ModifyEffectDurationProperty;
        var modifyDuration = new AdditionalEffectMetadataUpdate.ModifyDuration[modify.effectCodes.Length];
        for (int i = 0; i < modifyDuration.Length; i++) {
            modifyDuration[i] = new AdditionalEffectMetadataUpdate.ModifyDuration(modify.effectCodes[i], modify.durationFactors[i], modify.durationValues[i]);
        }

        return new AdditionalEffectMetadataUpdate(
            Cancel: cancelEffect,
            ImmuneIds: data.ImmuneEffectProperty.immuneEffectCodes,
            ImmuneCategories: Array.ConvertAll(data.ImmuneEffectProperty.immuneBuffCategories, category => (BuffCategory) category),
            ResetCooldown: data.ResetSkillCoolDownTimeProperty.skillCodes,
            Duration: modifyDuration);
    }

    private static AdditionalEffectMetadataReflect Convert(ReflectProperty reflect) {
        var values = new Dictionary<BasicAttribute, long>();
        var rates = new Dictionary<BasicAttribute, float>();

        values.AddIfNotDefault(BasicAttribute.PhysicalAtk, reflect.physicalReflectionValue);
        values.AddIfNotDefault(BasicAttribute.MagicalAtk, reflect.magicalReflectionValue);

        rates.AddIfNotDefault(BasicAttribute.PhysicalAtk, reflect.physicalReflectionRate);
        rates.AddIfNotDefault(BasicAttribute.MagicalAtk, reflect.magicalReflectionRate);
        return new AdditionalEffectMetadataReflect(
            Rate: reflect.reflectionRate,
            EffectId: reflect.reflectionAdditionalEffectId,
            EffectLevel: reflect.reflectionAdditionalEffectLevel,
            Count: reflect.reflectionCount,
            PhysicalRateLimit: reflect.physicalReflectionRateLimit,
            MagicalRateLimit: reflect.magicalReflectionRateLimit,
            Values: values,
            Rates: rates);
    }

    private static AdditionalEffectMetadataStatus Convert(StatusProperty status, OffensiveProperty offensive, DefensiveProperty defensive) {
        var values = new Dictionary<BasicAttribute, long>();
        var rates = new Dictionary<BasicAttribute, float>();
        var specialValues = new Dictionary<SpecialAttribute, float>();
        var specialRates = new Dictionary<SpecialAttribute, float>();

        if (status.Stat != null) {
            foreach (BasicAttribute attribute in Enum.GetValues<BasicAttribute>()) {
                values.AddIfNotDefault(attribute, status.Stat.Value((byte) attribute));
                rates.AddIfNotDefault(attribute, status.Stat.Rate((byte) attribute));
            }
        }

        if (status.SpecialAbility != null) {
            foreach (SpecialAttribute attribute in Enum.GetValues<SpecialAttribute>()) {
                byte attributeIndex = attribute.OptionIndex();
                if (attributeIndex == byte.MaxValue) {
                    continue;
                }

                specialValues.AddIfNotDefault(attribute, status.SpecialAbility.Value(attributeIndex));
                specialRates.AddIfNotDefault(attribute, status.SpecialAbility.Rate(attributeIndex));
            }
        }

        specialValues.AddIfNotDefault(SpecialAttribute.OffensiveMagicalDamage, offensive.mapDamageV);
        specialRates.AddIfNotDefault(SpecialAttribute.OffensiveMagicalDamage, offensive.mapDamageR);
        specialValues.AddIfNotDefault(SpecialAttribute.OffensivePhysicalDamage, offensive.papDamageV);
        specialRates.AddIfNotDefault(SpecialAttribute.OffensivePhysicalDamage, offensive.papDamageR);

        var resistances = new Dictionary<BasicAttribute, float>();
        resistances.AddIfNotDefault(BasicAttribute.MaxWeaponAtk, status.resWapR);
        resistances.AddIfNotDefault(BasicAttribute.BonusAtk, status.resBapR);
        resistances.AddIfNotDefault(BasicAttribute.CriticalDamage, status.resCadR);
        resistances.AddIfNotDefault(BasicAttribute.Accuracy, status.resAtpR);
        resistances.AddIfNotDefault(BasicAttribute.Evasion, status.resEvpR);
        resistances.AddIfNotDefault(BasicAttribute.Piercing, status.resPenR);
        resistances.AddIfNotDefault(BasicAttribute.AttackSpeed, status.resAspR);

        Debug.Assert(status.compulsionEventTypes.Length <= 1 && status.compulsionEventRate.Length <= 1);
        var compulsionEventType = BuffCompulsionEventType.None;
        if (status.compulsionEventTypes.Length > 0) {
            compulsionEventType = (BuffCompulsionEventType) status.compulsionEventTypes[0];
        }

        AdditionalEffectMetadataStatus.CompulsionEvent? compulsionEvent = null;
        if (compulsionEventType != BuffCompulsionEventType.None) {
            float compulsionEventRate = 0;
            if (status.compulsionEventRate.Length > 0) {
                compulsionEventRate = status.compulsionEventRate[0];
            }

            compulsionEvent = new AdditionalEffectMetadataStatus.CompulsionEvent(compulsionEventType, compulsionEventRate, status.compulsionEventSkillCodes);
        } else {
            // Ensure these fields are not set without a CompulsionEventType
            Debug.Assert(status.compulsionEventRate.Length == 0 && status.compulsionEventSkillCodes.Length == 0);
        }

        AdditionalEffectMetadataStatus.StatConversion? conversion = null;
        if (status.statChangeRate != 0) {
            conversion = new AdditionalEffectMetadataStatus.StatConversion((BasicAttribute) status.statChangeBase, (BasicAttribute) status.statChangeResult, status.statChangeRate);
        }

        return new AdditionalEffectMetadataStatus(
            Values: values,
            Rates: rates,
            SpecialValues: specialValues,
            SpecialRates: specialRates,
            Resistances: resistances,
            DeathResistanceHp: status.deathResistanceHP,
            Compulsion: compulsionEvent,
            Conversion: conversion,
            ImmuneBreak: offensive.hitImmuneBreak,
            Invincible: defensive.invincible != 0);
    }

    private static AdditionalEffectMetadataRecovery? Convert(RecoveryProperty recovery) {
        if (recovery is { RecoveryRate: <= 0, hpValue: <= 0, hpRate: <= 0, spValue: <= 0, spRate: <= 0, spConsumeRate: <= 0, epValue: <= 0, epRate: <= 0 }) {
            return null;
        }

        return new AdditionalEffectMetadataRecovery(
            RecoveryRate: recovery.RecoveryRate,
            HpValue: recovery.hpValue,
            HpRate: recovery.hpRate,
            HpConsumeRate: recovery.hpConsumeRate,
            SpValue: recovery.spValue,
            SpRate: recovery.spRate,
            SpConsumeRate: recovery.spConsumeRate,
            EpValue: recovery.epValue,
            EpRate: recovery.epRate,
            DisableCrit: recovery.disableCriticalRecovery);
    }

    private static AdditionalEffectMetadataDot.DotDamage? Convert(DotDamageProperty dotDamage) {
        if (dotDamage.type <= 0) {
            return null;
        }

        return new AdditionalEffectMetadataDot.DotDamage(
            Type: (AttackType) dotDamage.type,
            Element: (Element) dotDamage.element,
            UseGrade: dotDamage.useGrade,
            Rate: dotDamage.rate,
            HpValue: (int) dotDamage.value,
            SpValue: dotDamage.spValue,
            EpValue: dotDamage.epValue,
            DamageByTargetMaxHp: dotDamage.damageByTargetMaxHP,
            RecoverHpByDamage: dotDamage.casterRecoveryHpByDamage,
            IsConstDamage: dotDamage.isConstDotDamageValue,
            NotKill: dotDamage.notKill);
    }

    private static AdditionalEffectMetadataDot.DotBuff? Convert(DotBuffProperty? dotBuff) {
        if (dotBuff is not { buffID: > 0 }) {
            return null;
        }

        return new AdditionalEffectMetadataDot.DotBuff(Target: (DotTargetType) dotBuff.target, Id: dotBuff.buffID, Level: dotBuff.buffLevel);
    }

    private static AdditionalEffectMetadataShield? Convert(ShieldProperty shield) {
        if (shield is { hpValue: <= 0, hpByTargetMaxHP: <= 0 }) {
            return null;
        }

        return new AdditionalEffectMetadataShield(HpValue: shield.hpValue, HpByTargetMaxHp: shield.hpByTargetMaxHP);
    }

    [return: NotNullIfNotNull(nameof(invokeEffect))]
    private static AdditionalEffectMetadataInvokeEffect? Convert(InvokeEffectProperty? invokeEffect) {
        if (invokeEffect is null) {
            return null;
        }

        return new AdditionalEffectMetadataInvokeEffect(
            Types: invokeEffect.types.Select(type => (InvokeEffectType) type).ToArray(),
            Values: invokeEffect.values,
            Rates: invokeEffect.rates,
            EffectId: invokeEffect.effectID,
            EffectGroupId: invokeEffect.effectGroupID,
            SkillId: invokeEffect.skillID,
            SkillGroupId: invokeEffect.skillGroupID);
    }
}
