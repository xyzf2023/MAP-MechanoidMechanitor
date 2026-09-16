using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    internal static class GravityDisorderUtility
    {
        private static readonly AccessTools.FieldRef<Pawn_JobTracker, bool> StartingJob =
            AccessTools.FieldRefAccess<Pawn_JobTracker, bool>("startingNewJob");
        private static readonly AccessTools.FieldRef<Pawn_JobTracker, bool> DeterminingJob =
            AccessTools.FieldRefAccess<Pawn_JobTracker, bool>("determiningNextJob");

        internal static string BlockedReason => "MAP_GravityDisorder_Blocked".Translate();

        internal static Hediff_GravityDisorder? GetEffect(Pawn? pawn)
        {
            if (pawn?.health?.hediffSet == null || pawn.Dead || pawn.Destroyed)
                return null;
            return pawn.health.hediffSet.GetFirstHediffOfDef(
                GravityDisorderDefOf.MAP_GravityDisorder) as Hediff_GravityDisorder;
        }

        internal static bool IsAffected(Pawn? pawn) => GetEffect(pawn) != null;

        internal static bool IsWaitJob(Job? job) =>
            job != null && job.def == GravityDisorderDefOf.MAP_GravityDisorderWait;

        internal static bool IsPassiveLanding(Pawn pawn, Job? job)
        {
            return job != null
                && job.def == MAPMechanitor_JobDefOf.MAP_MechanicalFlightEmergencyLanding
                && GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                && (record?.Phase == MechanicalFlightPhase.EmergencyApproach
                    || record?.Phase == MechanicalFlightPhase.EmergencyLanding);
        }

        internal static bool BlocksMovement(Pawn pawn) =>
            IsAffected(pawn) && !IsPassiveLanding(pawn, pawn.CurJob);

        internal static bool AllowsJob(Pawn pawn, Job? job) =>
            !IsAffected(pawn) || IsWaitJob(job) || IsPassiveLanding(pawn, job);

        internal static Job GetControlJob(Pawn pawn)
        {
            // 只延续系统已启动的被动迫降，不抢占；创建、预留及重规划仍归飞行状态机。
            if (IsPassiveLanding(pawn, pawn.CurJob))
                return pawn.CurJob;

            // 返回当前实例而非 NoJob：NoJob 会放行后面的普通主思考树。
            return IsWaitJob(pawn.CurJob)
                ? pawn.CurJob : JobMaker.MakeJob(GravityDisorderDefOf.MAP_GravityDisorderWait);
        }

        internal static void EnsureControl(Hediff_GravityDisorder? effect)
        {
            Pawn? pawn = effect?.pawn;
            if (effect == null || effect.UpdatingControl || pawn?.Spawned != true
                || pawn.Dead || pawn.Destroyed || pawn.jobs == null || pawn.thinker == null
                || Scribe.mode != LoadSaveMode.Inactive
                || StartingJob(pawn.jobs) || DeterminingJob(pawn.jobs))
                return;

            bool validJob = pawn.jobs.curDriver != null
                && (IsWaitJob(pawn.CurJob) || IsPassiveLanding(pawn, pawn.CurJob));
            if (!effect.NeedsEntryCleanup && validJob)
                return;

            effect.UpdatingControl = true;
            try
            {
                if (!validJob)
                {
                    // 不运行旧任务的自动收尾 Job，也不清除 Lord 职责、精神状态或征召。
                    // 可能从旧任务的回调进入此处，故不立即回收仍在调用栈上的 Job。
                    pawn.jobs.StopAll(canReturnToPool: false);
                    pawn.pather?.StopDead();
                }
                if (effect.NeedsEntryCleanup)
                {
                    pawn.jobs.ClearQueuedJobs();
                    pawn.stances?.CancelBusyStanceHard();
                    if (!IsPassiveLanding(pawn, pawn.CurJob))
                    {
                        pawn.pather?.StopDead();
                        MechanicalFlightStraightPathPatch.ClearMotion(pawn);
                    }
                    effect.NeedsEntryCleanup = false;
                }
                if (!validJob)
                    pawn.jobs.CheckForJobOverride();
            }
            finally
            {
                effect.UpdatingControl = false;
            }
        }

        internal static void ReleaseControl(Pawn? pawn)
        {
            if (pawn?.jobs == null || IsAffected(pawn) || !IsWaitJob(pawn.CurJob))
                return;
            // 清理只属于本状态的任务；真实倒地、休眠和迫降由原系统继续处理。
            if (StartingJob(pawn.jobs) || DeterminingJob(pawn.jobs))
                return; // Job 自身的结束条件会在下一次更新补齐。
            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced,
                startNewJob: pawn.Spawned && !pawn.Dead, canReturnToPool: false);
        }

        internal static void CancelBurst(Verb verb)
        {
            if (verb.state != VerbState.Bursting)
                return;
            // 炮塔把冷却回调绑定在 Verb 上；中断射击不能永久清除这项绑定。
            var callback = verb.castCompleteCallback;
            verb.Reset();
            verb.castCompleteCallback = callback;
        }
    }
}
