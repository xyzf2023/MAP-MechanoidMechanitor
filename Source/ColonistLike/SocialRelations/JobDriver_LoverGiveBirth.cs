using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 恋人主动产程：原地等待读条，成功完成后生成新生儿并可选衔接「将婴儿带到安全处」。
    /// 中断不会移除怀孕状态，玩家可再次下达「开始分娩」。
    /// </summary>
    public sealed class JobDriver_LoverGiveBirth : JobDriver
    {
        private const string LogPrefix = "[MAP-机械族机械师] LoverPregnancy：";

        private Hediff_LoverPregnant? Pregnancy =>
            pawn?.health?.hediffSet?.GetFirstHediffOfDef(
                MAPMechanitor_HediffDefOf.MAP_LoverPregnant) as Hediff_LoverPregnant;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 成功产下新生儿后，由 Finalizer 衔接原版「将婴儿带到安全处」。
            Pawn? newbornForFinalizer = null;
            bool birthCompleted = false;
            SetFinalizerJob(
                condition =>
                {
                    if (condition != JobCondition.Succeeded
                        || newbornForFinalizer == null
                        || !newbornForFinalizer.Spawned
                        || newbornForFinalizer.Dead
                        || pawn == null
                        || !pawn.Spawned
                        || pawn.Dead
                        || pawn.Downed
                        || pawn.Drafted
                        || pawn.InMentalState
                        || pawn.IsBurning()
                        || pawn.Map != newbornForFinalizer.Map
                        || pawn.health?.capacities == null
                        || !pawn.health.capacities.CapableOf(
                            PawnCapacityDefOf.Manipulation))
                    {
                        return null!;
                    }

                    if (!ChildcareUtility.CanSuckle(
                        newbornForFinalizer,
                        out ChildcareUtility.BreastfeedFailReason? _))
                    {
                        return null!;
                    }

                    if (!ChildcareUtility.CanHaulBabyNow(
                        pawn,
                        newbornForFinalizer,
                        ignoreOtherReservations: false,
                        out ChildcareUtility.BreastfeedFailReason? _))
                    {
                        return null!;
                    }

                    return ChildcareUtility.MakeBringBabyToSafetyJob(
                        pawn,
                        newbornForFinalizer);
                });

            AddFailCondition(() => !birthCompleted && !CanContinueLabor());

            if (job.count <= 0)
            {
                job.count = Rand.Range(1250, 2501);
            }

            Toil wait = Toils_General.Wait(job.count)
                .WithProgressBarToilDelay(TargetIndex.None);
            yield return wait;

            yield return Toils_General.Do(
                () =>
                {
                    if (birthCompleted)
                    {
                        return;
                    }

                    Hediff_LoverPregnant? pregnancy = Pregnancy;
                    if (pregnancy == null || !pregnancy.ReadyForBirth)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    Pawn? newborn = LoverPregnancyUtility.TryCompleteBirth(pregnancy);
                    if (newborn == null)
                    {
                        Log.Warning(
                            $"{LogPrefix}{pawn.LabelShort} 分娩失败，孕期保留，可再次尝试。");
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    // 先标记完成，再移除孕期，避免 RemoveHediff 触发失败条件把成功 Job 改判失败。
                    birthCompleted = true;
                    newbornForFinalizer = newborn;
                    pawn.health?.RemoveHediff(pregnancy);
                });
        }

        private bool CanContinueLabor()
        {
            if (pawn == null
                || !pawn.Spawned
                || pawn.Dead
                || pawn.Downed
                || pawn.Drafted
                || pawn.InMentalState
                || pawn.IsBurning())
            {
                return false;
            }

            Hediff_LoverPregnant? pregnancy = Pregnancy;
            return pregnancy != null && pregnancy.ReadyForBirth;
        }
    }
}
