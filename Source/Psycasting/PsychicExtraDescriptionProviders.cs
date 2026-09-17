using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class HediffExtraDescriptionProvider_PsychicCore : HediffExtraDescriptionProvider
    {
        public override IEnumerable<HediffExtraDescriptionEntry> GetEntries(Hediff hediff)
        {
            if (!PsychicCoreUtility.IsRuntimeEffectExecutor(hediff))
            {
                yield break;
            }

            int level = PsychicCoreUtility.GetEffectivePsychicCoreLevel(hediff.pawn);
            float recovery = PsychicCoreUtility.GetPassivePsyfocusRecoveryPerHour(level);
            if (recovery > 0f)
            {
                yield return new HediffExtraDescriptionEntry(
                    "MAP_MechanoidMechanitor.HediffExtra.PsyfocusPerHour",
                    PsychicCoreUtility.FormatPsyfocusPercent(recovery));
            }

            if (hediff.CurStage is { blocksMentalBreaks: true })
            {
                yield return new HediffExtraDescriptionEntry(
                    "MAP_MechanoidMechanitor.HediffExtra.BlocksMentalBreaks");
            }
        }
    }

    public sealed class HediffExtraDescriptionProvider_PsychicActivation : HediffExtraDescriptionProvider
    {
        public override IEnumerable<HediffExtraDescriptionEntry> GetEntries(Hediff hediff)
        {
            float bonus = PsychicCoreUtility.GetPsychicActivationBonusPerHour(
                hediff.pawn, PsychicCoreUtility.GetEffectivePsychicCoreLevel(hediff.pawn));
            if (bonus > 0f)
            {
                yield return new HediffExtraDescriptionEntry(
                    "MAP_MechanoidMechanitor.HediffExtra.PsyfocusPerHour",
                    PsychicCoreUtility.FormatPsyfocusPercent(bonus));
            }
        }
    }
}
