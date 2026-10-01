using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>能力所需基础设施的生命周期入口。只补缺失项，不授予身份、不重置技能/作息。</summary>
    internal static class MechanoidMechanitorCapabilityLifecycleUtility
    {
        internal static void EnsureInfrastructure(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.health == null || pawn.Dead
                || pawn.RaceProps?.IsMechanoid != true || pawn.kindDef == null)
                return;
            if (Scribe.mode != LoadSaveMode.Inactive
                || MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore)
            {
                GameComponent_MechanoidMechanitorRegistry.QueuePostSpawnInitialization(pawn);
                return;
            }

            CompHumanWeaponUser.EnsureEquipmentInfrastructure(pawn);
            GameComponent_MechWeaponRegistry.Ensure(pawn);
            // 死亡离图会清空 interactions；后天机械师不一定具有先天社交组件。
            // 通过能力层复用补缺入口，保留已有关系和互动 Tracker。
            if (MechanoidMechanitorCapabilityUtility.HasCapability(pawn, MechanoidMechanitorCapability.ColonistLikeSocialTab))
                ColonistLikeSocialTrackerUtility.EnsureTrackers(pawn);
            if (CompWorkTabVisibleUser.PawnCanShowInWorkTab(pawn))
                CompWorkTabVisibleUser.EnsureWorkSettingsForWorkTab(pawn);
            if (MechanoidMechanitorCapabilityUtility.HasCapability(pawn, MechanoidMechanitorCapability.GeneralMechWork))
                MechWorkSettingsUtility.SynchronizeGeneralWorkSettings(pawn);
            if (MechanoidMechanitorCapabilityUtility.HasCapability(pawn, MechanoidMechanitorCapability.ColonistLikeTimetable))
                ColonistLikeMechTimetableUtility.EnsureTimetableState(pawn);
            if (MechanoidMechanitorCapabilityUtility.HasCapability(pawn, MechanoidMechanitorCapability.Recreation))
                MechanoidMechanitorRecreationUtility.EnsureReadingTracker(pawn);
            if (pawn.royalty == null
                && MechanoidMechanitorCapabilityUtility.HasCapability(pawn, MechanoidMechanitorCapability.Royalty))
                MechanoidMechanitorRoyaltyUtility.EnsureRoyaltyInfrastructure(pawn);
        }
    }
}
