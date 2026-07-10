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
