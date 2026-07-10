using Verse;

namespace MAP_MechanoidMechanitor
{
    public class Hediff_DataStreamDistribution : Hediff_DataProcessingAllocationBase
    {
        protected override bool IsPositiveOffset => false;

        protected override int GetAllocationSteps()
        {
            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            return registry?.GetTotalStepsForOverseer(pawn) ?? 0;
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
                    positive: false);
            }
        }
    }
}
