using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 右键选中机械族机械师并点击其自身时，提供“进行自我修复”的手动 Job 选项。
    /// 仅由本 MOD 注册的机械族机械师执行，且只在 <see cref="MechRepairUtility.CanRepair(Pawn)"/> 时显示。
    /// </summary>
    public class FloatMenuOptionProvider_MechanoidMechanitorSelfRepair : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool MechanoidCanDo => true;
        protected override bool RequiresManipulation => true;
        protected override bool CanSelfTarget => true;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            return ModsConfig.BiotechActive && context.FirstSelectedPawn != null;
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn pawn = context.FirstSelectedPawn;
            if (pawn == null)
            {
                yield break;
            }

            if (clickedPawn != pawn)
            {
                yield break;
            }

            if (!pawn.Spawned || pawn.Dead || pawn.Destroyed)
            {
                yield break;
            }

            if (!pawn.RaceProps.IsMechanoid)
            {
                yield break;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                yield break;
            }

            if (pawn.jobs == null || pawn.health == null)
            {
                yield break;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                yield break;
            }

            if (!MechRepairUtility.CanRepair(pawn))
            {
                yield break;
            }

            if (pawn.jobs.curJob != null
                && pawn.jobs.curJob.def == MAPMechanitor_JobDefOf.MAP_MechanoidMechanitorSelfRepair)
            {
                yield break;
            }

            yield return new FloatMenuOption(
                "进行自我修复",
                () =>
                {
                    Job job = JobMaker.MakeJob(
                        MAPMechanitor_JobDefOf.MAP_MechanoidMechanitorSelfRepair,
                        pawn);
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                });
        }
    }
}
