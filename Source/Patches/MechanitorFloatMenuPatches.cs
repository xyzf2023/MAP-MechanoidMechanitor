using HarmonyLib;
using RimWorld;
using Verse;

namespace MMT
{
    [HarmonyPatch(typeof(FloatMenuOptionProvider_Mechanitor), nameof(FloatMenuOptionProvider.SelectedPawnValid))]
    public static class MechanitorFloatMenuPatches
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, FloatMenuContext context, ref bool __result)
        {
            if (__result || pawn == null || !ModsConfig.BiotechActive)
            {
                return;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return;
            }

            if (!OverseerlessMechanitorUtility.IsNode(pawn))
            {
                return;
            }

            if (!MechanitorUtility.IsMechanitor(pawn))
            {
                return;
            }

            OverseerlessMechanitorUtility.EnsureBasicTrackers(pawn);
            OverseerlessMechanitorUtility.ClearExternalOverseerIfNode(pawn);
            __result = true;
        }
    }
}
