using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class Building_MassProductionMechGestator : Building_MechGestator
    {
        public const int FixedFormingTicks = 15000;

        public CompMassProductionMechGestator? MassProductionGestationComp =>
            GetComp<CompMassProductionMechGestator>();

        protected override string GetInspectStringExtra()
        {
            if (ActiveMechBill is not Bill_ProductionMech { State: FormingState.Forming })
            {
                return base.GetInspectStringExtra();
            }

            CompMassProductionMechGestator? comp = MassProductionGestationComp;
            if (comp == null || !comp.TimerInitialized)
            {
                return base.GetInspectStringExtra();
            }

            return string.Format(
                "{0}: {1}",
                "GestatingInspect".Translate(),
                Mathf.CeilToInt(comp.RemainingTicks).ToStringTicksToPeriod());
        }
    }
}
