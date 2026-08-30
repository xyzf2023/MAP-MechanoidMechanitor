using RimWorld;
using System.Text;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class Hediff_PsychicCore : Hediff_Level
    {
        public override void PostAdd(DamageInfo? dinfo)
        {
            if (pawn?.RaceProps.IsMechanoid == true)
            {
                MechanoidMechanitorPsycastUtility.EnsurePsycastInfrastructure(pawn);
            }

            base.PostAdd(dinfo);
            Severity = level;

            ClearExistingDisruptorFlash();
            MechanoidMechanitorPsycastUtility.SyncPsychicReceiver(pawn);
            if (pawn?.RaceProps.IsMechanoid == true)
            {
                MechanoidMechanitorPsycastUtility.EnsurePsycastInfrastructure(pawn);
            }

            PsychicCoreUtility.TryGainPsylinkLevel(pawn);
        }

        public override void ChangeLevel(int levelOffset)
        {
            int oldLevel = level;
            base.ChangeLevel(levelOffset);
            Severity = level;

            int gainedLevels = level - oldLevel;
            for (int i = 0; i < gainedLevels; i++)
            {
                PsychicCoreUtility.TryGainPsylinkLevel(pawn);
            }

            MechanoidMechanitorPsycastUtility.SyncPsychicReceiver(pawn);
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);

            if (pawn == null
                || pawn.Destroyed
                || pawn.Dead
                || pawn.psychicEntropy == null
                || !pawn.HasPsylink)
            {
                return;
            }

            float multiplier =
                PsychicCoreUtility.GetTotalPsyfocusRecoveryMultiplier(
                    pawn,
                    level);
            if (multiplier <= 0f)
            {
                return;
            }

            float offset =
                PsychicCoreUtility.StandardNaturalMeditationPsyfocusPerDay
                * multiplier
                * delta
                / 60000f;
            pawn.psychicEntropy.OffsetPsyfocusDirectly(offset);
        }

        public override string TipStringExtra
        {
            get
            {
                StringBuilder stringBuilder = new StringBuilder();
                string baseTip = base.TipStringExtra;
                if (!baseTip.NullOrEmpty())
                {
                    stringBuilder.Append(baseTip);
                    stringBuilder.AppendLine();
                }

                float baseMultiplier =
                    PsychicCoreUtility.GetPassivePsyfocusRecoveryMultiplier(level);
                if (baseMultiplier > 0f)
                {
                    stringBuilder.AppendLine(
                        " - 精神力自动恢复："
                        + PsychicCoreUtility.FormatPsyfocusPercent(
                            PsychicCoreUtility.GetPsyfocusRecoveryPerHour(baseMultiplier))
                        + "/小时");
                }

                if (CurStage is { blocksMentalBreaks: true })
                {
                    stringBuilder.AppendLine(" - 不再陷入精神崩溃");
                }

                return stringBuilder.ToString().TrimEnd('\n', '\r');
            }
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            ClearPsychicActivation();
            MechanoidMechanitorPsycastUtility.SyncPsychicReceiver(pawn);
        }

        private void ClearExistingDisruptorFlash()
        {
            if (pawn?.health?.hediffSet == null || HediffDefOf.DisruptorFlash == null)
            {
                return;
            }

            Hediff? existing =
                pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.DisruptorFlash);
            while (existing != null)
            {
                pawn.health.RemoveHediff(existing);
                existing =
                    pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.DisruptorFlash);
            }
        }

        private void ClearPsychicActivation()
        {
            HediffDef? activationDef = PsychicCoreUtility.PsychicActivationHediffDef;
            if (pawn?.health?.hediffSet == null || activationDef == null)
            {
                return;
            }

            Hediff? active = pawn.health.hediffSet.GetFirstHediffOfDef(activationDef);
            if (active != null)
            {
                pawn.health.RemoveHediff(active);
            }
        }
    }
}
