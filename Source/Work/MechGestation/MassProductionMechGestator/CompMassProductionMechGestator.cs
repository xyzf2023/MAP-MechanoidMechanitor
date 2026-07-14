using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompMassProductionMechGestator : ThingComp
    {
        private const int ReleaseRetryIntervalTicks = 60;

        private int remainingTicks = -1;

        private bool timerInitialized;

        private bool completionInProgress;

        private bool releasePending;

        private bool pendingResourceCountUpdate;

        private bool releaseFailureLogged;

        private bool releaseMissingLogged;

        private Bill_ProductionMech? settlementBlockedForBill;

        public int RemainingTicks => remainingTicks;

        public bool TimerInitialized => timerInitialized;

        public bool ReleasePending => releasePending;

        public float FormingPercent
        {
            get
            {
                if (!timerInitialized || remainingTicks < 0)
                {
                    return 0f;
                }

                return Mathf.Clamp01(
                    1f - remainingTicks / (float)Building_MassProductionMechGestator.FixedFormingTicks);
            }
        }

        public override void CompTick()
        {
            if (parent is not Building_MassProductionMechGestator gestator)
            {
                return;
            }

            if (releasePending)
            {
                TickReleasePending(gestator);
                return;
            }

            if (completionInProgress)
            {
                return;
            }

            Bill_Mech? activeBill = gestator.ActiveMechBill;

            if (activeBill is Bill_ProductionMech formedBill
                && formedBill.State == FormingState.Formed)
            {
                ResetTimer();
                if (!ReferenceEquals(settlementBlockedForBill, formedBill))
                {
                    RunSettlement(gestator, formedBill);
                }

                return;
            }

            if (!ReferenceEquals(settlementBlockedForBill, activeBill))
            {
                settlementBlockedForBill = null;
            }

            if (activeBill is Bill_ProductionMech productionBill
                && productionBill.State == FormingState.Forming)
            {
                if (!ReferenceEquals(settlementBlockedForBill, productionBill))
                {
                    TickForming(gestator, productionBill);
                }

                return;
            }

            ResetTimer();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref remainingTicks, "massProdGestatorRemainingTicks", -1);
            Scribe_Values.Look(ref timerInitialized, "massProdGestatorTimerInitialized", false);
            Scribe_Values.Look(ref releasePending, "massProdGestatorReleasePending", false);
            Scribe_Values.Look(ref pendingResourceCountUpdate, "massProdGestatorPendingResourceCountUpdate", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && timerInitialized)
            {
                if (remainingTicks < 0)
                {
                    remainingTicks = 0;
                }
                else if (remainingTicks > Building_MassProductionMechGestator.FixedFormingTicks)
                {
                    remainingTicks = Building_MassProductionMechGestator.FixedFormingTicks;
                }
            }
        }

        internal void MarkReleasePending(bool updateResourceCountsOnRelease)
        {
            releasePending = true;
            pendingResourceCountUpdate = updateResourceCountsOnRelease;
            releaseFailureLogged = false;
            releaseMissingLogged = false;
            settlementBlockedForBill = null;
        }

        internal void NotifyReleaseSucceeded()
        {
            releasePending = false;
            releaseFailureLogged = false;
            releaseMissingLogged = false;
        }

        internal void ClearReleasePending()
        {
            releasePending = false;
            pendingResourceCountUpdate = false;
            releaseFailureLogged = false;
            releaseMissingLogged = false;
        }

        internal void NotifySettlementBlocked(Bill_ProductionMech bill)
        {
            settlementBlockedForBill = bill;
        }

        internal void ApplyPendingResourceCountUpdate(Map map)
        {
            if (!pendingResourceCountUpdate || map == null)
            {
                return;
            }

            pendingResourceCountUpdate = false;
            try
            {
                map.resourceCounter.UpdateResourceCounts();
            }
            catch (Exception e)
            {
                Log.Error(
                    "[MAP-MechanoidMechanitor] resourceCounter.UpdateResourceCounts failed after mass production release. Building="
                    + parent.ToStringSafe()
                    + ": "
                    + e);
            }
        }

        internal void NotifyReleaseFailed(
            Building_MassProductionMechGestator gestator,
            Pawn product)
        {
            if (releaseFailureLogged)
            {
                return;
            }

            releaseFailureLogged = true;
            Log.ErrorOnce(
                "[MAP-MechanoidMechanitor] Mass production gestator failed to release product near InteractionCell. Building="
                + gestator.ToStringSafe()
                + ", product="
                + product.ToStringSafe()
                + ". Will retry without resettling the bill.",
                GestatorStableHash(gestator) ^ 0x4D505247);
        }

        internal void NotifyReleaseProductMissing(Building_MassProductionMechGestator gestator)
        {
            if (!releaseMissingLogged)
            {
                releaseMissingLogged = true;
                Log.Error(
                    "[MAP-MechanoidMechanitor] Mass production gestator releasePending was true but no pawn remains in innerContainer. Clearing releasePending. Building="
                    + gestator.ToStringSafe()
                    + ".");
            }

            ClearReleasePending();
        }

        private void TickReleasePending(Building_MassProductionMechGestator gestator)
        {
            if (!gestator.IsHashIntervalTick(ReleaseRetryIntervalTicks))
            {
                return;
            }

            Pawn? product = MassProductionMechGestatorCompletionUtility.FindContainedPawn(gestator);
            if (product == null)
            {
                NotifyReleaseProductMissing(gestator);
                return;
            }

            if (MassProductionMechGestatorCompletionUtility.TryReleaseProduct(gestator, this, product)
                && gestator.Map != null)
            {
                ApplyPendingResourceCountUpdate(gestator.Map);
            }
        }

        private void TickForming(
            Building_MassProductionMechGestator gestator,
            Bill_ProductionMech productionBill)
        {
            if (!timerInitialized)
            {
                remainingTicks = Building_MassProductionMechGestator.FixedFormingTicks;
                timerInitialized = true;
            }

            if (productionBill.suspended
                || !gestator.PoweredOn
                || !gestator.BoundPawnStateAllowsForming)
            {
                return;
            }

            if (remainingTicks > 0)
            {
                remainingTicks--;
            }

            if (remainingTicks <= 0)
            {
                CompleteFormingThenSettle(gestator, productionBill);
            }
        }

        private void CompleteFormingThenSettle(
            Building_MassProductionMechGestator gestator,
            Bill_ProductionMech bill)
        {
            if (completionInProgress)
            {
                return;
            }

            completionInProgress = true;
            try
            {
                bill.ForceCompleteAllCycles();
                MassProductionMechGestatorTimingPatches.RunWithVanillaBillTickAllowed(
                    bill,
                    bill.BillTick);

                if (!MassProductionMechGestatorCompletionUtility.TryAutoSettleAndRelease(
                        gestator,
                        this,
                        bill)
                    && !releasePending)
                {
                    NotifySettlementBlocked(bill);
                }
            }
            catch (Exception e)
            {
                if (!releasePending)
                {
                    NotifySettlementBlocked(bill);
                }

                Log.Error(
                    "[MAP-MechanoidMechanitor] Mass production gestator completion failed. Building="
                    + gestator.ToStringSafe()
                    + ", bill="
                    + bill.ToStringSafe()
                    + ", recipe="
                    + bill.recipe.ToStringSafe()
                    + ": "
                    + e);
            }
            finally
            {
                completionInProgress = false;
                if (bill.State != FormingState.Forming)
                {
                    ResetTimer();
                }
            }
        }

        private void RunSettlement(
            Building_MassProductionMechGestator gestator,
            Bill_ProductionMech bill)
        {
            if (completionInProgress)
            {
                return;
            }

            completionInProgress = true;
            try
            {
                if (!MassProductionMechGestatorCompletionUtility.TryAutoSettleAndRelease(
                        gestator,
                        this,
                        bill)
                    && !releasePending)
                {
                    NotifySettlementBlocked(bill);
                }
            }
            catch (Exception e)
            {
                if (!releasePending)
                {
                    NotifySettlementBlocked(bill);
                }

                Log.Error(
                    "[MAP-MechanoidMechanitor] Mass production gestator auto-settle from Formed state failed. Building="
                    + gestator.ToStringSafe()
                    + ", bill="
                    + bill.ToStringSafe()
                    + ", recipe="
                    + bill.recipe.ToStringSafe()
                    + ": "
                    + e);
            }
            finally
            {
                completionInProgress = false;
            }
        }

        private void ResetTimer()
        {
            timerInitialized = false;
            remainingTicks = -1;
        }

        private static int GestatorStableHash(Building_MassProductionMechGestator gestator)
        {
            return gestator.thingIDNumber;
        }
    }
}
