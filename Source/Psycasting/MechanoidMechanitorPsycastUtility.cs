using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public class Hediff_MechanoidMechanitorPsychicReceiver : Hediff
    {
        public override bool Visible => false;
    }

    public static class MechanoidMechanitorPsycastUtility
    {
        public const string PsychicReceiverHediffDefName =
            "MAP_MechanoidMechanitor_PsychicReceiver";

        private static HediffDef? psychicReceiverHediffDef;

        public static bool HasPsycastingCapability(Pawn? pawn)
        {
            return MechanoidMechanitorCapabilityUtility.HasCapability(
                pawn,
                MechanoidMechanitorCapability.Psycasting);
        }

        public static bool CanMaintainPsycastInfrastructure(Pawn? pawn)
        {
            return ModsConfig.RoyaltyActive
                && pawn != null
                && !pawn.Destroyed
                && !pawn.Dead
                && pawn.RaceProps.IsMechanoid
                && pawn.health?.hediffSet != null
                && HasPsycastingCapability(pawn);
        }

        public static void EnsurePsycastInfrastructure(Pawn? pawn)
        {
            if (pawn?.health?.hediffSet == null || pawn.Destroyed)
            {
                return;
            }

            SyncPsychicReceiver(pawn);
            if (!CanMaintainPsycastInfrastructure(pawn))
            {
                return;
            }

            pawn.abilities ??= new Pawn_AbilityTracker(pawn);
            pawn.skills ??= new Pawn_SkillTracker(pawn);

            if (pawn.psychicEntropy != null)
            {
                return;
            }

            pawn.psychicEntropy = new Pawn_PsychicEntropyTracker(pawn);
            pawn.psychicEntropy.SetInitialPsyfocusLevel();
            if (pawn.HasPsylink)
            {
                pawn.psychicEntropy.Notify_GainedPsylink();
            }
        }

        public static void SyncPsychicReceiver(Pawn? pawn)
        {
            if (pawn?.health?.hediffSet == null || pawn.Destroyed)
            {
                return;
            }

            HediffDef? receiverDef = GetPsychicReceiverHediffDef();
            if (receiverDef == null)
            {
                return;
            }

            bool shouldHave = ModsConfig.RoyaltyActive
                && !pawn.Dead
                && pawn.RaceProps.IsMechanoid
                && HasPsycastingCapability(pawn)
                && pawn.def.GetStatValueAbstract(StatDefOf.PsychicSensitivity)
                    <= float.Epsilon;

            Hediff? existing = pawn.health.hediffSet.GetFirstHediffOfDef(receiverDef);
            if (shouldHave)
            {
                if (existing == null)
                {
                    pawn.health.AddHediff(receiverDef);
                }

                return;
            }

            while (existing != null)
            {
                pawn.health.RemoveHediff(existing);
                existing = pawn.health.hediffSet.GetFirstHediffOfDef(receiverDef);
            }
        }

        public static bool IsPsylinkNeuroformer(Thing? item)
        {
            if (!ModsConfig.RoyaltyActive || item is not ThingWithComps thingWithComps)
            {
                return false;
            }

            CompUseEffect_InstallImplant? installComp =
                thingWithComps.TryGetComp<CompUseEffect_InstallImplant>();
            return installComp?.Props.hediffDef == HediffDefOf.PsychicAmplifier;
        }

        public static bool IsPsytrainer(Thing? item)
        {
            if (!ModsConfig.RoyaltyActive || item is not ThingWithComps thingWithComps)
            {
                return false;
            }

            CompUseEffect_GainAbility? gainAbilityComp =
                thingWithComps.TryGetComp<CompUseEffect_GainAbility>();
            return gainAbilityComp?.Props.ability?.IsPsycast == true;
        }

        public static bool IsSupportedPsycastConsumable(Thing? item)
        {
            return IsPsylinkNeuroformer(item) || IsPsytrainer(item);
        }

        public static bool CanUsePsycastConsumable(Pawn? pawn, Thing? item)
        {
            if (!ModsConfig.RoyaltyActive
                || pawn == null
                || pawn.Destroyed
                || pawn.Dead
                || pawn.Downed
                || pawn.Deathresting
                || pawn.Suspended
                || pawn.IsSelfShutdown()
                || pawn.jobs == null
                || !pawn.RaceProps.IsMechanoid
                || pawn.Faction == null
                || !pawn.Faction.IsPlayerSafe()
                || pawn.IsPrisoner
                || pawn.HostFaction != null
                || pawn.health?.hediffSet == null
                || !HasPsycastingCapability(pawn))
            {
                return false;
            }

            return IsSupportedPsycastConsumable(item);
        }

        public static bool CanBeMeditationSpotCandidate(Pawn? pawn, Map? map)
        {
            return ModsConfig.RoyaltyActive
                && pawn != null
                && map != null
                && !pawn.Destroyed
                && !pawn.Dead
                && pawn.Spawned
                && pawn.Map == map
                && pawn.RaceProps.IsMechanoid
                && pawn.Faction != null
                && pawn.Faction.IsPlayerSafe()
                && !pawn.IsPrisoner
                && pawn.HostFaction == null
                && HasPsycastingCapability(pawn);
        }

        public static bool ShouldAutoMeditate(Pawn? pawn, out Building? meditationSpot)
        {
            meditationSpot = null;
            if (!CanMaintainPsycastInfrastructure(pawn)
                || pawn == null
                || !pawn.Spawned
                || pawn.Map == null
                || pawn.Faction == null
                || !pawn.Faction.IsPlayerSafe()
                || pawn.Downed
                || pawn.Drafted
                || pawn.Suspended
                || pawn.IsSelfShutdown()
                || pawn.jobs == null
                || !pawn.HasPsylink
                || pawn.psychicEntropy == null
                || pawn.psychicEntropy.PsychicSensitivity <= float.Epsilon)
            {
                return false;
            }

            if (MechanoidMechanitorSelfWorkModeUtility.TryGetCurrentMode(
                    pawn,
                    out MechWorkModeDef? mode)
                && (MechanoidMechanitorSelfWorkModeUtility.IsRechargeMode(mode)
                    || MechanoidMechanitorSelfWorkModeUtility.IsSelfShutdownMode(mode)))
            {
                return false;
            }

            float target = Mathf.Min(pawn.psychicEntropy.TargetPsyfocus, 0.95f);
            if (pawn.psychicEntropy.CurrentPsyfocus >= target)
            {
                return false;
            }

            Building? assignedSpot = pawn.ownership?.AssignedMeditationSpot;
            if (!IsValidAssignedMeditationSpot(pawn, assignedSpot))
            {
                return false;
            }

            if (!MeditationUtility.CanMeditateNow(pawn)
                || !MeditationUtility.SafeEnvironmentalConditions(
                    pawn,
                    assignedSpot!.Position,
                    assignedSpot.Map))
            {
                return false;
            }

            meditationSpot = assignedSpot;
            return true;
        }

        public static Job? TryMakeAssignedMeditationJob(Pawn pawn)
        {
            if (!ShouldAutoMeditate(pawn, out Building? meditationSpot)
                || meditationSpot == null)
            {
                return null;
            }

            LocalTargetInfo focus = MeditationUtility.BestFocusAt(meditationSpot, pawn);
            Job job = JobMaker.MakeJob(
                JobDefOf.Meditate,
                meditationSpot,
                null,
                focus);
            job.ignoreJoyTimeAssignment = true;
            return job;
        }

        public static JobCondition GetAssignedMeditationEndCondition(
            JobDriver_Meditate driver)
        {
            Pawn? pawn = driver?.pawn;
            Job? job = driver?.job;
            if (pawn == null
                || job == null
                || !HasPsycastingCapability(pawn)
                || job.def != JobDefOf.Meditate)
            {
                return JobCondition.Ongoing;
            }

            Building? jobSpot = job.GetTarget(TargetIndex.A).Thing as Building;
            Building? assignedSpot = pawn.ownership?.AssignedMeditationSpot;
            if (jobSpot == null
                || assignedSpot != jobSpot
                || jobSpot.GetAssignedPawn() != pawn)
            {
                return JobCondition.Incompletable;
            }

            if (!IsValidAssignedMeditationSpot(pawn, jobSpot)
                || pawn.Drafted
                || pawn.Downed
                || pawn.Suspended
                || pawn.IsSelfShutdown())
            {
                return JobCondition.Incompletable;
            }

            if (MechanoidMechanitorSelfWorkModeUtility.TryGetCurrentMode(
                    pawn,
                    out MechWorkModeDef? mode)
                && (MechanoidMechanitorSelfWorkModeUtility.IsRechargeMode(mode)
                    || MechanoidMechanitorSelfWorkModeUtility.IsSelfShutdownMode(mode)))
            {
                return JobCondition.Incompletable;
            }

            Pawn_PsychicEntropyTracker? tracker = pawn.psychicEntropy;
            if (tracker == null
                || !pawn.HasPsylink
                || tracker.PsychicSensitivity <= float.Epsilon)
            {
                return JobCondition.Incompletable;
            }

            float target = Mathf.Min(tracker.TargetPsyfocus, 0.95f);
            return tracker.CurrentPsyfocus >= target
                ? JobCondition.Succeeded
                : JobCondition.Ongoing;
        }

        private static bool IsValidAssignedMeditationSpot(
            Pawn pawn,
            Building? meditationSpot)
        {
            if (meditationSpot == null
                || meditationSpot.Destroyed
                || !meditationSpot.Spawned
                || meditationSpot.Map != pawn.Map
                || meditationSpot.def != ThingDefOf.MeditationSpot
                || meditationSpot.GetAssignedPawn() != pawn
                || meditationSpot.IsForbidden(pawn)
                || !meditationSpot.Position.Standable(meditationSpot.Map))
            {
                return false;
            }

            return MeditationUtility.IsValidMeditationBuildingForPawn(
                meditationSpot,
                pawn);
        }

        private static HediffDef? GetPsychicReceiverHediffDef()
        {
            psychicReceiverHediffDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(
                    PsychicReceiverHediffDefName);
            return psychicReceiverHediffDef;
        }
    }

    [HarmonyPatch(
        typeof(MechanoidMechanitorCapabilityUtility),
        nameof(MechanoidMechanitorCapabilityUtility.GetCapabilities))]
    public static class Patch_MechanoidMechanitorCapabilityUtility_GetCapabilities_Psycasting
    {
        [HarmonyPostfix]
        public static void Postfix(
            Pawn? pawn,
            ref MechanoidMechanitorCapability __result)
        {
            if (pawn != null
                && GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(
                    pawn,
                    out _))
            {
                __result |= MechanoidMechanitorCapability.Psycasting;
            }
        }
    }
}
