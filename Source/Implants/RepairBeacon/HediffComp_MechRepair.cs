using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public enum MechRepairRateDisplayUnit
    {
        PerHour,
        PerSecond
    }

    public sealed class HediffComp_MechRepair : HediffComp
    {
        private const float TicksPerHour = 2500f;
        private const float TicksPerSecond = 60f;

        private int ticksUntilRepair;

        private HediffCompProperties_MechRepair Props =>
            (HediffCompProperties_MechRepair)props;

        public override string CompTipStringExtra
        {
            get
            {
                if (Props.repairIntervalTicks <= 0 || Props.repairAmount <= 0)
                {
                    return string.Empty;
                }

                bool displayPerSecond =
                    Props.displayRateUnit == MechRepairRateDisplayUnit.PerSecond;
                float ticksPerUnit = displayPerSecond
                    ? TicksPerSecond
                    : TicksPerHour;
                int repairRate = Mathf.RoundToInt(
                    Props.repairAmount * ticksPerUnit / Props.repairIntervalTicks);
                string translationKey = displayPerSecond
                    ? "MAP_MechanoidMechanitor.SelfWorkMode.Recovery.RepairPerSecond"
                    : "MAP_MechanoidMechanitor.SelfWorkMode.Recovery.RepairPerHour";
                return translationKey.Translate(repairRate);
            }
        }

        public override void CompPostMake()
        {
            base.CompPostMake();
            ticksUntilRepair = Mathf.Max(1, Props.repairIntervalTicks);
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            if (!Pawn.RaceProps.IsMechanoid)
            {
                return;
            }

            if (Props.requiresAutoRepair)
            {
                CompMechRepairable? repairable = Pawn.TryGetComp<CompMechRepairable>();
                if (repairable?.autoRepair != true || !MechRepairUtility.CanRepair(Pawn))
                {
                    return;
                }
            }

            ticksUntilRepair--;
            if (ticksUntilRepair > 0)
            {
                return;
            }

            ticksUntilRepair = Mathf.Max(1, Props.repairIntervalTicks);
            int repairAmount = Mathf.Max(0, Props.repairAmount);
            for (int i = 0; i < repairAmount; i++)
            {
                if (!MechRepairUtility.CanRepair(Pawn))
                {
                    break;
                }

                MechRepairUtility.RepairTick(Pawn);
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref ticksUntilRepair, "ticksUntilRepair", 0);
        }
    }

    public sealed class HediffCompProperties_MechRepair : HediffCompProperties
    {
        public int repairIntervalTicks = 120;
        public int repairAmount = 10;
        public bool requiresAutoRepair = true;
        public MechRepairRateDisplayUnit displayRateUnit =
            MechRepairRateDisplayUnit.PerHour;

        public HediffCompProperties_MechRepair()
        {
            compClass = typeof(HediffComp_MechRepair);
        }
    }
}
