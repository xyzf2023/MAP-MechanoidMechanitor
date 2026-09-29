using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MassProductionMechGestatorCompletionUtility
    {
        public static bool TryAutoSettleAndRelease(
            Building_MassProductionMechGestator gestator,
            CompMassProductionMechGestator comp,
            Bill_Mech bill)
        {
            if (gestator == null
                || comp == null
                || bill == null
                || !MassProductionMechGestatorBillUtility.IsSupported(bill))
            {
                return false;
            }

            if (comp.SettlementCommitted)
            {
                return false;
            }

            if (!ValidateFormedState(gestator, bill, out Pawn producer, out Pawn product))
            {
                return false;
            }

            Map? map = gestator.Map;
            if (map == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 量产机械培育仓缺少地图，无法自动结算。建筑="
                    + gestator.ToStringSafe()
                    + "，账单="
                    + bill.ToStringSafe()
                    + "，配方="
                    + bill.recipe.ToStringSafe()
                    + ".");
                return false;
            }

            bool updateResourceCounts = bill.repeatMode == BillRepeatModeDefOf.TargetCount;
            ThingStyleDef? style = ResolveProductStyle(bill);
            List<Thing> products = GenRecipe
                .FinalizeGestatedPawns(bill, producer, style, bill.graphicIndexOverride)
                .ToList();

            // 原版先递减 repeatCount，再调用配方回调。必须在外部回调前持久落锁，
            // 否则回调抛错并读档后会再次递减；已成型产物沿现有提交后路径继续释放。
            comp.MarkSettlementCommitted(bill, producer, updateResourceCounts);
            try
            {
                bill.Notify_IterationCompleted(producer, new List<Thing>());
            }
            catch (Exception ex)
            {
                Log.Error("[MAP-机械族机械师] 量产培育结算通知失败，保留提交状态并继续释放产物：" + ex);
            }

            if (!EnsureSettlementPostconditions(gestator, bill, product))
            {
                LogPostconditionFailureOnce(gestator, bill, product);
                return false;
            }

            SendSettlementNotifications(gestator, bill, producer, products, comp);

            bool released = TryReleaseProduct(gestator, comp, bill, product);
            if (released)
            {
                comp.ApplyPendingResourceCountUpdate(map);
            }

            return released;
        }

        public static bool TryContinueCommittedSettlement(
            Building_MassProductionMechGestator gestator,
            CompMassProductionMechGestator comp)
        {
            if (gestator == null || comp == null)
            {
                return false;
            }

            if (!comp.SettlementCommitted && !comp.ReleasePending)
            {
                return false;
            }

            Pawn? product = FindContainedPawn(gestator);
            if (product == null)
            {
                comp.NotifyReleaseProductMissing(gestator);
                return false;
            }

            Bill_Mech? bill = comp.CommittedBill;
            if (bill != null && !MassProductionMechGestatorBillUtility.IsSupported(bill))
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 量产机械培育仓恢复了不受支持的已提交账单。建筑="
                    + gestator.ToStringSafe()
                    + "，账单="
                    + bill.ToStringSafe()
                    + ".",
                    gestator.thingIDNumber ^ 0x4D505342);
                return false;
            }

            Bill_Mech? activeBill = gestator.ActiveMechBill;
            if (bill == null && MassProductionMechGestatorBillUtility.IsSupported(activeBill))
            {
                bill = activeBill;
            }

            if (bill != null && !AreSettlementPostconditionsMet(gestator, bill, product))
            {
                if (!EnsureSettlementPostconditions(gestator, bill, product))
                {
                    LogPostconditionFailureOnce(gestator, bill, product);
                    return false;
                }
            }
            else if (bill == null
                && MassProductionMechGestatorBillUtility.IsSupported(activeBill)
                && activeBill!.State == FormingState.Formed)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 量产机械培育仓已提交结算，但已提交账单引用丢失，当前账单仍处于已成型状态。建筑="
                    + gestator.ToStringSafe()
                    + "，当前账单="
                    + activeBill.ToStringSafe()
                    + ".",
                    gestator.thingIDNumber ^ 0x4D505343);
                return false;
            }

            if (bill != null && !AreSettlementPostconditionsMet(gestator, bill, product))
            {
                return false;
            }

            Pawn? producer = comp.SettledProducer;
            if (!comp.SettlementNotificationsSent && producer != null && !producer.Destroyed)
            {
                SendSettlementNotifications(
                    gestator,
                    bill,
                    producer,
                    new List<Thing> { product },
                    comp);
            }

            Map? map = gestator.Map;
            bool released = TryReleaseProduct(gestator, comp, bill, product);
            if (released && map != null)
            {
                comp.ApplyPendingResourceCountUpdate(map);
            }

            return released;
        }

        public static bool EnsureSettlementPostconditions(
            Building_MassProductionMechGestator gestator,
            Bill_Mech bill,
            Pawn product)
        {
            if (gestator == null
                || bill == null
                || product == null
                || !MassProductionMechGestatorBillUtility.IsSupported(bill))
            {
                return false;
            }

            if (!gestator.innerContainer.Contains(product))
            {
                return false;
            }

            if (bill.State != FormingState.Gathering || bill.BoundPawn != null)
            {
                bill.Reset();
            }

            if (ReferenceEquals(gestator.ActiveMechBill, bill))
            {
                gestator.ActiveBill = null;
            }

            return AreSettlementPostconditionsMet(gestator, bill, product);
        }

        public static bool AreSettlementPostconditionsMet(
            Building_MassProductionMechGestator gestator,
            Bill_Mech bill,
            Pawn product)
        {
            if (gestator == null
                || bill == null
                || product == null
                || !MassProductionMechGestatorBillUtility.IsSupported(bill))
            {
                return false;
            }

            if (ReferenceEquals(gestator.ActiveMechBill, bill))
            {
                return false;
            }

            if (bill.State != FormingState.Gathering)
            {
                return false;
            }

            if (bill.BoundPawn != null)
            {
                return false;
            }

            return gestator.innerContainer.Contains(product);
        }

        public static bool TryReleaseProduct(
            Building_MassProductionMechGestator gestator,
            CompMassProductionMechGestator comp,
            Bill_Mech? bill,
            Pawn product)
        {
            if (gestator == null || comp == null || product == null)
            {
                return false;
            }

            if (bill != null && !MassProductionMechGestatorBillUtility.IsSupported(bill))
            {
                return false;
            }

            if (!comp.ReleasePending && !comp.SettlementCommitted)
            {
                return false;
            }

            if (!gestator.innerContainer.Contains(product))
            {
                Pawn? remaining = FindContainedPawn(gestator);
                if (remaining == null)
                {
                    comp.NotifyReleaseProductMissing(gestator);
                    return false;
                }

                product = remaining;
            }

            if (bill != null && !AreSettlementPostconditionsMet(gestator, bill, product))
            {
                return false;
            }

            Map? map = gestator.Map;
            if (map == null || !gestator.Spawned)
            {
                return false;
            }

            if (!gestator.innerContainer.TryDrop(
                    product,
                    gestator.InteractionCell,
                    map,
                    ThingPlaceMode.Near,
                    out Thing _))
            {
                comp.NotifyReleaseFailed(gestator, product);
                return false;
            }

            // 产品已离开容器后再清除 settlementCommitted / releasePending。
            comp.NotifySettlementFullyCompleted();
            return true;
        }

        public static Pawn? FindContainedPawn(Building_MassProductionMechGestator gestator)
        {
            if (gestator?.innerContainer == null)
            {
                return null;
            }

            for (int i = 0; i < gestator.innerContainer.Count; i++)
            {
                if (gestator.innerContainer[i] is Pawn pawn)
                {
                    return pawn;
                }
            }

            return null;
        }

        private static void SendSettlementNotifications(
            Building_MassProductionMechGestator gestator,
            Bill_Mech? bill,
            Pawn producer,
            List<Thing> products,
            CompMassProductionMechGestator comp)
        {
            if (comp.SettlementNotificationsSent)
            {
                return;
            }

            comp.MarkSettlementNotificationsSent();

            try
            {
                RecordsUtility.Notify_BillDone(producer, products);
            }
            catch (Exception e)
            {
                Log.Error(
                    "[MAP-机械族机械师] 量产培育结算后更新生产记录失败（RecordsUtility.Notify_BillDone）。建筑="
                    + gestator.ToStringSafe()
                    + "，配方="
                    + bill?.recipe.ToStringSafe()
                    + ": "
                    + e);
            }

            try
            {
                if (products.Count > 0)
                {
                    Find.QuestManager.Notify_ThingsProduced(producer, products);
                }
            }
            catch (Exception e)
            {
                Log.Error(
                    "[MAP-机械族机械师] 量产培育结算后通知任务产物生成失败（QuestManager.Notify_ThingsProduced）。建筑="
                    + gestator.ToStringSafe()
                    + "，配方="
                    + bill?.recipe.ToStringSafe()
                    + ": "
                    + e);
            }
        }

        private static bool ValidateFormedState(
            Building_MassProductionMechGestator gestator,
            Bill_Mech bill,
            out Pawn producer,
            out Pawn product)
        {
            producer = null!;
            product = null!;

            if (!MassProductionMechGestatorBillUtility.IsSupported(bill))
            {
                return false;
            }

            if (bill.State != FormingState.Formed)
            {
                LogAutoSettleAbort(
                    gestator,
                    bill,
                    "账单尚未成型（状态=" + bill.State + ")");
                return false;
            }

            if (!ReferenceEquals(gestator.ActiveMechBill, bill))
            {
                LogAutoSettleAbort(
                    gestator,
                    bill,
                    "当前机械培育账单不匹配（当前账单="
                    + gestator.ActiveMechBill.ToStringSafe()
                    + ")");
                return false;
            }

            Pawn? boundPawn = bill.BoundPawn;
            if (boundPawn == null || boundPawn.Destroyed)
            {
                LogAutoSettleAbort(gestator, bill, "缺少账单绑定角色");
                return false;
            }

            Pawn? gestatingMech = gestator.GestatingMech;
            if (gestatingMech == null)
            {
                LogAutoSettleAbort(gestator, bill, "培育中的机械体为空");
                return false;
            }

            if (!gestator.innerContainer.Contains(gestatingMech))
            {
                LogAutoSettleAbort(
                    gestator,
                    bill,
                    "培育中的机械体不在内部容器中（机械体="
                    + gestatingMech.ToStringSafe()
                    + ")");
                return false;
            }

            producer = boundPawn;
            product = gestatingMech;
            return true;
        }

        private static void LogAutoSettleAbort(
            Building_MassProductionMechGestator gestator,
            Bill_Mech bill,
            string reason)
        {
            Log.ErrorOnce(
                "[MAP-机械族机械师] 量产培育自动结算已中止："
                + reason
                + "。建筑="
                + gestator.ToStringSafe()
                + "，账单="
                + bill.ToStringSafe()
                + "，配方="
                + bill.recipe.ToStringSafe()
                + "。保留已成型状态以便诊断。",
                gestator.thingIDNumber ^ bill.GetUniqueLoadID().GetHashCode() ^ 0x4D505341);
        }

        private static void LogPostconditionFailureOnce(
            Building_MassProductionMechGestator gestator,
            Bill_Mech bill,
            Pawn? product)
        {
            Log.ErrorOnce(
                "[MAP-机械族机械师] 量产培育完成通知后，结算后置条件不安全。产物保留在内部容器中，仅重试状态修复。建筑="
                + gestator.ToStringSafe()
                + "，账单="
                + bill.ToStringSafe()
                + "，配方="
                + bill.recipe.ToStringSafe()
                + "，当前账单="
                + gestator.ActiveMechBill.ToStringSafe()
                + "，状态="
                + bill.State
                + "，绑定角色="
                + bill.BoundPawn.ToStringSafe()
                + "，产物仍在容器内="
                + (product != null && gestator.innerContainer.Contains(product))
                + ".",
                gestator.thingIDNumber ^ bill.GetUniqueLoadID().GetHashCode() ^ 0x4D505350);
        }

        private static ThingStyleDef? ResolveProductStyle(Bill_Mech bill)
        {
            if (bill is not Bill_ProductionMech productionBill
                || !ModsConfig.IdeologyActive
                || productionBill.recipe.products == null
                || productionBill.recipe.products.Count != 1)
            {
                return null;
            }

            if (!productionBill.globalStyle)
            {
                return productionBill.style;
            }

            return Faction.OfPlayer.ideos?.PrimaryIdeo?.style
                .StyleForThingDef(productionBill.recipe.ProducedThingDef)
                ?.styleDef;
        }
    }
}
