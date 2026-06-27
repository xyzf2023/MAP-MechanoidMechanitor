using MAP_MechanoidMechanitor.Scenarios;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_NativeMechanoidMechanitor : CompProperties
    {
        public CompProperties_NativeMechanoidMechanitor()
        {
            compClass = typeof(CompNativeMechanoidMechanitor);
        }
    }

    public sealed class CompNativeMechanoidMechanitor : ThingComp
    {
        public override void PostPostMake()
        {
            base.PostPostMake();
            RegisterNativeMechanitorIfApplicable(pendingLegacyNativeStateImport: false);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            RegisterNativeMechanitorIfApplicable(pendingLegacyNativeStateImport: false);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                bool pendingLegacyImport = parent is Pawn pawn
                    && !GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(
                        pawn,
                        out _);
                RegisterNativeMechanitorIfApplicable(pendingLegacyImport);
            }
        }

        private void RegisterNativeMechanitorIfApplicable(bool pendingLegacyNativeStateImport)
        {
            if (parent is not Pawn pawn)
            {
                return;
            }

            if (!GameComponent_MechanoidMechanitorRegistry.EnsureNativeMechanitorRecord(
                    pawn,
                    pendingLegacyNativeStateImport))
            {
                return;
            }

            WardenWorkUtility.GrantAndEnsureInfrastructure(pawn);
        }
    }
}
