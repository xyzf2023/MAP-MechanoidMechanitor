using System;
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
        private const string NonHostileConfirmKey = "MAP_MechanoidMechanitor.MechHack.NonHostileConfirm";
        private const string ReportWithTargetKey = "MAP_MechanoidMechanitor.MechHack.ReportWithTarget";
        private const string ReportDefaultKey = "MAP_MechanoidMechanitor.MechHack.ReportDefault";

        private Faction? originalFaction;
        private bool confirmationResolved;
        private bool confirmedHostileAction;
        private Effecter? warmupEffecter;

        private Pawn? TargetPawn => job?.GetTarget(TargetIndex.A).Thing as Pawn;

        private static JobDef? MechHackJobDef =>
            DefDatabase<JobDef>.GetNamedSilentFail("MAP_Job_MechHack");

        private static HediffDef? ImmobilizeHediffDef =>
            DefDatabase<HediffDef>.GetNamedSilentFail(ImmobilizeHediffDefName);

        private static HistoryEventDef? MechHackHistoryEvent =>
            DefDatabase<HistoryEventDef>.GetNamedSilentFail(HistoryEventDefName);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref originalFaction, "originalFaction");
            Scribe_Values.Look(ref confirmationResolved, "confirmationResolved");
            Scribe_Values.Look(ref confirmedHostileAction, "confirmedHostileAction");
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
            AddFinishAction(_ => CleanupHackEffects());

            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => pawn.Downed);
            this.FailOn(() => !IsValidTargetDuringHack(requireLineOfSight: false));

            yield return Toils_General.StopDead();
            yield return MakeConfirmationToil();
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
            if (targetPawn != null)
            {
                originalFaction = targetPawn.Faction;
            }
        }

        private Toil MakeConfirmationToil()
        {
            Toil confirmToil = ToilMaker.MakeToil("MechHackConfirm");
            confirmToil.initAction = BeginConfirmation;
            confirmToil.defaultCompleteMode = ToilCompleteMode.Never;
            confirmToil.AddEndCondition(() =>
                confirmationResolved ? JobCondition.Succeeded : JobCondition.Ongoing);
            confirmToil.FailOn(() => !IsValidTargetDuringHack(requireLineOfSight: false));
            return confirmToil;
        }

        private void BeginConfirmation()
        {
            Pawn? targetPawn = TargetPawn;
            if (targetPawn == null || !IsValidTargetDuringHack(requireLineOfSight: false))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            originalFaction = targetPawn.Faction;

            if (!NeedsNonHostileConfirmation(originalFaction))
            {
                confirmationResolved = true;
                confirmedHostileAction = false;
                return;
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                NonHostileConfirmKey.Translate(),
                OnConfirmNonHostileHack,
                OnCancelNonHostileHack));
        }

        private static bool NeedsNonHostileConfirmation(Faction? faction)
        {
            return faction != null
                && faction != Faction.OfPlayer
                && !faction.HostileTo(Faction.OfPlayer);
        }

        private void OnConfirmNonHostileHack()
        {
            if (!IsActiveMechHackJob())
            {
                return;
            }

            confirmedHostileAction = true;
            confirmationResolved = true;
        }

        private void OnCancelNonHostileHack()
        {
            if (!IsActiveMechHackJob())
            {
                return;
            }

            EndJobWith(JobCondition.Incompletable);
        }

        private bool IsActiveMechHackJob()
        {
            JobDef? mechHackJobDef = MechHackJobDef;
            return mechHackJobDef != null
                && pawn.CurJob?.def == mechHackJobDef
                && pawn.jobs?.curDriver == this;
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

            if (!targetPawn.health.hediffSet.HasHediff(hediffDef))
            {
                targetPawn.health.AddHediff(HediffMaker.MakeHediff(hediffDef, targetPawn));
            }
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

            MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(pawn);

            Pawn? oldOverseer = targetPawn.GetOverseer();
            oldOverseer?.relations?.RemoveDirectRelation(
                PawnRelationDefOf.Overseer,
                targetPawn);

            targetPawn.SetFaction(Faction.OfPlayer);

            if (pawn.relations == null)
            {
                Log.Warning(
                    "[MAP-MechanoidMechanitor] Mech hack failed: Justice has no relations tracker.");
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            pawn.relations.AddDirectRelation(PawnRelationDefOf.Overseer, targetPawn);
            pawn.mechanitor?.Notify_BandwidthChanged();

            if (!VerifyHackSucceeded(targetPawn))
            {
                if (Prefs.DevMode)
                {
                    Log.Warning(
                        "[MAP-MechanoidMechanitor] Mech hack control transfer failed for " +
                        $"{targetPawn.LabelShort} ({targetPawn.kindDef?.defName ?? "unknown"}).");
                }

                EndJobWith(JobCondition.Incompletable);
                return;
            }

            ApplyDiplomaticConsequences(hackedFaction);
            StartHackCooldown(hackComp);
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
            Pawn? targetPawn = TargetPawn;
            HediffDef? hediffDef = ImmobilizeHediffDef;
            if (targetPawn?.health?.hediffSet == null || hediffDef == null)
            {
                return;
            }

            Hediff? hediff = targetPawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
            if (hediff != null)
            {
                targetPawn.health.RemoveHediff(hediff);
            }
        }
    }
}
