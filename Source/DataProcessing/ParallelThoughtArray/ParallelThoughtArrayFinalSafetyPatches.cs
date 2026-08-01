using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 并行思维阵列被 EMP 眩晕时必须停止提供增幅。
    /// CompPowerTrader 在 EMP 期间只会让 PowerOutput 临时归零，PowerOn 本身仍可能保持 true，
    /// 因此需要把 CompStunnable 的状态纳入 IsOperating 的最终结果。
    /// </summary>
    [HarmonyPatch(
        typeof(CompParallelThoughtArray),
        nameof(CompParallelThoughtArray.IsOperating),
        MethodType.Getter)]
    internal static class Patch_CompParallelThoughtArray_IsOperating_EMP
    {
        [HarmonyPostfix]
        private static void Postfix(
            CompParallelThoughtArray __instance,
            ref bool __result)
        {
            if (!__result)
            {
                return;
            }

            CompStunnable? stunnable =
                __instance.parent.TryGetComp<CompStunnable>();
            if (stunnable?.StunHandler.Stunned == true)
            {
                __result = false;
            }
        }
    }

    /// <summary>
    /// 修正意识保护第二轮完成后的最终判定。
    /// 原方法可能在第二轮已恢复到安全线、但仍保留合法分配时返回 false，
    /// 进而触发保守兜底并清空全部剩余分配。
    /// </summary>
    [HarmonyPatch(
        typeof(GameComponent_DataProcessingAllocationRegistry),
        "ProtectOverseerConsciousness")]
    internal static class Patch_DataProcessingConsciousnessProtection_FinalResult
    {
        [HarmonyPostfix]
        private static void Postfix(
            GameComponent_DataProcessingAllocationRegistry __instance,
            Pawn overseer,
            float anticipatedExternalLoss,
            ref bool __result)
        {
            if (__result
                || overseer == null
                || overseer.Dead
                || overseer.Destroyed)
            {
                return;
            }

            if (__instance.GetTotalStepsForOverseer(overseer) <= 0)
            {
                __result = true;
                return;
            }

            if (DataProcessingAllocationUtility.TryGetCurrentConsciousness(
                    overseer,
                    out float consciousness)
                && consciousness - anticipatedExternalLoss
                    >= DataProcessingAllocationUtility.MinReservedConsciousness)
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// 并行思维接口只允许人类机械师安装。
    /// 原版 CompUsable 只检查血肉身体与 MechlinkImplant，无法排除特殊种族 MOD
    /// 提供的非 Humanlike 血肉机械师，因此在最终使用判定中补充明确限制。
    /// </summary>
    [HarmonyPatch(typeof(CompUsable), nameof(CompUsable.CanBeUsedBy))]
    internal static class Patch_CompUsable_CanBeUsedBy_ParallelThoughtInterface
    {
        [HarmonyPostfix]
        private static void Postfix(
            CompUsable __instance,
            Pawn p,
            ref AcceptanceReport __result)
        {
            if (!__result.Accepted
                || __instance.parent.def
                    != MAPMechanitor_ThingDefOf.MAP_ParallelThoughtInterface)
            {
                return;
            }

            if (p == null
                || !p.RaceProps.Humanlike
                || p.RaceProps.IsMechanoid
                || p.mechanitor == null)
            {
                __result =
                    "MAP_MechanoidMechanitor.ParallelThoughtInterface.RequiresHumanMechanitor"
                        .Translate();
            }
        }
    }
}
