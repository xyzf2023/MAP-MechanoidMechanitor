using Verse;

namespace MAP_MechanoidMechanitor
{
    public enum AutonomousMechSetting
    {
        Always,
        Sun,
        Hermit
    }

    public sealed class CompProperties_AutonomousMech : CompProperties
    {
        // 默认保留第三方和其他 Def 原有的无条件先天授权。
        public AutonomousMechSetting setting = AutonomousMechSetting.Always;

        public CompProperties_AutonomousMech() => compClass = typeof(CompAutonomousMech);
    }

    /// <summary>由 Def 提供自律来源，不授予机械师身份或科研增益资格。</summary>
    public sealed class CompAutonomousMech : ThingComp
    {
        private CompProperties_AutonomousMech Props => (CompProperties_AutonomousMech)props;

        internal bool IsSettingsControlled => Props.setting != AutonomousMechSetting.Always;

        internal bool ProvidesAuthorization => Props.setting switch
        {
            AutonomousMechSetting.Always => true,
            AutonomousMechSetting.Sun => MAPMechanitorMod.Settings?.enableSunAutonomy ?? true,
            AutonomousMechSetting.Hermit => MAPMechanitorMod.Settings?.enableHermitAutonomy ?? true,
            _ => false
        };

        public override void PostPostMake()
        {
            base.PostPostMake();
            Synchronize();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Synchronize();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            Synchronize();
        }

        private void Synchronize()
        {
            if (parent is Pawn pawn)
                GameComponent_AutonomousMechRegistry.NotifyPawnLifecycle(pawn);
        }
    }
}
