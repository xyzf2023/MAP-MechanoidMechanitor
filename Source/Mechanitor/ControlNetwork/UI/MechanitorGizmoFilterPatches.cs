using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // UI 修复：隐藏无外部监管者 MAP mechanitor 节点（如正义）的「选择监管者」Gizmo。
    //
    // 原版 MechanitorUtility.GetMechGizmos 会给所有可控殖民地机械体生成
    // 「选择监管者」Command_Action（defaultLabel = CommandSelectOverseer）。
    // 正义是无外部监管者的 MAP mechanitor node（requiresExternalOverseer=false）。
    // 经 GetOverseer() 查询过滤后，正义对外没有 overseer，该按钮无实际用途。
    //
    // 本补丁仅在 HasNode && !RequiresExternalOverseer 时过滤该 UI 按钮。
    // 不修改 overseer relation、控制组、带宽、ThinkTree、self work mode。
    // 隐者、普通机械体、正义监管的机械体、控制组/远程护盾/Dev Gizmo 均不受影响。
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.GetMechGizmos))]
    public static class Patch_MechanitorUtility_GetMechGizmos_MAPNodeFilter
    {
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn mech)
        {
            if (mech == null
                || !ModsConfig.BiotechActive
                || !MAPMechanitorNodeUtility.HasNode(mech)
                || MAPMechanitorNodeUtility.RequiresExternalOverseer(mech))
            {
                foreach (Gizmo gizmo in __result)
                {
                    yield return gizmo;
                }

                yield break;
            }

            foreach (Gizmo gizmo in __result)
            {
                if (IsSelectOverseerGizmo(gizmo))
                {
                    continue;
                }

                yield return gizmo;
            }
        }

        private static bool IsSelectOverseerGizmo(Gizmo gizmo)
        {
            if (gizmo is not Command_Action command)
            {
                return false;
            }

            return command.defaultLabel == "CommandSelectOverseer".Translate();
        }
    }

    // 隐者（MAP_Mech_Hermit）使用 Vanilla mechanitor backend，因此会从原版
    // Pawn_MechanitorTracker.GetGizmos() 获得 Command_CallBossgroup（召唤机械族首领）。
    // 但该按钮对隐者不可用，故仅对隐者过滤此 Gizmo。
    // 其他机械师（含正义 MAP_Mech_Justice）一律保持原逻辑，不删除该按钮。
    // 不修改现有 Bossgroup 菜单/召唤合法性相关补丁，也不影响任何其它机械师 Gizmo。
    [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.GetGizmos))]
    public static class Patch_Pawn_MechanitorTracker_GetGizmos_HermitBossgroupFilter
    {
        private const string HermitDefName = "MAP_Mech_Hermit";

        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(
            IEnumerable<Gizmo> __result,
            Pawn_MechanitorTracker __instance)
        {
            if (__result == null)
            {
                yield break;
            }

            Pawn? pawn = __instance?.Pawn;
            bool isHermit = pawn?.def?.defName == HermitDefName;

            foreach (Gizmo gizmo in __result)
            {
                if (isHermit && gizmo is Command_CallBossgroup)
                {
                    continue;
                }

                yield return gizmo;
            }
        }
    }
}
