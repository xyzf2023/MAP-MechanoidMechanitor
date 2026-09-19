using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 仅供已配置展开效果的源机械体地面手动解除。等待时会话保持 Active，
    /// 到期才调用唯一解除服务；强制退出不经过本 Job，也不等待动画。
    /// targetA 是人类自己，targetB 是下达指令时的外甲实例。
    /// </summary>
    public sealed class JobDriver_MechFusionReleaseTransition : JobDriver
    {
        private MechFusionSession? session;
        private MechFusionTransitionVisual? visual;
        private bool cancelAfterLoad;

        internal bool IsShowingHuman => session?.IsActive == true
            && visual?.IsVisible == true;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // 未提交的手动解除在读档后取消，绝不从剩余等待直接解除。
                cancelAfterLoad = true;
            }
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => cancelAfterLoad || !TryGetMatchingSession(out _));
            this.AddFinishAction(_ => EndVisual());

            Toil wait = Toils_General.Wait(MechFusionTransitionVisual.DurationTicks);
            wait.handlingFacing = true;
            wait.initAction = delegate
            {
                if (!TryGetMatchingSession(out session))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                pawn.pather?.StopDead();
                pawn.Rotation = Rot4.South;
                visual = MechFusionVisualUtility.BeginExpandedTransition(
                    pawn, session!.SourcePawn!, job);
                if (visual == null)
                {
                    // Mote 创建失败不隐藏人类，也不阻塞已请求的解除。
                    ReadyForNextToil();
                    return;
                }

                MechFusionRenderUtility.RefreshTransitionGraphics(pawn);
            };
            wait.tickAction = delegate
            {
                pawn.pather?.StopDead();
                pawn.Rotation = Rot4.South;
                if (visual != null && !visual.IsVisible)
                {
                    // 被外力挪动、关闭特效或 Mote 提前消失时撤销视觉，等待仍可结束。
                    EndVisual();
                }
            };
            wait.WithProgressBarToilDelay(TargetIndex.A);
            yield return wait;

            Toil complete = ToilMaker.MakeToil("MechFusionReleaseTransition.Complete");
            complete.initAction = delegate
            {
                if (cancelAfterLoad || !TryGetMatchingSession(out MechFusionSession? current)
                    || !ReferenceEquals(current, session))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                EndVisual();
                // 原有 FinalizeSession 负责播放一次折跃效果，包括需要延迟恢复的情况。
                MechFusionTeardownService.TryTeardown(
                    current, MechFusionExitReason.Manual, force: false);
            };
            complete.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return complete;
        }

        private bool TryGetMatchingSession(out MechFusionSession? current)
        {
            current = null;
            return pawn.Spawned && !pawn.Dead && !pawn.Downed && !pawn.InMentalState
                && !MechanicalFlightUtility.IsAirborne(pawn)
                && GameComponent_MechFusionSessionRegistry.TryGetSessionForWearer(pawn, out current)
                && current?.IsActive == true
                && ReferenceEquals(current.FusionApparel, job.targetB.Thing)
                && current.FusionApparel is Apparel apparel && !apparel.Destroyed
                && pawn.apparel?.Wearing(apparel) == true
                && current.SourcePawn != null && !current.SourcePawn.Dead
                && !current.SourcePawn.Destroyed && !current.SourcePawn.Discarded
                && MechFusionVisualUtility.GetTransitionMoteDef(current.SourcePawn) != null;
        }

        internal void CancelVisualForSession(MechFusionSession endingSession)
        {
            if (ReferenceEquals(session, endingSession))
            {
                EndVisual();
            }
        }

        private void EndVisual()
        {
            MechFusionTransitionVisual? previous = visual;
            visual = null;
            previous?.End();
        }
    }
}
