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

            CompWorkTabVisibleUser? comp = pawn.GetComp<CompWorkTabVisibleUser>();
            if (comp == null)
            {
                return false;
            }

            return comp.Props.showInWorkTab;
        }

        public static void EnsureWorkSettingsForWorkTab(Pawn pawn)
        {
            if (!PawnCanShowInWorkTab(pawn))
            {
                return;
            }

            CompWorkTabVisibleUser comp = pawn.GetComp<CompWorkTabVisibleUser>()!;
            if (!comp.Props.ensureWorkSettings)
            {
                return;
            }

            if (pawn.workSettings == null)
            {
                pawn.workSettings = new Pawn_WorkSettings(pawn);
                pawn.workSettings.EnableAndInitialize();
                MechWorkSettingsUtility.RestrictToMechEnabledWorkTypes(pawn);
            }
            else if (!pawn.workSettings.Initialized)
            {
                pawn.workSettings.EnableAndInitialize();
                MechWorkSettingsUtility.RestrictToMechEnabledWorkTypes(pawn);
            }
        }
    }
}
