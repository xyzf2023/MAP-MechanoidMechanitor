using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 仿生主动产程：原地等待读条，成功完成后生成新生儿并可选衔接「将婴儿带到安全处」。
    /// 中断不会移除怀孕状态，玩家可再次下达「开始分娩」。
    /// </summary>
    public sealed class JobDriver_SyntheticGiveBirth : JobDriver
    {
        private const string LogPrefix = "[MAP-机械族机械师] SyntheticPregnancy：";

        private Hediff_SyntheticPregnant? Pregnancy =>
            pawn?.health?.hediffSet?.GetFirstHediffOfDef(
                MAPMechanitor_HediffDefOf.MAP_SyntheticPregnant) as Hediff_SyntheticPregnant;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
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

                    Hediff_SyntheticPregnant? pregnancy = Pregnancy;
                    if (pregnancy == null || !pregnancy.ReadyForBirth)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    Pawn? newborn = SyntheticPregnancyUtility.TryCompleteBirth(pregnancy);
                    if (newborn == null)
                    {
                        Log.Warning(
                            $"{LogPrefix}{pawn.LabelShort} 分娩失败，孕期保留，可再次尝试。");
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    birthCompleted = true;
                    newbornForFinalizer = newborn;
                    pawn.health?.RemoveHediff(pregnancy);

                    try
                    {
                        SyntheticCompanionStateUtility.ResetPregnancyApproachToAvoid(pawn);
                    }
                    catch (Exception resetException)
                    {
                        Log.Warning(
                            $"{LogPrefix}成功生产后重置生育方式失败（出生仍有效）：" +
                            resetException);
                    }
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

            Hediff_SyntheticPregnant? pregnancy = Pregnancy;
            return pregnancy != null && pregnancy.ReadyForBirth;
        }
    }
}
