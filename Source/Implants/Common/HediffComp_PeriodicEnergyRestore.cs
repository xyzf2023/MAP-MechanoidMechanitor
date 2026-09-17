using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class HediffComp_PeriodicEnergyRestore : HediffComp
    {
        private int ticksUntilRestore;

        private HediffCompProperties_PeriodicEnergyRestore Props =>
            (HediffCompProperties_PeriodicEnergyRestore)props;

        public override void CompPostMake()
        {
            base.CompPostMake();
            ticksUntilRestore = Mathf.Max(1, Props.intervalTicks);
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            ticksUntilRestore--;
            if (ticksUntilRestore > 0)
            {
                return;
            }

            ticksUntilRestore = Mathf.Max(1, Props.intervalTicks);
            if (Pawn.needs?.energy == null)
            {
                return;
            }

            float restored = Pawn.needs.energy.MaxLevel * Mathf.Max(0f, Props.restoreFraction);
            Pawn.needs.energy.CurLevel = Mathf.Min(
                Pawn.needs.energy.MaxLevel,
                Pawn.needs.energy.CurLevel + restored);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref ticksUntilRestore, "ticksUntilRestore", 0);
        }
    }

    public sealed class HediffCompProperties_PeriodicEnergyRestore : HediffCompProperties
    {
        public int intervalTicks = 300;
        public float restoreFraction = 0.01f;

        public HediffCompProperties_PeriodicEnergyRestore()
        {
            compClass = typeof(HediffComp_PeriodicEnergyRestore);
        }
    }
}
