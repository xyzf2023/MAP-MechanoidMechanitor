using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [DefOf]
    public static class MAPMechanitor_ThingDefOf
    {
        public static ThingDef MAP_ParallelThoughtArray = null!;
        public static ThingDef MAP_ParallelThoughtInterface = null!;

        static MAPMechanitor_ThingDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MAPMechanitor_ThingDefOf));
        }
    }
}
