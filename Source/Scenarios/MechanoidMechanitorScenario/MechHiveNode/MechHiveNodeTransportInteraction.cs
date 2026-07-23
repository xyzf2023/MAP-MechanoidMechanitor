using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械巢节点的运输舱目标菜单与抵达结算。所有分支互斥、不重复结算：
    /// 存在可带队 Pawn 时仅原版“在此组建远行队”；无可带队 Pawn 时按阶段/关系/需求/肃清指令
    /// 结算材料交付、肃清额度或内容丢失；不能带队的 Pawn 一律安全失踪，物品结算后消失。
    /// </summary>
    public static class MechHiveNodeTransportInteraction
    {
        public static IEnumerable<FloatMenuOption> GetFloatMenuOptions(
            MAPMechHiveNode node,
            IEnumerable<IThingHolder> pods,
            Action<PlanetTile, TransportersArrivalAction> launchAction)
        {
            if (node == null || !node.Spawned || node.Cleaned)
            {
                yield break;
            }

            Faction player = Faction.OfPlayer;
            PlanetTile tile = node.Tile;

            // 1) 存在可带队 Pawn：仅提供原版“在此组建远行队”，不做交付/额度结算。
            if (player != null && TransportersArrivalActionUtility.AnyPotentialCaravanOwner(pods, player))
            {
                foreach (FloatMenuOption option in TransportersArrivalActionUtility.GetFloatMenuOptions(
                    () => TransportersArrivalAction_FormCaravan.CanFormCaravanAt(pods, tile),
                    () => new TransportersArrivalAction_FormCaravan("MessageTransportPodsArrived"),
                    "FormCaravanHere".Translate(),
                    launchAction,
                    tile))
                {
                    yield return option;
                }

                yield break;
            }

            // 2) 无可带队 Pawn：按当前状态给出单一自定义选项（抵达时再次以实际状态结算）。
            bool purge = GameComponent_MechanoidMechanitorStoryState.IsPurgeDirectiveActive;
            bool canDeliver = node.AllowsMaterialDelivery && node.DemandMaterialDef != null;
            int need = canDeliver ? node.CurrentDemandCount : 0;
            int available = canDeliver
                ? CountMaterialInPods(pods, node.DemandMaterialDef!)
                : 0;
            bool sufficient = canDeliver && need > 0 && available >= need;

            string label;
            Action<Action>? confirm = null;
            if (sufficient)
            {
                label = "MAP_MechanoidMechanitor.MechHiveNode.Transport.Deliver".Translate();
            }
            else if (purge)
            {
                label = "MAP_MechanoidMechanitor.MechHiveNode.Transport.ConvertQuota".Translate();

                // 建设中、可交付但材料不足：发射前弹出 0.2 折算确认。
                if (canDeliver && !sufficient)
                {
                    confirm = action => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "MAP_MechanoidMechanitor.MechHiveNode.Transport.InsufficientConfirm".Translate(),
                        () => action()));
                }
            }
            else
            {
                label = "MAP_MechanoidMechanitor.MechHiveNode.Transport.DumpLost".Translate();
            }

            foreach (FloatMenuOption option in TransportersArrivalActionUtility.GetFloatMenuOptions(
                () => CanTarget(node),
                () => new MAPTransportersArrivalAction_MechHiveNode(node),
                label,
                launchAction,
                tile,
                confirm))
            {
                yield return option;
            }
        }

        public static FloatMenuAcceptanceReport CanTarget(MAPMechHiveNode node)
        {
            return node != null && node.Spawned && !node.Cleaned;
        }

        /// <summary>
        /// 抵达结算：按当前实际状态在“材料足量交付 / 肃清额度折算 / 内容丢失”三种互斥分支中择一，
        /// 随后不能带队的 Pawn 一律安全失踪、物品全部消失。
        /// </summary>
        public static void SettleArrival(
            MAPMechHiveNode node,
            List<ActiveTransporterInfo> transporters,
            PlanetTile tile)
        {
            if (transporters == null)
            {
                return;
            }

            Faction? player = Faction.OfPlayerSilentFail;

            // 抵达时若竟存在可带队 Pawn（状态变化），回退到原版组建远行队，避免物品/Pawn 丢失。
            if (player != null
                && TransportersArrivalActionUtility.AnyPotentialCaravanOwner(transporters, player))
            {
                new TransportersArrivalAction_FormCaravan("MessageTransportPodsArrived")
                    .Arrived(transporters, tile);
                return;
            }

            bool purge = GameComponent_MechanoidMechanitorStoryState.IsPurgeDirectiveActive;
            bool canDeliver = node != null
                && node.AllowsMaterialDelivery
                && node.DemandMaterialDef != null
                && !node.Cleaned;
            int need = canDeliver ? node!.CurrentDemandCount : 0;
            ThingDef? demandDef = canDeliver ? node!.DemandMaterialDef : null;
            int available = demandDef != null ? CountMaterialInTransporters(transporters, demandDef) : 0;
            bool sufficient = canDeliver && need > 0 && available >= need;

            if (sufficient)
            {
                SettleSufficientDelivery(node!, transporters, demandDef!, need, purge, tile);
            }
            else if (purge)
            {
                SettleQuotaOnly(transporters, tile);
            }
            else
            {
                SettleContentLost(transporters, tile);
            }
        }

        private static void SettleSufficientDelivery(
            MAPMechHiveNode node,
            List<ActiveTransporterInfo> transporters,
            ThingDef demandDef,
            int need,
            bool purge,
            PlanetTile tile)
        {
            float deliveredValue = 0f;
            float sameMaterialTotal = 0f;
            float otherValue = 0f;
            int remaining = need;

            for (int i = 0; i < transporters.Count; i++)
            {
                ThingOwner container = transporters[i].innerContainer;
                for (int j = 0; j < container.Count; j++)
                {
                    Thing thing = container[j];
                    if (thing is Pawn)
                    {
                        continue;
                    }

                    if (thing.def == demandDef)
                    {
                        sameMaterialTotal += thing.MarketValue * thing.stackCount;
                        if (remaining > 0)
                        {
                            int take = Math.Min(remaining, thing.stackCount);
                            deliveredValue += thing.MarketValue * take;
                            remaining -= take;
                        }
                    }
                    else
                    {
                        otherValue += thing.MarketValue * thing.stackCount;
                    }
                }
            }

            float excessSameValue = sameMaterialTotal - deliveredValue;

            node.NotifyMaterialsDelivered();

            if (purge)
            {
                // 满足需求部分 ×0.5；超出的同类材料与其他物品 ×0.2；分别向下取整后加入。
                MechHiveNodeDeliveryUtility.TryAddPurgeQuota(
                    deliveredValue,
                    MechHiveNodeDeliveryUtility.DeliveryQuotaMultiplier);
                MechHiveNodeDeliveryUtility.TryAddPurgeQuota(
                    excessSameValue + otherValue,
                    MechHiveNodeDeliveryUtility.ExcessQuotaMultiplier);
            }

            FinalizePawnsAndItems(transporters, tile);
            Messages.Message(
                "MAP_MechanoidMechanitor.MechHiveNode.Transport.DeliveredMessage".Translate(),
                new GlobalTargetInfo(tile),
                MessageTypeDefOf.PositiveEvent,
                historical: false);
        }

        private static void SettleQuotaOnly(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            float totalValue = 0f;
            for (int i = 0; i < transporters.Count; i++)
            {
                ThingOwner container = transporters[i].innerContainer;
                for (int j = 0; j < container.Count; j++)
                {
                    Thing thing = container[j];
                    if (thing is Pawn)
                    {
                        continue;
                    }

                    totalValue += thing.MarketValue * thing.stackCount;
                }
            }

            MechHiveNodeDeliveryUtility.TryAddPurgeQuota(
                totalValue,
                MechHiveNodeDeliveryUtility.ExcessQuotaMultiplier);

            FinalizePawnsAndItems(transporters, tile);
            Messages.Message(
                "MAP_MechanoidMechanitor.MechHiveNode.Transport.QuotaMessage".Translate(),
                new GlobalTargetInfo(tile),
                MessageTypeDefOf.NeutralEvent,
                historical: false);
        }

        private static void SettleContentLost(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            FinalizePawnsAndItems(transporters, tile);
            Messages.Message(
                "MessageTransportPodsArrivedAndLost".Translate(),
                new GlobalTargetInfo(tile),
                MessageTypeDefOf.NegativeEvent,
                historical: false);
        }

        /// <summary>
        /// 不能带队的 Pawn 一律安全失踪（玩家方 Pawn 走原版放逐流程），随后物品全部消失。
        /// 复用原版运输舱内容丢失路径：Banish + ClearAndDestroyContentsOrPassToWorld。
        /// </summary>
        private static void FinalizePawnsAndItems(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            Faction? player = Faction.OfPlayerSilentFail;
            for (int i = 0; i < transporters.Count; i++)
            {
                ThingOwner container = transporters[i].innerContainer;
                for (int j = container.Count - 1; j >= 0; j--)
                {
                    if (container[j] is Pawn pawn
                        && player != null
                        && (pawn.Faction == player || pawn.HostFaction == player))
                    {
                        PawnBanishUtility.Banish(pawn, tile);
                    }
                }

                container.ClearAndDestroyContentsOrPassToWorld();
            }
        }

        public static int CountMaterialInPods(IEnumerable<IThingHolder> pods, ThingDef def)
        {
            int total = 0;
            if (pods == null || def == null)
            {
                return 0;
            }

            foreach (IThingHolder holder in pods)
            {
                ThingOwner things = holder.GetDirectlyHeldThings();
                if (things == null)
                {
                    continue;
                }

                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i].def == def)
                    {
                        total += things[i].stackCount;
                    }
                }
            }

            return total;
        }

        public static int CountMaterialInTransporters(
            List<ActiveTransporterInfo> transporters,
            ThingDef def)
        {
            int total = 0;
            if (transporters == null || def == null)
            {
                return 0;
            }

            for (int i = 0; i < transporters.Count; i++)
            {
                ThingOwner container = transporters[i].innerContainer;
                for (int j = 0; j < container.Count; j++)
                {
                    if (container[j].def == def)
                    {
                        total += container[j].stackCount;
                    }
                }
            }

            return total;
        }
    }
}
