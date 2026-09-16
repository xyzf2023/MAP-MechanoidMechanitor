using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    internal static class MechServiceStandbyUtility
    {
        internal static bool IsWaiting(Pawn pawn) =>
            pawn?.CurJobDef == MechServiceStationDefOf.MAP_Job_MechServiceStandby;

        // 只保持能正常执行任务的玩家机械族；异常状态仍交给原版处理。
        internal static bool CanWait(Pawn pawn) => pawn != null && pawn.Spawned
            && !pawn.Destroyed && !pawn.Dead && !pawn.Downed && !pawn.Drafted
            && !pawn.InMentalState && pawn.Faction == Faction.OfPlayer && pawn.RaceProps.IsMechanoid;
    }

    public sealed class JobDriver_MechServiceStandby : JobDriver
    {
        private bool stationBindingAttempted;
        private CompMechServiceStation? Station => job.targetA.Thing?.TryGetComp<CompMechServiceStation>();
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        private void MaintainStation()
        {
            job.overrideFacing = Rot4.South;
            pawn.Rotation = Rot4.South;
            if (!stationBindingAttempted)
            {
                stationBindingAttempted = true;
                // 兼容旧存档：旧等待任务没有建筑目标，只认当前服务格上的台。
                if (!job.targetA.IsValid && pawn.Spawned)
                    foreach (Thing thing in pawn.Position.GetThingList(pawn.Map))
                        if (thing.TryGetComp<CompMechServiceStation>() is CompMechServiceStation candidate
                            && candidate.ServiceCell == pawn.Position)
                        { job.targetA = thing; break; }
            }
            CompMechServiceStation? station = Station;
            if (station == null || !station.parent.Spawned || station.parent.Map != pawn.Map
                || pawn.Position != station.ServiceCell)
            {
                EndJobWith(JobCondition.InterruptForced);
                return;
            }
            station.RegisterStandby(pawn);
            station.ReleaseStandbyIfClaimed();
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFailCondition(() => !MechServiceStandbyUtility.CanWait(pawn));
            AddFinishAction(_ => Station?.UnregisterStandby(pawn));
            Toil wait = ToilMaker.MakeToil("MechServiceStandby");
            wait.handlingFacing = true;
            wait.initAction = () => { pawn.pather?.StopDead(); MaintainStation(); };
            wait.tickIntervalAction = _ => MaintainStation();
            wait.defaultCompleteMode = ToilCompleteMode.Never;
            yield return wait;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref stationBindingAttempted, "stationBindingAttempted");
        }
    }

    // 与原版 KeepLyingDown 一样返回当前实例，不重启任务，也不刷新持续时间。
    // 玩家右键通过 TryTakeOrderedJob 直接结束等待；isIdle 使 Shift 指令也能立即接管。
    public sealed class JobGiver_ContinueMechServiceStandby : ThinkNode_JobGiver
    {
        protected override Job? TryGiveJob(Pawn pawn) =>
            MechServiceStandbyUtility.IsWaiting(pawn) && MechServiceStandbyUtility.CanWait(pawn)
                ? pawn.CurJob : null;
    }

    // Need_MechEnergy.NeedInterval 会绕过思考树直接启动 SelfShutdown。
    // 仅拒绝待命及已送达整备期间的自动休眠任务；保留能量、低电 Hediff 和生命周期处理。
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    internal static class MechServiceStandbyShutdownPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static bool Prefix(Pawn ___pawn, Job newJob)
        {
            if (newJob == null || newJob.def != JobDefOf.SelfShutdown || newJob.playerForced) return true;
            bool waiting = MechServiceStandbyUtility.IsWaiting(___pawn) && MechServiceStandbyUtility.CanWait(___pawn);
            bool receiving = ___pawn.CurJobDef == MechServiceStationDefOf.MAP_Job_ReceiveMechService
                && ___pawn.jobs.curDriver is JobDriver_UseMechServiceStation driver && driver.IsServicing;
            if (!waiting && !receiving) return true;
            // 自动休眠在此入口尚未预约或安装驱动，归还未使用的 Job，避免每次低电检查泄漏。
            JobMaker.ReturnToPool(newJob);
            return false;
        }
    }
}
