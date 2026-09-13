using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体资格的统一查询入口。所有调用者必须经过这里，不得再分别扫描
    /// ThingDef、PawnKindDef 或 Hediff。
    /// </summary>
    public static class MechFusionEligibilityUtility
    {
        public static bool HasInnateFusionMarker(Pawn? pawn)
        {
            return pawn != null
                && !pawn.Destroyed
                && pawn.GetComp<CompMechFusionInnate>() != null;
        }

        public static bool HasFusionEligibility(Pawn? pawn)
        {
            return GameComponent_MechFusionRegistry.HasEligibility(pawn);
        }

        internal static bool EnsureEligibilityRecord(Pawn? pawn)
        {
            return GameComponent_MechFusionRegistry.EnsureEligibilityRecord(pawn);
        }
    }
}
