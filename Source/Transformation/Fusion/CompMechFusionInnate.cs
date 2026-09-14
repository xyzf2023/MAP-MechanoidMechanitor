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
    /// 先天合体标记。该组件只负责在 Pawn 创建、Spawn 与读档时向唯一的
    /// 合体资格注册表自动登记，不提供普通游戏中的后天授予入口，也不在
    /// Hediff 或 PawnKindDef 上扫描资格。已登记记录由注册表持久保存。
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
