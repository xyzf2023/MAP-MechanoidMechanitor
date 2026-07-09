using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class Hediff_CommandFocus : HediffWithComps
    {
        public override string LabelInBrackets
        {
            get
            {
                GameComponent_DataProcessingAllocationRegistry? registry =
                    GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
                int steps = registry?.GetStepsForTarget(pawn) ?? 0;
                if (steps <= 0)
                {
                    return base.LabelInBrackets;
                }

                return DataProcessingAllocationUtility.FormatPercentDelta(steps, positive: true);
            }
        }
    }
}
