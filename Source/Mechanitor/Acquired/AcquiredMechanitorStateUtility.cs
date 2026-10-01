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
            ApplyInitialPassionPolicyOnce(pawn, record);
            MechanoidMechanitorSelfWorkModeUtility.ApplyAcquiredSelfWorkMode(
                pawn,
                record.SelfWorkMode
                    ?? MechanoidMechanitorSelfWorkModeUtility.SanitizeWorkMode(null));
            pawn.mechanitor?.Notify_BandwidthChanged();
        }

        /// <summary>
        /// 后天机械族机械师的“技能兴趣度最低为好奇”兴趣策略只允许应用一次。
        /// 新建记录的 InitialPassionPolicyApplied 初始为 false：首次运行读取当前设置，
        /// 设置开启则执行 None → Minor，设置关闭则不修改兴趣，完成后无条件把标记设为
        /// true；之后再次执行 EnsureAcquiredMechanitorState（状态维护、读档恢复等）时
        /// 标记已为 true，一律不再应用该策略。
        /// 旧存档中缺少该字段的已有记录在 ExposeData 时按 true（已处理）读入，
        /// 因此不会在第一次读档后把已有机械族机械师的无兴趣追溯提升为好奇。
        /// </summary>
        private static void ApplyInitialPassionPolicyOnce(
            Pawn pawn,
            MechanoidMechanitorRecord record)
        {
            if (record.InitialPassionPolicyApplied)
            {
                return;
            }

            MechanoidMechanitorSkillUtility.ApplyConfiguredInitialPassionFloor(pawn);
            record.InitialPassionPolicyApplied = true;
        }
    }
}
