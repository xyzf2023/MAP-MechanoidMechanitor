using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class HediffCompProperties_JusticeModeRecovery : HediffCompProperties
    {
        public int repairInterval = 600;
        public int repairAmount = 10;
        public int energyRestoreInterval = 300;
        public float energyRestoreFraction = 0.01f;

        public HediffCompProperties_JusticeModeRecovery()
        {
            compClass = typeof(HediffComp_JusticeModeRecovery);
        }
    }

    public class HediffComp_JusticeModeRecovery : HediffComp
    {
        public HediffCompProperties_JusticeModeRecovery Props =>
            (HediffCompProperties_JusticeModeRecovery)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            ApplyRecovery(parent.pawn, 1);
        }

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            ApplyRecovery(parent.pawn, delta);
        }

        private void ApplyRecovery(Pawn pawn, int delta)
        {
            if (pawn == null || pawn.Dead || !pawn.RaceProps.IsMechanoid || pawn.health == null || delta <= 0)
            {
                return;
            }

            int currentHashTick = pawn.HashOffsetTicks();

            if (Props.repairInterval > 0)
            {
                int repairCrossings = CountIntervalCrossings(
                    currentHashTick,
                    delta,
                    Props.repairInterval);
                for (int i = 0; i < repairCrossings; i++)
                {
                    MechRepairUtility.RepairTick(pawn, Props.repairAmount);
                }
            }

            if (Props.energyRestoreInterval > 0)
            {
                int energyCrossings = CountIntervalCrossings(
                    currentHashTick,
                    delta,
                    Props.energyRestoreInterval);
                if (energyCrossings > 0)
                {
                    Need_MechEnergy? energy = pawn.needs?.energy;
                    if (energy != null && energy.CurLevel < energy.MaxLevel)
                    {
                        energy.CurLevel = Mathf.Min(
                            energy.CurLevel
                                + energy.MaxLevel * Props.energyRestoreFraction * energyCrossings,
                            energy.MaxLevel);
                    }
                }
            }
        }

        private static int CountIntervalCrossings(int currentHashTick, int delta, int interval)
        {
            int startTick = currentHashTick - delta;
            int endCount = (int)Math.Floor((double)currentHashTick / interval);
            int startCount = (int)Math.Floor((double)startTick / interval);
            return endCount - startCount;
        }
    }
}
