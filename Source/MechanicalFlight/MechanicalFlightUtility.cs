using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    [StaticConstructorOnStartup]
    public static class MechanicalFlightUtility
    {
        internal enum VanillaFlightState
        {
            Unknown = -1,
            Grounded = 0,
            Flying = 1,
            TakingOff = 2,
            Landing = 3
        }

        private static readonly Texture2D FlightIcon =
            ContentFinder<Texture2D>.Get("UI/Commands/MechanicalFlight", false)
            ?? TexCommand.Install;

        private static readonly FieldInfo? VanillaFlightStateField =
            AccessTools.Field(typeof(Pawn_FlightTracker), "flightState");

        internal static VanillaFlightState GetVanillaFlightState(Pawn? pawn)
        {
            Pawn_FlightTracker? tracker = pawn?.flight;
            if (tracker == null || VanillaFlightStateField == null)
            {
                return VanillaFlightState.Unknown;
            }

            object? value = VanillaFlightStateField.GetValue(tracker);
            if (value == null)
            {
                return VanillaFlightState.Unknown;
            }
            return (VanillaFlightState)Convert.ToInt32(value);
        }

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
            record.PendingShutdownAfterLanding = false;
            record.GroundLandingBlockedNoticeSent = false;
            record.GroundJobBlockedNoticeSent = false;
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
            pawn.pather?.StopDead();
            MechanicalFlightStraightPathPatch.ClearMotion(pawn);
            record.Phase = MechanicalFlightPhase.Landing;
            record.TicksUntilNextEnergyDrain = 0;
            record.PendingShutdownAfterLanding = false;
            record.GroundLandingBlockedNoticeSent = false;
            record.GroundJobBlockedNoticeSent = false;
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
                ClearRuntimeState(record, forceLand: false);
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
                    CompleteNormalLanding(pawn, record);
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
                    // 当前位置不能安全降落：保留飞行与记录，恢复征召让玩家继续操控。
                    NotifyGroundLandingBlocked(pawn, record);
                    if (pawn.drafter != null && !pawn.Drafted)
                    {
                        pawn.drafter.Drafted = true;
                        return;
                    }
                    // 无征召控制器的机械体（非玩家/异常授权）沿用清理路径，避免无限悬停。
                    // 不强制降落，交由原版飞行状态机自行处理。
                    ClearRuntimeState(record, forceLand: false);
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
                pawn.pather?.StopDead();
                MechanicalFlightStraightPathPatch.ClearMotion(pawn);
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

        internal static void CompleteNormalLanding(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record)
        {
            bool pendingShutdown = record.PendingShutdownAfterLanding;
            MechanicalFlightStraightPathPatch.ClearMotion(pawn);
            if (pawn.CurJob != null)
            {
                pawn.CurJob.flying = false;
            }
            record.ResetRuntimeState();
            MechanicalFlightPresentationUtility.NotifyFlightEnded(pawn);
            GameComponent_MechanicalFlightRegistry.NotifyRuntimeStateChanged(record);
            if (pendingShutdown && !pawn.Dead && pawn.Spawned)
            {
                pawn.needs?.energy?.NeedInterval();
            }
        }

        internal static void CleanupUnavailableRecord(
            MechanicalFlightAuthorizationRecord record)
        {
            MechanicalFlightPresentationUtility.NotifyFlightEnded(record.Pawn);
            record.ResetRuntimeState();
        }

        internal static void NotifyGroundLandingBlocked(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record)
        {
            if (record.GroundLandingBlockedNoticeSent)
            {
                return;
            }
            record.GroundLandingBlockedNoticeSent = true;
            Messages.Message("MAP_MechanicalFlight_InvalidLanding".Translate(), pawn,
                MessageTypeDefOf.RejectInput, false);
        }

        internal static void NotifyGroundJobBlocked(Pawn pawn, bool showMessage)
        {
            if (!showMessage)
            {
                return;
            }
            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                || record == null || record.GroundJobBlockedNoticeSent)
            {
                return;
            }
            record.GroundJobBlockedNoticeSent = true;
            Messages.Message("MAP_MechanicalFlight_GroundJobBlocked".Translate(), pawn,
                MessageTypeDefOf.RejectInput, false);
        }

        internal static void ReconcileAfterLoad(
            IReadOnlyList<MechanicalFlightAuthorizationRecord> activeRecords)
        {
            for (int i = activeRecords.Count - 1; i >= 0; i--)
            {
                MechanicalFlightAuthorizationRecord record = activeRecords[i];
                Pawn? pawn = record.Pawn;
                if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map == null
                    || pawn.flight == null)
                {
                    ClearRuntimeState(record, forceLand: false);
                    continue;
                }

                if (record.IsEmergencySequence)
                {
                    MechanicalFlightEmergencyUtility.ReconcileAfterLoad(record);
                    continue;
                }

                if (record.ConsumesFlightEnergy)
                {
                    VanillaFlightState state = GetVanillaFlightState(pawn);
                    if (state == VanillaFlightState.Grounded)
                    {
                        ClearRuntimeState(record, forceLand: false);
                        continue;
                    }
                    if (state == VanillaFlightState.Landing)
                    {
                        record.Phase = MechanicalFlightPhase.Landing;
                        record.TicksUntilNextEnergyDrain = 0;
                        if (pawn.CurJob != null)
                        {
                            pawn.CurJob.flying = false;
                        }
                        GameComponent_MechanicalFlightRegistry.NotifyRuntimeStateChanged(record);
                        continue;
                    }
                    if (!pawn.flight.Flying)
                    {
                        pawn.flight.StartFlying();
                        if (!pawn.flight.Flying)
                        {
                            ClearRuntimeState(record, forceLand: false);
                        }
                    }
                    continue;
                }

                if (record.Phase == MechanicalFlightPhase.Landing)
                {
                    VanillaFlightState state = GetVanillaFlightState(pawn);
                    if (state == VanillaFlightState.Landing)
                    {
                        continue;
                    }
                    if (state == VanillaFlightState.TakingOff
                        || state == VanillaFlightState.Flying
                        || (state == VanillaFlightState.Unknown && pawn.flight.Flying))
                    {
                        pawn.flight.ForceLand();
                        continue;
                    }
                    CompleteNormalLanding(pawn, record);
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
