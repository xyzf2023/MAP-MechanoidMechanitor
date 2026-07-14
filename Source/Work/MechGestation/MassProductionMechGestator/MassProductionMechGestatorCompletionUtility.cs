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
            Bill_ProductionMech bill)
        {
            if (gestator == null || comp == null || bill == null)
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
                    "[MAP-MechanoidMechanitor] Mass production gestator cannot auto-settle because the map is missing. Building="
                    + gestator.ToStringSafe()
                    + ", bill="
                    + bill.ToStringSafe()
                    + ", recipe="
                    + bill.recipe.ToStringSafe()
                    + ".");
                return false;
            }

            bool updateResourceCounts = bill.repeatMode == BillRepeatModeDefOf.TargetCount;
            ThingStyleDef? style = ResolveProductStyle(bill);
            List<Thing> products = GenRecipe
                .FinalizeGestatedPawns(bill, producer, style, bill.graphicIndexOverride)
                .ToList();

            bill.Notify_IterationCompleted(producer, new List<Thing>());

            // Notify_IterationCompleted 已正常返回：本轮绝对不得再次结算。
            comp.MarkSettlementCommitted(bill, producer, updateResourceCounts);

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

            Bill_ProductionMech? bill = comp.CommittedBill;
            if (bill == null && gestator.ActiveMechBill is Bill_ProductionMech activeProduction)
            {
                bill = activeProduction;
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
                && gestator.ActiveMechBill is Bill_ProductionMech leftover
                && leftover.State == FormingState.Formed)
            {
                Log.ErrorOnce(
                    "[MAP-MechanoidMechanitor] Mass production gestator has settlementCommitted but lost committed bill reference while ActiveBill is still Formed. Building="
                    + gestator.ToStringSafe()
                    + ", activeBill="
                    + leftover.ToStringSafe()
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
            Bill_ProductionMech bill,
            Pawn product)
        {
            if (gestator == null || bill == null || product == null)
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
            Bill_ProductionMech bill,
            Pawn product)
        {
            if (gestator == null || bill == null || product == null)
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
            Bill_ProductionMech? bill,
            Pawn product)
        {
            if (gestator == null || comp == null || product == null)
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
            Bill_ProductionMech? bill,
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
                    "[MAP-MechanoidMechanitor] RecordsUtility.Notify_BillDone failed after mass production settlement. Building="
                    + gestator.ToStringSafe()
                    + ", recipe="
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
                    "[MAP-MechanoidMechanitor] QuestManager.Notify_ThingsProduced failed after mass production settlement. Building="
                    + gestator.ToStringSafe()
                    + ", recipe="
                    + bill?.recipe.ToStringSafe()
                    + ": "
                    + e);
            }
        }

        private static bool ValidateFormedState(
            Building_MassProductionMechGestator gestator,
            Bill_ProductionMech bill,
            out Pawn producer,
            out Pawn product)
        {
            producer = null!;
            product = null!;

            if (bill.State != FormingState.Formed)
            {
                LogAutoSettleAbort(
                    gestator,
                    bill,
                    "bill is not Formed (state=" + bill.State + ")");
                return false;
            }

            if (!ReferenceEquals(gestator.ActiveMechBill, bill))
            {
                LogAutoSettleAbort(
                    gestator,
                    bill,
                    "ActiveMechBill mismatch (activeBill="
                    + gestator.ActiveMechBill.ToStringSafe()
                    + ")");
                return false;
            }

            Pawn? boundPawn = bill.BoundPawn;
            if (boundPawn == null || boundPawn.Destroyed)
            {
                LogAutoSettleAbort(gestator, bill, "BoundPawn is missing");
                return false;
            }

            Pawn? gestatingMech = gestator.GestatingMech;
            if (gestatingMech == null)
            {
                LogAutoSettleAbort(gestator, bill, "GestatingMech is null");
                return false;
            }

            if (!gestator.innerContainer.Contains(gestatingMech))
            {
                LogAutoSettleAbort(
                    gestator,
                    bill,
                    "GestatingMech is not in innerContainer (mech="
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
            Bill_ProductionMech bill,
            string reason)
        {
            Log.ErrorOnce(
                "[MAP-MechanoidMechanitor] Mass production auto-settle aborted: "
                + reason
                + ". Building="
                + gestator.ToStringSafe()
                + ", bill="
                + bill.ToStringSafe()
                + ", recipe="
                + bill.recipe.ToStringSafe()
                + ". Leaving Formed state for diagnosis.",
                gestator.thingIDNumber ^ bill.GetUniqueLoadID().GetHashCode() ^ 0x4D505341);
        }

        private static void LogPostconditionFailureOnce(
            Building_MassProductionMechGestator gestator,
            Bill_ProductionMech bill,
            Pawn? product)
        {
            Log.ErrorOnce(
                "[MAP-MechanoidMechanitor] Mass production gestator settlement postconditions unsafe after Notify_IterationCompleted. Holding product in innerContainer and retrying repair only. Building="
                + gestator.ToStringSafe()
                + ", bill="
                + bill.ToStringSafe()
                + ", recipe="
                + bill.recipe.ToStringSafe()
                + ", activeBill="
                + gestator.ActiveMechBill.ToStringSafe()
                + ", state="
                + bill.State
                + ", boundPawn="
                + bill.BoundPawn.ToStringSafe()
                + ", productContained="
                + (product != null && gestator.innerContainer.Contains(product))
                + ".",
                gestator.thingIDNumber ^ bill.GetUniqueLoadID().GetHashCode() ^ 0x4D505350);
        }

        private static ThingStyleDef? ResolveProductStyle(Bill_ProductionMech bill)
        {
            if (!ModsConfig.IdeologyActive
                || bill.recipe.products == null
                || bill.recipe.products.Count != 1)
            {
                return null;
            }

            if (!bill.globalStyle)
            {
                return bill.style;
            }

            return Faction.OfPlayer.ideos?.PrimaryIdeo?.style
                .StyleForThingDef(bill.recipe.ProducedThingDef)
                ?.styleDef;
        }
    }
}
