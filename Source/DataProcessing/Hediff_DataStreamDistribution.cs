using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class Hediff_DataStreamDistribution : HediffWithComps
    {
        public override string LabelInBrackets
        {
            get
            {
                GameComponent_DataProcessingAllocationRegistry? registry =
                    GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
                int steps = registry?.GetTotalStepsForOverseer(pawn) ?? 0;
                if (steps <= 0)
                {
                    return base.LabelInBrackets;
                }

                return DataProcessingAllocationUtility.FormatPercentDelta(steps, positive: false);
            }
        }
    }
}
