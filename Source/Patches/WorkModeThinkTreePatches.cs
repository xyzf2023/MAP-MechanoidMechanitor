using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(ThinkNode_ConditionalWorkMode), "Satisfied")]
    public static class Patch_ThinkNode_ConditionalWorkMode_Satisfied
    {
        // 正义本体 Self Work Mode → 原版 ThinkTree WorkMode 分支映射。
        //
        // 正义不是受监管的普通殖民地机械体。原版 Satisfied() 查询
        // overseer → mechanitor → GetControlGroup(pawn) → WorkMode，正义不具备该链。
        // CompJusticeSelfWorkMode 保存独立的本体模式（非控制组 WorkMode）。
        // 映射：MAP_WorkMode_AutonomousDirective → MechWorkModeDefOf.Work；
        //       SelfShutdown → MechWorkModeDefOf.SelfShutdown。
        //
        // 仅 ThinkTree 条件映射，不创建虚拟控制组、不自监管、不在查询路径初始化 tracker。
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

        // 正义控制组中的普通机械体仍由原版先判断实际工作模式。
        // 当原版因自定义 Def 与 Work/Escort 不是同一对象而返回 false 时，
        // 再为正义专属模式补充等价关系：
        //   高效执行、机动作战、阵地防御 → Work；
        //   守卫（机动作战） → Escort。
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
            if (controlGroup == null || !WorkModeUtility.IsJusticeControlGroup(controlGroup))
            {
                return;
            }

            __result = WorkModeUtility.SatisfiesVanillaWorkMode(
                controlGroup.WorkMode,
                __instance.workMode);
        }
    }
}
