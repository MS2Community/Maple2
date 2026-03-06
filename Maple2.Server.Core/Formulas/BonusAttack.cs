using Maple2.Model.Enum;

namespace Maple2.Server.Core.Formulas;

public class BonusAttack {
    public static double Coefficient(int rightHandRarity, int leftHandRarity, JobCode jobCode) {
        if (rightHandRarity == 0) {
            return 0;
        }

        double weaponBonusAttackCoefficient = RarityMultiplier(rightHandRarity);
        if (leftHandRarity > 0) {
            weaponBonusAttackCoefficient = 0.5 * (weaponBonusAttackCoefficient + RarityMultiplier(leftHandRarity));
        }

        return 4.96 * weaponBonusAttackCoefficient * JobBonusMultiplier(jobCode);
    }

    private static double RarityMultiplier(int rarity) {
        return (rarity) switch {
            1 => 0.26,
            2 => 0.27,
            3 => 0.2883,
            4 => 0.5,
            5 or 6 => 1,
            _ => 0,
        };
    }

    private static double JobBonusMultiplier(JobCode jobCode) {
        return jobCode switch {
            JobCode.Newbie => 1.039,
            JobCode.Knight => 1.105,
            JobCode.Berserker => 1.354,
            JobCode.Wizard => 1.398,
            JobCode.Priest => 0.975,
            JobCode.Archer => 1.143,
            JobCode.HeavyGunner => 1.364,
            JobCode.Thief => 1.151,
            JobCode.Assassin => 1.114,
            JobCode.RuneBlader => 1.259,
            JobCode.Striker => 1.264,
            JobCode.SoulBinder => 1.177,
            _ => 1,
        };
    }
}
