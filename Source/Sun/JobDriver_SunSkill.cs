using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>技能仍是当前任务；只为普通移动保存目的地，不另行运行第二个技能驱动。</summary>
    public abstract class JobDriver_SunSkill : JobDriver
    {
        private bool allowMovementAtStart;
        private Job? movementJob;
        private Map? movementMap;
        private bool restoreMovementReservation;

        internal bool AllowsMovement => allowMovementAtStart && SunSkillMovementUtility.IsPlayerSun(pawn);

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            // 只在新任务开始时采样；读档不会调用此入口。
            allowMovementAtStart = SunSkillMovementUtility.AllowsNewCast(pawn);
            if (!AllowsMovement) return;
            movementJob = SunSkillMovementHandoff.TakeFor(pawn, job);
            if (movementJob == null) return;
            movementMap = pawn.Map;
            job.locomotionUrgency = movementJob.locomotionUrgency;
            RefreshMovement();
            if (movementJob != null) ReserveMovementDestination();
        }

        protected Toil MakeSkillStopToil()
        {
            // 仍占原有停止步骤的编号；读档重建时只登记清理，不重置进度。
            AddFinishAction(_ => DiscardMovement(stopPath: true));
            Toil stop = Toils_General.StopDead();
            stop.initAction = StopForSkill;
            return stop;
        }

        protected void StopForSkill()
        {
            RefreshMovement();
            if (!AllowsMovement || movementJob == null) pawn.pather?.StopDead();
        }

        protected bool CanMaintainSkillPosition(Map? castMap, IntVec3 origin, bool allowOwnedCooldown = true)
        {
            RefreshMovement();
            return pawn.Map == castMap
                && (AllowsMovement || (pawn.Position == origin && pawn.pather?.Moving != true))
                && (pawn.stances?.FullBodyBusy != true
                    || (allowOwnedCooldown && SunSkillCooldown.Owns(pawn, job)));
        }

        internal Job? CaptureMovement()
        {
            if (!AllowsMovement || movementJob == null || pawn.Map != movementMap
                || pawn.pather?.Moving != true) return null;
            return SunSkillMovementUtility.Snapshot(movementJob);
        }

        internal bool TryTakeMovement(Job requested)
        {
            if (!AllowsMovement || !SunSkillMovementUtility.CanMove(pawn, requested)
                || pawn.stances?.stunner?.Stunned == true) return false;

            // 清除原版排队命令，但不软取消射击姿态，也不改变技能的打断标志。
            requested.playerForced = true;
            pawn.jobs.ClearQueuedJobs();
            DiscardMovement(stopPath: false);
            movementJob = SunSkillMovementUtility.Snapshot(requested);
            movementJob.playerForced = true;
            movementMap = pawn.Map;
            job.locomotionUrgency = movementJob.locomotionUrgency;
            ReserveMovementDestination();
            if (pawn.jobs.curDriver != this) return false;
            pawn.pather.StartPath(requested.targetA, PathEndMode.OnCell);
            // 即时抵达或寻路失败可能已清除移动状态，仍视为接受本次移动命令。
            return pawn.jobs.curDriver == this;
        }

        public override void Notify_PatherArrived()
        {
            if (movementJob == null)
            {
                base.Notify_PatherArrived();
                return;
            }
            // 路径完成与技能步骤完成无关，不能推进技能的 Toil。
            DiscardMovement(stopPath: false);
        }

        public override void Notify_PatherFailed()
        {
            if (movementJob == null)
            {
                base.Notify_PatherFailed();
                return;
            }
            DiscardMovement(stopPath: false);
        }

        private void RefreshMovement()
        {
            if (movementJob == null) return;
            if (!AllowsMovement || !pawn.Spawned || movementMap == null || pawn.Map != movementMap)
            {
                DiscardMovement(stopPath: true);
                return;
            }
            // 原版可能因路径尚未就绪而不通知驱动；停止后也不能留下待恢复的旧目的地。
            if (pawn.pather?.Moving != true)
            {
                DiscardMovement(stopPath: false);
                return;
            }
            if (restoreMovementReservation && Scribe.mode == LoadSaveMode.Inactive)
            {
                restoreMovementReservation = false;
                ReserveMovementDestination();
            }
        }

        private void ReserveMovementDestination()
        {
            if (movementJob == null || movementMap == null || pawn.Map != movementMap) return;
            // 移动期间归当前技能任务所有；快照仅在技能结束后正式成为 Goto。
            movementMap.pawnDestinationReservationManager.ReleaseClaimedBy(pawn, job);
            movementMap.pawnDestinationReservationManager.Reserve(pawn, job, movementJob.targetA.Cell);
        }

        internal void DiscardMovement(bool stopPath)
        {
            if (movementJob == null) return;
            Job discarded = movementJob;
            movementJob = null;
            if (stopPath) pawn.pather?.StopDead();
            movementMap?.pawnDestinationReservationManager.ReleaseClaimedBy(pawn, job);
            movementMap = null;
            restoreMovementReservation = false;
            JobMaker.ReturnToPool(discarded);
        }

        internal void PrepareMovementAfterSkill(JobCondition condition, bool startNewJob)
        {
            if (movementJob == null) return;
            // 新技能接替时，其下令上下文已持有独立快照，旧驱动只放弃所有权。
            if (job.playerInterruptedForced && SunSkillMovementHandoff.HasMovementFor(pawn))
            {
                DiscardMovement(stopPath: false);
                return;
            }
            if (!startNewJob || job.playerInterruptedForced || !AllowsMovement
                || (condition != JobCondition.Succeeded && condition != JobCondition.InterruptForced
                    && condition != JobCondition.Incompletable)
                || pawn.Map != movementMap || pawn.pather?.Moving != true
                || (job.playerForced && !pawn.Drafted)
                || !SunSkillMovementUtility.CanMove(pawn, movementJob)) return;

            // EndCurrentJob 对征召 Pawn 不执行 finalizer Job，故通过原版队列恢复移动。
            Job resume = movementJob;
            movementJob = null;
            movementMap = null;
            restoreMovementReservation = false;
            if (resume.TryMakePreToilReservations(pawn, errorOnFailed: false))
                pawn.jobs.jobQueue.EnqueueFirst(resume, JobTag.Misc);
            else
            {
                pawn.ClearReservationsForJob(resume);
                pawn.pather?.StopDead();
                JobMaker.ReturnToPool(resume);
            }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref allowMovementAtStart, "sunSkillAllowMovementAtStart", false);
            Scribe_Deep.Look(ref movementJob, "sunSkillMovementJob");
            Scribe_References.Look(ref movementMap, "sunSkillMovementMap");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                restoreMovementReservation = movementJob != null;
            // base 会重建 Toil，必须先恢复本次许可与移动快照。
            base.ExposeData();
        }
    }
}
