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
                && AutonomousMechUtility.IsAutonomousMech(otherPawn))
            {
                return false;
            }

            // 原版也允许以“普通机械体 → 机械师”的顺序写入双向关系。
            // 自律普通机械体不能利用反向调用绕过门控；机械师作为控制者仍可写入下属关系。
            if (AutonomousMechUtility.IsAutonomousMech(___pawn)
                && !MechanitorUtility.IsMechanitor(___pawn))
            {
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
