using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [DefOf]
    public static class MAPMechanitor_JobDefOf
    {
        public static JobDef MAP_JusticeUseBossChipForBandwidth = null!;
        public static JobDef MAP_UseAutonomousDirectiveCore = null!;
        static MAPMechanitor_JobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MAPMechanitor_JobDefOf));
        }
    }
}
