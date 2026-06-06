using HarmonyLib;
using RimWorld;
using Verse;

namespace MMT
{
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.GetOverseer))]
    public static class ShadowOverseerQueryPatches
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref Pawn __result)
        {
            if (__result != null || pawn == null)
            {
                return;
            }

            if (OverseerlessMechanitorUtility.IsNode(pawn))
            {
                return;
            }

            Pawn? shadowOverseer = MMT_ShadowOverseerManager.Current?.GetShadowOverseer(pawn);
            if (shadowOverseer != null)
            {
                __result = shadowOverseer;
            }
        }
    }
}
