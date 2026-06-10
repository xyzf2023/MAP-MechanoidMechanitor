using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MMT
{
    [HarmonyPatch(typeof(ThinkNode_ConditionalWorkMode), "Satisfied")]
    public static class ThinkNode_ConditionalWorkMode_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, ref bool __result)
        {
            if (pawn != null
                && ModsConfig.BiotechActive
                && OverseerlessMechanitorUtility.IsOverseerlessMechanitorNodeSubject(pawn))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}
