using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(ThinkNode_ConditionalWorkMode), "Satisfied")]
    public static class Patch_ThinkNode_ConditionalWorkMode_Satisfied
    {
        private const string GuardMobileCombatDefName = "MAP_WorkMode_MobileCombat_Guard";

        [HarmonyPostfix]
        public static void Postfix(ThinkNode_ConditionalWorkMode __instance, Pawn pawn, ref bool __result)
        {
            if (__result || pawn == null)
            {
                return;
            }

            if (__instance.workMode != MechWorkModeDefOf.Escort)
            {
                return;
            }

            if (!pawn.RaceProps.IsMechanoid || pawn.Faction != Faction.OfPlayer)
            {
                return;
            }

            Pawn? overseer = pawn.GetOverseer();
            MechWorkModeDef? workMode = overseer?.mechanitor?.GetControlGroup(pawn)?.WorkMode;
            if (workMode != null && workMode.defName == GuardMobileCombatDefName)
            {
                __result = true;
            }
        }
    }
}
