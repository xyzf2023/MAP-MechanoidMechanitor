using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.GetOverseer))]
    public static class Patch_MechanitorUtility_GetOverseer_MAPNodeFilter
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref Pawn? __result)
        {
            if (__result == null || pawn == null)
            {
                return;
            }

            if (!ModsConfig.BiotechActive)
            {
                return;
            }

            if (!MAPMechanitorNodeUtility.HasNode(pawn))
            {
                return;
            }

            if (MAPMechanitorNodeUtility.RequiresExternalOverseer(pawn))
            {
                return;
            }

            __result = null;
        }
    }
}
