using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [DefOf]
    public static class MechServiceStationDefOf
    {
        public static JobDef MAP_Job_UseMechServiceStation = null!;

        static MechServiceStationDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(MechServiceStationDefOf));
    }

    public sealed class CompProperties_MechServiceStation : CompProperties
    {
        public float energyPerTick = 0.005f;
        public int repairIntervalTicks = 60;
        public int repairAmount = 10;

        public CompProperties_MechServiceStation() => compClass = typeof(CompMechServiceStation);
    }
}
