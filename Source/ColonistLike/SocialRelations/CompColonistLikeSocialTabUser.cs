using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 殖民者式社交面板能力来源；只补齐社交面板需要的关系与互动 Tracker。
    /// </summary>
    public sealed class CompProperties_ColonistLikeSocialTabUser : CompProperties
    {
        public CompProperties_ColonistLikeSocialTabUser()
        {
            compClass = typeof(CompColonistLikeSocialTabUser);
        }
    }

    public sealed class CompColonistLikeSocialTabUser : ThingComp
    {
        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureSocialTrackers();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureSocialTrackers();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureSocialTrackers();
            }
        }

        private void EnsureSocialTrackers()
        {
            if (parent is not Pawn pawn)
            {
                return;
            }

            pawn.relations ??= new Pawn_RelationsTracker(pawn);
            pawn.interactions ??= new Pawn_InteractionsTracker(pawn);
        }
    }
}
