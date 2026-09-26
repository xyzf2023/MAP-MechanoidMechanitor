using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体的机械族侧右键入口：
    /// 选中有资格的机械族右键合法人类，给源机械族下达合体接近 Job。
    /// 正式合体仍由 MechFusionStartService.TryStartFusion 统一执行。
    /// </summary>
    public sealed class FloatMenuOptionProvider_MechFusion
        : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;

        protected override bool Undrafted => true;

        protected override bool Multiselect => false;

        protected override bool MechanoidCanDo => true;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            return ModsConfig.BiotechActive
                && context.FirstSelectedPawn != null;
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(
            Pawn clickedPawn,
            FloatMenuContext context)
        {
            Pawn? source = context.FirstSelectedPawn;
            Pawn wearer = clickedPawn;
            if (source == null
                || wearer == null
                || ReferenceEquals(source, wearer)
                || (!MechFusionEligibilityUtility.HasFusionEligibility(source)
                    && !MechFusionEligibilityUtility.HasInnateFusionMarker(source))
                || !wearer.RaceProps.Humanlike)
            {
                yield break;
            }

            string label =
                "MAP_MechanoidMechanitor.Fusion.Gizmo.Label".Translate();
            // 先天组件存在但记录缺失时，由统一校验器补登记。
            if (!MechFusionValidator.CanStart(
                    source,
                    wearer,
                    out string? failureReason))
            {
                yield return new FloatMenuOption(
                    label + ": " + (failureReason ?? string.Empty),
                    null);
                yield break;
            }

            yield return new FloatMenuOption(
                label,
                delegate
                {
                    // 菜单打开后双方状态仍可能变化，必须在打断源机械族任务前重新校验。
                    if (!MechFusionValidator.CanStart(source, wearer, out string? orderFailureReason))
                    {
                        Messages.Message(
                            orderFailureReason ?? string.Empty,
                            source,
                            MessageTypeDefOf.RejectInput,
                            historical: false);
                        return;
                    }

                    Job job = JobMaker.MakeJob(
                        MAPMechanitor_JobDefOf.MAP_MechFusionApproach,
                        wearer);
                    if (source.jobs != null)
                    {
                        job.playerForced = true;
                        if (source.CurJob != null)
                        {
                            source.CurJob.playerInterruptedForced = true;
                        }

                        // StartJob 会结束旧 Job，但不会清除机械飞行层独立维护的
                        // 直线路径绘制缓存。先同时停止原版 Pather 与该缓存，避免
                        // 新命令开始后仍显示上一条移动路线。
                        source.pather?.StopDead();
                        MechanicalFlightStraightPathPatch.ClearMotion(source);
                        source.jobs.ClearQueuedJobs();
                        source.jobs.StartJob(
                            job,
                            JobCondition.InterruptForced,
                            resumeCurJobAfterwards: false,
                            cancelBusyStances: true,
                            tag: JobTag.Misc,
                            preToilReservationsCanFail: true);
                        return;
                    }

                    Messages.Message(
                        "MAP_MechanoidMechanitor.Fusion.Approach.OrderFailed"
                            .Translate(),
                        source,
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                });
        }
    }
}
