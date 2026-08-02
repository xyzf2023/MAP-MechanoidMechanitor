using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompMassProductionMechGestator : ThingComp
    {
        private const int ReleaseRetryIntervalTicks = 60;

        private const int IdleBillPollIntervalTicks = 60;

        private int remainingTicks = -1;

        private bool timerInitialized;

        private bool completionInProgress;

        private bool settlementCommitted;

        private bool releasePending;

        private bool pendingResourceCountUpdate;

        private bool settlementNotificationsSent;

        private bool releaseFailureLogged;

        private bool releaseMissingLogged;

        /// <summary>
        /// 运行时空闲确认：最近完整检查时无 Forming/Formed/已提交结算/待释放需逐 Tick 处理。
        /// 不序列化；读档后默认为 false，强制首 Tick 完整检查。
        /// </summary>
        private bool idleStateConfirmed;

        private Bill_Mech? settlementBlockedForBill;

        private Bill_Mech? committedBill;

        private Pawn? settledProducer;

        public int RemainingTicks => remainingTicks;

        public bool TimerInitialized => timerInitialized;

        public bool SettlementCommitted => settlementCommitted;

        public bool ReleasePending => releasePending;

        public bool SettlementNotificationsSent => settlementNotificationsSent;

        public Bill_Mech? CommittedBill => committedBill;

        public Pawn? SettledProducer => settledProducer;

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

            // 第一优先级：已提交结算或待释放时，绝不二次结算。
            if (settlementCommitted || releasePending)
            {
                idleStateConfirmed = false;
                TickCommittedSettlement(gestator);
                return;
            }

            if (completionInProgress)
            {
                idleStateConfirmed = false;
                return;
            }

            // 已确认空闲：仅在哈希间隔轮询账单，避免每 Tick 读取 ActiveMechBill。
            if (idleStateConfirmed && !gestator.IsHashIntervalTick(IdleBillPollIntervalTicks))
            {
                return;
            }

            Bill_Mech? activeBill = gestator.ActiveMechBill;

            // 第二优先级：受支持账单已 Formed，且尚未提交结算。
            if (MassProductionMechGestatorBillUtility.IsSupported(activeBill)
                && activeBill!.State == FormingState.Formed)
            {
                idleStateConfirmed = false;
                ResetTimer();
                if (!ReferenceEquals(settlementBlockedForBill, activeBill))
                {
                    RunSettlement(gestator, activeBill);
                }

                return;
            }

            if (!ReferenceEquals(settlementBlockedForBill, activeBill))
            {
                settlementBlockedForBill = null;
            }

            // 第三优先级：受支持账单正在 Forming。
            if (MassProductionMechGestatorBillUtility.IsSupported(activeBill)
                && activeBill!.State == FormingState.Forming)
            {
                idleStateConfirmed = false;
                if (!ReferenceEquals(settlementBlockedForBill, activeBill))
                {
                    TickForming(gestator, activeBill);
                }

                return;
            }

            // 第四优先级：其他状态。不清除仍有效的 settlementCommitted（已在第一优先级处理）。
            ResetTimer();

            // 仅无结算受阻时确认空闲；存在 settlementBlockedForBill 时保持逐 Tick 检查。
            if (settlementBlockedForBill == null)
            {
                idleStateConfirmed = true;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref remainingTicks, "massProdGestatorRemainingTicks", -1);
            Scribe_Values.Look(ref timerInitialized, "massProdGestatorTimerInitialized", false);
            Scribe_Values.Look(ref settlementCommitted, "massProdGestatorSettlementCommitted", false);
            Scribe_Values.Look(ref releasePending, "massProdGestatorReleasePending", false);
            Scribe_Values.Look(ref pendingResourceCountUpdate, "massProdGestatorPendingResourceCountUpdate", false);
            Scribe_Values.Look(ref settlementNotificationsSent, "massProdGestatorSettlementNotificationsSent", false);
            Scribe_References.Look(ref committedBill, "massProdGestatorCommittedBill");
            Scribe_References.Look(ref settledProducer, "massProdGestatorSettledProducer");

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

        internal void MarkSettlementCommitted(
            Bill_Mech bill,
            Pawn producer,
            bool updateResourceCountsOnRelease)
        {
            idleStateConfirmed = false;
            settlementCommitted = true;
            releasePending = true;
            committedBill = bill;
            settledProducer = producer;
            pendingResourceCountUpdate = updateResourceCountsOnRelease;
            settlementNotificationsSent = false;
            releaseFailureLogged = false;
            releaseMissingLogged = false;
            settlementBlockedForBill = null;
        }

        internal void MarkSettlementNotificationsSent()
        {
            settlementNotificationsSent = true;
        }

        internal void NotifySettlementFullyCompleted()
        {
            idleStateConfirmed = false;
            releasePending = false;
            settlementCommitted = false;
            committedBill = null;
            settledProducer = null;
            settlementNotificationsSent = false;
            releaseFailureLogged = false;
            releaseMissingLogged = false;
        }

        internal void ClearSettlementAndReleaseState()
        {
            idleStateConfirmed = false;
            releasePending = false;
            settlementCommitted = false;
            committedBill = null;
            settledProducer = null;
            pendingResourceCountUpdate = false;
            settlementNotificationsSent = false;
            releaseFailureLogged = false;
            releaseMissingLogged = false;
        }

        internal void NotifySettlementBlocked(Bill_Mech bill)
        {
            idleStateConfirmed = false;
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
                    "[MAP-MechanoidMechanitor] Mass production gestator settlementCommitted/releasePending was true but no pawn remains in innerContainer. Clearing settlement state. Building="
                    + gestator.ToStringSafe()
                    + ".");
            }

            ClearSettlementAndReleaseState();
        }

        internal void DebugAdvanceForming(float fraction)
        {
            if (parent is not Building_MassProductionMechGestator gestator)
            {
                return;
            }

            Bill_Mech? activeBill = gestator.ActiveMechBill;
            if (!MassProductionMechGestatorBillUtility.IsSupported(activeBill)
                || activeBill!.State != FormingState.Forming)
            {
                return;
            }

            if (settlementCommitted || releasePending || completionInProgress)
            {
                return;
            }

            if (fraction <= 0f)
            {
                return;
            }

            idleStateConfirmed = false;

            if (!timerInitialized)
            {
                remainingTicks = Building_MassProductionMechGestator.FixedFormingTicks;
                timerInitialized = true;
            }

            remainingTicks -= Mathf.RoundToInt(
                Building_MassProductionMechGestator.FixedFormingTicks * fraction);
            remainingTicks = Mathf.Clamp(
                remainingTicks,
                0,
                Building_MassProductionMechGestator.FixedFormingTicks);
        }

        internal void DebugCompleteForming()
        {
            if (parent is not Building_MassProductionMechGestator gestator)
            {
                return;
            }

            Bill_Mech? activeBill = gestator.ActiveMechBill;
            if (!MassProductionMechGestatorBillUtility.IsSupported(activeBill)
                || activeBill!.State != FormingState.Forming)
            {
                return;
            }

            if (settlementCommitted || releasePending || completionInProgress)
            {
                return;
            }

            idleStateConfirmed = false;

            if (!timerInitialized)
            {
                remainingTicks = Building_MassProductionMechGestator.FixedFormingTicks;
                timerInitialized = true;
            }

            remainingTicks = 0;
        }

        internal void DebugCompleteAllForming()
        {
            DebugCompleteForming();
        }

        private void TickCommittedSettlement(Building_MassProductionMechGestator gestator)
        {
            if (!gestator.IsHashIntervalTick(ReleaseRetryIntervalTicks))
            {
                return;
            }

            MassProductionMechGestatorCompletionUtility.TryContinueCommittedSettlement(gestator, this);
        }

        private void TickForming(
            Building_MassProductionMechGestator gestator,
            Bill_Mech bill)
        {
            if (!timerInitialized)
            {
                remainingTicks = Building_MassProductionMechGestator.FixedFormingTicks;
                timerInitialized = true;
            }

            if (bill.suspended
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
                CompleteFormingThenSettle(gestator, bill);
            }
        }

        private void CompleteFormingThenSettle(
            Building_MassProductionMechGestator gestator,
            Bill_Mech bill)
        {
            if (completionInProgress || settlementCommitted)
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
                    && !settlementCommitted
                    && !releasePending)
                {
                    NotifySettlementBlocked(bill);
                }
            }
            catch (Exception e)
            {
                if (!settlementCommitted && !releasePending)
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
            Bill_Mech bill)
        {
            if (completionInProgress || settlementCommitted)
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
                    && !settlementCommitted
                    && !releasePending)
                {
                    NotifySettlementBlocked(bill);
                }
            }
            catch (Exception e)
            {
                if (!settlementCommitted && !releasePending)
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
            if (!timerInitialized && remainingTicks == -1)
            {
                return;
            }

            timerInitialized = false;
            remainingTicks = -1;
        }

        private static int GestatorStableHash(Building_MassProductionMechGestator gestator)
        {
            return gestator.thingIDNumber;
        }
    }
}
