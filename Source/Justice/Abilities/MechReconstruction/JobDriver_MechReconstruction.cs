using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public class JobDriver_MechReconstruction : JobDriver
    {
        private const int DefaultReconstructionDurationTicks = 900;
        private const string WarmupEffecterDefName = "MAP_Effecter_MechReconstructionWarmupOnTarget";
        private const string InvalidTargetMessageKey = "MAP_MechanoidMechanitor.MechReconstruction.InvalidTarget";
        private const string ReportWithTargetKey = "MAP_MechanoidMechanitor.MechReconstruction.ReportWithTarget";
        private const string ReportDefaultKey = "MAP_MechanoidMechanitor.MechReconstruction.ReportDefault";

        private Effecter? warmupEffecter;

        private Corpse? TargetCorpse => job?.GetTarget(TargetIndex.A).Thing as Corpse;

        public override string GetReport()
        {
            Corpse? corpse = TargetCorpse;
            return corpse != null
                ? ReportWithTargetKey.Translate(corpse.LabelCap)
                : ReportDefaultKey.Translate();
        }

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
            AddFinishAction(_ => CleanupWarmupEffecter());

            Toil gotoTarget = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            gotoTarget.FailOnDespawnedOrNull(TargetIndex.A);
            gotoTarget.FailOn(() => !IsValidTarget());
            yield return gotoTarget;

            CompAbilityEffect_MechReconstruction? reconstructionComp = GetReconstructionComp();
            int durationTicks = Mathf.Max(
                0,
                reconstructionComp?.Props.reconstructionDurationTicks
                    ?? DefaultReconstructionDurationTicks);

            Toil reconstruct = Toils_General.Wait(durationTicks, TargetIndex.A);
            reconstruct.initAction = StartWarmupEffecter;
            reconstruct.tickAction = MaintainWarmupEffecter;
            reconstruct.FailOnDespawnedOrNull(TargetIndex.A);
            reconstruct.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            reconstruct.FailOn(() => !IsValidTarget());
            reconstruct.WithProgressBarToilDelay(TargetIndex.A);
            yield return reconstruct;

            yield return Toils_General.Do(CompleteReconstruction);
        }

        private bool IsValidTarget()
        {
            Corpse? corpse = TargetCorpse;
            CompAbilityEffect_MechReconstruction? reconstructionComp = GetReconstructionComp();

            return corpse != null
                && !corpse.Destroyed
                && corpse.Spawned
                && corpse.Map == pawn.Map
                && reconstructionComp != null
                && reconstructionComp.CanReconstruct(corpse);
        }

        private CompAbilityEffect_MechReconstruction? GetReconstructionComp()
        {
            Ability? ability = job?.ability;
            if (ability?.comps == null)
            {
                return null;
            }

            foreach (AbilityComp comp in ability.comps)
            {
                if (comp is CompAbilityEffect_MechReconstruction reconstructionComp)
                {
                    return reconstructionComp;
                }
            }

            return null;
        }

        private void StartWarmupEffecter()
        {
            if (!IsValidTarget())
            {
                RejectAndEnd();
                return;
            }

            Corpse? corpse = TargetCorpse;
            EffecterDef? warmupEffecterDef =
                DefDatabase<EffecterDef>.GetNamedSilentFail(WarmupEffecterDefName);

            if (corpse == null || !corpse.Spawned || warmupEffecterDef == null)
            {
                return;
            }

            warmupEffecter = warmupEffecterDef.SpawnAttached(corpse, corpse.MapHeld, 1f);
            warmupEffecter.Trigger(corpse, corpse, -1);
        }

        private void MaintainWarmupEffecter()
        {
            if (!IsValidTarget())
            {
                RejectAndEnd();
                return;
            }

            Corpse? corpse = TargetCorpse;
            if (warmupEffecter != null && corpse != null && corpse.Spawned)
            {
                warmupEffecter.EffectTick(corpse, corpse);
            }
        }

        private void CompleteReconstruction()
        {
            CleanupWarmupEffecter();

            Corpse? corpse = TargetCorpse;
            CompAbilityEffect_MechReconstruction? reconstructionComp = GetReconstructionComp();

            if (corpse == null
                || reconstructionComp == null
                || !corpse.Spawned
                || corpse.Map != pawn.Map
                || !reconstructionComp.CanReconstruct(corpse))
            {
                RejectAndEnd();
                return;
            }

            Pawn innerPawn = corpse.InnerPawn;
            bool resurrected = ResurrectionUtility.TryResurrect(innerPawn, null);
            if (!resurrected || !innerPawn.Spawned)
            {
                RejectAndEnd();
                return;
            }

            if (innerPawn.needs?.energy != null)
            {
                innerPawn.needs.energy.CurLevel = innerPawn.needs.energy.MaxLevel * 0.5f;
            }

            innerPawn.health.RemoveAllHediffs();

            if (innerPawn.RaceProps.IsMechanoid && MechRepairUtility.IsMissingWeapon(innerPawn))
            {
                MechRepairUtility.GenerateWeapon(innerPawn);
            }

            if (reconstructionComp.Props.appliedEffecterDef != null)
            {
                Effecter appliedEffecter = reconstructionComp.Props.appliedEffecterDef.SpawnAttached(
                    innerPawn,
                    innerPawn.MapHeld,
                    1f);
                appliedEffecter.Trigger(innerPawn, innerPawn, -1);
                appliedEffecter.Cleanup();
            }

            innerPawn.stances.stagger.StaggerFor(60, 0.17f);
            innerPawn.GenerateNecessaryName();
            AssignToJustice(innerPawn);

            Ability? ability = job.ability;
            if (ability != null)
            {
                ability.StartCooldown(ability.def.cooldownTicksRange.RandomInRange);
            }
        }

        private void AssignToJustice(Pawn reconstructedMech)
        {
            MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(pawn);

            if (reconstructedMech.Faction != Faction.OfPlayer)
            {
                reconstructedMech.SetFaction(Faction.OfPlayer, null);
            }

            if (pawn.mechanitor == null
                || pawn.relations == null
                || reconstructedMech.OverseerSubject == null)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 机械重构成功，但正义无法监管 " +
                    $"{reconstructedMech.LabelShort}（{reconstructedMech.kindDef?.defName ?? "unknown"}）。");
                return;
            }

            // 由统一工具负责旧监管者清理、关系写入、控制组分配与带宽刷新。
            // 若本正义已是其监管者，工具为幂等操作（不会重复添加关系）。
            if (!MAPOverseerAssignmentUtility.TryAssignActualOverseer(pawn, reconstructedMech))
            {
                Log.Warning(
                    "[MAP-机械族机械师] 机械重构成功，但分配监管者失败 " +
                    $"{reconstructedMech.LabelShort}（{reconstructedMech.kindDef?.defName ?? "unknown"}）。");
            }
        }

        private void RejectAndEnd()
        {
            Messages.Message(
                InvalidTargetMessageKey.Translate(),
                pawn,
                MessageTypeDefOf.RejectInput,
                historical: false);
            EndJobWith(JobCondition.Incompletable);
        }

        private void CleanupWarmupEffecter()
        {
            warmupEffecter?.Cleanup();
            warmupEffecter = null;
        }
    }
}
