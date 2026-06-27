using MAP_MechanoidMechanitor;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class AnimalHandlingWorkAuthorizationRecord : IExposable
    {
        public Pawn? Pawn;
        public AnimalHandlingWorkScope Scope = AnimalHandlingWorkScope.FullHandling;
        public int DefaultPriority = 3;
        public bool DefaultPriorityInitialized;

        public AnimalHandlingWorkAuthorizationRecord()
        {
        }

        public AnimalHandlingWorkAuthorizationRecord(
            Pawn pawn,
            AnimalHandlingWorkScope scope,
            int defaultPriority)
        {
            Pawn = pawn;
            Scope = scope;
            DefaultPriority = defaultPriority;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_Values.Look(ref Scope, "scope", AnimalHandlingWorkScope.FullHandling);
            Scribe_Values.Look(ref DefaultPriority, "defaultPriority", 3);
            Scribe_Values.Look(
                ref DefaultPriorityInitialized,
                "defaultPriorityInitialized",
                false);
        }
    }
}
