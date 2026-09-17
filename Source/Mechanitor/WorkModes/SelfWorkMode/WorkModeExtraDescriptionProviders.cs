using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class HediffExtraDescriptionProvider_WorkModeRecovery : HediffExtraDescriptionProvider
    {
        public override IEnumerable<HediffExtraDescriptionEntry> GetEntries(Hediff hediff)
        {
            var recovery = hediff.TryGetComp<HediffComp_MechanoidMechanitorModeRecovery>()?.props
                as HediffCompProperties_MechanoidMechanitorModeRecovery;
            if (recovery == null)
            {
                yield break;
            }

            if (recovery.repairInterval > 0 && recovery.repairAmount > 0)
            {
                int rate = Mathf.RoundToInt(recovery.repairAmount * 2500f / recovery.repairInterval);
                yield return new HediffExtraDescriptionEntry(
                    "MAP_MechanoidMechanitor.SelfWorkMode.Recovery.RepairPerHour", rate);
            }

            if (recovery.energyRestoreInterval > 0 && recovery.energyRestoreFraction > 0f)
            {
                float rate = recovery.energyRestoreFraction * 100f * 2500f / recovery.energyRestoreInterval;
                yield return new HediffExtraDescriptionEntry(
                    "MAP_MechanoidMechanitor.SelfWorkMode.Recovery.EnergyPerHour", rate.ToString("F1"));
            }
        }
    }
}
