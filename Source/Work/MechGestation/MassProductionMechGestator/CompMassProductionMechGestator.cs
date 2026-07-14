using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompMassProductionMechGestator : ThingComp
    {
        private int remainingTicks = -1;

        private bool timerInitialized;

        private bool completionInProgress;

        public int RemainingTicks => remainingTicks;

        public bool TimerInitialized => timerInitialized;

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

            Bill_Mech? activeBill = gestator.ActiveMechBill;
            if (activeBill is not Bill_ProductionMech productionBill
                || productionBill.State != FormingState.Forming)
            {
                ResetTimer();
                return;
            }

            if (!timerInitialized)
            {
                remainingTicks = Building_MassProductionMechGestator.FixedFormingTicks;
                timerInitialized = true;
            }

            if (completionInProgress)
            {
                return;
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
                CompleteForming(productionBill);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref remainingTicks, "massProdGestatorRemainingTicks", -1);
            Scribe_Values.Look(ref timerInitialized, "massProdGestatorTimerInitialized", false);

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

        private void ResetTimer()
        {
            timerInitialized = false;
            remainingTicks = -1;
        }

        private void CompleteForming(Bill_ProductionMech bill)
        {
            if (completionInProgress)
            {
                return;
            }

            completionInProgress = true;
            try
            {
                bill.ForceCompleteAllCycles();
                MassProductionMechGestatorTimingPatches.AllowVanillaBillTickOnce = true;
                try
                {
                    bill.BillTick();
                }
                finally
                {
                    MassProductionMechGestatorTimingPatches.AllowVanillaBillTickOnce = false;
                }
            }
            finally
            {
                completionInProgress = false;
            }
        }
    }
}
