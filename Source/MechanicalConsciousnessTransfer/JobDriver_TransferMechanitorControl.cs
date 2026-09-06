using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 轨道数据网络研究完成后的“控制权交接”JobDriver。
    /// 与“意识转移”Job 在业务上完全隔离：只调用 MechanicalControlHandoffUtility，
    /// 绝不执行技能合并、宿主替换、科研能力迁移或机械意识 Hediff 迁移。
    /// </summary>
    public class JobDriver_TransferMechanitorControl : JobDriver
    {
        private const int TransferDurationTicks = 600;
        private const string FailedKey = "MAP_MechanoidMechanitor.Justice.ControlHandoff.Failed";

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFailCondition(() => !CanTransferControlNow());

            yield return Toils_General.StopDead();

            Toil wait = Toils_General.Wait(TransferDurationTicks, TargetIndex.A);
            wait.FailOn(() => !CanTransferControlNow());
            wait.WithProgressBarToilDelay(TargetIndex.A);
            yield return wait;

            yield return Toils_General.Do(ApplyTransfer);
        }

        private Pawn? GetSourceFromJob()
        {
            return job.GetTarget(TargetIndex.A).Pawn;
        }

        private Pawn? GetTargetFromJob()
        {
            return job.GetTarget(TargetIndex.B).Pawn;
        }

        private bool CanTransferControlNow()
        {
            Pawn? source = GetSourceFromJob();
            Pawn? target = GetTargetFromJob();
            if (source != pawn)
            {
                return false;
            }

            // 研究状态（轨道数据网络）或目标失效时，此处会立即失效并安全终止 Job。
            return MechanicalControlHandoffUtility.CanTransferControl(source, target);
        }

        private void ApplyTransfer()
        {
            Pawn? source = GetSourceFromJob();
            Pawn? target = GetTargetFromJob();
            if (source != pawn
                || !MechanicalControlHandoffUtility.CanTransferControl(source, target))
            {
                ShowFailureMessage();
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            if (!MechanicalControlHandoffUtility.TryTransferControl(source, target))
            {
                ShowFailureMessage();
                EndJobWith(JobCondition.Incompletable);
            }
        }

        private void ShowFailureMessage()
        {
            Messages.Message(
                FailedKey.Translate(),
                pawn,
                MessageTypeDefOf.RejectInput);
        }
    }
}
