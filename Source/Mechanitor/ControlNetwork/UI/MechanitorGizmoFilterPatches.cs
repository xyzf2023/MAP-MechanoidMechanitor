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
}
