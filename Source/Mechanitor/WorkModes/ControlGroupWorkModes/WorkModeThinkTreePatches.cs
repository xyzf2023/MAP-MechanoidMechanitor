using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(ThinkNode_ConditionalWorkMode), "Satisfied")]
    public static class Patch_ThinkNode_ConditionalWorkMode_Satisfied
    {
        [HarmonyPrefix]
        public static bool Prefix(ThinkNode_ConditionalWorkMode __instance, Pawn pawn, ref bool __result)
        {
            if (!MechanoidMechanitorSelfWorkModeUtility.TryGetCurrentMode(
                    pawn,
                    out MechWorkModeDef? currentMode))
            {
                return true;
            }

            if (pawn == null || !pawn.RaceProps.IsMechanoid || pawn.Faction != Faction.OfPlayer)
            {
                __result = false;
                return false;
            }

            // 自律行为/本体模式 → 原版思维树：自律指令→Work，充电→Recharge，休眠→SelfShutdown
            MechWorkModeDef vanillaMode =
                MechanoidMechanitorSelfWorkModeUtility.GetMappedVanillaWorkMode(currentMode);
            __result = __instance.workMode == vanillaMode;
            return false;
        }

        [HarmonyPostfix]
        public static void Postfix(ThinkNode_ConditionalWorkMode __instance, Pawn pawn, ref bool __result)
        {
            if (__result || pawn == null)
            {
                return;
            }

            if (!pawn.RaceProps.IsMechanoid || pawn.Faction != Faction.OfPlayer)
            {
                return;
            }

            Pawn? overseer = pawn.GetOverseer();
            MechanitorControlGroup? controlGroup = overseer?.mechanitor?.GetControlGroup(pawn);
            if (controlGroup == null
                || !MechanoidMechanitorWorkModeUtility.IsMechanoidMechanitorControlGroup(controlGroup))
            {
                return;
            }

            __result = MechanoidMechanitorWorkModeUtility.SatisfiesVanillaWorkMode(
                controlGroup.WorkMode,
                __instance.workMode);
        }
    }
}
