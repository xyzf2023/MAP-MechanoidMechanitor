using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class HediffComp_RepairBeacon : HediffComp_DistributedImplantEffect
    {
        protected override HediffDef DistributedHediffDef =>
            MAPMechanitor_HediffDefOf.MAP_RepairBoost;

        protected override bool CanApplyTo(Pawn recipient, bool isSelf)
        {
            CompMechRepairable? repairable = recipient.TryGetComp<CompMechRepairable>();
            return repairable?.autoRepair == true
                && MechRepairUtility.CanRepair(recipient);
        }
    }

    public sealed class HediffCompProperties_RepairBeacon : HediffCompProperties
    {
        public HediffCompProperties_RepairBeacon()
        {
            compClass = typeof(HediffComp_RepairBeacon);
        }
    }
}
