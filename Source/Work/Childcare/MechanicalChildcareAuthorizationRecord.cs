using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class MechanicalChildcareAuthorizationRecord : IExposable
    {
        public Pawn? Pawn;
        public int DefaultPriority = 3;
        public bool DefaultPriorityInitialized;

        public MechanicalChildcareAuthorizationRecord()
        {
        }

        public MechanicalChildcareAuthorizationRecord(Pawn pawn, int defaultPriority)
        {
            Pawn = pawn;
            DefaultPriority = defaultPriority;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_Values.Look(ref DefaultPriority, "defaultPriority", 3);
            Scribe_Values.Look(
                ref DefaultPriorityInitialized,
                "defaultPriorityInitialized",
                false);
        }
    }
}
