using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    internal static class ColonistLikeWorkThinkTreeUtility
    {
        public static bool ShouldUseColonistLikeWorkThinkTree(Pawn? pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (!ModsConfig.BiotechActive)
            {
                return false;
            }

            if (!pawn.Spawned || pawn.Dead)
            {
                return false;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return false;
            }

            if (pawn.RaceProps == null || !pawn.RaceProps.IsMechanoid)
            {
                return false;
            }

            if (!CompWorkTabVisibleUser.PawnCanShowInWorkTab(pawn))
            {
                return false;
            }

            if (MAPMechanitorMod.Settings == null || !MAPMechanitorMod.Settings.addJusticeToWorkTab)
            {
                return false;
            }

            CompWorkTabVisibleUser.EnsureWorkSettingsForWorkTab(pawn);
            return true;
        }
    }

    [HarmonyPatch(typeof(ThinkNode_ConditionalPlayerControlledMech), "Satisfied")]
    public static class ThinkNode_ConditionalPlayerControlledMech_ColonistLikeWorkPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (ColonistLikeWorkThinkTreeUtility.ShouldUseColonistLikeWorkThinkTree(pawn))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(ThinkNode_ConditionalWorkMode), "Satisfied")]
    public static class ThinkNode_ConditionalWorkMode_ColonistLikeWorkPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ThinkNode_ConditionalWorkMode __instance, Pawn pawn, ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (__instance == null || __instance.workMode == null)
            {
                return;
            }

            if (__instance.workMode != MechWorkModeDefOf.Work)
            {
                return;
            }

            if (ColonistLikeWorkThinkTreeUtility.ShouldUseColonistLikeWorkThinkTree(pawn))
            {
                __result = true;
            }
        }
    }
}
