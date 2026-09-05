using System;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class HediffCompProperties_MechanoidMechanitorModeRecovery : HediffCompProperties
    {
        public int repairInterval = 600;
        public int repairAmount = 10;
        public int energyRestoreInterval = 300;
        public float energyRestoreFraction = 0.01f;
        public bool showRecoveryRates;

        public HediffCompProperties_MechanoidMechanitorModeRecovery()
        {
            compClass = typeof(HediffComp_MechanoidMechanitorModeRecovery);
        }
    }

    public class HediffComp_MechanoidMechanitorModeRecovery : HediffComp
    {
        private const float TicksPerHour = 2500f;

        public HediffCompProperties_MechanoidMechanitorModeRecovery Props =>
            (HediffCompProperties_MechanoidMechanitorModeRecovery)props;

        public override string CompTipStringExtra
        {
            get
            {
                if (!Props.showRecoveryRates)
                {
                    return string.Empty;
                }

                StringBuilder result = new StringBuilder();
                if (Props.repairInterval > 0 && Props.repairAmount > 0)
                {
                    int repairPerHour = Mathf.RoundToInt(
                        Props.repairAmount * TicksPerHour / Props.repairInterval);
                    result.Append(
                        "MAP_MechanoidMechanitor.SelfWorkMode.Recovery.RepairPerHour"
                            .Translate(repairPerHour));
                }

                if (Props.energyRestoreInterval > 0 && Props.energyRestoreFraction > 0f)
                {
                    if (result.Length > 0)
                    {
                        result.AppendLine();
                    }

                    int energyPercentPerHour = Mathf.RoundToInt(
                        Props.energyRestoreFraction
                            * 100f
                            * TicksPerHour
                            / Props.energyRestoreInterval);
                    result.Append(
                        "MAP_MechanoidMechanitor.SelfWorkMode.Recovery.EnergyPerHour"
                            .Translate(energyPercentPerHour));
                }

                return result.ToString();
            }
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

            if (Props.repairInterval > 0 && Props.repairAmount > 0)
            {
                int repairCrossings = CountIntervalCrossings(
                    currentHashTick,
                    delta,
                    Props.repairInterval);
                if (repairCrossings > 0)
                {
                    long repairUnitsLong = (long)repairCrossings * Props.repairAmount;
                    if (repairUnitsLong > 0)
                    {
                        for (long i = 0; i < repairUnitsLong; i++)
                        {
                            if (!MechRepairUtility.CanRepair(pawn))
                            {
                                break;
                            }

                            MechRepairUtility.RepairTick(pawn);
                        }
                    }
                }
            }

            if (Props.energyRestoreInterval > 0 && Props.energyRestoreFraction > 0f)
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
