using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_WardenWorkUser : CompProperties
    {
        public int defaultPriority = 3;

        public CompProperties_WardenWorkUser()
        {
            compClass = typeof(CompWardenWorkUser);
        }
    }

    /// <summary>
    /// 挂载于 PawnDef 的静态监管授权标记；配合 WardenWorkUtility 补齐监管基础设施。
    /// </summary>
    public class CompWardenWorkUser : ThingComp
    {
        private bool defaultPriorityInitialized;

        public CompProperties_WardenWorkUser Props => (CompProperties_WardenWorkUser)props;

        public int DefaultPriority => Props.defaultPriority;

        internal bool DefaultPriorityInitialized => defaultPriorityInitialized;

        internal void MarkDefaultPriorityInitialized()
        {
            defaultPriorityInitialized = true;
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
            WardenWorkUtility.EnsureInfrastructure(parent as Pawn);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            WardenWorkUtility.EnsureInfrastructure(parent as Pawn);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(
                ref defaultPriorityInitialized,
                "defaultPriorityInitialized",
                false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                WardenWorkUtility.EnsureInfrastructure(parent as Pawn);
            }
        }
    }
}
