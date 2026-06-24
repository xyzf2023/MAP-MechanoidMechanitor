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
        private int chipBandwidthBonus;
        private MechWorkModeDef? selfWorkMode;
        private bool roleWorkSettingsInitialized;

        public int ChipBandwidthBonus => chipBandwidthBonus;

        public int CurrentIntrinsicBandwidth =>
            MechanoidMechanitorRoleUtility.AcquiredBaseExtraBandwidth + chipBandwidthBonus;

        public int MaxIntrinsicBandwidth =>
            MechanoidMechanitorRoleUtility.AcquiredMaxIntrinsicBandwidth;

        public int RemainingIntrinsicBandwidth =>
            System.Math.Max(0, MaxIntrinsicBandwidth - CurrentIntrinsicBandwidth);

        public MechWorkModeDef CurrentSelfWorkMode =>
            MechanoidMechanitorSelfWorkModeUtility.SanitizeWorkMode(selfWorkMode);

        public int AddChipBandwidth(int requestedAmount)
        {
            if (requestedAmount <= 0)
            {
                return 0;
            }

            int actualAdded = System.Math.Min(requestedAmount, RemainingIntrinsicBandwidth);
            if (actualAdded <= 0)
            {
                return 0;
            }

            chipBandwidthBonus += actualAdded;
            Pawn.mechanitor?.Notify_BandwidthChanged();
            return actualAdded;
        }

        public void SetSelfWorkMode(MechWorkModeDef? mode)
        {
            MechWorkModeDef sanitized =
                MechanoidMechanitorSelfWorkModeUtility.SanitizeWorkMode(mode);
            if (CurrentSelfWorkMode == sanitized)
            {
                return;
            }

            selfWorkMode = sanitized;
            MechanoidMechanitorSelfWorkModeUtility.ApplyAcquiredSelfWorkMode(Pawn, sanitized);
            MechanoidMechanitorSelfWorkModeUtility.NotifyModeChanged(Pawn, sanitized);
        }

        public override void CompPostMake()
        {
            base.CompPostMake();
            selfWorkMode = MechanoidMechanitorSelfWorkModeUtility.SanitizeWorkMode(selfWorkMode);
        }

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            SyncRegistryOnAdd();
            EnsureState();
        }

        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            Pawn?.Notify_DisabledWorkTypesChanged();
            Pawn?.mechanitor?.Notify_BandwidthChanged();

            if (Pawn != null)
            {
                GameComponent_MechanoidMechanitorRegistry
                    .NotifyAcquiredMechanitorHediffRemoved(Pawn);
            }
        }

        public override void Notify_Spawned()
        {
            base.Notify_Spawned();
            GameComponent_MechanoidMechanitorRegistry.RefreshMechanitorRegistration(Pawn);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref chipBandwidthBonus, "acquiredMechanitorChipBandwidthBonus", 0);
            Scribe_Defs.Look(ref selfWorkMode, "acquiredMechanitorSelfWorkMode");
            Scribe_Values.Look(
                ref roleWorkSettingsInitialized,
                "acquiredMechanitorRoleWorkSettingsInitialized",
                false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                int maxBonus = System.Math.Max(
                    0,
                    MaxIntrinsicBandwidth - MechanoidMechanitorRoleUtility.AcquiredBaseExtraBandwidth);
                chipBandwidthBonus = UnityEngine.Mathf.Clamp(chipBandwidthBonus, 0, maxBonus);
                selfWorkMode = MechanoidMechanitorSelfWorkModeUtility.SanitizeWorkMode(selfWorkMode);

                GameComponent_MechanoidMechanitorRegistry.RegisterLegacyAcquiredMechanitor(Pawn);
                GameComponent_MechanoidMechanitorRegistry.RefreshMechanitorRegistration(Pawn);
                LongEventHandler.ExecuteWhenFinished(EnsureState);
            }
        }

        public override void CopyFrom(HediffComp other)
        {
            base.CopyFrom(other);
            if (other is not HediffComp_AcquiredMechanoidMechanitor source)
            {
                return;
            }

            chipBandwidthBonus = source.chipBandwidthBonus;
            selfWorkMode = source.selfWorkMode;
            roleWorkSettingsInitialized = source.roleWorkSettingsInitialized;
        }

        private void SyncRegistryOnAdd()
        {
            if (Pawn == null)
            {
                return;
            }

            if (GameComponent_MechanoidMechanitorRegistry.IsRegisteredAcquiredMechanitor(Pawn))
            {
                GameComponent_MechanoidMechanitorRegistry.RefreshMechanitorRegistration(Pawn);
            }
            else
            {
                GameComponent_MechanoidMechanitorRegistry.RegisterLegacyAcquiredMechanitor(Pawn);
            }
        }

        private void EnsureState()
        {
            if (Pawn == null || Pawn.Destroyed)
            {
                return;
            }

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
            InitializeRoleWorkSettingsIfNeeded();
            MechanoidMechanitorSelfWorkModeUtility.ApplyAcquiredSelfWorkMode(
                Pawn,
                CurrentSelfWorkMode);
        }

        private void InitializeRoleWorkSettingsIfNeeded()
        {
            if (roleWorkSettingsInitialized || Pawn.workSettings == null)
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

            roleWorkSettingsInitialized = true;
        }
    }
}
