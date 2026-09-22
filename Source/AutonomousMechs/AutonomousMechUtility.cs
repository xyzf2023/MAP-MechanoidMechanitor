using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>自身控制资格的只读入口；不赋予机械师、节点或类殖民者身份。</summary>
    public static class AutonomousMechUtility
    {
        public static bool IsAutonomousMech(Pawn? pawn) =>
            GameComponent_AutonomousMechRegistry.IsAuthorized(pawn);

        public static bool IsPlayerAutonomousMech(Pawn? pawn) =>
            IsAutonomousMech(pawn) && pawn!.IsColonyMech && !pawn.Dead;

        public static bool UsesPersonalRechargeSettings(Pawn? pawn) =>
            IsPlayerAutonomousMech(pawn) && pawn!.needs?.energy != null;

        /// <summary>控制消费端共用：自律优先，其余 MAP 节点继续修正监管关系方向。</summary>
        internal static bool TryGetSubjectProfile(Pawn? pawn, out bool requiresExternalOverseer)
        {
            requiresExternalOverseer = false;
            if (IsAutonomousMech(pawn))
            {
                return true;
            }
            return MAPMechanitorNodeUtility.TryGetVanillaControlNodeProfile(
                pawn, out requiresExternalOverseer);
        }

        internal static bool CanReceiveAuthorization(Pawn? pawn) =>
            ModsConfig.BiotechActive
            && pawn != null && !pawn.Destroyed && !pawn.Discarded
            && pawn.health != null && !pawn.health.Dead
            && pawn.needs != null && pawn.mindState != null && pawn.jobs != null
            && pawn.RaceProps?.IsMechanoid == true
            && pawn.OverseerSubject != null && pawn.kindDef != null;

        /// <summary>安全生命周期调用；读档修复不打断当前工作，不创建 mechanitor Tracker。</summary>
        internal static void RefreshRuntime(Pawn pawn, bool reevaluateJobs)
        {
            if (!CanReceiveAuthorization(pawn))
            {
                return;
            }

            bool authorized = IsAutonomousMech(pawn);
            if (authorized)
            {
                MAPOverseerlessNodeUtility.ClearExternalOverseerIfNode(pawn);
            }

            // 在重建组件和重新评估工作之前撤掉旧来源，避免以残留加成选择工作。
            if (!GameComponent_AutonomousMechRegistry.RefreshPendingOverseerEffects(pawn, reevaluateJobs))
                return;

            if (pawn.Faction != Faction.OfPlayer)
            {
                return;
            }

            PawnComponentsUtility.AddAndRemoveDynamicComponents(pawn);
            if (authorized && MechWorkSettingsUtility.TryEnsureWorkSettingsInitialized(pawn))
            {
                MechWorkSettingsUtility.RestrictToMechEnabledWorkTypes(pawn);
            }

            if (!authorized && pawn.drafter?.Drafted == true
                && !MechanitorUtility.CanDraftMech(pawn).Accepted)
            {
                pawn.drafter.Drafted = false;
            }

            if (reevaluateJobs && pawn.Spawned && !pawn.InMentalState && !pawn.Drafted)
            {
                if (authorized)
                    pawn.TryGetComp<CompCanBeDormant>()?.WakeUp();
                pawn.jobs?.CheckForJobOverride();
            }
        }
    }
}
