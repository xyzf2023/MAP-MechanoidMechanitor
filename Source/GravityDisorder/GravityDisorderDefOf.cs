using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [DefOf]
    public static class GravityDisorderDefOf
    {
        public static HediffDef MAP_GravityDisorder = null!;
        public static JobDef MAP_GravityDisorderWait = null!;
        public static ThinkTreeDef MAP_GravityDisorderThinkTree = null!;

        static GravityDisorderDefOf() =>
            DefOfHelper.EnsureInitializedInCtor(typeof(GravityDisorderDefOf));
    }
}
