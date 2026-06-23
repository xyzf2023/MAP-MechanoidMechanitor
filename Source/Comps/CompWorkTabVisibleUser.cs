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

        public static void EnsureWorkSettingsForWorkTab(Pawn pawn)
        {
            if (!PawnCanShowInWorkTab(pawn))
            {
                return;
            }

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

            if (pawn.workSettings == null)
            {
                pawn.workSettings = new Pawn_WorkSettings(pawn);
            }

            if (!pawn.workSettings.Initialized)
            {
                pawn.workSettings.EnableAndInitialize();
            }

            MechWorkSettingsUtility.RestrictToMechEnabledWorkTypes(pawn);
        }
    }
}
