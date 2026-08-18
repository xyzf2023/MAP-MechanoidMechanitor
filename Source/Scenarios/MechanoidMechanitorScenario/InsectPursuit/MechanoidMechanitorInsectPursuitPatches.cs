using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 追杀模式标准虫灾的无厚岩顶 fallback 补丁。
    /// 采用最小作用域：仅当 IncidentWorker_Infestation.CanFireNowSub 执行期间，
    /// 且原版 InfestationCellFinder.TryFindCell 失败时，才返回备用位置。
    /// 不影响 Quest / Jelly / DeepDrill / Wastepack / 其他独立 TryFindCell 调用。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class MechanoidMechanitorInsectPursuitPatches
    {
        /// <summary>
        /// 进入 CanFireNowSub 时打开作用域（仅当需要 fallback）。
        /// 使用 Finalizer 保证即使异常也清理作用域（thread-static 不会泄漏）。
        /// </summary>
        [HarmonyPatch(typeof(IncidentWorker_Infestation), "CanFireNowSub")]
        public static class MechanoidMechanitorInsectPursuit_InfestationCanFireNow_Patch
        {
            [HarmonyPrefix]
            public static void Prefix()
            {
                if (MechanoidMechanitorInsectPursuitInfestationUtility
                    .CanUseNoThickRoofFallback())
                {
                    MechanoidMechanitorInsectPursuitInfestationUtility
                        .BeginStandardInfestationCanFireScope();
                }
            }

            [HarmonyFinalizer]
            public static void Finalizer()
            {
                MechanoidMechanitorInsectPursuitInfestationUtility
                    .EndStandardInfestationCanFireScope();
            }
        }

        /// <summary>
        /// 仅当：原版失败 + 处于 CanFireNowSub 作用域 + 允许 fallback + 找到备用格时，
        /// 改写结果为成功并填充备用位置。使 Storyteller 在平原地图也能认为标准虫灾可触发。
        /// </summary>
        [HarmonyPatch(typeof(InfestationCellFinder), nameof(InfestationCellFinder.TryFindCell))]
        public static class MechanoidMechanitorInsectPursuit_TryFindCell_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(
                ref bool __result,
                Map map,
                ref IntVec3 cell)
            {
                if (__result)
                {
                    return;
                }

                if (!MechanoidMechanitorInsectPursuitInfestationUtility
                    .InStandardInfestationCanFireScope)
                {
                    return;
                }

                if (!MechanoidMechanitorInsectPursuitInfestationUtility
                    .CanUseNoThickRoofFallback())
                {
                    return;
                }

                if (MechanoidMechanitorInsectPursuitInfestationUtility
                    .TryFindFallbackCell(map, out IntVec3 fallbackCell))
                {
                    __result = true;
                    cell = fallbackCell;
                }
            }
        }

        /// <summary>
        /// 在 IncidentWorker_Infestation.TryExecuteWorker 执行前，为 parms 准备 infestationLocOverride。
        /// 仅当原版 cell 不存在时写入 fallback；有原版 cell 时完全不修改 parms。
        /// 覆盖：Storyteller 普通 Infestation、Pursuit 额外 Infestation、Pursuit 地下 Infestation、
        /// 以及直接 forced TryExecute 的标准 Infestation。只有当前追杀模式 + 设置开启才生效。
        /// </summary>
        [HarmonyPatch(typeof(IncidentWorker_Infestation), "TryExecuteWorker")]
        public static class MechanoidMechanitorInsectPursuit_InfestationTryExecute_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(
                IncidentWorker __instance,
                IncidentParms parms)
            {
                if (parms == null)
                {
                    return;
                }

                Map? map = parms.target as Map;
                if (map == null)
                {
                    return;
                }

                MechanoidMechanitorInsectPursuitInfestationUtility
                    .PrepareInfestationParmsForExecution(map, parms);
            }
        }
    }
}
