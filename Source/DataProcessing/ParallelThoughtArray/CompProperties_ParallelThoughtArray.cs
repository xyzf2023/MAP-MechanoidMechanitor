using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_ParallelThoughtArray : CompProperties
    {
        public int minBoostPercent = ParallelThoughtArrayUtility.MinBoostPercent;
        public int maxBoostPercent = ParallelThoughtArrayUtility.MaxBoostPercent;
        public int boostStepPercent = ParallelThoughtArrayUtility.BoostStepPercent;
        public float basePowerConsumption = ParallelThoughtArrayUtility.BasePowerConsumption;
        public float powerPerBoostStep = ParallelThoughtArrayUtility.PowerPerBoostStep;
        public float idlePowerConsumption = ParallelThoughtArrayUtility.IdlePowerConsumption;
        public int fallbackRefreshIntervalTicks = ParallelThoughtArrayUtility.FallbackRefreshIntervalTicks;

        public CompProperties_ParallelThoughtArray()
        {
            compClass = typeof(CompParallelThoughtArray);
        }
    }
}
