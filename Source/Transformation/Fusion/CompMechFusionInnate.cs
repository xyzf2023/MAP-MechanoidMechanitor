using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_MechFusionInnate : CompProperties
    {
        public CompProperties_MechFusionInnate()
        {
            compClass = typeof(CompMechFusionInnate);
        }
    }

    /// <summary>
    /// 先天合体标记。合体只能是机械族的先天能力：该组件只负责在 Pawn 创建、
    /// Spawn 与读档时向唯一的合体资格注册表登记，不提供任何后天授予入口，
    /// 也不在 Hediff 或 PawnKindDef 上扫描资格。
    /// </summary>
    public sealed class CompMechFusionInnate : ThingComp
    {
        public override void PostPostMake()
        {
            base.PostPostMake();
            RegisterIfApplicable();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            RegisterIfApplicable();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RegisterIfApplicable();
            }
        }

        private void RegisterIfApplicable()
        {
            if (parent is Pawn pawn)
            {
                MechFusionEligibilityUtility.EnsureEligibilityRecord(pawn);
            }
        }
    }
}
