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
        /// 调用方须先通过 <see cref="PawnCanShowInWorkTab"/>；本方法不再重复资格查询。
        /// </summary>
        public static void EnsureWorkSettingsForWorkTab(Pawn pawn)
        {
            if (pawn.guest == null)
            {
                pawn.guest = new Pawn_GuestTracker(pawn);
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
