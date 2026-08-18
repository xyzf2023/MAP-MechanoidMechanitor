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
        /// 进入 CanFireNowSub 时打开作用域并记录当前 IncidentParms（仅当需要 fallback）。
        /// 使用 Finalizer 保证即使异常也清理作用域（thread-static 不会泄漏）。
        /// </summary>
        [HarmonyPatch(typeof(IncidentWorker_Infestation), "CanFireNowSub")]
        public static class MechanoidMechanitorInsectPursuit_InfestationCanFireNow_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(IncidentParms parms)
            {
                if (MechanoidMechanitorInsectPursuitInfestationUtility
                    .CanUseNoThickRoofFallback())
                {
                    MechanoidMechanitorInsectPursuitInfestationUtility
                        .BeginStandardInfestationCanFireScope(parms);
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
        /// 同时把找到的 fallback cell 直接缓存进当前 IncidentParms.infestationLocOverride，
        /// 让后续 TryExecuteWorker 复用同一格，避免二次随机。
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
                    IncidentParms? activeParms =
                        MechanoidMechanitorInsectPursuitInfestationUtility
                            .StandardInfestationCanFireParms;
                    if (activeParms != null
                        && !activeParms.infestationLocOverride.HasValue)
                    {
                        activeParms.infestationLocOverride = fallbackCell;
                    }

                    __result = true;
                    cell = fallbackCell;
                }
            }
        }

        /// <summary>
        /// 在 IncidentWorker_Infestation.TryExecuteWorker 执行前，为 parms 准备 infestationLocOverride。
        /// 覆盖：Storyteller 普通 Infestation、Pursuit 额外 Infestation、Pursuit 地下 Infestation，
        /// 以及直接 forced TryExecute 的标准 Infestation。
        ///
        /// 只有当前追杀模式 + 设置开启 + 非 quest 标准 Infestation 才接管；
        /// 其他情况（Default / Ally / PermanentNeutral / 设置关闭 / Quest）一律放行，不改变原版行为。
        ///
        /// 当本模块接管但既无 vanilla cell 又无 fallback 时，返回 false 安全阻止执行，
        /// 避免 IncidentWorker_Infestation 在没有任何 Tunnel 的情况下仍返回 true。
        /// </summary>
        [HarmonyPatch(typeof(IncidentWorker_Infestation), "TryExecuteWorker")]
        public static class MechanoidMechanitorInsectPursuit_InfestationTryExecute_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(
                IncidentWorker __instance,
                IncidentParms parms,
                ref bool __result)
            {
                if (parms == null)
                {
                    return true;
                }

                Map? map = parms.target as Map;
                if (map == null)
                {
                    return true;
                }

                if (!MechanoidMechanitorInsectPursuitInfestationUtility
                    .ShouldManageStandardInfestationExecution(parms))
                {
                    return true;
                }

                if (!MechanoidMechanitorInsectPursuitInfestationUtility
                    .PrepareInfestationParmsForExecution(map, parms))
                {
                    __result = false;
                    return false;
                }

                return true;
            }
        }
    }
}
