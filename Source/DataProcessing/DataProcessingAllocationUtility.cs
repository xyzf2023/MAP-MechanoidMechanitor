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

        /// <summary>特化数值效果最多按 200%（40 档）计算；意识加成不受此限制。</summary>
        public const int MaxSpecializationEffectSteps = 40;

        private const string DataStreamDistributionDefName = "MAP_DataStreamDistribution";
        private const string LegacyCommandFocusDefName = "MAP_CommandFocus";
        private const string GeneralTuningDefName = "MAP_CommandFocus_GeneralTuning";
        private const string ProductionCoordinationDefName = "MAP_CommandFocus_ProductionCoordination";
        private const string FireControlCalculationDefName = "MAP_CommandFocus_FireControlCalculation";
        private const string AssaultProtocolDefName = "MAP_CommandFocus_AssaultProtocol";

        private static HediffDef? dataStreamDistributionDef;
        private static HediffDef? generalTuningDef;
        private static HediffDef? productionCoordinationDef;
        private static HediffDef? fireControlCalculationDef;
        private static HediffDef? assaultProtocolDef;

        public static HediffDef? DataStreamDistributionDef =>
            dataStreamDistributionDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(DataStreamDistributionDefName);

        public static HediffDef? GeneralTuningDef =>
            generalTuningDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(GeneralTuningDefName);

        public static HediffDef? ProductionCoordinationDef =>
            productionCoordinationDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(ProductionCoordinationDefName);

        public static HediffDef? FireControlCalculationDef =>
            fireControlCalculationDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(FireControlCalculationDefName);

        public static HediffDef? AssaultProtocolDef =>
            assaultProtocolDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(AssaultProtocolDefName);

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

        /// <summary>
        /// 将任意枚举值规范化为已知特化；无效值统一回退为 GeneralTuning。
        /// </summary>
        public static DataProcessingSpecialization NormalizeSpecialization(
            DataProcessingSpecialization specialization)
        {
            switch (specialization)
            {
                case DataProcessingSpecialization.GeneralTuning:
                case DataProcessingSpecialization.ProductionCoordination:
                case DataProcessingSpecialization.FireControlCalculation:
                case DataProcessingSpecialization.AssaultProtocol:
                    return specialization;
                default:
                    return DataProcessingSpecialization.GeneralTuning;
            }
        }

        /// <summary>
        /// 特化成长比例：0~200%（0~40 档）线性，超过封顶为 1。
        /// </summary>
        public static float GetSpecializationProgress(int steps)
        {
            int effectiveSteps = Mathf.Clamp(steps, 0, MaxSpecializationEffectSteps);
            return effectiveSteps / (float)MaxSpecializationEffectSteps;
        }

        public static float GetWorkSpeedOffset(
            int steps,
            DataProcessingSpecialization specialization)
        {
            float progress = GetSpecializationProgress(steps);
            specialization = NormalizeSpecialization(specialization);

            switch (specialization)
            {
                case DataProcessingSpecialization.GeneralTuning:
                    return GeneralMaxWorkSpeedOffset * progress;
                case DataProcessingSpecialization.ProductionCoordination:
                    return ProductionMaxWorkSpeedOffset * progress;
                default:
                    return 0f;
            }
        }

        public static float GetMoveSpeedOffset(
            int steps,
            DataProcessingSpecialization specialization)
        {
            float progress = GetSpecializationProgress(steps);
            specialization = NormalizeSpecialization(specialization);

            switch (specialization)
            {
                case DataProcessingSpecialization.GeneralTuning:
                    return GeneralMaxMoveSpeedOffset * progress;
                case DataProcessingSpecialization.ProductionCoordination:
                    return ProductionMaxMoveSpeedOffset * progress;
                case DataProcessingSpecialization.FireControlCalculation:
                    return FireControlMaxMoveSpeedOffset * progress;
                case DataProcessingSpecialization.AssaultProtocol:
                    return AssaultMaxMoveSpeedOffset * progress;
                default:
                    return 0f;
            }
        }

        public static float GetAimingDelayFactor(
            int steps,
            DataProcessingSpecialization specialization)
        {
            float progress = GetSpecializationProgress(steps);
            specialization = NormalizeSpecialization(specialization);

            switch (specialization)
            {
                case DataProcessingSpecialization.GeneralTuning:
                    return Mathf.Lerp(1f, GeneralMinAimingDelayFactor, progress);
                case DataProcessingSpecialization.FireControlCalculation:
                    return Mathf.Lerp(1f, FireControlMinAimingDelayFactor, progress);
                default:
                    return 1f;
            }
        }

        public static float GetRangedCooldownFactor(
            int steps,
            DataProcessingSpecialization specialization)
        {
            float progress = GetSpecializationProgress(steps);
            specialization = NormalizeSpecialization(specialization);

            switch (specialization)
            {
                case DataProcessingSpecialization.GeneralTuning:
                    return Mathf.Lerp(1f, GeneralMinRangedCooldownFactor, progress);
                case DataProcessingSpecialization.FireControlCalculation:
                    return Mathf.Lerp(1f, FireControlMinRangedCooldownFactor, progress);
                default:
                    return 1f;
            }
        }

        public static float GetMeleeCooldownFactor(
            int steps,
            DataProcessingSpecialization specialization)
        {
            float progress = GetSpecializationProgress(steps);
            specialization = NormalizeSpecialization(specialization);

            switch (specialization)
            {
                case DataProcessingSpecialization.GeneralTuning:
                    return Mathf.Lerp(1f, GeneralMinMeleeCooldownFactor, progress);
                case DataProcessingSpecialization.AssaultProtocol:
                    return Mathf.Lerp(1f, AssaultMinMeleeCooldownFactor, progress);
                default:
                    return 1f;
            }
        }

        public static float GetIncomingDamageFactor(
            int steps,
            DataProcessingSpecialization specialization)
        {
            float progress = GetSpecializationProgress(steps);
            specialization = NormalizeSpecialization(specialization);

            switch (specialization)
            {
                case DataProcessingSpecialization.GeneralTuning:
                    return Mathf.Lerp(1f, GeneralMinIncomingDamageFactor, progress);
                case DataProcessingSpecialization.AssaultProtocol:
                    return Mathf.Lerp(1f, AssaultMinIncomingDamageFactor, progress);
                default:
                    return 1f;
            }
        }

        public static float GetStaggerDurationFactor(
            int steps,
            DataProcessingSpecialization specialization)
        {
            specialization = NormalizeSpecialization(specialization);

            switch (specialization)
            {
                case DataProcessingSpecialization.GeneralTuning:
                {
                    float progress = GetSpecializationProgress(steps);
                    return Mathf.Lerp(1f, GeneralMinStaggerDurationFactor, progress);
                }

                case DataProcessingSpecialization.AssaultProtocol:
                {
                    int effectiveSteps = Mathf.Clamp(
                        steps,
                        0,
                        AssaultStaggerImmunitySteps);

                    float factor = 1f - effectiveSteps * 0.05f;
                    return Mathf.Max(AssaultMinStaggerDurationFactor, factor);
                }

                default:
                    return 1f;
            }
        }

        public static float GetMechEnergyUsageFactor(
            int steps,
            DataProcessingSpecialization specialization)
        {
            float progress = GetSpecializationProgress(steps);
            specialization = NormalizeSpecialization(specialization);

            if (specialization != DataProcessingSpecialization.ProductionCoordination)
            {
                return 1f;
            }

            return Mathf.Lerp(1f, ProductionMinMechEnergyUsageFactor, progress);
        }

        // 通用调谐：40档，即200%时达到最终上限。
        public const float GeneralMaxWorkSpeedOffset = 2.00f;
        public const float GeneralMaxMoveSpeedOffset = 4.00f;
        public const float GeneralMinAimingDelayFactor = 0.60f;
        public const float GeneralMinRangedCooldownFactor = 0.60f;
        public const float GeneralMinMeleeCooldownFactor = 0.60f;
        public const float GeneralMinIncomingDamageFactor = 0.60f;
        public const float GeneralMinStaggerDurationFactor = 0.60f;

        // 生产统筹：40档，即200%时达到最终上限。
        public const float ProductionMaxWorkSpeedOffset = 4.00f;
        public const float ProductionMaxMoveSpeedOffset = 4.00f;
        public const float ProductionMinMechEnergyUsageFactor = 0.60f;

        // 火控演算：40档，即200%时达到最终上限。
        public const float FireControlMinAimingDelayFactor = 0.20f;
        public const float FireControlMinRangedCooldownFactor = 0.20f;
        public const float FireControlMaxMoveSpeedOffset = 2.00f;

        // 强袭协议的普通属性：40档，即200%时达到最终上限。
        public const float AssaultMinMeleeCooldownFactor = 0.20f;
        public const float AssaultMaxMoveSpeedOffset = 4.00f;
        public const float AssaultMinIncomingDamageFactor = 0.20f;

        // 强袭协议的抑止时间：20档，即100%时达到完全免疫。
        public const int AssaultStaggerImmunitySteps = 20;
        public const float AssaultMinStaggerDurationFactor = 0f;

        /// <summary>
        /// 返回指定特化对应的指令聚焦 HediffDef；未知特化回退通用调谐。
        /// </summary>
        public static HediffDef? GetCommandFocusDef(DataProcessingSpecialization specialization)
        {
            specialization = NormalizeSpecialization(specialization);
            switch (specialization)
            {
                case DataProcessingSpecialization.GeneralTuning:
                    return GeneralTuningDef;
                case DataProcessingSpecialization.ProductionCoordination:
                    return ProductionCoordinationDef;
                case DataProcessingSpecialization.FireControlCalculation:
                    return FireControlCalculationDef;
                case DataProcessingSpecialization.AssaultProtocol:
                    return AssaultProtocolDef;
                default:
                    return GeneralTuningDef;
            }
        }

        /// <summary>
        /// 由 HediffDef 反推特化；旧 MAP_CommandFocus 视为通用调谐。
        /// </summary>
        public static DataProcessingSpecialization GetSpecializationForHediffDef(HediffDef? def)
        {
            if (def == null)
            {
                return DataProcessingSpecialization.GeneralTuning;
            }

            switch (def.defName)
            {
                case GeneralTuningDefName:
                    return DataProcessingSpecialization.GeneralTuning;
                case ProductionCoordinationDefName:
                    return DataProcessingSpecialization.ProductionCoordination;
                case FireControlCalculationDefName:
                    return DataProcessingSpecialization.FireControlCalculation;
                case AssaultProtocolDefName:
                    return DataProcessingSpecialization.AssaultProtocol;
                case LegacyCommandFocusDefName:
                    return DataProcessingSpecialization.GeneralTuning;
                default:
                    return DataProcessingSpecialization.GeneralTuning;
            }
        }

        /// <summary>
        /// 是否为任意指令聚焦类 HediffDef（旧 Def 或四种新特化 Def）。
        /// </summary>
        public static bool IsAnyCommandFocusDef(HediffDef? def)
        {
            if (def == null)
            {
                return false;
            }

            switch (def.defName)
            {
                case LegacyCommandFocusDefName:
                case GeneralTuningDefName:
                case ProductionCoordinationDefName:
                case FireControlCalculationDefName:
                case AssaultProtocolDefName:
                    return true;
                default:
                    return false;
            }
        }

        internal static string GetSpecializationTip(DataProcessingSpecialization specialization)
        {
            specialization = DataProcessingAllocationUtility.NormalizeSpecialization(specialization);
            switch (specialization)
            {
                case DataProcessingSpecialization.GeneralTuning:
                    return "MAP_MechanoidMechanitor.DataProcessing.Specialization.GeneralTuning.Tooltip".Translate();
                case DataProcessingSpecialization.ProductionCoordination:
                    return "MAP_MechanoidMechanitor.DataProcessing.Specialization.ProductionCoordination.Tooltip".Translate();
                case DataProcessingSpecialization.FireControlCalculation:
                    return "MAP_MechanoidMechanitor.DataProcessing.Specialization.FireControlCalculation.Tooltip".Translate();
                case DataProcessingSpecialization.AssaultProtocol:
                    return "MAP_MechanoidMechanitor.DataProcessing.Specialization.AssaultProtocol.Tooltip".Translate();
                default:
                    return "MAP_MechanoidMechanitor.DataProcessing.Specialization.GeneralTuning.Tooltip".Translate();
            }
        }

        public static string GetSpecializationLabel(DataProcessingSpecialization specialization)
        {
            specialization = NormalizeSpecialization(specialization);
            switch (specialization)
            {
                case DataProcessingSpecialization.GeneralTuning:
                    return "MAP_MechanoidMechanitor.DataProcessing.Specialization.GeneralTuning".Translate();
                case DataProcessingSpecialization.ProductionCoordination:
                    return "MAP_MechanoidMechanitor.DataProcessing.Specialization.ProductionCoordination".Translate();
                case DataProcessingSpecialization.FireControlCalculation:
                    return "MAP_MechanoidMechanitor.DataProcessing.Specialization.FireControlCalculation".Translate();
                case DataProcessingSpecialization.AssaultProtocol:
                    return "MAP_MechanoidMechanitor.DataProcessing.Specialization.AssaultProtocol".Translate();
                default:
                    return "MAP_MechanoidMechanitor.DataProcessing.Specialization.GeneralTuning".Translate();
            }
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

            if (!DataProcessingAllocatorEligibilityUtility.IsEligibleDataProcessingOverseer(overseer)
                || overseer.mechanitor == null
                || !target.RaceProps.IsMechanoid)
            {
                return false;
            }

            bool isSelf = IsSelfAllocationPair(overseer, target);
            if (isSelf)
            {
                // 只有机械族机械师允许自我分配；人类接口机械师永远不能自我分配。
                if (!DataProcessingAllocatorEligibilityUtility.CanUseSelfAllocation(overseer)
                    || !ResearchFeatureUnlockUtility.IsSelfDirectiveFocusUnlocked())
                {
                    return false;
                }
            }
            else if (DataProcessingOverseerResolver.GetAllocationOverseer(target) != overseer)
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
