using System.Collections.Generic;
using UnityEngine;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族机械师专用的手动自我修复 JobDriver。
    /// 独立于原版 <see cref="JobDriver_RepairMech"/>，原地执行，不预约、不寻路、不面向自己。
    /// </summary>
    public class JobDriver_MechanoidMechanitorSelfRepair : JobDriver
    {
        private const int DefaultTicksPerHeal = 120;

        private Pawn Target => job.targetA.Pawn;

        /// <summary>
        /// 每执行一次 <see cref="MechRepairUtility.RepairTick(Pawn)"/> 所需的 tick 数。
        /// 直接复用 <see cref="StatDefOf.MechRepairSpeed"/>，使其继续受制作技能等 Stat 修正影响。
        /// </summary>
        private int TicksPerHeal
            => Mathf.Max(1, Mathf.RoundToInt(DefaultTicksPerHeal / pawn.GetStatValue(StatDefOf.MechRepairSpeed)));

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 维修者与维修目标是同一个 Pawn，不预约自己，也不对任何目标做预约。
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            if (!ModsConfig.BiotechActive)
            {
                yield break;
            }

            if (pawn == null || pawn.Dead || pawn.Destroyed)
            {
                yield break;
            }

            Pawn target = Target;
            if (target == null || target != pawn)
            {
                yield break;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                yield break;
            }

            if (pawn.health == null || pawn.health.hediffSet == null)
            {
                yield break;
            }

            AddFailCondition(() =>
                pawn == null
                || pawn.Dead
                || pawn.Destroyed
                || !MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn)
                || job.targetA.Pawn != pawn);

            Toil wait = Toils_General.Wait(int.MaxValue);
            wait.WithEffect(EffecterDefOf.MechRepairing, TargetIndex.A);
            wait.PlaySustainerOrSound(SoundDefOf.RepairMech_Touch);
            wait.AddPreInitAction(() => ticksToNextRepair = TicksPerHeal);
            wait.tickIntervalAction = delegate(int delta)
            {
                ticksToNextRepair -= delta;
                if (ticksToNextRepair <= 0)
                {
                    if (pawn.needs != null && pawn.needs.energy != null)
                    {
                        pawn.needs.energy.CurLevel
                            -= pawn.GetStatValue(StatDefOf.MechEnergyLossPerHP);
                    }

                    MechRepairUtility.RepairTick(pawn);
                    ticksToNextRepair = TicksPerHeal;
                }

                if (pawn.skills != null)
                {
                    pawn.skills.Learn(SkillDefOf.Crafting, 0.05f * delta);
                }
            };
            wait.AddEndCondition(() =>
                MechRepairUtility.CanRepair(pawn) ? JobCondition.Ongoing : JobCondition.Succeeded);
            wait.activeSkill = () => SkillDefOf.Crafting;
            yield return wait;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksToNextRepair, "ticksToNextRepair", 0);
        }

        private int ticksToNextRepair;
    }
}
