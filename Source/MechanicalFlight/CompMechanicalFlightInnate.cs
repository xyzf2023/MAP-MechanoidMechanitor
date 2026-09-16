using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_MechanicalFlightInnate : CompProperties
    {
        public MechanicalFlightProfileDef? profile;
        // 与飞行配置的重量级倍率相乘；未配置时不改变耗能。
        public float energyDrainMultiplier = 1f;

        public CompProperties_MechanicalFlightInnate()
        {
            compClass = typeof(CompMechanicalFlightInnate);
        }
    }

    /// <summary>
    /// ThingDef 级先天飞行标记。Pawn 创建、生成及读档时自动登记；
    /// 具体飞行参数由 profile 指定，留空时使用默认机械飞行配置。
    /// </summary>
    public sealed class CompMechanicalFlightInnate : ThingComp
    {
        public CompProperties_MechanicalFlightInnate Props =>
            (CompProperties_MechanicalFlightInnate)props;

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
                GameComponent_MechanicalFlightRegistry.EnsureInnateAuthorization(
                    pawn,
                    Props.profile);
            }
        }
    }
}
