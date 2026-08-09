using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [DefOf]
    public static class FactionOutpostDefOf
    {
        public static WorldObjectDef MAP_FactionOutpost = null!;

        public static SitePartDef MAP_FactionOutpost_Building = null!;

        public static SitePartDef MAP_FactionOutpost_Completed = null!;

        static FactionOutpostDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(FactionOutpostDefOf));
        }
    }
}
