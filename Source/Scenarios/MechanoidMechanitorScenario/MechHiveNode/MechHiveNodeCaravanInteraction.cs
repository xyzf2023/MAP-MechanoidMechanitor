using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械巢节点的远行队世界地图右键菜单。只提供本 MOD 自定义入口：
    /// 进攻（受永久关系限制，非敌对时确认并转敌）与前往交付材料（仅盟友且有有效需求）。
    /// 不提供任何可绕过关系限制的通用进入入口。
    /// </summary>
    public static class MechHiveNodeCaravanInteraction
    {
        public static IEnumerable<FloatMenuOption> GetFloatMenuOptions(
            MAPMechHiveNode node,
            Caravan caravan)
        {
            // 交付材料（前往）：仅在盟友且节点仍有有效需求时提供。
            if (node.AllowsMaterialDelivery)
            {
                foreach (FloatMenuOption option in CaravanArrivalActionUtility.GetFloatMenuOptions(
                    () => CanDeliver(node),
                    () => new MAPCaravanArrivalAction_DeliverToMechHiveNode(node),
                    "MAP_MechanoidMechanitor.MechHiveNode.Caravan.GoDeliver".Translate(node.Label),
                    caravan,
                    node.Tile,
                    node))
                {
                    yield return option;
                }
            }

            // 进攻：敌对可直接进攻；普通中立/盟友需确认并转敌；永久中立/盟友不提供。
            if (MechHiveNodeRelationUtility.CanPlayerAttack())
            {
                Action<Action>? confirmProxy = BuildAttackConfirmProxy(node);
                foreach (FloatMenuOption option in CaravanArrivalActionUtility.GetFloatMenuOptions(
                    () => CanAttack(node),
                    () => new MAPCaravanArrivalAction_AttackMechHiveNode(node),
                    "AttackSettlement".Translate(node.Label),
                    caravan,
                    node.Tile,
                    node,
                    confirmProxy))
                {
                    yield return option;
                }
            }
        }

        public static FloatMenuAcceptanceReport CanAttack(MAPMechHiveNode node)
        {
            if (node == null || !node.Spawned || node.Cleaned)
            {
                return false;
            }

            if (!MechHiveNodeRelationUtility.CanPlayerAttack())
            {
                return false;
            }

            if (node.EnterCooldownBlocksEntering())
            {
                return FloatMenuAcceptanceReport.WithFailMessage(
                    "MessageEnterCooldownBlocksEntering".Translate(
                        node.EnterCooldownTicksLeft().ToStringTicksToPeriod()));
            }

            return true;
        }

        public static FloatMenuAcceptanceReport CanDeliver(MAPMechHiveNode node)
        {
            if (node == null || !node.Spawned)
            {
                return false;
            }

            return node.AllowsMaterialDelivery;
        }

        /// <summary>
        /// 若远行队正停留在允许交付的建设中节点地块，返回“提供材料”Gizmo；否则返回 null。
        /// 显示与点击均重新校验关系、阶段、需求与携带量。
        /// </summary>
        public static Gizmo? TryGetDeliverGizmo(Caravan caravan)
        {
            if (caravan == null || !caravan.Spawned)
            {
                return null;
            }

            MAPMechHiveNode? node = FindNodeAt(caravan.Tile);
            if (node == null || !node.AllowsMaterialDelivery || node.DemandMaterialDef == null)
            {
                return null;
            }

            Command_Action command = new Command_Action
            {
                defaultLabel = "MAP_MechanoidMechanitor.MechHiveNode.ProvideMaterials.Label".Translate(),
                defaultDesc = "MAP_MechanoidMechanitor.MechHiveNode.ProvideMaterials.Desc".Translate(),
                icon = TexCommand.ForbidOff,
                action = () => ExecuteCaravanDelivery(node, caravan)
            };

            int need = node.CurrentDemandCount;
            if (MechHiveNodeDeliveryUtility.CountInCaravan(caravan, node.DemandMaterialDef) < need)
            {
                command.Disable(
                    "MAP_MechanoidMechanitor.MechHiveNode.Caravan.Insufficient".Translate());
            }

            return command;
        }

        /// <summary>执行远行队交付：精确扣除需求、清除需求、加速完成，并按肃清规则结算额度。</summary>
        public static void ExecuteCaravanDelivery(MAPMechHiveNode node, Caravan caravan)
        {
            if (node == null || caravan == null)
            {
                return;
            }

            // 点击时重新完整校验（关系、阶段、需求、清理、地块、携带量）。
            if (!node.AllowsMaterialDelivery
                || node.DemandMaterialDef == null
                || !caravan.Spawned
                || caravan.Tile != node.Tile)
            {
                Messages.Message(
                    "MAP_MechanoidMechanitor.MechHiveNode.Caravan.Insufficient".Translate(),
                    new GlobalTargetInfo(caravan.Tile),
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            int need = node.CurrentDemandCount;
            ThingDef def = node.DemandMaterialDef;
            if (need <= 0)
            {
                return;
            }

            float removedValue = MechHiveNodeDeliveryUtility.TryConsumeFromCaravan(caravan, def, need);
            if (removedValue < 0f)
            {
                Messages.Message(
                    "MAP_MechanoidMechanitor.MechHiveNode.Caravan.Insufficient".Translate(),
                    new GlobalTargetInfo(caravan.Tile),
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            node.NotifyMaterialsDelivered();

            // 肃清指令开启时，交付价值按 0.5 折算为肃清额度（统一接口）。
            MechHiveNodeDeliveryUtility.TryAddPurgeQuota(
                removedValue,
                MechHiveNodeDeliveryUtility.DeliveryQuotaMultiplier);

            Messages.Message(
                "MAP_MechanoidMechanitor.MechHiveNode.Caravan.Delivered".Translate(),
                new GlobalTargetInfo(caravan.Tile),
                MessageTypeDefOf.PositiveEvent,
                historical: false);
        }

        private static MAPMechHiveNode? FindNodeAt(PlanetTile tile)
        {
            if (!tile.Valid)
            {
                return null;
            }

            List<WorldObject> objects = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] is MAPMechHiveNode node && node.Tile == tile)
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>非敌对（普通中立/普通盟友）进攻前弹出确认并转敌；已敌对时无需确认。</summary>
        private static Action<Action>? BuildAttackConfirmProxy(MAPMechHiveNode node)
        {
            if (!MechHiveNodeRelationUtility.AttackRequiresConfirmation())
            {
                return null;
            }

            Faction? mechHive = MechHiveNodeRelationUtility.GetMechHive();
            string factionName = mechHive?.Name ?? node.LabelCap;
            return action =>
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "ConfirmAttackFriendlyFaction".Translate(node.LabelCap, factionName),
                    delegate
                    {
                        MechHiveNodeRelationUtility.TryTurnHostileForAttack();
                        action();
                    }));
            };
        }
    }
}
