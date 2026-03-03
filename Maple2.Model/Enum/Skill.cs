using System.ComponentModel;

namespace Maple2.Model.Enum;

public enum SkillType : byte {
    [Description("s_word_skill_active: Active")]
    Active = 0,
    [Description("s_word_skill_passive: Passive")]
    Passive = 1,
    [Description("s_word_skill_move: Action")]
    Action = 2, // Literally only 11000054
    [Description("")]
    Consumable = 3,
}

public enum SkillSubType : byte {
    Type0 = 0,
    Type1 = 1,
    Type2 = 2,
    Type3 = 3,
    Type4 = 4,
    Type5 = 5,
    Type6 = 6,
    Type7 = 7,
    Type8 = 8,
    Type9 = 9,
}

public enum RangeType : byte {
    [Description("")]
    None = 0,
    [Description("s_word_nearrange: Close Range")]
    Melee = 1,
    [Description("s_word_longrange: Long Range")]
    Range = 2,
    [Description("Invalid?")]
    Unknown = 3, // 10200282,10200283,35001301
}

public enum AttackType : byte {
    None = 0,
    [Description("s_word_element_physics: Physical")]
    Physical = 1,
    [Description("s_word_magic: Magic")]
    Magic = 2,
    All = 3,
}

public enum Element : byte {
    None = 0,
    Fire = 1,
    Ice = 2,
    Electric = 3,
    Holy = 4,
    Dark = 5,
    Poison = 6,
    Physical = 7,
}

public enum SkillGroupType : byte {
    None = 0,
    Default = 1,
    Lapenshard = 2,
}

public enum SkillRank : byte {
    Basic = 0,
    Awakening = 1,
    Both = 2,
}

public enum SkillRegion {
    None = 0,
    Box = 1,
    Cylinder = 2,
    Frustum = 3,
    HoleCylinder = 4,
}

public enum SkillTargetType {
    None = 0,
    Owner = 1,
    Target = 2,
    Caster = 3,
    PetOwner = 4,
    Attacker = 5,
    RegionBuff = 6,
    RegionDebuff = 7,
    RegionPet = 8,
}

public enum IncludeCasterType {
    Exclude = 0,
    Priority = 1,
    Last = 2,
}

public enum DotTargetType {
    Caster = 0,
    Owner = 1,
}

public enum ApplyTargetType {
    None = 0,
    Hostile = 1,
    Friendly = 2,
    Player1 = 3, // Unknown,
    RegionBuff = 5, // Includes speed pads in Crazy Runners
    RegionBuff2 = 6, // Includes Healing spots
    Player4 = 7, // Unknown, Debuff (Archeon's ice bombs)
    HungryMobs = 8,
}

public enum SkillTarget {
    Self = 0,
    Owner = 1,
    Target = 2,
}

public enum SkillOwner {
    Self = 0, // ?
    Caster = 1,
    RegionOwner = 5,
}

//   1:
//   2: If you're attacked within 18 sec, grants Counterattack Chance, increasing damage by 3%.
//   4: If you're attacked within 18 sec, grants Counterattack Chance, increasing damage by 3%.
//   5: Casting Flame Tornado temporarily grants Flame Imp.
//   6: The eagle's majesty inspires. Restores 1 spirit every sec. The eagle also attacks on hit.
//   7: Blocking an attack has a 40% chance to grant Counterattack Chance, increasing all damage by 3% for 18 sec.
//  10: At 5 stacks, activates ultimate curse.
//  11: Greatly increases movement speed for 3 sec when investigating nearby objects.
//  13: Can be used while Meridian Flow III is active.\n\nDeals 1668% damage and removes Meridian Flow III.
//  14: 10700021, Empowered while Cunning is active.
//      Grants a Deflector Shield that absorbs damage equal to 30% of your max health.
//  16: 10200261, Triggers Death Rattle when one of the following conditions is met: (Conditions: 4, 6, 16)
//      - You use a skill that consumes Dark Aura while it is at 10 stacks (16)
//      - You are surrounded by 8 or more enemies (6)
//      - Your health is reduced to 10% or below. (4)
//  17: EventEffectId
// [Bonus Effects]\nConsume 1 Awakened Mantra Core to instantly charge to 5.\nUse Vision Torrent to turn this skill into Vision Spirit Burn.\nSoul Exhaustion prevents the use of Spirit Burn for 10 sec.\nVision Amplification increases magic damage by 12% when Vision Torrent is active.
//
//
//  18: 90051156, Increases movement speed for 10 sec after Mining, Foraging, Ranching, or Farming.
//  19: 61100139, Defense +2% for 10 sec when owner's attack misses
//  20: EventSkillId
// 102: 10300271, Gains a stack when Frost is inflicted.
// 103: 11000266, Deals another hit for 500% damage after hitting a target all 9 times.

//   1:
//   2:
//   4: Player Attacked
//   5: Skill Casted? (See: InvokeEffectProperty)
//   6: Enemies Nearby
//   7: Blocked Attack
//  10: Buff Stacks
//  11: Investigating Objects
//  13:
//  14:
//  16:
//  17:
//  18: Life Skills (Mining, Foraging, Ranching, or Farming)
//  19: Attack Misses
//  20:
// 102: Frost Inflicted (10300271)
// 103: Target Hit All 9x (11000266)
// -----
// All: 1,2,4,5,6,7,10,11,13,14,16,17,18,19,20,102,103
// IgnoreOwner => 1,2,4,5,6,7,10,16,17,18
// SkillIds => 4,6,7,14,20
// BuffIds => 16,17,102
public enum EventConditionType {
    Activate = 0, // always 0 in skills
    Tick = 0,
    OnEvade = 1, // owner
    OnBlock = 2, // owner
    OnAttacked = 4, // owner, target
    OnOwnerAttackCrit = 5, // owner
    OnOwnerAttackHit = 6, // owner
    OnSkillCasted = 7, // owner, caster

    OnBuffStacksReached = 10, // owner, caster
    OnInvestigate = 11, // owner. not fired in homes
    OnDeath = 13, // owner
    OnSkillCastEnd = 14, // owner. unsure
    OnEffectApplied = 16, // owner
    OnEffectRemoved = 17, // owner
    OnLifeSkillGather = 18, // owner
    OnAttackMiss = 19, // owner,
    OnEmote = 20, // owner
    UnknownWizardEvent = 102,
    UnknownStrikerEvent = 103, // owner
}

public enum CompulsionType {
    None = 0,
    Hit = 1,
    Critical = 2,
    Interrupt = 3, // unconfirmed
}

public enum TargetType {
    Self = 0, // used on skill attacks whos sole purpose is deploying a region skill/self buff
    Hostile = 1,
    Friendly = 2,
    Player1 = 3, // Unknown,
    Player2 = 5, // Unknown,
    Player3 = 6, // Unknown, Recovery
    Player4 = 7, // Unknown, Debuff (Archeon's ice bombs)

    HungryMobs = 8,
}

public enum BounceType {
    None = 0,
    Range = 1, // within range
    Chain = 2, // Bounce continues as long as another entity is in range of the last bounce
    Pierce = 3, // Linear
    Boomerang = 4, // Shield Toss, Shadow Cutter
    Unknown5 = 5,
}

[Flags]
public enum SuperArmor {
    None = 0,
    StunImmunity = 1,
    KnockbackImmunity = 2,
}
