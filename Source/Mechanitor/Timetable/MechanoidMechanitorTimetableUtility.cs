using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族机械师专属“作息解释层”（三层架构中的 B 层）。
    /// 把 Pawn_TimetableTracker 中保存的真实 TimeAssignmentDef 解释为
    /// 本 MOD 的作息意图（Intent），并供各 JobGiver 集中读取，
    /// 避免散落在多处重复判断 pawn.timetable.CurrentAssignment。
    ///
    /// 规则严格固定：
    /// - Anything / Work / 未知 MOD Assignment -> Neutral（本 MOD不额外干预）
    /// - Sleep -> Recharge
    /// - Joy -> Recreation
    /// - Meditate -> Meditation（仅 Royalty 存在时自然生效）
    /// </summary>
    public enum MechanoidMechanitorScheduleIntent
    {
        Neutral,
        Recharge,
        Recreation,
        Meditation
    }

    public static class MechanoidMechanitorTimetableUtility
    {
        /// <summary>
        /// 读取当前真实（Raw）的 TimeAssignmentDef，不附加任何解释。
        /// 第三方 MOD 自定义 Assignment（如教育 MOD 的动态 Assignment）也能通过本方法
        /// 被原样读回，从而维持对未来 Education overhaul 等兼容的基础。
        /// RawAssignment 永远真实，只有 GetCurrentIntent 返回 Neutral。
        /// </summary>
        public static TimeAssignmentDef? GetRawCurrentAssignment(Pawn? pawn)
        {
            if (pawn?.timetable == null)
            {
                return null;
            }

            return pawn.timetable.CurrentAssignment;
        }

        /// <summary>
        /// 把当前真实 Assignment 解释为机械族机械师作息意图。
        /// </summary>
        public static MechanoidMechanitorScheduleIntent GetCurrentIntent(Pawn? pawn)
        {
            TimeAssignmentDef? assignment = GetRawCurrentAssignment(pawn);
            if (assignment == null)
            {
                return MechanoidMechanitorScheduleIntent.Neutral;
            }

            if (assignment == TimeAssignmentDefOf.Sleep)
            {
                return MechanoidMechanitorScheduleIntent.Recharge;
            }

            if (assignment == TimeAssignmentDefOf.Joy)
            {
                return MechanoidMechanitorScheduleIntent.Recreation;
            }

            if (ModsConfig.RoyaltyActive
                && TimeAssignmentDefOf.Meditate != null
                && assignment == TimeAssignmentDefOf.Meditate)
            {
                return MechanoidMechanitorScheduleIntent.Meditation;
            }

            // Anything / Work / 任何第三方自定义 Assignment 一律视为 Neutral。
            // 本 MOD 仅不额外干预，绝不把原始 Assignment 改写成 Anything。
            return MechanoidMechanitorScheduleIntent.Neutral;
        }

        public static bool IsSleepTime(Pawn? pawn) =>
            GetCurrentIntent(pawn) == MechanoidMechanitorScheduleIntent.Recharge;

        public static bool IsJoyTime(Pawn? pawn) =>
            GetCurrentIntent(pawn) == MechanoidMechanitorScheduleIntent.Recreation;

        public static bool IsMeditationTime(Pawn? pawn) =>
            GetCurrentIntent(pawn) == MechanoidMechanitorScheduleIntent.Meditation;

        public static bool IsNeutralTime(Pawn? pawn) =>
            GetCurrentIntent(pawn) == MechanoidMechanitorScheduleIntent.Neutral;

        /// <summary>
        /// 该 Pawn 当前是否正在执行由本 MOD 作息充电 JobGiver 发起的原版 MechCharge。
        /// 用于 Sleep 结束后的续充探测：只要当前仍在执行本 MOD 的续充 Job，
        /// 就暂时抑制原版普通自动充电，让工作有机会在每轮 override 重评估时抢占。
        ///
        /// 该状态完全由 CurJob 派生，不保存、不写存档、不建立任何集合。
        /// Pawn 停止该 Job（工作抢占 / 达到上限 / 死亡 / 离图）即自动失效，
        /// 因此不会出现“永久残留 suppress 充电”的脏标记（见任务十七）。
        /// </summary>
        public static bool IsExecutingManagedScheduledRecharge(Pawn? pawn)
        {
            if (pawn?.CurJobDef != JobDefOf.MechCharge)
            {
                return false;
            }

            Job? job = pawn.CurJob;
            return job?.jobGiver is JobGiver_MechanoidMechanitorScheduledRecharge
                || job?.jobGiver is JobGiver_MechanoidMechanitorPostSleepRecharge;
        }
    }
}
