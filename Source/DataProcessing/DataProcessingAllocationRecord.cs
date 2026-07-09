using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class DataProcessingAllocationRecord : IExposable
    {
        public Pawn? overseer;
        public Pawn? target;
        public int steps;

        public DataProcessingAllocationRecord()
        {
        }

        public DataProcessingAllocationRecord(Pawn overseer, Pawn target, int steps)
        {
            this.overseer = overseer;
            this.target = target;
            this.steps = steps;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref overseer, "overseer");
            Scribe_References.Look(ref target, "target");
            Scribe_Values.Look(ref steps, "steps", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && steps < 0)
            {
                steps = 0;
            }
        }
    }
}
