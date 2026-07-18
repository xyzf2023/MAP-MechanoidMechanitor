using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class MechanoidMechanitorPurgeDirective_KillPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn __instance, ref bool __state)
        {
            __state = __instance != null
                && !__instance.Dead
                && __instance.health != null
                && !__instance.health.isBeingKilled;
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, bool __state)
        {
            if (!__state || __instance == null || !__instance.Dead)
            {
                return;
            }

            if (!GameComponent_MechanoidMechanitorStoryState.IsPurgeDirectiveActive)
            {
                return;
            }

            if (__instance.RaceProps == null || !__instance.RaceProps.Humanlike)
            {
                return;
            }

            GameComponent_MechanoidMechanitorStoryState.TryAddPurgeDirectiveRewardPoints(
                MechanoidMechanitorPurgeDirectiveRuntimeState.HumanlikeDeathRewardPoints);
        }
    }
}
