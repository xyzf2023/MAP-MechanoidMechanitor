using MAP_MechanoidMechanitor.Scenarios;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_MechanicalChildcareUser : CompProperties
    {
        public int defaultPriority = 3;

        public CompProperties_MechanicalChildcareUser()
        {
            compClass = typeof(CompMechanicalChildcareUser);
        }
    }

    /// <summary>
    /// 挂载于 PawnDef 的静态机械保育授权标记；配合 MechanicalChildcareUtility 补齐保育基础设施。
    /// </summary>
    public class CompMechanicalChildcareUser : ThingComp
    {
        private bool defaultPriorityInitialized;

        public CompProperties_MechanicalChildcareUser Props =>
            (CompProperties_MechanicalChildcareUser)props;

        public int DefaultPriority => Props.defaultPriority;

        internal bool DefaultPriorityInitialized => defaultPriorityInitialized;

        internal void MarkDefaultPriorityInitialized()
        {
            defaultPriorityInitialized = true;
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
            MechanicalChildcareUtility.EnsureInfrastructure(parent as Pawn);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            MechanicalChildcareUtility.EnsureInfrastructure(parent as Pawn);
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
                MechanicalChildcareUtility.EnsureInfrastructure(parent as Pawn);
            }
        }
    }
}
