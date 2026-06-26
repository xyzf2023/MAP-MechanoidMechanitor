using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_HumanApparelUser : CompProperties
    {
        public bool allowWearFloatMenu = true;
        public bool allowRemoveApparel = true;
        public bool ensureApparelTracker = true;
        public bool enableHumanApparelRendering = true;
        public BodyTypeDef? apparelBodyType;

        public CompProperties_HumanApparelUser()
        {
            compClass = typeof(CompHumanApparelUser);
        }
    }

    public class CompHumanApparelUser : ThingComp
    {
        public CompProperties_HumanApparelUser Props => (CompProperties_HumanApparelUser)props;

        public bool AllowWearFloatMenu => Props.allowWearFloatMenu;

        public bool AllowRemoveApparel => Props.allowRemoveApparel;

        public bool EnableHumanApparelRendering => Props.enableHumanApparelRendering;

        public BodyTypeDef? ApparelBodyType => Props.apparelBodyType;

        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureApparelTracker();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureApparelTracker();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureApparelTracker();
            }
        }

        private void EnsureApparelTracker()
        {
            if (parent is not Pawn pawn || !Props.ensureApparelTracker)
            {
                return;
            }

            if (pawn.apparel == null)
            {
                pawn.apparel = new Pawn_ApparelTracker(pawn);
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit
                && pawn.apparel != null
                && pawn.apparel.WornApparelCount > 0)
            {
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            }
        }
    }
}
