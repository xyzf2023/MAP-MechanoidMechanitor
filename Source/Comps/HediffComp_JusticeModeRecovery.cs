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
            Pawn pawn = parent.pawn;
            if (pawn == null || pawn.Dead || !pawn.RaceProps.IsMechanoid || pawn.health == null)
            {
                return;
            }

            if (pawn.IsHashIntervalTick(Props.repairInterval))
            {
                MechRepairUtility.RepairTick(pawn, Props.repairAmount);
            }

            if (pawn.IsHashIntervalTick(Props.energyRestoreInterval))
            {
                Need_MechEnergy? energy = pawn.needs?.energy;
                if (energy != null && energy.CurLevel < energy.MaxLevel)
                {
                    energy.CurLevel = Mathf.Min(
                        energy.CurLevel + energy.MaxLevel * Props.energyRestoreFraction,
                        energy.MaxLevel);
                }
            }
        }
    }
}
