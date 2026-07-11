using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class AcquiredMechanitorStateUtility
    {
        public static void EnsureAcquiredMechanitorState(
            Pawn pawn,
            MechanoidMechanitorRecord record)
        {
            if (pawn == null || pawn.Destroyed || record == null)
            {
                return;
            }

            MechanoidMechanitorWorkAuthorizationUtility.GrantAndEnsureInfrastructure(pawn);

            if (pawn.story == null)
            {
                pawn.story = new Pawn_StoryTracker(pawn);
            }

            if (pawn.story.bodyType == null)
            {
                pawn.story.bodyType = BodyTypeDefOf.Male;
            }

            pawn.Notify_DisabledWorkTypesChanged();
            MechanoidMechanitorRoleUtility.EnsureRoleState(pawn);
            InitializeRoleWorkSettingsIfNeeded(pawn, record);
            MechanoidMechanitorSelfWorkModeUtility.ApplyAcquiredSelfWorkMode(
                pawn,
                record.SelfWorkMode
                    ?? MechanoidMechanitorSelfWorkModeUtility.SanitizeWorkMode(null));
            pawn.mechanitor?.Notify_BandwidthChanged();
        }

        private static void InitializeRoleWorkSettingsIfNeeded(
            Pawn pawn,
            MechanoidMechanitorRecord record)
        {
            if (record.RoleWorkSettingsInitialized || pawn.workSettings == null)
            {
                return;
            }

            foreach (WorkTypeDef workType in MechanoidMechanitorRoleUtility.GetRoleWorkTypes())
            {
                if (!pawn.WorkTypeIsDisabled(workType)
                    && pawn.workSettings.GetPriority(workType) == 0)
                {
                    pawn.workSettings.SetPriority(workType, 3);
                }
            }

            record.RoleWorkSettingsInitialized = true;
        }
    }
}
