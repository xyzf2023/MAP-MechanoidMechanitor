using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class WardenWorkAuthorizationRecord : IExposable
    {
        public Pawn? Pawn;
        public bool DefaultPriorityInitialized;

        public WardenWorkAuthorizationRecord()
        {
        }

        public WardenWorkAuthorizationRecord(Pawn pawn)
        {
            Pawn = pawn;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_Values.Look(
                ref DefaultPriorityInitialized,
                "defaultPriorityInitialized",
                false);
        }
    }
}
