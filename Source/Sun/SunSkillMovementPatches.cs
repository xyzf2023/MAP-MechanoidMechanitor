using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    internal static class SunSkillMovementUtility
    {
        internal static bool IsPlayerSun(Pawn pawn) => pawn.Faction == Faction.OfPlayer
            && SunEnergyAuraUtility.IsPlayerSun(pawn);

        internal static bool AllowsNewCast(Pawn pawn) =>
            MAPMechanitorMod.Settings?.allowSunSkillsWhileMoving == true && IsPlayerSun(pawn);

        internal static bool IsSkill(Job job) => job.def == EnergyPulseDefOf.MAP_EnergyPulse
            || job.def == HighEnergyLaserBeamDefOf.MAP_HighEnergyLaserBeam
            || job.def == AnnihilationCannonDefOf.MAP_AnnihilationCannon;

        internal static bool IsOrdinaryMove(Job job) => job.def == JobDefOf.Goto
            && job.targetA.IsValid && !job.targetA.HasThing
            && !job.exitMapOnArrival && !job.failIfCantJoinOrCreateCaravan
            && job.ritualTag.NullOrEmpty() && job.dutyTag.NullOrEmpty() && job.controlGroupTag == null
            && (job.targetQueueA == null || job.targetQueueA.Count == 0)
            && (job.targetQueueB == null || job.targetQueueB.Count == 0);

        internal static bool CanMove(Pawn pawn, Job job) => IsOrdinaryMove(job)
            && pawn.Spawned && pawn.Map != null && pawn.pather != null
            && !pawn.Dead && !pawn.Downed && !pawn.InMentalState
            && pawn.stances?.stunner?.Stunned != true && job.targetA.Cell.InBounds(pawn.Map)
            && !GravityDisorderUtility.IsAffected(pawn)
            && !MechanicalFlightEmergencyUtility.IsEmergencySequence(pawn)
            && GroupFlightPatches.AllowsJob(pawn, job)
            && !GroupFlightUtility.BlocksIndependentMovement(pawn)
            && GroupFlightUtility.CanMoveFormationTo(pawn, job.targetA.Cell);

        internal static Job Snapshot(Job movement)
        {
            Job snapshot = movement.Clone();
            // 原版 Clone 沿用原 loadID；并行快照必须拥有自己的存档与预约身份。
            snapshot.loadID = Find.UniqueIDsManager.GetNextJobID();
            snapshot.playerInterruptedForced = false;
            return snapshot;
        }
    }

    /// <summary>只跨一次下令/启动调用传递移动快照；嵌套与异常退出均恢复之前的上下文。</summary>
    internal sealed class SunSkillMovementHandoff : IDisposable
    {
        [ThreadStatic] private static SunSkillMovementHandoff? current;
        private readonly SunSkillMovementHandoff? previous;
        private readonly Pawn pawn;
        private readonly Job skillJob;
        private Job? movement;
        private bool disposed;

        internal bool HasMovement => movement != null;

        internal SunSkillMovementHandoff(Pawn pawn, Job skillJob)
        {
            previous = current;
            this.pawn = pawn;
            this.skillJob = skillJob;
            if (pawn.jobs.curDriver is JobDriver_SunSkill driver)
                movement = driver.CaptureMovement();
            else if (pawn.CurJob is Job moving && SunSkillMovementUtility.IsOrdinaryMove(moving)
                && pawn.pather?.Moving == true)
                movement = SunSkillMovementUtility.Snapshot(moving);
            movement ??= TakeFor(pawn, skillJob);
            current = this;
        }

        internal static Job? TakeFor(Pawn pawn, Job skillJob)
        {
            if (current == null || current.pawn != pawn || current.skillJob != skillJob) return null;
            Job? result = current.movement;
            current.movement = null;
            return result;
        }

        internal static bool HasMovementFor(Pawn pawn) => current?.pawn == pawn
            && current.skillJob != pawn.CurJob && current.movement != null;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            current = previous;
            if (movement != null) JobMaker.ReturnToPool(movement);
            movement = null;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob))]
    internal static class SunSkillOrderedMovementPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static bool Prefix(Pawn ___pawn, Job job, bool requestQueueing,
            bool __runOriginal, ref bool __result, out SunSkillMovementHandoff? __state)
        {
            __state = null;
            // 先让重力紊乱、群体飞行和迫降补丁决定是否接受命令。
            if (!__runOriginal || job == null) return true;
            if (SunSkillMovementUtility.IsSkill(job) && SunSkillMovementUtility.AllowsNewCast(___pawn))
                __state = new SunSkillMovementHandoff(___pawn, job);
            if (requestQueueing || KeyBindingDefOf.QueueOrder.IsDownEvent
                || !SunSkillMovementUtility.IsOrdinaryMove(job)
                || !(___pawn.jobs.curDriver is JobDriver_SunSkill driver) || !driver.AllowsMovement)
                return true;
            __result = driver.TryTakeMovement(job);
            return false;
        }

        public static void Finalizer(SunSkillMovementHandoff? __state) => __state?.Dispose();
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    internal static class SunSkillMovementStartPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(Pawn ___pawn, Job newJob, bool __runOriginal,
            out SunSkillMovementHandoff? __state)
        {
            __state = null;
            if (!__runOriginal || newJob == null || newJob == ___pawn.CurJob) return;
            if (SunSkillMovementUtility.IsSkill(newJob) && SunSkillMovementUtility.AllowsNewCast(___pawn))
                __state = new SunSkillMovementHandoff(___pawn, newJob);
            if (___pawn.jobs.curDriver is JobDriver_SunSkill driver)
                driver.DiscardMovement(stopPath: __state?.HasMovement != true);
        }

        public static void Finalizer(SunSkillMovementHandoff? __state) => __state?.Dispose();
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    internal static class SunSkillMovementEndPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(Pawn ___pawn, JobCondition condition, bool startNewJob, bool __runOriginal)
        {
            if (__runOriginal && ___pawn.jobs.curDriver is JobDriver_SunSkill driver)
                driver.PrepareMovementAfterSkill(condition, startNewJob);
        }
    }

    [HarmonyPatch(typeof(FloatMenuOptionProvider_DraftedMove),
        nameof(FloatMenuOptionProvider_DraftedMove.PawnGotoAction))]
    internal static class SunSkillSameCellMovementPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(Pawn pawn, IntVec3 clickCell, IntVec3 gotoLoc, bool __runOriginal)
        {
            // 原版点击脚下不生成 Goto；此时只停止并行移动，技能及原版点击反馈继续运行。
            if (__runOriginal && pawn.Position == gotoLoc && pawn.Map != null
                && !pawn.Map.exitMapGrid.IsExitCell(clickCell) && !KeyBindingDefOf.QueueOrder.IsDownEvent
                && pawn.jobs.curDriver is JobDriver_SunSkill driver && driver.AllowsMovement)
            {
                pawn.jobs.ClearQueuedJobs();
                driver.DiscardMovement(stopPath: true);
            }
        }
    }
}
