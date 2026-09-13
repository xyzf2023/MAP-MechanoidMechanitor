using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体接近 Job。始终由源机械族执行，targetA 为待合体的人类。
    /// 流程：完整校验 → 规划普通路径 A → 必要时合体快速飞行转移
    /// （垂直升空 → 不可见阶段地图内重定位 → 垂直降落）→ 地面走到人类
    /// 相邻格 → 折跃视觉 → 短暂延迟 → 现有 TryStartFusion 正式合体。
    /// 规划结果只存在于运行期，读档后重新规划。
    /// </summary>
    public sealed class JobDriver_MechFusionApproach : JobDriver
    {
        private enum FusionFlightStage
        {
            Takeoff,
            Ascending,
            Descending
        }

        private const int TransitionDelayTicks = 5;
        private const int FusionAscentTimeoutTicks = 120;
        private const int FusionDescentTimeoutTicks = 120;
        private const int MaxFlightLegs = 2;

        private Toil validateToil = null!;
        private Toil approachToil = null!;
        private Toil fusionFlightToil = null!;
        private Toil transitionToil = null!;

        private MechFusionApproachPlan? plan;
        private FusionFlightStage flightStage;
        private int flightLegsUsed;
        private int phaseDeadlineTick;

        // 读档后不恢复飞行视觉与旧规划：首个 Tick 安全回到重规划入口。
        // 只存在于运行期，不写入存档。
        private bool needsReplanAfterLoad;

        private Pawn? Wearer => job.targetA.Thing as Pawn;

        private static int CurrentTick => Find.TickManager?.TicksGame ?? 0;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 只借道目标人类身边，不预留人类本身，避免打断目标当前的工作。
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // 此时 base 已经完成 SetupToils；这里只设置标记，
                // 绝不在 ExposeData 阶段引用 Toil 或 JumpToToil。
                plan = null;
                needsReplanAfterLoad = true;
            }
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.AddFinishAction(delegate
            {
                if (MechanicalFlightUtility.IsFusionRelocating(pawn))
                {
                    // 玩家覆盖、强制中断等异常出口：必须回到合法地面态。
                    MechanicalFlightUtility.CancelFusionRelocation(pawn);
                }
            });

            validateToil = MakeValidateAndPlanToil();
            approachToil = MakeApproachToil();
            fusionFlightToil = MakeFusionFlightToil();
            transitionToil = MakeTransitionToil();
            Toil delayToil = Toils_General.Wait(
                TransitionDelayTicks,
                TargetIndex.A);
            Toil completeToil = MakeCompleteFusionToil();

            yield return validateToil;
            yield return approachToil;
            yield return fusionFlightToil;
            yield return transitionToil;
            yield return delayToil;
            yield return completeToil;
        }

        public override void Notify_PatherFailed()
        {
            ReportFailure(
                "MAP_MechanoidMechanitor.Fusion.Approach.Unreachable"
                    .Translate());
            base.Notify_PatherFailed();
        }

        private Toil MakeValidateAndPlanToil()
        {
            Toil toil = ToilMaker.MakeToil("MechFusionApproach.ValidateAndPlan");
            toil.initAction = delegate
            {
                plan = null;
                flightLegsUsed = 0;

                if (!ValidatePair(out string? failureReason))
                {
                    FailJob(failureReason);
                    return;
                }

                if (!TryBuildPlan())
                {
                    FailJob(
                        "MAP_MechanoidMechanitor.Fusion.Approach.Unreachable"
                            .Translate());
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        private Toil MakeApproachToil()
        {
            Toil toil = ToilMaker.MakeToil("MechFusionApproach.Ground");
            toil.initAction = delegate
            {
                if (plan?.UseFlight == true)
                {
                    JumpTo(fusionFlightToil);
                    return;
                }

                UpdateGroundApproach();
            };
            toil.tickAction = UpdateGroundApproach;
            toil.defaultCompleteMode = ToilCompleteMode.Never;
            return toil;
        }

        private void UpdateGroundApproach()
        {
            if (TryHandleLoadRecovery())
            {
                return;
            }

            Pawn? wearer = Wearer;
            if (!IsWearerUsable(wearer))
            {
                FailJob(
                    "MAP_MechanoidMechanitor.Fusion.Failure.TargetUnavailable"
                        .Translate());
                return;
            }

            if (pawn.Downed || pawn.Dead || !pawn.Spawned)
            {
                FailJob(
                    "MAP_MechanoidMechanitor.Fusion.Failure.SourceUnavailable"
                        .Translate());
                return;
            }

            if (IsAdjacentToWearer(wearer!))
            {
                // 已到达合法相邻格：明确跳入折跃，绝不按顺序落入飞行 Toil。
                pawn.pather?.StopDead();
                JumpTo(transitionToil);
                return;
            }

            if (pawn.pather == null || pawn.pather.MovingNow)
            {
                return;
            }

            if (pawn.Position == wearer!.Position)
            {
                // 被目标挤入同格：先走到任意合法相邻格再继续接近。
                if (TryFindEscapeCell(wearer!, out IntVec3 escapeCell))
                {
                    pawn.pather.StartPath(escapeCell, PathEndMode.OnCell);
                }
                else
                {
                    FailJob(
                        "MAP_MechanoidMechanitor.Fusion.Approach.Unreachable"
                            .Translate());
                }
                return;
            }

            if (pawn.CanReach(wearer!, PathEndMode.Touch, Danger.Deadly))
            {
                pawn.pather.StartPath(wearer!, PathEndMode.Touch);
                return;
            }

            // 地面路线失效：允许重新规划一次（目标移动、地形变化或落到新位置）。
            if (!TryBuildPlan())
            {
                FailJob(
                    "MAP_MechanoidMechanitor.Fusion.Approach.Unreachable"
                        .Translate());
                return;
            }

            if (plan?.UseFlight == true)
            {
                JumpTo(fusionFlightToil);
                return;
            }

            if (!pawn.CanReach(wearer!, PathEndMode.Touch, Danger.Deadly))
            {
                FailJob(
                    "MAP_MechanoidMechanitor.Fusion.Approach.Unreachable"
                        .Translate());
                return;
            }

            pawn.pather.StartPath(wearer!, PathEndMode.Touch);
        }

        private Toil MakeFusionFlightToil()
        {
            Toil toil = ToilMaker.MakeToil("MechFusionApproach.FusionFlight");
            toil.initAction = BeginFusionFlight;
            toil.tickAction = UpdateFusionFlight;
            toil.defaultCompleteMode = ToilCompleteMode.Never;
            return toil;
        }

        private void BeginFusionFlight()
        {
            flightStage = FusionFlightStage.Takeoff;
            phaseDeadlineTick = 0;

            if (plan?.UseFlight != true || flightLegsUsed >= MaxFlightLegs)
            {
                ReturnToGroundApproachOrFail();
                return;
            }

            Pawn? wearer = Wearer;
            if (!ValidatePair(out string? failureReason))
            {
                FailJob(failureReason);
                return;
            }

            if (IsAdjacentToWearer(wearer!))
            {
                // 目标已经移动到自己身边：不需要起飞，直接进入折跃。
                if (plan != null)
                {
                    plan.UseFlight = false;
                }
                JumpTo(transitionToil);
                return;
            }

            if (!MechFusionRelocationUtility.CanSourceUseFusionFlight(pawn))
            {
                // 飞行资格在最后阶段发生变化：回到地面方案。
                MechanicalFlightUtility.CancelFusionRelocation(pawn);
                ReturnToGroundApproachOrFail();
                return;
            }

            if (!ValidateLandingPlan(wearer!))
            {
                if (!TryBuildPlan() || plan?.UseFlight != true
                    || !ValidateLandingPlan(wearer!))
                {
                    ReturnToGroundApproachOrFail();
                    return;
                }
            }

            if (!MechanicalFlightUtility.TryBeginFusionTakeoff(pawn))
            {
                MechanicalFlightUtility.CancelFusionRelocation(pawn);
                ReturnToGroundApproachOrFail();
                return;
            }

            flightLegsUsed++;
            flightStage = FusionFlightStage.Ascending;
            phaseDeadlineTick = CurrentTick + FusionAscentTimeoutTicks;
        }

        private void UpdateFusionFlight()
        {
            if (TryHandleLoadRecovery())
            {
                return;
            }

            Pawn? wearer = Wearer;
            if (!IsWearerUsable(wearer))
            {
                AbortFusionFlight();
                FailJob(
                    "MAP_MechanoidMechanitor.Fusion.Failure.TargetUnavailable"
                        .Translate());
                return;
            }

            if (pawn.Downed || pawn.Dead || !pawn.Spawned)
            {
                AbortFusionFlight();
                FailJob(
                    "MAP_MechanoidMechanitor.Fusion.Failure.SourceUnavailable"
                        .Translate());
                return;
            }

            if (plan?.UseFlight != true
                || !MechanicalFlightUtility.IsFusionRelocating(pawn))
            {
                // 飞行运行态或规划已经不完整（例如读档清理、异常取消）：
                // 不空转、不恢复半空动画，直接回到重规划入口。
                RestartPlanning();
                return;
            }

            switch (flightStage)
            {
                case FusionFlightStage.Ascending:
                    UpdateFusionAscent(wearer!);
                    return;

                case FusionFlightStage.Descending:
                    UpdateFusionDescent();
                    return;
            }
        }

        private void UpdateFusionAscent(Pawn wearer)
        {
            float factor =
                MechanicalFlightUtility.GetFusionRelocationVisualFactor(pawn);
            if (factor < 0.999f)
            {
                if (CurrentTick >= phaseDeadlineTick)
                {
                    // 升空表现异常：绝不把可见中的机械族瞬移，安全取消。
                    AbortFusionFlight();
                    ReturnToGroundApproachOrFail();
                }
                return;
            }

            // 不可见阶段：真正迁移前再次验证落点与落地后可走路径。
            if (!ValidateLandingPlan(wearer))
            {
                if (!TryBuildPlan() || plan?.UseFlight != true
                    || !ValidateLandingPlan(wearer))
                {
                    AbortFusionFlight();
                    ReturnToGroundApproachOrFail();
                    return;
                }
            }

            if (!MechFusionRelocationUtility.ApplyRelocation(
                    pawn,
                    plan!.LandingCell,
                    out string? relocationFailure))
            {
                AbortFusionFlight();
                FailJob(relocationFailure);
                return;
            }

            if (!MechanicalFlightUtility.TryBeginFusionDescent(pawn))
            {
                AbortFusionFlight();
                ReturnToGroundApproachOrFail();
                return;
            }

            flightStage = FusionFlightStage.Descending;
            phaseDeadlineTick = CurrentTick + FusionDescentTimeoutTicks;
        }

        private void UpdateFusionDescent()
        {
            float factor =
                MechanicalFlightUtility.GetFusionRelocationVisualFactor(pawn);
            if (factor > 0.001f && CurrentTick < phaseDeadlineTick)
            {
                return;
            }

            MechanicalFlightUtility.CompleteFusionLanding(pawn);
            if (plan != null)
            {
                plan.UseFlight = false;
                plan.LandingCell = IntVec3.Invalid;
            }

            // 快速飞行只负责跨过远距离；落地后统一回到地面接近。
            JumpTo(approachToil);
        }

        private void AbortFusionFlight()
        {
            MechanicalFlightUtility.CancelFusionRelocation(pawn);
        }

        /// <summary>
        /// 读档后的安全恢复：飞行运行态已由统一飞行注册表清理。
        /// 只在运行期第一个 Tick 回到重规划入口，最多重新计算一次方案，
        /// 绝不尝试恢复升空/降落进度。
        /// </summary>
        private bool TryHandleLoadRecovery()
        {
            if (!needsReplanAfterLoad)
            {
                return false;
            }

            needsReplanAfterLoad = false;
            RestartPlanning();
            return true;
        }

        /// <summary>
        /// 丢弃运行期规划并回到 ValidateAndPlan 重新规划。
        /// 只允许在 Tick 中调用，禁止在读档阶段引用 Toil。
        /// </summary>
        private void RestartPlanning()
        {
            if (MechanicalFlightUtility.IsFusionRelocating(pawn))
            {
                MechanicalFlightUtility.CancelFusionRelocation(pawn);
            }

            plan = null;
            flightStage = FusionFlightStage.Takeoff;
            flightLegsUsed = 0;
            JumpTo(validateToil);
        }

        private void ReturnToGroundApproachOrFail()
        {
            if (plan != null)
            {
                plan.UseFlight = false;
            }

            Pawn? wearer = Wearer;
            if (IsWearerUsable(wearer)
                && pawn.CanReach(wearer!, PathEndMode.Touch, Danger.Deadly))
            {
                JumpTo(approachToil);
                return;
            }

            FailJob(
                "MAP_MechanoidMechanitor.Fusion.Approach.Unreachable"
                    .Translate());
        }

        private Toil MakeTransitionToil()
        {
            Toil toil = ToilMaker.MakeToil("MechFusionApproach.Transition");
            toil.initAction = delegate
            {
                if (!ValidatePair(out string? failureReason))
                {
                    FailJob(failureReason);
                    return;
                }

                Pawn? wearer = Wearer;
                if (!IsWearerUsable(wearer))
                {
                    FailJob(
                        "MAP_MechanoidMechanitor.Fusion.Failure.TargetUnavailable"
                            .Translate());
                    return;
                }

                if (!IsAdjacentToWearer(wearer!))
                {
                    // 目标在最后一步移动：回到接近环节重新靠近。
                    JumpTo(approachToil);
                    return;
                }

                pawn.pather?.StopDead();
                MechFusionVisualUtility.PlayFusionTransition(pawn, wearer);
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        private Toil MakeCompleteFusionToil()
        {
            Toil toil = ToilMaker.MakeToil("MechFusionApproach.Complete");
            toil.initAction = delegate
            {
                if (!ValidatePair(out string? failureReason))
                {
                    FailJob(failureReason);
                    return;
                }

                Pawn? wearer = Wearer;
                if (wearer == null)
                {
                    FailJob(
                        "MAP_MechanoidMechanitor.Fusion.Failure.TargetUnavailable"
                            .Translate());
                    return;
                }

                if (!MechFusionStartService.TryStartFusion(
                        pawn,
                        wearer,
                        out string? startFailure))
                {
                    FailJob(
                        startFailure
                            ?? "MAP_MechanoidMechanitor.Fusion.Failure.Unexpected"
                                .Translate());
                    return;
                }

                EndJobWith(JobCondition.Succeeded);
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        private bool TryBuildPlan()
        {
            plan = null;
            Pawn? wearer = Wearer;
            if (!IsWearerUsable(wearer))
            {
                return false;
            }

            if (!MechFusionApproachUtility.TryPlan(
                    pawn,
                    wearer!,
                    out MechFusionApproachPlan newPlan))
            {
                return false;
            }

            plan = newPlan;
            return true;
        }

        private bool ValidateLandingPlan(Pawn wearer)
        {
            if (plan?.UseFlight != true || !plan.LandingCell.IsValid)
            {
                return false;
            }

            if (!MechFusionRelocationUtility.IsLandingCellStillValid(
                    pawn,
                    wearer,
                    plan.LandingCell,
                    out int pathSteps))
            {
                plan.UseFlight = false;
                plan.LandingCell = IntVec3.Invalid;
                return false;
            }

            plan.LandingPathSteps = pathSteps;
            return true;
        }

        private void JumpTo(Toil toil)
        {
            pawn.jobs?.curDriver?.JumpToToil(toil);
        }

        private bool ValidatePair(out string? failureReason)
        {
            failureReason = null;
            Pawn? wearer = Wearer;
            if (wearer == null)
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Fusion.Failure.TargetUnavailable"
                        .Translate();
                return false;
            }

            return MechFusionValidator.CanStart(pawn, wearer, out failureReason);
        }

        private static bool IsWearerUsable(Pawn? wearer)
        {
            return wearer != null
                && !wearer.Destroyed
                && !wearer.Discarded
                && !wearer.Dead
                && wearer.Spawned
                && wearer.Map != null;
        }

        private bool IsAdjacentToWearer(Pawn wearer)
        {
            return pawn.Spawned
                && pawn.Map == wearer.Map
                && pawn.Position.AdjacentTo8Way(wearer.Position);
        }

        private bool TryFindEscapeCell(Pawn wearer, out IntVec3 escapeCell)
        {
            escapeCell = IntVec3.Invalid;
            Map? map = pawn.Map;
            if (map == null)
            {
                return false;
            }

            IntVec3 fallback = IntVec3.Invalid;
            for (int i = 0; i < GenAdj.AdjacentCells.Length; i++)
            {
                IntVec3 candidate = wearer.Position + GenAdj.AdjacentCells[i];
                if (!candidate.InBounds(map)
                    || !candidate.WalkableBy(map, pawn))
                {
                    continue;
                }

                if (!fallback.IsValid)
                {
                    fallback = candidate;
                }

                if (candidate.GetFirstPawn(map) == null)
                {
                    escapeCell = candidate;
                    return true;
                }
            }

            escapeCell = fallback;
            return escapeCell.IsValid;
        }

        private void FailJob(string? failureReason)
        {
            ReportFailure(failureReason);
            EndJobWith(JobCondition.Incompletable);
        }

        private void ReportFailure(string? failureReason)
        {
            if (string.IsNullOrEmpty(failureReason) || job?.playerForced != true)
            {
                return;
            }

            Messages.Message(
                failureReason!,
                pawn,
                MessageTypeDefOf.RejectInput,
                historical: false);
        }
    }
}
