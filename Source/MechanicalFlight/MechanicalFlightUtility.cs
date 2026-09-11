using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    [StaticConstructorOnStartup]
    public static class MechanicalFlightUtility
    {
        private static readonly Texture2D FlightIcon =
            ContentFinder<Texture2D>.Get("UI/Commands/MechanicalFlight", false)
            ?? TexCommand.Install;

        public static bool IsAirborne(Pawn? pawn)
        {
            return GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                && record != null && record.IsRuntimeActive;
        }

        public static bool IsActivelyFlying(Pawn? pawn)
        {
            return GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                && record?.ConsumesFlightEnergy == true;
        }

        public static bool UsesAerialMovement(Pawn? pawn)
        {
            return GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                && record?.UsesAerialMovement == true;
        }

        public static bool HasHoverVisual(Pawn? pawn)
        {
            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                || record == null || pawn?.flight == null)
            {
                return false;
            }

            return record.UsesAerialMovement
                || ((record.Phase == MechanicalFlightPhase.Landing
                        || record.Phase == MechanicalFlightPhase.EmergencyLanding)
                    && pawn.flight.PositionOffsetFactor > 0f);
        }

        public static bool CanEverFlyWithAuthorization(Pawn? pawn)
        {
            if (!GameComponent_MechanicalFlightRegistry.IsAuthorized(pawn) || pawn == null)
            {
                return false;
            }

            if (pawn.IsMutant && pawn.mutant?.Def?.disableFlying == true)
            {
                return false;
            }

            if (!pawn.RaceProps.canFlyInVacuum && pawn.MapHeld?.Biome?.inVacuum == true)
            {
                return false;
            }
            return true;
        }

        public static Command_Action MakeCommand(Pawn pawn)
        {
            bool active = IsActivelyFlying(pawn);
            Command_Action command = new()
            {
                defaultLabel = active
                    ? "MAP_MechanicalFlight_LandLabel".Translate()
                    : "MAP_MechanicalFlight_TakeoffLabel".Translate(),
                defaultDesc = active
                    ? "MAP_MechanicalFlight_LandDesc".Translate()
                    : "MAP_MechanicalFlight_TakeoffDesc".Translate(),
                icon = FlightIcon,
                action = () => ToggleFlight(pawn)
            };

            string? reason = GetDisabledReason(pawn);
            if (!reason.NullOrEmpty())
            {
                command.Disable(reason);
            }
            return command;
        }

        public static bool TryStartAerialMove(Pawn? pawn, IntVec3 cell)
        {
            if (pawn?.Map == null || !IsActivelyFlying(pawn) || !cell.InBounds(pawn.Map))
            {
                return false;
            }

            Job job = JobMaker.MakeJob(JobDefOf.Goto, cell);
            job.locomotionUrgency = LocomotionUrgency.Sprint;
            job.expiryInterval = -1;
            job.flying = true;
            return pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public static bool TryBeginTakeoff(Pawn? pawn)
        {
            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                || record == null || pawn == null || !CanStartFlight(pawn, record))
            {
                return false;
            }

            MechanicalFlightProfileDef profile = record.Profile!;
            if (profile.breakThinRoofOnTakeoff)
            {
                MechanicalFlightRoofUtility.BreakThinRoofArea(pawn, profile);
            }

            pawn.flight!.StartFlying();
            if (!pawn.flight.Flying)
            {
                return false;
            }

            record.Phase = MechanicalFlightPhase.TakingOff;
            record.EmergencyLandingTarget = IntVec3.Invalid;
            record.LowEnergyWarningSent = false;
            record.TicksUntilNextEnergyDrain =
                Mathf.Max(1, profile.energyDrainIntervalTicks);
            if (pawn.CurJob != null)
            {
                pawn.CurJob.flying = true;
            }

            MechanicalFlightEnergyUtility.TryConsumeMaximumEnergyFraction(
                pawn, profile.energyDrainFraction);
            GameComponent_MechanicalFlightRegistry.NotifyRuntimeStateChanged(record);
            MechanicalFlightPresentationUtility.NotifyFlightStarted(pawn, record);
            return true;
        }

        public static bool TryBeginLanding(Pawn? pawn, bool showMessage = false)
        {
            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                || record == null || pawn?.Map == null || !record.ConsumesFlightEnergy)
            {
                return false;
            }

            MechanicalFlightProfileDef? profile = record.Profile;
            if (profile == null || MechanicalFlightRoofUtility.HasThickRoof(pawn.Position, pawn.Map))
            {
                if (showMessage)
                {
                    Messages.Message("MAP_MechanicalFlight_ThickRoofLanding".Translate(), pawn,
                        MessageTypeDefOf.RejectInput, false);
                }
                return false;
            }
            if (!pawn.Position.WalkableBy(pawn.Map, pawn))
            {
                if (showMessage)
                {
                    Messages.Message("MAP_MechanicalFlight_InvalidLanding".Translate(), pawn,
                        MessageTypeDefOf.RejectInput, false);
                }
                return false;
            }

            if (profile.breakThinRoofOnLanding)
            {
                MechanicalFlightRoofUtility.BreakThinRoofArea(pawn, profile);
            }
            record.Phase = MechanicalFlightPhase.Landing;
            record.TicksUntilNextEnergyDrain = 0;
            if (pawn.CurJob != null)
            {
                pawn.CurJob.flying = false;
            }
            pawn.flight?.ForceLand();
            GameComponent_MechanicalFlightRegistry.NotifyRuntimeStateChanged(record);
            return true;
        }

        internal static void Tick(MechanicalFlightAuthorizationRecord record)
        {
            Pawn? pawn = record.Pawn;
            MechanicalFlightProfileDef? profile = record.Profile;
            if (pawn == null || profile == null || pawn.Dead || !pawn.Spawned || pawn.Map == null)
            {
                ClearRuntimeState(record, forceLand: true);
                return;
            }

            if (record.IsEmergencySequence)
            {
                MechanicalFlightEmergencyUtility.Tick(record);
                return;
            }

            if (pawn.Downed)
            {
                MechanicalFlightEmergencyUtility.TryCrashFromDowned(pawn);
                return;
            }

            if (record.Phase == MechanicalFlightPhase.Landing)
            {
                if (pawn.flight?.Flying != true)
                {
                    record.ResetRuntimeState();
                    MechanicalFlightPresentationUtility.NotifyFlightEnded(pawn);
                    GameComponent_MechanicalFlightRegistry.NotifyRuntimeStateChanged(record);
                }
                return;
            }

            if (pawn.flight?.Flying != true)
            {
                ClearRuntimeState(record, forceLand: false);
                return;
            }

            if (record.Phase == MechanicalFlightPhase.TakingOff
                && pawn.flight.PositionOffsetFactor >= 0.999f)
            {
                record.Phase = MechanicalFlightPhase.Hovering;
            }

            if (!pawn.Drafted)
            {
                pawn.pather?.StopDead();
                if (!TryBeginLanding(pawn))
                {
                    ClearRuntimeState(record, forceLand: true);
                }
                return;
            }

            MechanicalFlightPresentationUtility.Tick(pawn, record);

            record.TicksUntilNextEnergyDrain--;
            if (record.TicksUntilNextEnergyDrain <= 0)
            {
                MechanicalFlightEnergyUtility.TryConsumeMaximumEnergyFraction(
                    pawn, profile.energyDrainFraction);
                record.TicksUntilNextEnergyDrain =
                    Mathf.Max(1, profile.energyDrainIntervalTicks);
            }

            if (!MechanicalFlightEnergyUtility.TryGetEnergyFraction(pawn, out float energy))
            {
                TryBeginLanding(pawn);
                return;
            }

            MechanicalFlightEmergencyUtility.TrySendLowEnergyWarning(pawn, record, energy);
            if (energy <= 0f)
            {
                MechanicalFlightEmergencyUtility.TryBeginEmergencySequence(pawn);
            }
            else if (energy < profile.automaticLandingEnergy)
            {
                TryBeginLanding(pawn);
            }
        }

        internal static void ClearRuntimeState(
            MechanicalFlightAuthorizationRecord record,
            bool forceLand)
        {
            Pawn? pawn = record.Pawn;
            if (forceLand && pawn?.flight?.Flying == true)
            {
                pawn.flight.ForceLand();
            }
            if (pawn?.CurJob != null)
            {
                pawn.CurJob.flying = false;
            }
            if (pawn?.CurJobDef == MAPMechanitor_JobDefOf.MAP_MechanicalFlightEmergencyLanding)
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
            record.ResetRuntimeState();
            MechanicalFlightPresentationUtility.NotifyFlightEnded(pawn);
            GameComponent_MechanicalFlightRegistry.NotifyRuntimeStateChanged(record);
        }

        internal static void ReconcileAfterLoad(
            IReadOnlyList<MechanicalFlightAuthorizationRecord> activeRecords)
        {
            for (int i = activeRecords.Count - 1; i >= 0; i--)
            {
                MechanicalFlightAuthorizationRecord record = activeRecords[i];
                Pawn? pawn = record.Pawn;
                if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.flight == null)
                {
                    ClearRuntimeState(record, forceLand: false);
                    continue;
                }

                if (record.IsEmergencySequence)
                {
                    MechanicalFlightEmergencyUtility.ReconcileAfterLoad(record);
                    continue;
                }

                if (record.ConsumesFlightEnergy && !pawn.flight.Flying)
                {
                    pawn.flight.StartFlying();
                    if (!pawn.flight.Flying)
                    {
                        ClearRuntimeState(record, forceLand: false);
                    }
                }
                else if (record.Phase == MechanicalFlightPhase.Landing
                    && pawn.flight.Flying)
                {
                    pawn.flight.ForceLand();
                }
            }
        }

        private static void ToggleFlight(Pawn pawn)
        {
            if (IsActivelyFlying(pawn))
            {
                TryBeginLanding(pawn, showMessage: true);
            }
            else
            {
                string? reason = GetDisabledReason(pawn);
                if (reason.NullOrEmpty())
                {
                    TryBeginTakeoff(pawn);
                }
                else
                {
                    Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, false);
                }
            }
        }

        private static bool CanStartFlight(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record)
        {
            return GetDisabledReason(pawn, record).NullOrEmpty();
        }

        private static string? GetDisabledReason(Pawn pawn)
        {
            GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record);
            return GetDisabledReason(pawn, record);
        }

        private static string? GetDisabledReason(
            Pawn? pawn,
            MechanicalFlightAuthorizationRecord? record)
        {
            if (pawn == null || record?.Profile == null || !pawn.Spawned
                || pawn.Map == null || pawn.Dead || pawn.Downed)
            {
                return "MAP_MechanicalFlight_Unavailable".Translate();
            }
            if (record.IsEmergencySequence)
            {
                return "MAP_MechanicalFlight_EmergencyLandingBlocked".Translate();
            }
            if (!pawn.Drafted)
            {
                return "MAP_MechanicalFlight_Unavailable".Translate();
            }
            if (record.Phase == MechanicalFlightPhase.Landing)
            {
                return "MAP_MechanicalFlight_Landing".Translate();
            }
            if (record.ConsumesFlightEnergy)
            {
                if (MechanicalFlightRoofUtility.HasThickRoof(pawn.Position, pawn.Map))
                {
                    return "MAP_MechanicalFlight_ThickRoofLanding".Translate();
                }
                return pawn.Position.WalkableBy(pawn.Map, pawn)
                    ? null
                    : "MAP_MechanicalFlight_InvalidLanding".Translate();
            }
            if (!MechanicalFlightEnergyUtility.TryGetEnergyFraction(pawn, out float energy))
            {
                return "MAP_MechanicalFlight_NoEnergyNeed".Translate();
            }
            if (energy < record.Profile.minimumTakeoffEnergy)
            {
                return "MAP_MechanicalFlight_LowEnergy".Translate(
                    record.Profile.minimumTakeoffEnergy.ToStringPercent());
            }
            if (MechanicalFlightRoofUtility.HasThickRoof(pawn.Position, pawn.Map))
            {
                return "MAP_MechanicalFlight_ThickRoofTakeoff".Translate();
            }
            if (pawn.flight == null || !pawn.flight.CanFlyNow)
            {
                return "MAP_MechanicalFlight_Cooling".Translate();
            }
            return null;
        }
    }
}
