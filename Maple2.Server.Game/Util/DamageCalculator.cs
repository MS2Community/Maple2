using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Formulas;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Game.Util;

public static class DamageCalculator {
    public static (DamageType, long) CalculateDamage(IActor caster, IActor target, DamagePropertyRecord property) {
        // Check block
        if (Damage.TriggerCompulsionEvent(target.Buffs.TotalCompulsionRate(BuffCompulsionEventType.BlockChance, property.SkillId))) {
            return (DamageType.Block, 0);
        }

        // Check evase
        if (Damage.TriggerCompulsionEvent(target.Buffs.TotalCompulsionRate(BuffCompulsionEventType.EvasionChanceOverride, property.SkillId))) {
            return (DamageType.Miss, 0);
        }

        // Get hit rate
        if (Damage.Evade(target.Buffs.GetResistance(BasicAttribute.Accuracy), target.Stats.Values[BasicAttribute.Evasion].Total, target.Stats.Values[BasicAttribute.Dexterity].Total,
                caster.Stats.Values[BasicAttribute.Accuracy].Total, caster.Buffs.GetResistance(BasicAttribute.Evasion), caster.Stats.Values[BasicAttribute.Luck].Total, property.SuperArmorBreak)) {
            return (DamageType.Miss, 0);
        }

        var damageType = DamageType.Normal;
        (double minBonusAtkDamage, double maxBonusAtkDamage) = caster.Stats.GetBonusAttack(target.Buffs.GetResistance(BasicAttribute.BonusAtk), target.Buffs.GetResistance(BasicAttribute.MaxWeaponAtk));
        double attackDamage = minBonusAtkDamage + (maxBonusAtkDamage - minBonusAtkDamage) * double.Lerp(Random.Shared.NextDouble(), Random.Shared.NextDouble(), Random.Shared.NextDouble());

        // change the NPCNormalDamage to be changed depending on target?
        double damageBonus = 1 + (caster.Stats.Values[SpecialAttribute.TotalDamage].Multiplier()) + (caster.Stats.Values[SpecialAttribute.NormalNpcDamage].Multiplier());
        if (target is FieldNpc npc && npc.Value.IsBoss) {
            damageBonus += caster.Stats.Values[SpecialAttribute.BossNpcDamage].Multiplier();
        }

        // Get elemental bonus
        double elementBonus = 0;
        switch (property.Element) {
            case Element.Fire:
                elementBonus = caster.Stats.Values[SpecialAttribute.FireDamage].Multiplier();
                break;
            case Element.Ice:
                elementBonus = caster.Stats.Values[SpecialAttribute.IceDamage].Multiplier();
                break;
            case Element.Electric:
                elementBonus = caster.Stats.Values[SpecialAttribute.ElectricDamage].Multiplier();
                break;
            case Element.Holy:
                elementBonus = caster.Stats.Values[SpecialAttribute.HolyDamage].Multiplier();
                break;
            case Element.Dark:
                elementBonus = caster.Stats.Values[SpecialAttribute.DarkDamage].Multiplier();
                break;
            case Element.Poison:
                elementBonus = caster.Stats.Values[SpecialAttribute.PoisonDamage].Multiplier();
                break;
        }
        damageBonus += elementBonus;

        // Get range bonus
        switch (property.RangeType) {
            case RangeType.None:
                break;
            case RangeType.Melee:
                damageBonus += caster.Stats.Values[SpecialAttribute.MeleeDamage].Multiplier();
                break;
            case RangeType.Range:
                damageBonus += caster.Stats.Values[SpecialAttribute.RangedDamage].Multiplier();
                break;
        }

        damageBonus += -target.Buffs.GetResistance(BasicAttribute.AttackSpeed) * caster.Stats.Values[BasicAttribute.AttackSpeed].Total;

        // Check for crit and get crit damage
        if (property.CanCrit) {
            if (property.CompulsionTypes.Contains(CompulsionType.Critical)) {
                damageType = DamageType.Critical;
            } else {
                damageType = caster.Stats.GetCriticalRate(target.Stats.Values[BasicAttribute.CriticalEvasion].Total);
            }
        }

        damageBonus *= damageType == DamageType.Critical ? caster.Stats.GetCriticalDamage(target.Buffs.GetResistance(BasicAttribute.CriticalDamage)) : 1;

        // Get invoked buff values
        // TODO: Need to make this flexible to get invoke values from skills or buffs. If buff -> InvokeEffectType.IncreaseDotDamage
        (int invokeValue, float invokeRate) = caster.Buffs.GetInvokeValues(InvokeEffectType.IncreaseSkillDamage, property.SkillId, property.SkillGroup);

        double damageMultiplier = damageBonus * (1 + invokeRate) * (property.Rate + invokeValue);

        double defensePierce = 1 - Math.Min(0.3, (1 / (1 + target.Buffs.GetResistance(BasicAttribute.Piercing)) * (caster.Stats.Values[BasicAttribute.Piercing].Multiplier() - 1)));
        damageMultiplier *= 1 / (Math.Max(target.Stats.Values[BasicAttribute.Defense].Total, 1) * defensePierce);

        // Check resistances
        double attackTypeAmount = 0;
        double resistance = 0;
        double finalDamage = 0;
        switch (property.AttackType) {
            case AttackType.Physical:
                resistance = Damage.CalculateResistance(target.Stats.Values[BasicAttribute.PhysicalRes].Total, caster.Stats.Values[SpecialAttribute.PhysicalPiercing].Multiplier());
                attackTypeAmount = caster.Stats.Values[BasicAttribute.PhysicalAtk].Total;
                finalDamage = caster.Stats.Values[SpecialAttribute.OffensivePhysicalDamage].Multiplier();
                break;
            case AttackType.Magic:
                resistance = Damage.CalculateResistance(target.Stats.Values[BasicAttribute.MagicalRes].Total, caster.Stats.Values[SpecialAttribute.MagicalPiercing].Multiplier());
                attackTypeAmount = caster.Stats.Values[BasicAttribute.MagicalAtk].Total;
                finalDamage = caster.Stats.Values[SpecialAttribute.OffensiveMagicalDamage].Multiplier();
                break;
            // If all, calculate the higher of the two
            case AttackType.All:
                BasicAttribute attackTypeAttribute = caster.Stats.Values[BasicAttribute.PhysicalAtk].Total >= caster.Stats.Values[BasicAttribute.MagicalAtk].Total
                    ? BasicAttribute.PhysicalAtk
                    : BasicAttribute.MagicalAtk;
                SpecialAttribute piercingAttribute = attackTypeAttribute == BasicAttribute.PhysicalAtk
                    ? SpecialAttribute.PhysicalPiercing
                    : SpecialAttribute.MagicalPiercing;
                SpecialAttribute finalDamageAttribute = attackTypeAttribute == BasicAttribute.PhysicalAtk
                    ? SpecialAttribute.OffensivePhysicalDamage
                    : SpecialAttribute.OffensiveMagicalDamage;
                attackTypeAmount = Math.Max(caster.Stats.Values[BasicAttribute.PhysicalAtk].Total, caster.Stats.Values[BasicAttribute.MagicalAtk].Total) * 0.5f;
                resistance = Damage.CalculateResistance(target.Stats.Values[attackTypeAttribute].Total, caster.Stats.Values[piercingAttribute].Multiplier());
                finalDamage = caster.Stats.Values[finalDamageAttribute].Multiplier();
                break;
        }

        damageMultiplier *= attackTypeAmount * resistance * (finalDamage == 0 ? 1 : finalDamage);
        attackDamage *= damageMultiplier * Constant.AttackDamageFactor + property.Value;

        // Apply any shields
        foreach (Buff buff in target.Buffs.EnumerateBuffs().Where(buff => buff.Metadata.Shield != null)) {
            if (buff.ShieldHealth >= attackDamage) {
                buff.ShieldHealth -= (long) attackDamage;
                attackDamage = 0;
                target.Field.Broadcast(BuffPacket.Update(buff, BuffFlag.UpdateBuff));
                return (DamageType.Block, (long) attackDamage);
            }

            attackDamage -= buff.ShieldHealth;
            target.Buffs.Remove(buff.Id, target.ObjectId);
        }

        return (damageType, (long) Math.Max(1, attackDamage));
    }
}
