using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [DefOf]
    public static class MAPMechanitor_HediffDefOf
    {
        public static HediffDef MAP_SyntheticPregnant = null!;
        public static HediffDef MAP_ExtraordinaryOffspring = null!;

        static MAPMechanitor_HediffDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MAPMechanitor_HediffDefOf));
        }
    }
}
