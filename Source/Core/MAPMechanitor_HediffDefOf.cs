using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [DefOf]
    public static class MAPMechanitor_HediffDefOf
    {
        public static HediffDef MAP_SyntheticPregnant = null!;
        public static HediffDef MAP_ExtraordinaryOffspring = null!;

        public static HediffDef MAP_ProductivityCore = null!;
        public static HediffDef MAP_ProductivityCoreActive = null!;
        public static HediffDef MAP_VoidEngine = null!;
        public static HediffDef MAP_ReactorSelfPowering = null!;
        public static HediffDef MAP_RepairBeacon = null!;
        public static HediffDef MAP_RepairBoost = null!;
        public static HediffDef MAP_BeaconRepairGuidance = null!;
        public static HediffDef MAP_BattlefieldRepairProtocolActive = null!;
        public static HediffDef MAP_QuantumCommunicator = null!;

        static MAPMechanitor_HediffDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MAPMechanitor_HediffDefOf));
        }
    }
}
