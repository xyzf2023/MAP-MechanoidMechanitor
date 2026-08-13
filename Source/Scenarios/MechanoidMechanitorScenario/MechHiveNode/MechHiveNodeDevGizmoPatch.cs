using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 为机械巢节点补充与普通派系前哨一致的 DEV 上帝模式“立即完成建设”按钮。
    /// MAPMechHiveNode 当前继承 WorldObject.GetGizmos 而未自行覆写，因此在原版入口做后置包装，
    /// 仅对 MAPMechHiveNode 生效；实际阶段转换仍复用节点自身唯一的 SwitchToCompleted()。
    /// </summary>
    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.GetGizmos))]
    internal static class MechHiveNodeDevGizmoPatch
    {
        private static readonly MethodInfo? SwitchToCompletedMethod =
            AccessTools.Method(typeof(MAPMechHiveNode), "SwitchToCompleted", Type.EmptyTypes);

        private static void Postfix(WorldObject __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!(__instance is MAPMechHiveNode node))
            {
                return;
            }

            __result = AppendDevGizmo(__result, node);
        }

        private static IEnumerable<Gizmo> AppendDevGizmo(
            IEnumerable<Gizmo> original,
            MAPMechHiveNode node)
        {
            foreach (Gizmo gizmo in original)
            {
                yield return gizmo;
            }

            // 与普通派系前哨保持同一显示规则：仅 DEV + 上帝模式、仅建设中、已清理不显示。
            if (!DebugSettings.ShowDevGizmos || !node.IsBuilding || node.Cleaned)
            {
                yield break;
            }

            Command_Action completeConstruction = new Command_Action
            {
                defaultLabel =
                    "MAP_MechanoidMechanitor.MechHiveNode.Dev.CompleteConstruction.Label"
                        .Translate(),
                defaultDesc =
                    "MAP_MechanoidMechanitor.MechHiveNode.Dev.CompleteConstruction.Desc"
                        .Translate(),
                icon = TexButton.Plus,
                action = () => DevForceCompleteConstruction(node)
            };

            // 与节点自然/材料加速完成逻辑一致：已加载地图时禁止切换阶段，避免布局与阶段错位。
            if (node.HasMap)
            {
                completeConstruction.Disable(
                    "MAP_MechanoidMechanitor.MechHiveNode.Dev.CompleteConstruction.MapLoaded"
                        .Translate());
            }

            yield return completeConstruction;
        }

        private static void DevForceCompleteConstruction(MAPMechHiveNode node)
        {
            // 点击时再次复查，避免 Gizmo 创建后状态变化造成越权切换。
            if (!DebugSettings.ShowDevGizmos
                || !node.IsBuilding
                || node.Cleaned
                || node.HasMap)
            {
                return;
            }

            if (SwitchToCompletedMethod == null)
            {
                Log.ErrorOnce(
                    "[MAP] 未找到 MAPMechHiveNode.SwitchToCompleted，无法执行 DEV 立即完成建设。",
                    184732651);
                return;
            }

            try
            {
                SwitchToCompletedMethod.Invoke(node, null);
            }
            catch (TargetInvocationException ex)
            {
                Exception actual = ex.InnerException ?? ex;
                Log.Error("[MAP] DEV 立即完成机械巢节点建设时发生异常: " + actual);
            }
            catch (Exception ex)
            {
                Log.Error("[MAP] DEV 立即完成机械巢节点建设时发生异常: " + ex);
            }
        }
    }
}
