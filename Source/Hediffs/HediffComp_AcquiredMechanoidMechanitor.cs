using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class HediffCompProperties_AcquiredMechanoidMechanitor : HediffCompProperties
    {
        public HediffCompProperties_AcquiredMechanoidMechanitor()
        {
            compClass = typeof(HediffComp_AcquiredMechanoidMechanitor);
        }
    }

    public sealed class HediffComp_AcquiredMechanoidMechanitor : HediffComp
    {
        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            EnsureState();
        }

        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            Pawn?.Notify_DisabledWorkTypesChanged();
            Pawn?.mechanitor?.Notify_BandwidthChanged();
            GameComponent_MechanoidMechanitorRegistry.NotifyAcquiredMechanitorHediffRemoved(Pawn);
        }

        public override void Notify_Spawned()
        {
            base.Notify_Spawned();
            GameComponent_MechanoidMechanitorRegistry.RequestAcquiredHediffMirror(Pawn);
            EnsureState();
        }

        private void EnsureState()
        {
            if (Pawn == null || Pawn.Destroyed)
            {
                return;
            }

            if (!GameComponent_MechanoidMechanitorRegistry.TryGetAcquiredMechanitorRecord(
                    Pawn,
                    out MechanoidMechanitorRecord? record)
                || record == null)
            {
                return;
            }

            WardenWorkUtility.GrantAndEnsureInfrastructure(Pawn);

            if (Pawn.story == null)
            {
                Pawn.story = new Pawn_StoryTracker(Pawn);
            }
            if (Pawn.story.bodyType == null)
            {
                Pawn.story.bodyType = BodyTypeDefOf.Male;
            }

            Pawn.Notify_DisabledWorkTypesChanged();
            MechanoidMechanitorRoleUtility.EnsureRoleState(Pawn);
            InitializeRoleWorkSettingsIfNeeded(record);
            MechanoidMechanitorSelfWorkModeUtility.ApplyAcquiredSelfWorkMode(
                Pawn,
                record.SelfWorkMode
                    ?? MechanoidMechanitorSelfWorkModeUtility.SanitizeWorkMode(null));
        }

        private void InitializeRoleWorkSettingsIfNeeded(MechanoidMechanitorRecord record)
        {
            if (record.RoleWorkSettingsInitialized || Pawn.workSettings == null)
            {
                return;
            }

            foreach (WorkTypeDef workType in
                     MechanoidMechanitorRoleUtility.GetRoleWorkTypes())
            {
                if (!Pawn.WorkTypeIsDisabled(workType)
                    && Pawn.workSettings.GetPriority(workType) == 0)
                {
                    Pawn.workSettings.SetPriority(workType, 3);
                }
            }

            record.RoleWorkSettingsInitialized = true;
        }
    }
}
