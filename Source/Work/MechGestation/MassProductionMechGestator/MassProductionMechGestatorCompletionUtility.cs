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

            bool settlementLooksComplete = gestator.ActiveMechBill != bill
                && bill.State == FormingState.Gathering
                && gestator.innerContainer.Contains(product);

            if (!settlementLooksComplete)
            {
                Log.Error(
                    "[MAP-MechanoidMechanitor] Mass production gestator bill settlement ended in an unexpected state. Building="
                    + gestator.ToStringSafe()
                    + ", bill="
                    + bill.ToStringSafe()
                    + ", recipe="
                    + bill.recipe.ToStringSafe()
                    + ", activeBill="
                    + gestator.ActiveMechBill.ToStringSafe()
                    + ", state="
                    + bill.State
                    + ", productContained="
                    + gestator.innerContainer.Contains(product)
                    + ".");
            }

            // Notify_IterationCompleted 已成功：后续失败只进入释放重试，不得再次结算。
            comp.MarkReleasePending(updateResourceCounts);

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
                    + bill.recipe.ToStringSafe()
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
                    + bill.recipe.ToStringSafe()
                    + ": "
                    + e);
            }

            bool released = TryReleaseProduct(gestator, comp, product);
            if (released)
            {
                comp.ApplyPendingResourceCountUpdate(map);
            }

            return released;
        }

        public static bool TryReleaseProduct(
            Building_MassProductionMechGestator gestator,
            CompMassProductionMechGestator comp,
            Pawn product)
        {
            if (gestator == null || comp == null || product == null)
            {
                return false;
            }

            if (!comp.ReleasePending)
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

            // 先清 releasePending，保留 pendingResourceCountUpdate 供调用方在产品落地后更新。
            comp.NotifyReleaseSucceeded();
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
