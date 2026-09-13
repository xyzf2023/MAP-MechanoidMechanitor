using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体接近 Job。始终由源机械族执行，targetA 为待合体的人类。
    /// 右键下达合体只负责启动本 Job；接近、最终校验与折跃过渡都在这里完成，
    /// 正式合体仍然只由 MechFusionStartService.TryStartFusion 执行。
    /// </summary>
    public sealed class JobDriver_MechFusionApproach : JobDriver
    {
        private const int TransitionDelayTicks = 5;

        private Toil approachToil = null!;

        private Pawn? Wearer => job.targetA.Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 只借道目标人类身边，不预留人类本身，避免打断目标当前的工作。
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);

            Toil validateToil = MakeValidateStartToil();
            approachToil = MakeApproachToil();
            Toil transitionToil = MakeTransitionToil();
            Toil delayToil = Toils_General.Wait(
                TransitionDelayTicks,
                TargetIndex.A);
            Toil completeToil = MakeCompleteFusionToil();

            yield return validateToil;
            yield return approachToil;
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

        private Toil MakeValidateStartToil()
        {
            Toil toil = ToilMaker.MakeToil("MechFusionApproach.ValidateStart");
            toil.initAction = delegate
            {
                if (!ValidatePair(out string? failureReason))
                {
                    FailJob(failureReason);
                    return;
                }

                Pawn? wearer = Wearer;
                if (wearer == null
                    || !pawn.CanReach(wearer, PathEndMode.Touch, Danger.Deadly))
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
            Toil toil = ToilMaker.MakeToil("MechFusionApproach.Goto");
            toil.initAction = UpdateApproach;
            toil.tickIntervalAction = delegate
            {
                UpdateApproach();
            };
            toil.defaultCompleteMode = ToilCompleteMode.Never;
            return toil;
        }

        private void UpdateApproach()
        {
            Pawn? wearer = Wearer;
            if (!IsWearerUsable(wearer))
            {
                FailJob(
                    "MAP_MechanoidMechanitor.Fusion.Failure.TargetUnavailable"
                        .Translate());
                return;
            }

            if (pawn.CanReachImmediate(wearer!, PathEndMode.Touch))
            {
                pawn.pather?.StopDead();
                if (pawn.jobs?.curDriver != null)
                {
                    pawn.jobs.curDriver.ReadyForNextToil();
                }
                return;
            }

            if (pawn.pather == null || pawn.pather.MovingNow)
            {
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

                if (!pawn.CanReachImmediate(wearer!, PathEndMode.Touch))
                {
                    // 目标在最后一步移动：回到接近环节重新靠近。
                    if (approachToil != null)
                    {
                        pawn.jobs?.curDriver?.JumpToToil(approachToil);
                    }
                    else
                    {
                        FailJob(
                            "MAP_MechanoidMechanitor.Fusion.Approach.Unreachable"
                                .Translate());
                    }
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
