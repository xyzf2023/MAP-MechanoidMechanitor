using HarmonyLib;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MMT
{
    [HarmonyPatch(typeof(FloatMenuOptionProvider), nameof(FloatMenuOptionProvider.SelectedPawnValid))]
    public static class MechanitorFloatMenuPatches
    {
        [HarmonyPostfix]
        public static void Postfix(
            FloatMenuOptionProvider __instance,
            Pawn pawn,
            FloatMenuContext context,
            ref bool __result)
        {
            if (__instance is not FloatMenuOptionProvider_Mechanitor)
            {
                return;
            }

            if (__result || pawn == null || !ModsConfig.BiotechActive)
            {
                return;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return;
            }

            if (!MAPMechanitorNodeUtility.IsMechanitorNodeController(pawn))
            {
                return;
            }

            if (!MechanitorUtility.IsMechanitor(pawn))
            {
                return;
            }

            __result = true;
        }
    }
}
