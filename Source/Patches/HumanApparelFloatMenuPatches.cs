using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(FloatMenuOptionProvider), nameof(FloatMenuOptionProvider.SelectedPawnValid))]
    public static class HumanApparelFloatMenuPatches
    {
        [HarmonyPrefix]
        public static bool Prefix(
            FloatMenuOptionProvider __instance,
            Pawn pawn,
            FloatMenuContext context,
            ref bool __result)
        {
            if (__instance is not FloatMenuOptionProvider_Wear
                || !HumanApparelUtility.CanUseWearFloatMenu(pawn))
            {
                return true;
            }

            __result = WearSelectedPawnValid(pawn, context);
            return false;
        }

        private static bool WearSelectedPawnValid(Pawn pawn, FloatMenuContext context)
        {
            if (pawn.IsMutant
                && pawn.mutant.Def.whitelistedFloatMenuProviders != null
                && !pawn.mutant.Def.whitelistedFloatMenuProviders.Contains(
                    FloatMenuMakerMap.currentProvider.GetType()))
            {
                return false;
            }

            if (pawn != context.FirstSelectedPawn)
            {
                return false;
            }

            if (pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }

            if (pawn.Dead || pawn.Downed)
            {
                return false;
            }

            if (!pawn.Spawned)
            {
                return false;
            }

            if (pawn.apparel == null || pawn.jobs == null)
            {
                return false;
            }

            return true;
        }
    }
}
