using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_HumanApparelUser : CompProperties
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

    public sealed class CompHumanApparelUser : ThingComp
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

            // 原版穿衣及服装架换装会记录强制服装；机械体不会自动创建此 Tracker。
            // 沿用 Pawn 的原版存档字段，并保留已有策略和强制服装记录。
            pawn.outfits ??= new Pawn_OutfitTracker(pawn);
            pawn.outfits.forcedHandler ??= new OutfitForcedHandler();

            if (Scribe.mode == LoadSaveMode.PostLoadInit
                && pawn.apparel != null
                && pawn.apparel.WornApparelCount > 0)
            {
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            }
        }

        public override List<PawnRenderNode> CompRenderNodes()
        {
            if (parent is not Pawn pawn || !EnableHumanApparelRendering)
            {
                return null!;
            }

            if (pawn.apparel == null || pawn.apparel.WornApparelCount == 0)
            {
                return null!;
            }

            PawnRenderTree? tree = pawn.Drawer?.renderer?.renderTree;
            if (tree == null)
            {
                return null!;
            }

            return HumanApparelRenderNodeFactory.CreateNodes(pawn, tree) ?? null!;
        }
    }
}
