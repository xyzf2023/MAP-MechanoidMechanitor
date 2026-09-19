using UnityEngine;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 主脑（CerebrexCore）作为敌对 BOSS 时使用的额外战斗技能参数集中定义。
    /// 所有默认值、上下限、步进与 Clamp / Normalize 方法都集中在此，
    /// 不在 UI 或控制器中散落范围常量。
    /// 旧存档缺少当前动作参数时使用此处兼容默认值，等同于旧版单目标行为：
    /// 单目标、30 秒持续、50～75 秒冷却。
    /// </summary>
    public static class CerebrexBossDifficultyValues
    {
        // ===== 总开关 =====
        public const bool DefaultEnableExtraSkills = true;

        // ===== 召唤机械族 =====
        public const bool DefaultEnableSummoning = true;
        public const int DefaultSummonIntervalTicks = 900;
        public const int DefaultMechsPerWave = 8;
        public const int DefaultMaxLivingSummonedMechs = 32;
        public const bool DefaultApplyMobileCombatToSummons = true;

        // ===== 带宽干扰 =====
        public const bool DefaultEnableBandwidthInterference = true;
        public const int DefaultBandwidthCooldownMinTicks = 3000;
        public const int DefaultBandwidthCooldownMaxTicks = 4500;
        public const int DefaultBandwidthDurationTicks = 1800;
        public const int DefaultBandwidthMaxTargets = 1;

        // ===== EMP =====
        public const bool DefaultEnableEmp = true;
        public const int DefaultEmpCooldownMinTicks = 2400;
        public const int DefaultEmpCooldownMaxTicks = 3600;
        public const int DefaultEmpBaseDurationTicks = 1200;
        public const float DefaultEmpRadius = 75f;

        // ===== 召唤范围 / 步进 =====
        public const int MinSummonIntervalTicks = 300;
        public const int MaxSummonIntervalTicks = 7200;
        public const int SummonIntervalStepTicks = 60;

        public const int MinMechsPerWave = 1;
        public const int MaxMechsPerWave = 50;

        public const int MinMaxLivingSummonedMechs = 1;
        public const int MaxMaxLivingSummonedMechs = 100;

        // ===== 带宽范围 / 步进 =====
        public const int MinBandwidthCooldownTicks = 300;
        public const int MaxBandwidthCooldownTicks = 18000;
        public const int BandwidthCooldownStepTicks = 60;

        public const int MinBandwidthDurationTicks = 60;
        public const int MaxBandwidthDurationTicks = 7200;
        public const int BandwidthDurationStepTicks = 60;

        public const int MinBandwidthMaxTargets = 1;
        public const int MaxBandwidthMaxTargets = 20;

        // ===== EMP 范围 / 步进 =====
        public const int MinEmpCooldownTicks = 300;
        public const int MaxEmpCooldownTicks = 18000;
        public const int EmpCooldownStepTicks = 60;

        public const int MinEmpBaseDurationTicks = 60;
        public const int MaxEmpBaseDurationTicks = 7200;
        public const int EmpBaseDurationStepTicks = 60;

        public const float MinEmpRadius = 10f;
        public const float MaxEmpRadius = 150f;
        public const float EmpRadiusStep = 5f;

        /// <summary>
        /// 把数值钳制到 [min, max] 并按 step 对齐（保证 Rand.RangeInclusive 不会收到反向范围）。
        /// </summary>
        public static int ClampStep(int value, int min, int max, int step)
        {
            if (step <= 0)
            {
                step = 1;
            }

            int clamped = Mathf.Clamp(value, min, max);
            int stepped = Mathf.RoundToInt((float)clamped / step) * step;
            return Mathf.Clamp(stepped, min, max);
        }

        public static int ClampSummonIntervalTicks(int value)
        {
            return ClampStep(
                value,
                MinSummonIntervalTicks,
                MaxSummonIntervalTicks,
                SummonIntervalStepTicks);
        }

        public static int ClampMechsPerWave(int value)
        {
            return Mathf.Clamp(value, MinMechsPerWave, MaxMechsPerWave);
        }

        public static int ClampMaxLivingSummonedMechs(int value)
        {
            return Mathf.Clamp(
                value,
                MinMaxLivingSummonedMechs,
                MaxMaxLivingSummonedMechs);
        }

        public static int ClampBandwidthDurationTicks(int value)
        {
            return ClampStep(
                value,
                MinBandwidthDurationTicks,
                MaxBandwidthDurationTicks,
                BandwidthDurationStepTicks);
        }

        public static int ClampBandwidthMaxTargets(int value)
        {
            return Mathf.Clamp(value, MinBandwidthMaxTargets, MaxBandwidthMaxTargets);
        }

        public static int ClampEmpBaseDurationTicks(int value)
        {
            return ClampStep(
                value,
                MinEmpBaseDurationTicks,
                MaxEmpBaseDurationTicks,
                EmpBaseDurationStepTicks);
        }

        public static float ClampEmpRadius(float value)
        {
            float clamped = Mathf.Clamp(value, MinEmpRadius, MaxEmpRadius);
            // 与 UI 5 格步进保持一致：读取、界面、战斗快照三层都得到一致值。
            float stepped = Mathf.Round(clamped / EmpRadiusStep) * EmpRadiusStep;
            return Mathf.Clamp(stepped, MinEmpRadius, MaxEmpRadius);
        }

        /// <summary>
        /// 安全归一化冷却区间：分别钳制到合法范围，并保证最短冷却不大于最长冷却。
        /// </summary>
        public static (int min, int max) ClampBandwidthCooldownRange(int min, int max)
        {
            int clampedMin = ClampStep(
                min,
                MinBandwidthCooldownTicks,
                MaxBandwidthCooldownTicks,
                BandwidthCooldownStepTicks);
            int clampedMax = ClampStep(
                max,
                MinBandwidthCooldownTicks,
                MaxBandwidthCooldownTicks,
                BandwidthCooldownStepTicks);
            if (clampedMin > clampedMax)
            {
                int swap = clampedMin;
                clampedMin = clampedMax;
                clampedMax = swap;
            }

            return (clampedMin, clampedMax);
        }

        /// <summary>
        /// 安全归一化冷却区间：分别钳制到合法范围，并保证最短冷却不大于最长冷却。
        /// </summary>
        public static (int min, int max) ClampEmpCooldownRange(int min, int max)
        {
            int clampedMin = ClampStep(
                min,
                MinEmpCooldownTicks,
                MaxEmpCooldownTicks,
                EmpCooldownStepTicks);
            int clampedMax = ClampStep(
                max,
                MinEmpCooldownTicks,
                MaxEmpCooldownTicks,
                EmpCooldownStepTicks);
            if (clampedMin > clampedMax)
            {
                int swap = clampedMin;
                clampedMin = clampedMax;
                clampedMax = swap;
            }

            return (clampedMin, clampedMax);
        }
    }
}
