using MAP_MechanoidMechanitor.Scenarios;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_AnimalHandlingWorkUser : CompProperties
    {
        public int defaultPriority = 3;

        public CompProperties_AnimalHandlingWorkUser()
        {
            compClass = typeof(CompAnimalHandlingWorkUser);
        }
    }

    /// <summary>
    /// 挂载于 PawnDef 的静态驯兽授权标记；配合 AnimalHandlingWorkUtility 补齐驯兽基础设施。
    /// </summary>
    public class CompAnimalHandlingWorkUser : ThingComp
    {
        private bool defaultPriorityInitialized;

        public CompProperties_AnimalHandlingWorkUser Props =>
            (CompProperties_AnimalHandlingWorkUser)props;

        public int DefaultPriority => Props.defaultPriority;

        internal bool DefaultPriorityInitialized => defaultPriorityInitialized;

        internal void MarkDefaultPriorityInitialized()
        {
            defaultPriorityInitialized = true;
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
            AnimalHandlingWorkUtility.EnsureInfrastructure(parent as Pawn);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            AnimalHandlingWorkUtility.EnsureInfrastructure(parent as Pawn);
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
                AnimalHandlingWorkUtility.EnsureInfrastructure(parent as Pawn);
            }
        }
    }
}
