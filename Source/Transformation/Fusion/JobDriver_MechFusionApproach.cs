using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体接近 Job。始终由源机械族执行，targetA 为待合体的人类。
    /// 流程：完整校验 → 规划普通路径 A → 必要时合体快速飞行转移
    /// （沿地图 +Z 飞出北边界 → 隐藏并地图内重定位 → 下一帧从目标列北边界外
    /// 垂直降落）→ 地面走到人类相邻格 → 展开或折跃视觉 → 对应延迟 →
    /// 现有 TryStartFusion 正式合体。规划结果只存在于运行期，读档后重新规划。
    /// </summary>
    public sealed class JobDriver_MechFusionApproach : JobDriver
    {
        private enum FusionFlightStage
        {
            Takeoff,
            Ascending,
            HiddenRelocation,
            WaitingForDescent,
            Descending
        }

        private const int TransitionDelayTicks = 5;
        private const int HiddenRelocationTimeoutTicks = 120;
        private const int MaxFlightLegs = 2;

        private Toil validateToil = null!;
        private Toil approachToil = null!;
        private Toil fusionFlightToil = null!;
        private Toil transitionToil = null!;

        private MechFusionApproachPlan? plan;
        private FusionFlightStage flightStage;
        private int flightLegsUsed;
        private int phaseDeadlineTick;
        private int relocationFrame = -1;
        private MechFusionTransitionVisual? transitionVisual;
        private bool playCompletionEffect;
        private Pawn? waitingWearer;
        private Job? wearerWaitJob;
        private int wearerWaitJobId = -1;

        internal bool IsTransitionSourceHidden => transitionVisual?.IsVisible == true;

        // 读档后不恢复飞行视觉与旧规划：首个 Tick 安全回到重规划入口。
        // 只存在于运行期，不写入存档。
        private bool needsReplanAfterLoad;

        private Pawn? Wearer => job.targetA.Thing as Pawn;

        private static int CurrentTick => Find.TickManager?.TicksGame ?? 0;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 接近时不预留人类；战车展开过渡开始后才临时要求人类等待。
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
            // 覆盖接近、飞行和折跃等待阶段，精神状态出现后不再继续执行玩家合体指令。
            this.FailOn(() => pawn.InMentalState);
            this.AddFinishAction(delegate
            {
                EndTransitionVisual();
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
            Toil delayToil = MakeTransitionDelayToil();
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
            relocationFrame = -1;

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
            phaseDeadlineTick = CurrentTick
                + MechanicalFlightVisualSmoothing.GetFusionPhaseTimeoutTicks(pawn);
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

                case FusionFlightStage.HiddenRelocation:
                    UpdateHiddenRelocation();
                    return;

                case FusionFlightStage.WaitingForDescent:
                    UpdateWaitingForDescent();
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

            // 机械族已经完整越过地图北边界。隐藏之前再次验证落点，
            // 避免进入不可见阶段后才发现原规划已经完全失效。
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

            // 已飞出北边界：同一 Tick 立即隐藏并完成地图内逻辑换位；
            // 真正开始下降则由 WaitingForDescent 强制推迟到下一渲染帧。
            MechanicalFlightVisualSmoothing.SetFusionHidden(pawn, true);
            flightStage = FusionFlightStage.HiddenRelocation;
            phaseDeadlineTick = CurrentTick + HiddenRelocationTimeoutTicks;
            UpdateHiddenRelocation();
        }

        private void UpdateHiddenRelocation()
        {
            if (plan?.UseFlight != true || !plan.LandingCell.IsValid)
            {
                AbortFusionFlight();
                ReturnToGroundApproachOrFail();
                return;
            }

            if (!MechFusionRelocationUtility.ApplyRelocation(
                    pawn,
                    plan.LandingCell,
                    out string? relocationFailure))
            {
                AbortFusionFlight();
                FailJob(relocationFailure);
                return;
            }

            // 真实 Pawn 已经位于合法 landingCell，但仍保持地图外隐藏。
            // 记录当前渲染帧，明确禁止同一帧立即开始下降。
            relocationFrame = RealTime.frameCount;
            flightStage = FusionFlightStage.WaitingForDescent;
            phaseDeadlineTick = CurrentTick + HiddenRelocationTimeoutTicks;
        }

        private void UpdateWaitingForDescent()
        {
            if (RealTime.frameCount <= relocationFrame
                && CurrentTick < phaseDeadlineTick)
            {
                return;
            }

            if (!MechanicalFlightUtility.TryBeginFusionDescent(pawn))
            {
                AbortFusionFlight();
                ReturnToGroundApproachOrFail();
                return;
            }

            flightStage = FusionFlightStage.Descending;
            phaseDeadlineTick = CurrentTick
                + MechanicalFlightVisualSmoothing.GetFusionPhaseTimeoutTicks(pawn);
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

            relocationFrame = -1;

            // 快速飞行只负责跨过远距离；落地后统一回到地面接近。
            JumpTo(approachToil);
        }

        private void AbortFusionFlight()
        {
            relocationFrame = -1;
            MechanicalFlightUtility.CancelFusionRelocation(pawn);
        }

        /// <summary>
        /// 读档后的安全恢复：飞行运行态已由统一飞行注册表清理。
        /// 只在运行期第一个 Tick 回到重规划入口，最多重新计算一次方案，
        /// 绝不尝试恢复升空/隐藏/降落进度。
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
            EndTransitionVisual();
            if (MechanicalFlightUtility.IsFusionRelocating(pawn))
            {
                MechanicalFlightUtility.CancelFusionRelocation(pawn);
            }

            plan = null;
            flightStage = FusionFlightStage.Takeoff;
            flightLegsUsed = 0;
            phaseDeadlineTick = 0;
            relocationFrame = -1;
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
                if (TryHandleLoadRecovery())
                {
                    return;
                }

                EndTransitionVisual();
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
                MechFusionVisualUtility.FaceEachOther(pawn, wearer);
                if (MechFusionVisualUtility.GetTransitionMoteDef(pawn) != null
                    && !MechanicalFlightUtility.IsAirborne(pawn))
                {
                    // 多留一个 tick，避免目标先于源机械体结算等待到期而提前走动。
                    // ForceWait 会暂停原 Job；可暂停的工作由原版队列在结束后恢复。
                    PawnUtility.ForceWait(wearer!, MechFusionTransitionVisual.DurationTicks + 1,
                        maintainPosture: true, maintainSleep: true);
                    Job? startedWait = wearer!.CurJob;
                    if (startedWait == null || startedWait.startTick != CurrentTick
                        || startedWait.expiryInterval != MechFusionTransitionVisual.DurationTicks + 1)
                    {
                        // 等待未实际开始（例如被其他系统替换），不接管或清理其当前任务。
                        FailJob(null);
                        return;
                    }

                    waitingWearer = wearer;
                    wearerWaitJob = startedWait;
                    wearerWaitJobId = startedWait.loadID;
                    wearer.pather?.StopDead();
                    wearer.Drawer?.tweener?.ResetTweenedPosToRoot();
                }

                transitionVisual = MechFusionVisualUtility.BeginExpandedTransition(
                    pawn, pawn, job, visualTarget: wearer);
                playCompletionEffect = transitionVisual != null;
                if (!playCompletionEffect)
                {
                    MechFusionVisualUtility.PlayFusionTransition(pawn, wearer);
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        private Toil MakeTransitionDelayToil()
        {
            // 保持原有六个 Toil 的索引，兼容正在接近/等待中的旧存档。
            Toil toil = Toils_General.Wait(TransitionDelayTicks, TargetIndex.A);
            toil.initAction = delegate
            {
                pawn.pather?.StopDead();
                ticksLeftThisToil = playCompletionEffect
                    ? MechFusionTransitionVisual.DurationTicks : TransitionDelayTicks;
            };
            toil.tickAction = delegate
            {
                if (TryHandleLoadRecovery())
                {
                    return;
                }

                if (!playCompletionEffect)
                {
                    return;
                }

                if (MechanicalFlightUtility.IsAirborne(pawn))
                {
                    // 展开后又收到起飞指令时取消本次合体，不让静态机壳遮住飞行实体。
                    FailJob("MAP_MechanoidMechanitor.Fusion.Failure.SourceUnavailable".Translate());
                    return;
                }

                if (!IsWearerStillWaiting())
                {
                    // 人类收到新指令或等待被中断时取消合体，不覆盖其新 Job。
                    FailJob(null);
                    return;
                }

                waitingWearer?.pather?.StopDead();
                if (!ValidatePair(out string? failureReason))
                {
                    FailJob(failureReason);
                    return;
                }

                if (!IsAdjacentToWearer(Wearer!))
                {
                    // 被外力挪出相邻范围时先清理等待与虚影，再重新接近。
                    EndTransitionVisual();
                    JumpTo(approachToil);
                    return;
                }

                if (transitionVisual != null && !transitionVisual.IsVisible)
                {
                    // Mote 提前消失时恢复实体，完成时仍只播放一次折跃。
                    transitionVisual.End();
                    transitionVisual = null;
                }
            };
            return toil;
        }

        private void EndTransitionVisual()
        {
            MechFusionTransitionVisual? previous = transitionVisual;
            transitionVisual = null;
            playCompletionEffect = false;
            try
            {
                previous?.End();
            }
            finally
            {
                EndWearerWait();
            }
        }

        private bool IsWearerStillWaiting()
        {
            return waitingWearer != null && wearerWaitJob != null
                && ReferenceEquals(waitingWearer.CurJob, wearerWaitJob)
                && wearerWaitJob.loadID == wearerWaitJobId;
        }

        private void EndWearerWait()
        {
            Pawn? previousWearer = waitingWearer;
            bool endOwnedWait = IsWearerStillWaiting();
            waitingWearer = null;
            wearerWaitJob = null;
            wearerWaitJobId = -1;
            if (endOwnedWait)
            {
                previousWearer!.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }

        private Toil MakeCompleteFusionToil()
        {
            Toil toil = ToilMaker.MakeToil("MechFusionApproach.Complete");
            toil.initAction = delegate
            {
                if (TryHandleLoadRecovery())
                {
                    return;
                }

                // Delay 到期会直接进入本 Toil，不再执行最后一次等待 tickAction。
                if (playCompletionEffect && MechanicalFlightUtility.IsAirborne(pawn))
                {
                    FailJob("MAP_MechanoidMechanitor.Fusion.Failure.SourceUnavailable".Translate());
                    return;
                }

                if (waitingWearer != null && !IsWearerStillWaiting())
                {
                    FailJob(null);
                    return;
                }

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

                if (!IsAdjacentToWearer(wearer))
                {
                    // 折跃效果到正式合体之间目标又移动：不允许远距离直接合体，
                    // 回到地面接近重新靠近；再次相邻后允许重新播放折跃效果。
                    EndTransitionVisual();
                    JumpTo(approachToil);
                    return;
                }

                bool playCompletedTransition = playCompletionEffect;
                Map sourceMap = pawn.Map;
                IntVec3 sourcePosition = pawn.Position;
                EndTransitionVisual();
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

                if (playCompletedTransition)
                {
                    MechFusionVisualUtility.PlayCompletedFusionTransition(
                        sourceMap, sourcePosition, wearer);
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
