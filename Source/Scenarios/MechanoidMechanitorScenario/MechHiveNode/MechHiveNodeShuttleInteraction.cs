using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械巢节点的穿梭机世界目标菜单。
    /// 不调用原版 Site 的 VisitSite 入口，避免绕过机械巢自己的关系、冷却与地图初始化规则；
    /// 只提供明确的“攻击机械巢”与“在此组建远行队”两类行为。
    /// </summary>
    public static class MechHiveNodeShuttleInteraction
    {
        public static IEnumerable<FloatMenuOption> GetFloatMenuOptions(
            MAPMechHiveNode node,
            IEnumerable<IThingHolder> pods,
            Action<PlanetTile, TransportersArrivalAction> launchAction)
        {
            if (node == null || !node.Spawned || node.Cleaned || pods == null)
            {
                yield break;
            }

            List<IThingHolder> podList = pods.ToList();
            PlanetTile tile = node.Tile;
            Faction? player = Faction.OfPlayerSilentFail;

            CompTransporter? firstPod = podList.FirstOrDefault() as CompTransporter;
            TransportShip? transportShip = firstPod?.Shuttle?.shipParent;

            // 攻击入口：敌对可直接攻击；普通中立/盟友沿用现有确认并转敌流程；
            // 永久中立/永久盟友由 CanPlayerAttack 统一阻止。
            if (transportShip != null
                && !transportShip.Disposed
                && MechHiveNodeRelationUtility.CanPlayerAttack()
                && TransportersArrivalActionUtility.AnyNonDownedColonist(podList))
            {
                Action<Action>? attackConfirm =
                    MechHiveNodeCaravanInteraction.GetAttackConfirmProxy(node);

                foreach (FloatMenuOption option in TransportersArrivalActionUtility.GetFloatMenuOptions(
                    () => CanAttack(podList, node, transportShip),
                    () => new MAPTransportersArrivalAction_AttackMechHiveNodeShuttle(
                        node,
                        transportShip),
                    "AttackShuttle".Translate(node.Label),
                    launchAction,
                    tile,
                    attackConfirm))
                {
                    yield return option;
                }
            }

            // 组建远行队与攻击并列存在，不按关系一刀切。
            // 这样敌对节点也允许玩家先在节点地块落地，再通过远行队入口决定下一步行为。
            if (player != null
                && TransportersArrivalActionUtility.AnyPotentialCaravanOwner(podList, player))
            {
                foreach (FloatMenuOption option in TransportersArrivalActionUtility.GetFloatMenuOptions(
                    () => TransportersArrivalAction_FormCaravan.CanFormCaravanAt(podList, tile),
                    () => new TransportersArrivalAction_FormCaravan("MessageShuttleArrived"),
                    "FormCaravanHere".Translate(),
                    launchAction,
                    tile))
                {
                    yield return option;
                }
            }
        }

        private static FloatMenuAcceptanceReport CanAttack(
            IEnumerable<IThingHolder> pods,
            MAPMechHiveNode node,
            TransportShip? transportShip)
        {
            FloatMenuAcceptanceReport report =
                MAPTransportersArrivalAction_AttackMechHiveNode.CanAttack(pods, node);
            if (!report)
            {
                return report;
            }

            if (transportShip == null
                || transportShip.Disposed
                || transportShip.shipThing == null)
            {
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// MAPMechHiveNode 原实现主动关闭了原版 Site 的 Shuttle VisitSite 入口。
    /// 在保留该安全约束的前提下，将这个空入口替换为机械巢自己的 Shuttle 分流逻辑。
    /// </summary>
    [HarmonyPatch(typeof(MAPMechHiveNode), nameof(MAPMechHiveNode.GetShuttleFloatMenuOptions))]
    public static class MAPMechHiveNode_GetShuttleFloatMenuOptions_Patch
    {
        public static bool Prefix(
            MAPMechHiveNode __instance,
            IEnumerable<IThingHolder> pods,
            Action<PlanetTile, TransportersArrivalAction> launchAction,
            ref IEnumerable<FloatMenuOption> __result)
        {
            __result = MechHiveNodeShuttleInteraction.GetFloatMenuOptions(
                __instance,
                pods,
                launchAction);
            return false;
        }
    }
}
