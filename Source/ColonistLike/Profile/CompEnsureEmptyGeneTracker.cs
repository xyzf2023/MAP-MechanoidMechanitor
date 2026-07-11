using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_EnsureEmptyGeneTracker : CompProperties
    {
        public CompProperties_EnsureEmptyGeneTracker()
        {
            compClass = typeof(CompEnsureEmptyGeneTracker);
        }
    }

    public sealed class CompEnsureEmptyGeneTracker : ThingComp
    {
        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureGeneTracker();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureGeneTracker();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureGeneTracker();
            }
        }

        private void EnsureGeneTracker()
        {
            if (parent is not Pawn pawn)
            {
                return;
            }

            if (pawn.genes == null)
            {
                pawn.genes = new Pawn_GeneTracker(pawn);
            }
        }
    }
}
