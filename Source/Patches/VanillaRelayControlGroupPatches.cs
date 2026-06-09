using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(PawnColumnWorker_ControlGroup), nameof(PawnColumnWorker_ControlGroup.DoCell))]
    public static class VanillaRelayControlGroupPatches
    {
        [HarmonyPrefix]
        public static bool DoCell_Prefix(Pawn pawn)
        {
            if (pawn == null || pawn.IsGestating())
            {
                return true;
            }

            Pawn overseer = pawn.GetOverseer();
            if (overseer == null)
            {
                return true;
            }

            if (!VanillaRelayMechanitorUtility.IsVanillaRelayMechanitor(overseer))
            {
                return true;
            }

            if (!pawn.RaceProps.IsMechanoid)
            {
                return true;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return true;
            }

            if (MAPMechanitorNodeUtility.HasNode(pawn))
            {
                return true;
            }

            if (pawn.GetMechControlGroup() == null && overseer.mechanitor != null)
            {
                overseer.mechanitor.AssignPawnControlGroup(pawn, null);
            }

            if (pawn.GetMechControlGroup() == null)
            {
                return false;
            }

            return true;
        }
    }
}
