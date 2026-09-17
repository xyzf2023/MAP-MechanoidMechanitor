using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class HediffExtraDescriptionProvider_ProductivityCore : HediffExtraDescriptionProvider
    {
        public override IEnumerable<HediffExtraDescriptionEntry> GetEntries(Hediff hediff)
        {
            if (hediff.pawn == null)
            {
                yield break;
            }

            const string keyPrefix = "MAP_MechanoidMechanitor.Implants.ProductivityCore.ActiveTooltip.";
            int level = ProductivityCoreUtility.GetEffectiveLevelForWorker(hediff.pawn);
            float offset = level * ProductivityCoreUtility.WorkSpeedOffsetPerLevel;
            yield return new HediffExtraDescriptionEntry(keyPrefix + "WorkSpeed", offset.ToStringPercent());
            yield return new HediffExtraDescriptionEntry(keyPrefix + "Quality", level);
        }
    }

    public enum MechRepairRateDisplayUnit
    {
        PerHour,
        PerSecond
    }

    public sealed class HediffExtraDescriptionProvider_MechRepair : HediffExtraDescriptionProvider
    {
        public MechRepairRateDisplayUnit displayRateUnit = MechRepairRateDisplayUnit.PerHour;

        public override IEnumerable<HediffExtraDescriptionEntry> GetEntries(Hediff hediff)
        {
            var repair = hediff.TryGetComp<HediffComp_MechRepair>()?.props
                as HediffCompProperties_MechRepair;
            if (repair == null || repair.repairIntervalTicks <= 0 || repair.repairAmount <= 0)
            {
                yield break;
            }

            bool perSecond = displayRateUnit == MechRepairRateDisplayUnit.PerSecond;
            float ticksPerUnit = perSecond ? 60f : 2500f;
            int rate = Mathf.RoundToInt(repair.repairAmount * ticksPerUnit / repair.repairIntervalTicks);
            yield return new HediffExtraDescriptionEntry(
                perSecond
                    ? "MAP_MechanoidMechanitor.SelfWorkMode.Recovery.RepairPerSecond"
                    : "MAP_MechanoidMechanitor.SelfWorkMode.Recovery.RepairPerHour",
                rate);
        }
    }

    public sealed class HediffExtraDescriptionProvider_PeriodicEnergyRestore : HediffExtraDescriptionProvider
    {
        public override IEnumerable<HediffExtraDescriptionEntry> GetEntries(Hediff hediff)
        {
            var restore = hediff.TryGetComp<HediffComp_PeriodicEnergyRestore>()?.props
                as HediffCompProperties_PeriodicEnergyRestore;
            if (restore == null || restore.intervalTicks <= 0 || restore.restoreFraction <= 0f)
            {
                yield break;
            }

            float rate = restore.restoreFraction * 100f * 2500f / restore.intervalTicks;
            yield return new HediffExtraDescriptionEntry(
                "MAP_MechanoidMechanitor.SelfWorkMode.Recovery.EnergyPerHour", rate.ToString("F1"));
        }
    }
}
