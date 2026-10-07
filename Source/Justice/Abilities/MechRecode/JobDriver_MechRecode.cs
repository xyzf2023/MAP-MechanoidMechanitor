using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public class JobDriver_MechRecode : JobDriver
    {
        private const float DefaultRecodeTicksPerBandwidth = 300f;
        private const string InvalidTargetMessageKey = "MAP_MechanoidMechanitor.Justice.Ability.MechRecode.InvalidTarget";
        private const string SuccessMessageKey = "MAP_MechanoidMechanitor.Justice.Ability.MechRecode.Success";

        private Corpse? TargetCorpse => job?.GetTarget(TargetIndex.A).Thing as Corpse;

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
            Toil gotoTarget = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            gotoTarget.FailOnDespawnedOrNull(TargetIndex.A);
            gotoTarget.FailOn(() => !IsValidTarget());
            yield return gotoTarget;

            CompAbilityEffect_MechRecode? recodeComp = GetRecodeComp();
            Corpse? corpse = TargetCorpse;
            float bandwidthCost = corpse?.InnerPawn?.GetStatValue(StatDefOf.BandwidthCost) ?? 0f;
            float recodeTicksPerBandwidth = recodeComp?.Props.recodeTicksPerBandwidth
                ?? DefaultRecodeTicksPerBandwidth;
            int durationTicks = Mathf.RoundToInt(
                bandwidthCost * recodeTicksPerBandwidth);

            Toil recode = Toils_General.Wait(
                Mathf.Max(0, durationTicks),
                TargetIndex.A);
            recode.FailOnDespawnedOrNull(TargetIndex.A);
            recode.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            recode.FailOn(() => !IsValidTarget());
            recode.WithProgressBarToilDelay(TargetIndex.A);
            yield return recode;

            yield return Toils_General.Do(CompleteRecode);
        }

        private bool IsValidTarget()
        {
            Corpse? corpse = TargetCorpse;
            CompAbilityEffect_MechRecode? recodeComp = GetRecodeComp();

            return corpse != null
                && !corpse.Destroyed
                && corpse.Spawned
                && corpse.Map == pawn.Map
                && recodeComp != null
                && recodeComp.CanRecode(corpse);
        }

        private CompAbilityEffect_MechRecode? GetRecodeComp()
        {
            Ability? ability = job?.ability;
            if (ability?.comps == null)
            {
                return null;
            }

            foreach (AbilityComp comp in ability.comps)
            {
                if (comp is CompAbilityEffect_MechRecode recodeComp)
                {
                    return recodeComp;
                }
            }

            return null;
        }

        private void CompleteRecode()
        {
            Corpse? corpse = TargetCorpse;
            CompAbilityEffect_MechRecode? recodeComp = GetRecodeComp();
            Faction? player = Faction.OfPlayerSilentFail;

            string rejectionMessageKey = InvalidTargetMessageKey;
            if (player == null
                || corpse == null
                || recodeComp == null
                || !corpse.Spawned
                || corpse.Map != pawn.Map
                || !recodeComp.CanRecode(corpse, out rejectionMessageKey))
            {
                Messages.Message(
                    rejectionMessageKey.Translate(),
                    pawn,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            Pawn innerPawn = corpse.InnerPawn;
            float bandwidthCost = innerPawn.GetStatValue(StatDefOf.BandwidthCost);
            int cooldownTicks = Mathf.RoundToInt(
                bandwidthCost * recodeComp.Props.cooldownTicksPerBandwidth);

            // 尸体内 Pawn 也通过原版入口完成归属变更、任务信号及关系通知；此处不复活。
            innerPawn.SetFaction(player);
            job.ability?.StartCooldown(cooldownTicks);

            Messages.Message(
                SuccessMessageKey.Translate(innerPawn.LabelShortCap),
                corpse,
                MessageTypeDefOf.PositiveEvent,
                historical: false);
        }
    }
}
