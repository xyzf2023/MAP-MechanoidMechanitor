using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public class JobDriver_MechHack : JobDriver
    {
        private const string WarmupEffecterDefName = "MAP_Effecter_MechReconstructionWarmupOnTarget";
        private const string ImmobilizeHediffDefName = "MAP_Hediff_MechHackImmobilized";
        private const string HistoryEventDefName = "MAP_MechHack";
        private const string ReportWithTargetKey = "MAP_MechanoidMechanitor.MechHack.ReportWithTarget";
        private const string ReportDefaultKey = "MAP_MechanoidMechanitor.MechHack.ReportDefault";

        private Faction? originalFaction;
        private bool confirmedHostileAction;
        private bool addedImmobilizeHediff;
        private Effecter? warmupEffecter;

        private Pawn? TargetPawn => job?.GetTarget(TargetIndex.A).Thing as Pawn;

        private static HediffDef? ImmobilizeHediffDef =>
            DefDatabase<HediffDef>.GetNamedSilentFail(ImmobilizeHediffDefName);

        private static HistoryEventDef? MechHackHistoryEvent =>
            DefDatabase<HistoryEventDef>.GetNamedSilentFail(HistoryEventDefName);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref originalFaction, "originalFaction");
            Scribe_Values.Look(ref confirmedHostileAction, "confirmedHostileAction");
            Scribe_Values.Look(ref addedImmobilizeHediff, "addedImmobilizeHediff");
        }

        public override string GetReport()
        {
            Pawn? targetPawn = TargetPawn;
            return targetPawn != null
                ? ReportWithTargetKey.Translate(targetPawn.LabelCap)
                : ReportDefaultKey.Translate();
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(_ => CleanupHackEffects());

            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => pawn.Downed);
            this.FailOn(() => !IsValidTargetDuringHack(requireLineOfSight: false));

            yield return Toils_General.StopDead();
            yield return Toils_General.Do(ApplyImmobilizeHediff);

            CompAbilityEffect_MechHack? hackComp = GetHackComp();
            int durationTicks = Mathf.Max(
                0,
                hackComp?.Props.hackDurationTicks ?? 900);

            Toil hackWait = Toils_General.Wait(durationTicks, TargetIndex.A);
            hackWait.FailOnDespawnedOrNull(TargetIndex.A);
            hackWait.FailOn(() => !IsValidTargetDuringHack(requireLineOfSight: true));
            hackWait.WithProgressBarToilDelay(TargetIndex.A);
            hackWait.AddPreInitAction(StartWarmupEffecter);
            hackWait.AddPreTickAction(MaintainWarmupEffecter);
            yield return hackWait;

            yield return Toils_General.Do(CompleteHack);
        }

        public override void Notify_Starting()
        {
            base.Notify_Starting();

            Pawn? targetPawn = TargetPawn;
            originalFaction = targetPawn?.Faction;

            confirmedHostileAction =
                originalFaction != null
                && originalFaction != Faction.OfPlayer
                && !originalFaction.HostileTo(Faction.OfPlayer);
        }

        private bool IsValidTargetDuringHack(bool requireLineOfSight)
        {
            Pawn? targetPawn = TargetPawn;
            CompAbilityEffect_MechHack? hackComp = GetHackComp();

            if (targetPawn == null
                || !targetPawn.Spawned
                || targetPawn.Dead
                || pawn.Map == null
                || targetPawn.Map != pawn.Map
                || hackComp == null
                || !hackComp.CanHack(targetPawn, pawn))
            {
                return false;
            }

            if (targetPawn.Faction != originalFaction)
            {
                return false;
            }

            if (requireLineOfSight
                && !GenSight.LineOfSight(pawn.Position, targetPawn.Position, pawn.Map))
            {
                return false;
            }

            return true;
        }

        private void ApplyImmobilizeHediff()
        {
            addedImmobilizeHediff = false;

            if (!IsValidTargetDuringHack(requireLineOfSight: true))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            Pawn? targetPawn = TargetPawn;
            HediffDef? hediffDef = ImmobilizeHediffDef;
            if (targetPawn?.health?.hediffSet == null || hediffDef == null)
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            if (targetPawn.health.hediffSet.HasHediff(hediffDef))
            {
                return;
            }

            Hediff hediff = HediffMaker.MakeHediff(hediffDef, targetPawn);
            targetPawn.health.AddHediff(hediff);
            addedImmobilizeHediff = true;
        }

        private void StartWarmupEffecter()
        {
            if (!IsValidTargetDuringHack(requireLineOfSight: true))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            Pawn? targetPawn = TargetPawn;
            EffecterDef? warmupEffecterDef =
                DefDatabase<EffecterDef>.GetNamedSilentFail(WarmupEffecterDefName);

            if (targetPawn == null || !targetPawn.Spawned || warmupEffecterDef == null)
            {
                return;
            }

            warmupEffecter = warmupEffecterDef.SpawnAttached(targetPawn, targetPawn.MapHeld, 1f);
            warmupEffecter.Trigger(targetPawn, targetPawn, -1);
        }

        private void MaintainWarmupEffecter()
        {
            if (!IsValidTargetDuringHack(requireLineOfSight: true))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            Pawn? targetPawn = TargetPawn;
            if (warmupEffecter != null && targetPawn != null && targetPawn.Spawned)
            {
                warmupEffecter.EffectTick(targetPawn, targetPawn);
            }
        }

        private void CompleteHack()
        {
            CleanupWarmupEffecter();

            Pawn? targetPawn = TargetPawn;
            CompAbilityEffect_MechHack? hackComp = GetHackComp();

            if (targetPawn == null
                || hackComp == null
                || !IsValidTargetDuringHack(requireLineOfSight: true))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            Faction? hackedFaction = originalFaction;
            Pawn? oldOverseer = targetPawn.GetOverseer();

            MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(pawn);

            if (pawn.relations == null
                || pawn.mechanitor == null
                || targetPawn.OverseerSubject == null
                || Faction.OfPlayer == null)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 骇入中止：正义无法建立监管者关系。");
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            oldOverseer?.relations?.RemoveDirectRelation(
                PawnRelationDefOf.Overseer,
                targetPawn);

            targetPawn.SetFaction(Faction.OfPlayer);
            pawn.relations.AddDirectRelation(PawnRelationDefOf.Overseer, targetPawn);
            pawn.mechanitor.Notify_BandwidthChanged();

            if (!VerifyHackSucceeded(targetPawn))
            {
                Log.Warning(
                    "[MAP-机械族机械师] 骇入控制权转移失败：" +
                    $"{targetPawn.LabelShort}（{targetPawn.kindDef?.defName ?? "unknown"}），正在回滚。");

                RollbackFailedHack(targetPawn, hackedFaction, oldOverseer);
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            ApplyDiplomaticConsequences(hackedFaction);
            StartHackCooldown(hackComp);
        }

        private void RollbackFailedHack(
            Pawn targetPawn,
            Faction? originalTargetFaction,
            Pawn? oldOverseer)
        {
            if (targetPawn == null)
            {
                return;
            }

            pawn.relations?.TryRemoveDirectRelation(
                PawnRelationDefOf.Overseer,
                targetPawn);

            targetPawn.SetFaction(originalTargetFaction);

            if (oldOverseer != null
                && !oldOverseer.Dead
                && oldOverseer.relations != null)
            {
                oldOverseer.relations.AddDirectRelation(
                    PawnRelationDefOf.Overseer,
                    targetPawn);
            }

            pawn.mechanitor?.Notify_BandwidthChanged();
            oldOverseer?.mechanitor?.Notify_BandwidthChanged();
        }

        private bool VerifyHackSucceeded(Pawn targetPawn)
        {
            return targetPawn.Faction == Faction.OfPlayer
                && targetPawn.GetOverseer() == pawn;
        }

        private void ApplyDiplomaticConsequences(Faction? hackedFaction)
        {
            if (!confirmedHostileAction
                || hackedFaction == null
                || hackedFaction == Faction.OfPlayer
                || hackedFaction.HostileTo(Faction.OfPlayer))
            {
                return;
            }

            HistoryEventDef? historyEvent = MechHackHistoryEvent;
            string reason = historyEvent?.label ?? "骇入机械体";

            if (hackedFaction.HasGoodwill)
            {
                Faction.OfPlayer.TryAffectGoodwillWith(
                    hackedFaction,
                    Faction.OfPlayer.GoodwillToMakeHostile(hackedFaction),
                    canSendMessage: true,
                    canSendHostilityLetter: true,
                    historyEvent,
                    null);
            }
            else
            {
                hackedFaction.SetRelationDirect(
                    Faction.OfPlayer,
                    FactionRelationKind.Hostile,
                    canSendHostilityLetter: true,
                    reason,
                    null);
            }
        }

        private void StartHackCooldown(CompAbilityEffect_MechHack hackComp)
        {
            job.ability?.StartCooldown(hackComp.Props.cooldownTicks);
        }

        private CompAbilityEffect_MechHack? GetHackComp()
        {
            Ability? ability = job?.ability;
            if (ability?.comps == null)
            {
                return null;
            }

            foreach (AbilityComp comp in ability.comps)
            {
                if (comp is CompAbilityEffect_MechHack hackComp)
                {
                    return hackComp;
                }
            }

            return null;
        }

        private void CleanupHackEffects()
        {
            CleanupWarmupEffecter();
            RemoveImmobilizeHediff();
        }

        private void CleanupWarmupEffecter()
        {
            warmupEffecter?.Cleanup();
            warmupEffecter = null;
        }

        private void RemoveImmobilizeHediff()
        {
            if (!addedImmobilizeHediff)
            {
                return;
            }

            Pawn? targetPawn = TargetPawn;
            HediffDef? hediffDef = ImmobilizeHediffDef;
            if (targetPawn?.health?.hediffSet == null || hediffDef == null)
            {
                addedImmobilizeHediff = false;
                return;
            }

            Hediff? hediff = targetPawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
            if (hediff != null)
            {
                targetPawn.health.RemoveHediff(hediff);
            }

            addedImmobilizeHediff = false;
        }
    }
}
