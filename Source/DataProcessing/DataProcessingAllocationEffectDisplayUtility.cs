using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class DataProcessingEffectDisplayEntry
    {
        public readonly string label;
        public readonly string tooltip;
        public readonly bool available;
        public readonly int requiredSteps;

        public DataProcessingEffectDisplayEntry(
            string label,
            string tooltip,
            bool available,
            int requiredSteps = 0)
        {
            this.label = label;
            this.tooltip = tooltip;
            this.available = available;
            this.requiredSteps = requiredSteps;
        }
    }

    /// <summary>
    /// 统一生成数据处理的功能权限与特化数值效果，避免不同窗口各自维护一套描述。
    /// </summary>
    public static class DataProcessingAllocationEffectDisplayUtility
    {
        public static List<DataProcessingEffectDisplayEntry> BuildFunctionalPermissions(int steps)
        {
            List<DataProcessingEffectDisplayEntry> result =
                new List<DataProcessingEffectDisplayEntry>();

            AddPermission(
                result,
                steps,
                DataProcessingAllocationUtility.CommandRangeThresholdSteps,
                "MAP_MechanoidMechanitor.DataProcessing.EffectCommandRange",
                "MAP_MechanoidMechanitor.DataProcessing.EffectCommandRange.Tooltip");

            AddPermission(
                result,
                steps,
                DataProcessingAllocationUtility.TravelNodeThresholdSteps,
                "MAP_MechanoidMechanitor.DataProcessing.EffectTravelLead",
                "MAP_MechanoidMechanitor.DataProcessing.EffectTravelLead.Tooltip");

            if (ModsConfig.OdysseyActive)
            {
                AddPermission(
                    result,
                    steps,
                    DataProcessingAllocationUtility.ShuttlePilotThresholdSteps,
                    "MAP_MechanoidMechanitor.DataProcessing.EffectShuttlePilot",
                    "MAP_MechanoidMechanitor.DataProcessing.EffectShuttlePilot.Tooltip");
            }

            return result;
        }

        public static List<DataProcessingEffectDisplayEntry> BuildSpecializationEffects(
            int steps,
            DataProcessingSpecialization specialization)
        {
            List<DataProcessingEffectDisplayEntry> result =
                new List<DataProcessingEffectDisplayEntry>();

            specialization =
                DataProcessingAllocationUtility.NormalizeSpecialization(specialization);

            float work = DataProcessingAllocationUtility.GetWorkSpeedOffset(steps, specialization);
            float move = DataProcessingAllocationUtility.GetMoveSpeedOffset(steps, specialization);
            float aim = DataProcessingAllocationUtility.GetAimingDelayFactor(steps, specialization);
            float ranged = DataProcessingAllocationUtility.GetRangedCooldownFactor(steps, specialization);
            float melee = DataProcessingAllocationUtility.GetMeleeCooldownFactor(steps, specialization);
            float damage = DataProcessingAllocationUtility.GetIncomingDamageFactor(steps, specialization);
            float stagger = DataProcessingAllocationUtility.GetStaggerDurationFactor(steps, specialization);
            float energy = DataProcessingAllocationUtility.GetMechEnergyUsageFactor(steps, specialization);

            if (work > 0.0001f)
            {
                result.Add(new DataProcessingEffectDisplayEntry(
                    "MAP_MechanoidMechanitor.DataProcessing.EffectWorkSpeed"
                        .Translate(work.ToStringPercent()),
                    string.Empty,
                    true));
            }

            if (move > 0.0001f)
            {
                result.Add(new DataProcessingEffectDisplayEntry(
                    "MAP_MechanoidMechanitor.DataProcessing.EffectMoveSpeed"
                        .Translate(move.ToString("F1")),
                    string.Empty,
                    true));
            }

            AddFactorEffect(
                result,
                aim,
                "MAP_MechanoidMechanitor.DataProcessing.EffectAimingDelay");
            AddFactorEffect(
                result,
                ranged,
                "MAP_MechanoidMechanitor.DataProcessing.EffectRangedCooldown");
            AddFactorEffect(
                result,
                melee,
                "MAP_MechanoidMechanitor.DataProcessing.EffectMeleeCooldown");
            AddFactorEffect(
                result,
                damage,
                "MAP_MechanoidMechanitor.DataProcessing.EffectIncomingDamage");
            AddFactorEffect(
                result,
                stagger,
                "MAP_MechanoidMechanitor.DataProcessing.EffectStaggerDuration");
            AddFactorEffect(
                result,
                energy,
                "MAP_MechanoidMechanitor.DataProcessing.EffectMechEnergyUsage");

            if (result.Count == 0)
            {
                result.Add(new DataProcessingEffectDisplayEntry(
                    "MAP_MechanoidMechanitor.DataProcessing.EffectNone".Translate(),
                    string.Empty,
                    true));
            }

            return result;
        }

        private static void AddPermission(
            List<DataProcessingEffectDisplayEntry> result,
            int steps,
            int requiredSteps,
            string labelKey,
            string tooltipKey)
        {
            bool available = steps >= requiredSteps;
            string baseLabel = labelKey.Translate();
            string label = available
                ? baseLabel
                : "MAP_MechanoidMechanitor.DataProcessing.PermissionLocked".Translate(
                    baseLabel,
                    DataProcessingAllocationUtility.StepsToPercent(requiredSteps).ToStringPercent(),
                    DataProcessingAllocationUtility.StepsToPercent(steps).ToStringPercent());

            result.Add(new DataProcessingEffectDisplayEntry(
                label,
                tooltipKey.Translate(),
                available,
                requiredSteps));
        }

        private static void AddFactorEffect(
            List<DataProcessingEffectDisplayEntry> result,
            float factor,
            string key)
        {
            if (factor >= 0.9999f)
            {
                return;
            }

            result.Add(new DataProcessingEffectDisplayEntry(
                key.Translate(factor.ToStringPercent()),
                string.Empty,
                true));
        }
    }
}
