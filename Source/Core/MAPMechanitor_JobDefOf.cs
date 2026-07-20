using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [DefOf]
    public static class MAPMechanitor_JobDefOf
    {
        public static JobDef MAP_MechanoidMechanitorUseBossChipForBandwidth = null!;
        public static JobDef MAP_UseAutonomousDirectiveCore = null!;
        public static JobDef MAP_UseBionicCompanionModule = null!;
        public static JobDef MAP_TransferMechanicalConsciousness = null!;
        public static JobDef MAP_SyntheticGiveBirth = null!;
        public static JobDef MAP_ContactMechanoidOvermind = null!;

        static MAPMechanitor_JobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MAPMechanitor_JobDefOf));
        }
    }
}
