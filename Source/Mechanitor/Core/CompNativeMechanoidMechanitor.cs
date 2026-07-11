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
            RegisterNativeMechanitorIfApplicable();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            RegisterNativeMechanitorIfApplicable();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RegisterNativeMechanitorIfApplicable();
            }
        }

        private void RegisterNativeMechanitorIfApplicable()
        {
            if (parent is not Pawn pawn)
            {
                return;
            }

            if (!GameComponent_MechanoidMechanitorRegistry.EnsureNativeMechanitorRecord(pawn))
            {
                return;
            }

            MechanoidMechanitorWorkAuthorizationUtility.GrantAndEnsureInfrastructure(pawn);
        }
    }
}
