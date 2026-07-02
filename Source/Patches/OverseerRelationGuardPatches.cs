using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.AddDirectRelation))]
    public static class OverseerRelationGuardPatches
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn ___pawn, PawnRelationDef def, Pawn otherPawn)
        {
            if (def != PawnRelationDefOf.Overseer)
            {
                return true;
            }

            if (otherPawn != null
                && MAPMechanitorNodeUtility.HasNode(otherPawn)
                && !MAPMechanitorNodeUtility.RequiresExternalOverseer(otherPawn))
            {
                if (Prefs.DevMode)
                {
                    Log.Warning(
                        $"[MAP-MechanoidMechanitor] Blocked attempt to assign overseer to node: controller={___pawn?.LabelShort ?? "null"}, " +
                        $"node={otherPawn.LabelShort}");
                }

                return false;
            }

            if (___pawn != null
                && otherPawn != null
                && ___pawn == otherPawn
                && MAPMechanitorNodeUtility.IsMechanitorNodeController(otherPawn))
            {
                return false;
            }

            return true;
        }
    }
}
