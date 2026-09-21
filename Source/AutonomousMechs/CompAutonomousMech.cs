using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_AutonomousMech : CompProperties
    {
        public CompProperties_AutonomousMech() => compClass = typeof(CompAutonomousMech);
    }

    /// <summary>由 Def 提供自律来源，不授予机械师身份或科研增益资格。</summary>
    public sealed class CompAutonomousMech : ThingComp
    {
        public override void PostPostMake()
        {
            base.PostPostMake();
            Synchronize();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Synchronize();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            Synchronize();
        }

        private void Synchronize()
        {
            if (parent is Pawn pawn)
                GameComponent_AutonomousMechRegistry.NotifyPawnLifecycle(pawn);
        }
    }
}
