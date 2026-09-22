using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_WorkTabVisibleUser : CompProperties
    {
        public bool showInWorkTab = true;
        public bool ensureWorkSettings = true;

        public CompProperties_WorkTabVisibleUser()
        {
            compClass = typeof(CompWorkTabVisibleUser);
        }
    }

    public class CompWorkTabVisibleUser : ThingComp
    {
        public CompProperties_WorkTabVisibleUser Props =>
            (CompProperties_WorkTabVisibleUser)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (parent is Pawn pawn)
                MechanoidMechanitorCapabilityLifecycleUtility.EnsureInfrastructure(pawn);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit && parent is Pawn pawn)
                GameComponent_MechanoidMechanitorRegistry.QueuePostSpawnInitialization(pawn);
        }

        public static bool PawnCanShowInWorkTab(Pawn? pawn)
        {
            if (pawn == null || pawn.Dead)
            {
                return false;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return false;
            }

            if (pawn.DevelopmentalStage.Baby())
            {
                return false;
            }

            return MechanoidMechanitorRoleUtility.AllowsWorkTab(pawn);
        }

        /// <summary>
        /// 仅为已确认可显示于工作面板的 Pawn 补齐 guest/workSettings。
        /// 仅生命周期调用；工作面板读取不得调用此方法。
        /// </summary>
        public static void EnsureWorkSettingsForWorkTab(Pawn pawn)
        {
            if (pawn.kindDef == null || !PawnCanShowInWorkTab(pawn))
                return;
            if (pawn.guest == null)
            {
                pawn.guest = new Pawn_GuestTracker(pawn);
            }

            // 已初始化：刷新面板时不查询后天机械师身份或 WorkTab 组件。
            if (pawn.workSettings?.Initialized == true)
            {
                return;
            }

            CompWorkTabVisibleUser? comp = pawn.GetComp<CompWorkTabVisibleUser>();
            bool shouldEnsureWorkSettings =
                MechanoidMechanitorRoleUtility.IsAcquiredMechanoidMechanitor(pawn)
                || comp?.Props.ensureWorkSettings == true;
            if (!shouldEnsureWorkSettings)
            {
                return;
            }

            bool createdOrInitializedWorkSettings = false;

            if (pawn.workSettings == null)
            {
                pawn.workSettings = new Pawn_WorkSettings(pawn);
                createdOrInitializedWorkSettings = true;
            }

            if (!pawn.workSettings.Initialized)
            {
                pawn.workSettings.EnableAndInitialize();
                createdOrInitializedWorkSettings = true;
            }

            // 仅在新建或首次初始化工作设置时限制可用类型，避免刷新面板时重置玩家优先级。
            if (createdOrInitializedWorkSettings)
            {
                MechWorkSettingsUtility.RestrictToMechEnabledWorkTypes(pawn);
            }
        }
    }
}
