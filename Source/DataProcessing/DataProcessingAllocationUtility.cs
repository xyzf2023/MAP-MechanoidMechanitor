using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class DataProcessingAllocationUtility
    {
        public const float StepPercent = 0.05f;
        public const float MinReservedConsciousness = 0.50f;
        public const int MinReservedConsciousnessPercent = 50;
        public const int MinConsciousnessPercentToAddStep = 55;

        /// <summary>5%：脱离指挥范围。</summary>
        public const int CommandRangeThresholdSteps = 1;

        /// <summary>15%：完整虚拟旅行节点。</summary>
        public const int TravelNodeThresholdSteps = 3;

        /// <summary>15%：穿梭机驾驶资格。</summary>
        public const int ShuttlePilotThresholdSteps = 3;

        /// <summary>特殊数值效果最多按 100%（20 档）计算；意识加成不受此限制。</summary>
        public const int MaxSpecialEffectSteps = 20;

        public const float WorkSpeedOffsetPerStep = 0.15f;
        public const float AttackTimingFactorReductionPerTenPercentTier = 0.05f;
        public const float MoveSpeedOffsetPerTenPercentTier = 0.5f;
        public const float IncomingDamageReductionPerTenPercentTier = 0.05f;
        public const float StaggerFactorReductionPerStep = 0.10f;
        public const int MoveSpeedMinEffectSteps = 4;
        public const int IncomingDamageMinEffectSteps = 6;

        /// <summary>20%：开始缩短抑止时间。</summary>
        public const int StaggerMinEffectSteps = 4;

        /// <summary>50%：完全免疫原版抑止。</summary>
        public const int StaggerImmunitySteps = 10;

        private const string DataStreamDistributionDefName = "MAP_DataStreamDistribution";
        private const string CommandFocusDefName = "MAP_CommandFocus";

        private static HediffDef? dataStreamDistributionDef;
        private static HediffDef? commandFocusDef;

        public static HediffDef? DataStreamDistributionDef =>
            dataStreamDistributionDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(DataStreamDistributionDefName);

        public static HediffDef? CommandFocusDef =>
            commandFocusDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(CommandFocusDefName);

        public static float StepsToPercent(int steps)
        {
            return steps * StepPercent;
        }

        public static string FormatPercentDelta(int steps, bool positive)
        {
            int percent = steps * 5;
            return positive ? $"+{percent}%" : $"-{percent}%";
        }

        /// <summary>
        /// 按实际百分点格式化意识偏移；整数不强制小数，半百分点保留一位。
        /// </summary>
        public static string FormatPercentDelta(float percentPoints, bool positive)
        {
            string body = FormatPercentPoints(Mathf.Abs(percentPoints));
            return positive ? $"+{body}%" : $"-{body}%";
        }

        public static string FormatPercentPoints(float percentPoints)
        {
            float tenths = Mathf.Round(Mathf.Abs(percentPoints) * 10f) / 10f;
            if (Mathf.Approximately(tenths % 1f, 0f))
            {
                return Mathf.RoundToInt(tenths).ToString();
            }

            return tenths.ToString("0.0");
        }

        public static bool IsSelfAllocationPair(Pawn? overseer, Pawn? target)
        {
            return overseer != null
                && target != null
                && ReferenceEquals(overseer, target);
        }

        public static int GetSpecialEffectSteps(int steps)
        {
            return Mathf.Clamp(steps, 0, MaxSpecialEffectSteps);
        }

        public static float GetWorkSpeedOffset(int steps)
        {
            return GetSpecialEffectSteps(steps) * WorkSpeedOffsetPerStep;
        }

        /// <summary>
        /// 瞄准时间、远程冷却与近战冷却共用的时间系数。
        /// </summary>
        public static float GetAttackTimingFactor(int steps)
        {
            int effectSteps = GetSpecialEffectSteps(steps);
            return 1f
                - effectSteps / 2 * AttackTimingFactorReductionPerTenPercentTier;
        }

        public static float GetMoveSpeedOffset(int steps)
        {
            int effectSteps = GetSpecialEffectSteps(steps);
            return effectSteps >= MoveSpeedMinEffectSteps
                ? effectSteps / 2 * MoveSpeedOffsetPerTenPercentTier
                : 0f;
        }

        public static float GetIncomingDamageFactor(int steps)
        {
            int effectSteps = GetSpecialEffectSteps(steps);
            return effectSteps >= IncomingDamageMinEffectSteps
                ? 1f - effectSteps / 2 * IncomingDamageReductionPerTenPercentTier
                : 1f;
        }

        /// <summary>
        /// 抑止持续时间倍率。20% 起效时直接为 ×60%，50% 起为 ×0%。
        /// 与攻击时序系数相互独立。
        /// </summary>
        public static float GetStaggerDurationFactor(int steps)
        {
            int normalizedSteps = Mathf.Max(0, steps);
            if (normalizedSteps < StaggerMinEffectSteps)
            {
                return 1f;
            }

            int effectiveSteps = Mathf.Min(normalizedSteps, StaggerImmunitySteps);
            return Mathf.Max(0f, 1f - effectiveSteps * StaggerFactorReductionPerStep);
        }

        public static float GetCurrentConsciousness(Pawn? pawn)
        {
            TryGetCurrentConsciousness(pawn, out float consciousness);
            return consciousness;
        }

        /// <summary>
        /// 尝试读取意识容量。失败时 <paramref name="consciousness"/> 为安全默认值 0，并返回 false。
        /// </summary>
        public static bool TryGetCurrentConsciousness(Pawn? pawn, out float consciousness)
        {
            consciousness = 0f;
            if (pawn?.health?.capacities == null)
            {
                return false;
            }

            try
            {
                float level = pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
                if (float.IsNaN(level) || float.IsInfinity(level))
                {
                    Log.ErrorOnce(
                        "[MAP-机械族机械师] 读取意识容量得到无效数值：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}），value={level}。",
                        BuildConsciousnessReadFailureLogKey(pawn));
                    return false;
                }

                consciousness = level;
                return true;
            }
            catch (System.Exception ex)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 读取意识容量失败：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}",
                    BuildConsciousnessReadFailureLogKey(pawn));
                return false;
            }
        }

        private const int ConsciousnessReadFailureLogKeyBase = 0x4D415043; // "MAPC"

        private static int BuildConsciousnessReadFailureLogKey(Pawn pawn)
        {
            return unchecked(ConsciousnessReadFailureLogKeyBase + pawn.thingIDNumber);
        }

        public static int GetCurrentConsciousnessPercent(Pawn? pawn)
        {
            return Mathf.RoundToInt(GetCurrentConsciousness(pawn) * 100f);
        }

        public static int GetAdditionalAssignableSteps(Pawn? overseer)
        {
            int availablePercent =
                GetCurrentConsciousnessPercent(overseer) - MinReservedConsciousnessPercent;
            return availablePercent >= 5 ? availablePercent / 5 : 0;
        }

        public static bool CanAddStep(Pawn? overseer)
        {
            return GetCurrentConsciousnessPercent(overseer)
                >= MinConsciousnessPercentToAddStep;
        }

        public static bool HasCommandRangeBypass(Pawn? mech)
        {
            if (!ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked())
            {
                return false;
            }

            if (mech == null || !ModsConfig.BiotechActive)
            {
                return false;
            }

            if (mech.Faction == null || !mech.Faction.IsPlayerSafe())
            {
                return false;
            }

            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            return registry != null
                && registry.HasAtLeast(mech, CommandRangeThresholdSteps);
        }

        public static bool HasVirtualTravelNode(Pawn? pawn)
        {
            if (!ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked())
            {
                return false;
            }

            if (pawn == null || !ModsConfig.BiotechActive)
            {
                return false;
            }

            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            return registry != null
                && registry.HasAtLeast(pawn, TravelNodeThresholdSteps);
        }

        public static bool HasShuttlePilotAllocation(Pawn? pawn)
        {
            if (!ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked())
            {
                return false;
            }

            if (pawn == null || !ModsConfig.BiotechActive)
            {
                return false;
            }

            if (!pawn.RaceProps.IsMechanoid)
            {
                return false;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return false;
            }

            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            return registry != null
                && registry.HasAtLeast(pawn, ShuttlePilotThresholdSteps);
        }

        public static bool IsValidAllocationPair(Pawn? overseer, Pawn? target)
        {
            if (!ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked())
            {
                return false;
            }

            if (overseer == null
                || target == null
                || overseer.Dead
                || target.Dead
                || overseer.Destroyed
                || target.Destroyed)
            {
                return false;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(overseer)
                || overseer.mechanitor == null
                || !target.RaceProps.IsMechanoid)
            {
                return false;
            }

            bool isSelf = IsSelfAllocationPair(overseer, target);
            if (isSelf)
            {
                if (!ResearchFeatureUnlockUtility.IsSelfDirectiveFocusUnlocked())
                {
                    return false;
                }
            }
            else if (target.GetOverseer() != overseer)
            {
                return false;
            }

            if (overseer.Faction == null
                || !overseer.Faction.IsPlayerSafe()
                || target.Faction == null
                || !target.Faction.IsPlayerSafe())
            {
                return false;
            }

            return true;
        }
    }
}
