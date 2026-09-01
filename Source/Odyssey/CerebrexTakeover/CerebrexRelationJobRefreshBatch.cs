using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 主脑接管关系同步专用“工作刷新批次”。
    /// 一批接管关系通知执行期间，Lord.Notify_FactionRelationsChanged 末尾对单位工作跟踪器的
    /// 刷新请求被收集并去重，仅在最外层批次退出时统一执行一次，避免同一批单位被反复打断重启工作。
    /// 该机制仅作用于本 MOD 接管关系写入触发的指定调用点，不改变原版其他路径的行为。
    /// </summary>
    internal sealed class CerebrexRelationJobRefreshBatch : IDisposable
    {
        private static readonly AccessTools.FieldRef<Pawn_JobTracker, Pawn> TrackerPawn =
            AccessTools.FieldRefAccess<Pawn_JobTracker, Pawn>("pawn");

        private readonly GameComponent_CerebrexTakeoverState state;
        private bool disposed;

        private CerebrexRelationJobRefreshBatch(GameComponent_CerebrexTakeoverState state)
        {
            this.state = state;
        }

        /// <summary>
        /// 进入一个批次。若当前没有有效的主脑接管状态组件，返回 null，调用方以 using(null) 安全跳过。
        /// </summary>
        public static CerebrexRelationJobRefreshBatch? Enter()
        {
            GameComponent_CerebrexTakeoverState? state =
                GameComponent_CerebrexTakeoverState.Current;
            if (state == null)
            {
                return null;
            }

            state.EnterRelationJobRefreshBatch();
            return new CerebrexRelationJobRefreshBatch(state);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            state.ExitRelationJobRefreshBatch();
        }

        /// <summary>
        /// 原版 Lord.Notify_FactionRelationsChanged 末尾 EndCurrentJob 调用的静态包装。
        /// 当处于本 MOD 主脑关系写入批次内且参数符合原版调用时，仅收集请求而不立即刷新；
        /// 其余情况完整透传原调用，不改变语义。
        /// </summary>
        internal static void EndCurrentJobOrQueue(
            Pawn_JobTracker tracker,
            JobCondition condition,
            bool startNewJob,
            bool canReturnToPool)
        {
            if (tracker == null)
            {
                return;
            }

            GameComponent_CerebrexTakeoverState? state =
                GameComponent_CerebrexTakeoverState.Current;
            if (state != null
                && state.IsRelationJobRefreshBatchActive
                && CerebrexTakeoverRelationUtility.IsApplying
                && condition == JobCondition.InterruptForced
                && startNewJob
                && canReturnToPool)
            {
                state.QueueJobRefreshFor(tracker);
                return;
            }

            tracker.EndCurrentJob(condition, startNewJob, canReturnToPool);
        }

        /// <summary>
        /// 统一刷新前校验：Pawn 非空、存活、未销毁、已生成并属于当前有效地图，
        /// 且记录的 tracker 仍是其工作跟踪器。不合规记录直接丢弃。
        /// </summary>
        internal static bool IsTrackerValidForRefresh(Pawn_JobTracker tracker)
        {
            if (tracker == null)
            {
                return false;
            }

            Pawn? pawn = TrackerPawn(tracker);
            if (pawn == null || pawn.Destroyed || pawn.Dead || pawn.Discarded)
            {
                return false;
            }

            if (!pawn.Spawned || pawn.Map == null)
            {
                return false;
            }

            if (pawn.jobs != tracker)
            {
                return false;
            }

            return true;
        }
    }
}
