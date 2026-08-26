using UnityEngine;

namespace MAP_MechanoidMechanitor
{
    public static class JusticeBossDifficultyValues
    {
        public const bool DefaultEnableMortarShield = true;
        public const bool DefaultEnableBulletShield = true;

        public const int DefaultAutoMortarCount = 3;
        public const int DefaultAutoChargeBlasterCount = 3;
        public const int DefaultAutoInfernoCount = 3;

        public const int DefaultTotalWaves = 11;
        public const int DefaultWaveIntervalTicks = 1250;
        public const int DefaultMechsPerWave = 5;

        public const bool DefaultAllowBossReplacement = true;

        public const bool DefaultApplyMobileCombatToSummons = true;

        public const int MinTurretCount = 0;
        public const int MaxTurretCount = 20;

        public const int MinTotalWaves = 1;
        public const int MaxTotalWaves = 50;

        public const int MinWaveIntervalTicks = 300;
        public const int MaxWaveIntervalTicks = 7200;
        public const int WaveIntervalStepTicks = 50;

        public const int MinMechsPerWave = 1;
        public const int MaxMechsPerWave = 50;

        public static int ClampTurretCount(int value)
        {
            return Mathf.Clamp(value, MinTurretCount, MaxTurretCount);
        }

        public static int ClampTotalWaves(int value)
        {
            return Mathf.Clamp(value, MinTotalWaves, MaxTotalWaves);
        }

        public static int ClampWaveIntervalTicks(int value)
        {
            int clamped = Mathf.Clamp(
                value,
                MinWaveIntervalTicks,
                MaxWaveIntervalTicks);

            int stepped = Mathf.RoundToInt(
                clamped / (float)WaveIntervalStepTicks)
                * WaveIntervalStepTicks;

            return Mathf.Clamp(
                stepped,
                MinWaveIntervalTicks,
                MaxWaveIntervalTicks);
        }

        public static int ClampMechsPerWave(int value)
        {
            return Mathf.Clamp(
                value,
                MinMechsPerWave,
                MaxMechsPerWave);
        }
    }
}
