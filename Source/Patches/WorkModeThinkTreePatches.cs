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

        // 正义本体 Self Work Mode → 原版 ThinkTree WorkMode 分支映射。
        //
        // 正义不是受监管的普通殖民地机械体。原版 Satisfied() 查询
        // overseer → mechanitor → GetControlGroup(pawn) → WorkMode，正义不具备该链。
        // CompJusticeSelfWorkMode 保存独立的本体模式（非控制组 WorkMode）。
        // 映射：MAP_WorkMode_AutonomousDirective → MechWorkModeDefOf.Work；
        //       SelfShutdown → MechWorkModeDefOf.SelfShutdown。
        //
        // 仅 ThinkTree 条件映射，不创建虚拟控制组、不自监管、不在查询路径初始化 tracker。
        // 下方 Postfix 保留正义控制组中普通机械体的守卫/机动作战 → Escort 映射。
        [HarmonyPrefix]
        public static bool Prefix(ThinkNode_ConditionalWorkMode __instance, Pawn pawn, ref bool __result)
        {
            CompJusticeSelfWorkMode? comp = CompJusticeSelfWorkMode.GetFor(pawn);
            if (comp == null)
            {
                return true;
            }

            if (pawn == null || !pawn.RaceProps.IsMechanoid || pawn.Faction != Faction.OfPlayer)
            {
                __result = false;
                return false;
            }

            if (comp.IsSelfShutdown)
            {
                __result = __instance.workMode == MechWorkModeDefOf.SelfShutdown;
            }
            else
            {
                __result = __instance.workMode == MechWorkModeDefOf.Work;
            }

            return false;
        }

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
