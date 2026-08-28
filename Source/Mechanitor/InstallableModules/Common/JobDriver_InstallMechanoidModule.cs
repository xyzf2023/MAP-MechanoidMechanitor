using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族模块通用安装 JobDriver。
    /// 从 TargetA 获取模块，从模块获取 CompInstallableMechanoidModule，
    /// 不根据当前 JobDef 或目标 defName 区分效果。
    /// 只有具体效果明确返回成功后才会销毁模块并发送成功消息。
    /// </summary>
    public class JobDriver_InstallMechanoidModule : JobDriver
    {
        private Thing Module => TargetThingA;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(
                job.GetTarget(TargetIndex.A),
                job,
                1,
                -1,
                null,
                errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFailCondition(() => !CanInstallNow());

            // 兼容旧存档：旧版安装 JobDriver 的索引 0 同样是 Reserve toil。
            // 即使 TryMakePreToilReservations 已完成预留，此处也必须保留，以免改变
            // 后续 Goto、Wait 和安装 toil 的索引。相同 Pawn、Job 和目标的重复预留是幂等的。
            yield return Toils_Reserve.Reserve(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            int duration = Module?.TryGetComp<CompInstallableMechanoidModule>()?.InstallDurationTicks
                ?? 600;

            Toil wait = Toils_General.Wait(duration, TargetIndex.A);
            wait.WithProgressBarToilDelay(TargetIndex.A);
            yield return wait;

            yield return Toils_General.Do(ApplyInstallation);
        }

        private bool CanInstallNow()
        {
            if (pawn == null
                || Module == null
                || Module.Destroyed
                || !Module.Spawned
                || Module.IsForbidden(pawn))
            {
                return false;
            }

            CompInstallableMechanoidModule? comp =
                Module.TryGetComp<CompInstallableMechanoidModule>();
            if (comp == null || !comp.IsValidInstaller(pawn))
            {
                return false;
            }

            CompMechanoidModuleInstallEffect? effect = comp.TryGetInstallEffect();
            if (effect == null)
            {
                return false;
            }

            return effect.CanInstallNow(pawn).Accepted;
        }

        private void ApplyInstallation()
        {
            if (Module == null)
            {
                return;
            }

            CompInstallableMechanoidModule? comp =
                Module.TryGetComp<CompInstallableMechanoidModule>();
            if (comp == null)
            {
                return;
            }

            CompMechanoidModuleInstallEffect? effect = comp.TryGetInstallEffect();
            if (effect == null)
            {
                return;
            }

            // 完成前再次完整验证；任一条件失效则安全中止，不销毁物品。
            if (!CanInstallNow())
            {
                Messages.Message(
                    effect.GetRejectMessage(pawn),
                    pawn,
                    MessageTypeDefOf.RejectInput);
                return;
            }

            if (!effect.TryApply(pawn))
            {
                Messages.Message(
                    effect.GetRejectMessage(pawn),
                    pawn,
                    MessageTypeDefOf.RejectInput);
                return;
            }

            Module.Destroy(DestroyMode.Vanish);
            Messages.Message(
                effect.GetSuccessMessage(pawn),
                pawn,
                MessageTypeDefOf.PositiveEvent);
        }
    }
}
