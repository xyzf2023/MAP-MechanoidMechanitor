using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [DefOf]
    public static class MechanoidMechanitorInsectPursuitDefOf
    {
        public static IncidentDef MAP_InsectPursuit = null!;

        static MechanoidMechanitorInsectPursuitDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(
                typeof(MechanoidMechanitorInsectPursuitDefOf));
        }
    }
}
