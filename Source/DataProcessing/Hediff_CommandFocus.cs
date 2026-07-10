using System.Collections.Generic;
using RimWorld;
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

        protected override void ConfigureAdditionalStage(HediffStage stage, int steps)
        {
            List<StatModifier>? offsets = null;
            List<StatModifier>? factors = null;

            float workSpeedOffset = DataProcessingAllocationUtility.GetWorkSpeedOffset(steps);
            if (workSpeedOffset > 0f)
            {
                offsets ??= new List<StatModifier>();
                offsets.Add(new StatModifier
                {
                    stat = StatDefOf.WorkSpeedGlobal,
                    value = workSpeedOffset
                });
            }

            float moveSpeedOffset = DataProcessingAllocationUtility.GetMoveSpeedOffset(steps);
            if (moveSpeedOffset > 0f)
            {
                offsets ??= new List<StatModifier>();
                offsets.Add(new StatModifier
                {
                    stat = StatDefOf.MoveSpeed,
                    value = moveSpeedOffset
                });
            }

            float attackTimingFactor =
                DataProcessingAllocationUtility.GetAttackTimingFactor(steps);
            if (attackTimingFactor < 1f)
            {
                factors ??= new List<StatModifier>();
                factors.Add(new StatModifier
                {
                    stat = StatDefOf.AimingDelayFactor,
                    value = attackTimingFactor
                });
                factors.Add(new StatModifier
                {
                    stat = StatDefOf.RangedCooldownFactor,
                    value = attackTimingFactor
                });
                factors.Add(new StatModifier
                {
                    stat = StatDefOf.MeleeCooldownFactor,
                    value = attackTimingFactor
                });
            }

            float incomingDamageFactor =
                DataProcessingAllocationUtility.GetIncomingDamageFactor(steps);
            if (incomingDamageFactor < 1f)
            {
                factors ??= new List<StatModifier>();
                factors.Add(new StatModifier
                {
                    stat = StatDefOf.IncomingDamageFactor,
                    value = incomingDamageFactor
                });
            }

            stage.statOffsets = offsets;
            stage.statFactors = factors;
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

                return DataProcessingAllocationUtility.FormatPercentDelta(
                    steps,
                    positive: true);
            }
        }
    }
}
