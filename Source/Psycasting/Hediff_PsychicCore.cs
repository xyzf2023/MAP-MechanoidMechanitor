using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class Hediff_PsychicCore : Hediff_Level
    {
        private int pendingPsyfocusRecoveryTicks;

        public override void PostAdd(DamageInfo? dinfo)
        {
            if (pawn?.RaceProps.IsMechanoid == true)
            {
                MechanoidMechanitorPsycastUtility.EnsurePsycastInfrastructure(pawn);
            }

            base.PostAdd(dinfo);
            Severity = level;

            PsychicCoreUtility.ClearExistingDisruptorFlash(pawn);
            MechanoidMechanitorPsycastUtility.SyncPsychicReceiver(pawn);
            if (pawn?.RaceProps.IsMechanoid == true)
            {
                MechanoidMechanitorPsycastUtility.EnsurePsycastInfrastructure(pawn);
            }

            PsychicCoreUtility.TryGainPsylinkLevel(pawn);
            PsychicCoreUtility.SyncPsychicActivationAbility(pawn);
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
            PsychicCoreUtility.TickRuntimeEffects(
                this,
                ref pendingPsyfocusRecoveryTicks,
                delta);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref pendingPsyfocusRecoveryTicks,
                "pendingPsyfocusRecoveryTicks",
                0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pendingPsyfocusRecoveryTicks =
                    pendingPsyfocusRecoveryTicks < 0
                        ? 0
                        : System.Math.Min(
                            pendingPsyfocusRecoveryTicks,
                            PsychicCoreUtility.PsyfocusRecoverySettlementTicks);
            }
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            PsychicCoreUtility.SyncPsychicActivationAbility(pawn);
            MechanoidMechanitorPsycastUtility.SyncPsychicReceiver(pawn);
        }
    }
}
