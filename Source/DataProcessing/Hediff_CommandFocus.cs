using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class Hediff_CommandFocus : Hediff_DataProcessingAllocationBase
    {
        protected override bool IsPositiveOffset => true;

        protected override int GetAllocationSteps()
        {
            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            return registry?.GetStepsForTarget(pawn) ?? 0;
        }

        protected override float GetConsciousnessOffsetMultiplier()
        {
            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;

            return registry?.IsSelfAllocationTarget(pawn) == true
                ? 0.5f
                : 1f;
        }

        protected override int GetAllocationStageVariantKey()
        {
            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;

            return registry?.IsSelfAllocationTarget(pawn) == true ? 1 : 0;
        }

        protected override void ConfigureAdditionalStage(HediffStage stage, int steps)
        {
            DataProcessingSpecialization specialization =
                DataProcessingAllocationUtility.GetSpecializationForHediffDef(def);

            List<StatModifier>? offsets = null;
            List<StatModifier>? factors = null;

            float workSpeedOffset = DataProcessingAllocationUtility.GetWorkSpeedOffset(
                steps,
                specialization);
            if (!Mathf.Approximately(workSpeedOffset, 0f))
            {
                AddOffset(ref offsets, StatDefOf.WorkSpeedGlobal, workSpeedOffset);
            }

            float moveSpeedOffset = DataProcessingAllocationUtility.GetMoveSpeedOffset(
                steps,
                specialization);
            if (!Mathf.Approximately(moveSpeedOffset, 0f))
            {
                AddOffset(ref offsets, StatDefOf.MoveSpeed, moveSpeedOffset);
            }

            float aimingDelayFactor = DataProcessingAllocationUtility.GetAimingDelayFactor(
                steps,
                specialization);
            if (!Mathf.Approximately(aimingDelayFactor, 1f))
            {
                AddFactor(ref factors, StatDefOf.AimingDelayFactor, aimingDelayFactor);
            }

            float rangedCooldownFactor = DataProcessingAllocationUtility.GetRangedCooldownFactor(
                steps,
                specialization);
            if (!Mathf.Approximately(rangedCooldownFactor, 1f))
            {
                AddFactor(ref factors, StatDefOf.RangedCooldownFactor, rangedCooldownFactor);
            }

            float meleeCooldownFactor = DataProcessingAllocationUtility.GetMeleeCooldownFactor(
                steps,
                specialization);
            if (!Mathf.Approximately(meleeCooldownFactor, 1f))
            {
                AddFactor(ref factors, StatDefOf.MeleeCooldownFactor, meleeCooldownFactor);
            }

            float incomingDamageFactor = DataProcessingAllocationUtility.GetIncomingDamageFactor(
                steps,
                specialization);
            if (!Mathf.Approximately(incomingDamageFactor, 1f))
            {
                AddFactor(ref factors, StatDefOf.IncomingDamageFactor, incomingDamageFactor);
            }

            float staggerDurationFactor = DataProcessingAllocationUtility.GetStaggerDurationFactor(
                steps,
                specialization);
            if (!Mathf.Approximately(staggerDurationFactor, 1f))
            {
                AddFactor(ref factors, StatDefOf.StaggerDurationFactor, staggerDurationFactor);
            }

            float mechEnergyUsageFactor = DataProcessingAllocationUtility.GetMechEnergyUsageFactor(
                steps,
                specialization);
            if (!Mathf.Approximately(mechEnergyUsageFactor, 1f))
            {
                AddFactor(ref factors, StatDefOf.MechEnergyUsageFactor, mechEnergyUsageFactor);
            }

            stage.statOffsets = offsets;
            stage.statFactors = factors;
        }

        private static void AddOffset(
            ref List<StatModifier>? offsets,
            StatDef stat,
            float value)
        {
            offsets ??= new List<StatModifier>();
            offsets.Add(new StatModifier
            {
                stat = stat,
                value = value
            });
        }

        private static void AddFactor(
            ref List<StatModifier>? factors,
            StatDef stat,
            float value)
        {
            factors ??= new List<StatModifier>();
            factors.Add(new StatModifier
            {
                stat = stat,
                value = value
            });
        }

        public override string LabelInBrackets
        {
            get
            {
                int steps = GetAllocationSteps();
                if (steps <= 0)
                {
                    return base.LabelInBrackets;
                }

                float percentPoints = steps * 5f * GetConsciousnessOffsetMultiplier();
                return DataProcessingAllocationUtility.FormatPercentDelta(
                    percentPoints,
                    positive: true);
            }
        }
    }
}
