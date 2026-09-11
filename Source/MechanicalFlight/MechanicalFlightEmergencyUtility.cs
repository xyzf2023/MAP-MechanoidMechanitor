using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public static class MechanicalFlightEmergencyUtility
    {
        public static bool IsEmergencySequence(Pawn? pawn)
        {
            return GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                && record?.IsEmergencySequence == true;
        }

        public static bool TryBeginEmergencySequence(Pawn? pawn)
        {
            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                || record == null || pawn?.Map == null || pawn.Dead
                || pawn.flight?.Flying != true || !record.IsRuntimeActive)
            {
                return false;
            }
            if (record.IsEmergencySequence)
            {
                return true;
            }

            MechanicalFlightEnergyUtility.TrySetEnergyFraction(pawn, 0f);
            if (!TryFindSafeLandingCell(pawn, record.Profile, out IntVec3 target))
            {
                Crash(pawn, record);
                return true;
            }

            record.EmergencyLandingTarget = target;
            record.TicksUntilNextEnergyDrain = 0;
            record.Phase = pawn.Position == target
                ? MechanicalFlightPhase.EmergencyLanding
                : MechanicalFlightPhase.EmergencyApproach;
            GameComponent_MechanicalFlightRegistry.NotifyRuntimeStateChanged(record);

            if (pawn.Drafted)
            {
                pawn.drafter.Drafted = false;
            }
            StartOrRepairEmergencyJob(pawn, record);
            if (record.Phase == MechanicalFlightPhase.EmergencyLanding)
            {
                BeginEmergencyLanding(pawn, record);
            }
            return true;
        }

        internal static void Tick(MechanicalFlightAuthorizationRecord record)
        {
            Pawn? pawn = record.Pawn;
            if (pawn?.Map == null || pawn.Dead || !pawn.Spawned)
            {
                MechanicalFlightUtility.ClearRuntimeState(record, forceLand: true);
                return;
            }
            if (pawn.Downed)
            {
                Crash(pawn, record);
                return;
            }

            if (pawn.flight?.Flying == true)
            {
                MechanicalFlightPresentationUtility.Tick(pawn, record);
            }

            switch (record.Phase)
            {
                case MechanicalFlightPhase.EmergencyApproach:
                    if (pawn.flight?.Flying != true)
                    {
                        Crash(pawn, record);
                        return;
                    }
                    if (pawn.Position == record.EmergencyLandingTarget
                        && pawn.pather?.MovingNow != true)
                    {
                        BeginEmergencyLanding(pawn, record);
                    }
                    else
                    {
                        StartOrRepairEmergencyJob(pawn, record);
                    }
                    break;

                case MechanicalFlightPhase.EmergencyLanding:
                    if (pawn.flight?.Flying != true)
                    {
                        CompleteLandingAndShutdown(pawn, record);
                    }
                    break;

                case MechanicalFlightPhase.Crashing:
                    Crash(pawn, record);
                    break;
            }
        }

        internal static void ReconcileAfterLoad(MechanicalFlightAuthorizationRecord record)
        {
            Pawn? pawn = record.Pawn;
            if (pawn?.Map == null || pawn.Dead || !pawn.Spawned || pawn.flight == null)
            {
                MechanicalFlightUtility.ClearRuntimeState(record, forceLand: false);
                return;
            }

            if (record.Phase == MechanicalFlightPhase.EmergencyApproach)
            {
                if (!record.EmergencyLandingTarget.IsValid
                    || !record.EmergencyLandingTarget.InBounds(pawn.Map))
                {
                    Crash(pawn, record);
                    return;
                }
                if (!pawn.flight.Flying)
                {
                    pawn.flight.StartFlying();
                }
                if (!pawn.flight.Flying)
                {
                    Crash(pawn, record);
                    return;
                }
                StartOrRepairEmergencyJob(pawn, record);
            }
            else if (record.Phase == MechanicalFlightPhase.EmergencyLanding)
            {
                if (pawn.flight.Flying)
                {
                    pawn.flight.ForceLand();
                }
                else
                {
                    CompleteLandingAndShutdown(pawn, record);
                }
            }
            else if (record.Phase == MechanicalFlightPhase.Crashing)
            {
                Crash(pawn, record);
            }
        }

        internal static void TrySendLowEnergyWarning(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record,
            float energyFraction)
        {
            if (record.LowEnergyWarningSent || record.Profile == null || pawn.Map == null
                || energyFraction >= record.Profile.lowEnergyWarningThreshold
                || pawn.Position.WalkableBy(pawn.Map, pawn))
            {
                return;
            }

            record.LowEnergyWarningSent = true;
            Find.LetterStack.ReceiveLetter(
                "MAP_MechanicalFlight_LowEnergyLetterLabel".Translate(),
                "MAP_MechanicalFlight_LowEnergyLetterText".Translate(pawn.LabelShortCap),
                LetterDefOf.NegativeEvent,
                pawn);
        }

        private static void StartOrRepairEmergencyJob(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record)
        {
            IntVec3 target = record.EmergencyLandingTarget;
            if (!target.IsValid || pawn.Map == null || !target.InBounds(pawn.Map))
            {
                Crash(pawn, record);
                return;
            }
            if (pawn.Position == target)
            {
                pawn.pather?.StopDead();
                BeginEmergencyLanding(pawn, record);
                return;
            }

            if (pawn.CurJobDef != MAPMechanitor_JobDefOf.MAP_MechanicalFlightEmergencyLanding
                || pawn.CurJob?.targetA.Cell != target)
            {
                Job job = JobMaker.MakeJob(
                    MAPMechanitor_JobDefOf.MAP_MechanicalFlightEmergencyLanding, target);
                job.flying = true;
                pawn.jobs.StartJob(job, JobCondition.InterruptForced, null,
                    resumeCurJobAfterwards: false, cancelBusyStances: true,
                    thinkTree: null, tag: JobTag.SatisfyingNeeds);
            }
            else if (pawn.pather?.MovingNow != true)
            {
                pawn.pather?.StartPath(target, PathEndMode.OnCell);
            }
        }

        private static void BeginEmergencyLanding(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record)
        {
            if (record.Phase == MechanicalFlightPhase.Crashing)
            {
                return;
            }
            if (pawn.Map == null || pawn.Position != record.EmergencyLandingTarget
                || !IsSafeLandingCell(pawn.Position, pawn, pawn.Map))
            {
                Crash(pawn, record);
                return;
            }

            record.Phase = MechanicalFlightPhase.EmergencyLanding;
            record.TicksUntilNextEnergyDrain = 0;
            pawn.pather?.StopDead();
            if (pawn.CurJob != null)
            {
                pawn.CurJob.flying = false;
            }
            if (record.Profile?.breakThinRoofOnLanding == true)
            {
                MechanicalFlightRoofUtility.BreakThinRoofArea(pawn, record.Profile);
            }
            pawn.flight?.ForceLand();
            GameComponent_MechanicalFlightRegistry.NotifyRuntimeStateChanged(record);
        }

        private static void CompleteLandingAndShutdown(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record)
        {
            pawn.pather?.StopDead();
            if (pawn.CurJobDef == MAPMechanitor_JobDefOf.MAP_MechanicalFlightEmergencyLanding)
            {
                pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
            }
            record.ResetRuntimeState();
            MechanicalFlightPresentationUtility.NotifyFlightEnded(pawn);
            GameComponent_MechanicalFlightRegistry.NotifyRuntimeStateChanged(record);

            // 状态先恢复为地面，再让原版完整处理自我关机、休眠 Hediff 与休眠任务。
            pawn.needs?.energy?.NeedInterval();
        }

        private static bool TryFindSafeLandingCell(
            Pawn pawn,
            MechanicalFlightProfileDef? profile,
            out IntVec3 result)
        {
            result = IntVec3.Invalid;
            Map? map = pawn.Map;
            if (map == null)
            {
                return false;
            }

            int radius = Math.Max(0, profile?.emergencyLandingSearchRadius ?? 3);
            float bestDistance = float.MaxValue;
            foreach (IntVec3 cell in CellRect.CenteredOn(pawn.Position, radius).ClipInsideMap(map))
            {
                if (!IsSafeLandingCell(cell, pawn, map))
                {
                    continue;
                }
                float distance = cell.DistanceToSquared(pawn.Position);
                if (!result.IsValid || distance < bestDistance
                    || (Mathf.Approximately(distance, bestDistance)
                        && CellComesFirst(cell, result)))
                {
                    result = cell;
                    bestDistance = distance;
                }
            }
            return result.IsValid;
        }

        private static bool CellComesFirst(IntVec3 left, IntVec3 right)
        {
            return left.z < right.z || (left.z == right.z && left.x < right.x);
        }

        internal static bool IsSafeLandingCell(IntVec3 cell, Pawn pawn, Map map)
        {
            if (!cell.InBounds(map) || !cell.Standable(map)
                || !cell.WalkableBy(map, pawn)
                || MechanicalFlightRoofUtility.HasThickRoof(cell, map)
                || cell.GetTerrain(map).dangerous || cell.ContainsStaticFire(map)
                || !pawn.CanReserve(cell)
                || cell.GetFirstBuilding(map) != null)
            {
                return false;
            }

            Room? room = cell.GetRoom(map);
            if (room?.IsPrisonCell == true)
            {
                return false;
            }

            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Pawn other && !ReferenceEquals(other, pawn))
                {
                    return false;
                }
            }
            for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
            {
                IntVec3 adjacent = cell + GenAdj.CardinalDirections[i];
                if (!adjacent.InBounds(map))
                {
                    continue;
                }
                List<Thing> adjacentThings = adjacent.GetThingList(map);
                for (int j = 0; j < adjacentThings.Count; j++)
                {
                    Thing thing = adjacentThings[j];
                    if (thing.def.hasInteractionCell && thing.InteractionCell == cell)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        internal static void Crash(Pawn pawn, MechanicalFlightAuthorizationRecord record)
        {
            Map? map = pawn.Map;
            if (map == null || !pawn.Spawned)
            {
                MechanicalFlightUtility.ClearRuntimeState(record, forceLand: true);
                return;
            }

            record.Phase = MechanicalFlightPhase.Crashing;
            IntVec3 center = pawn.Position;
            pawn.pather?.StopDead();
            pawn.flight?.ForceLand();

            MechanicalFlightProfileDef? profile = record.Profile;
            int damage = GetWeightClassMultiplier(pawn)
                * Math.Max(0, profile?.crashDamagePerWeightClass ?? 30);
            int explosionRadius = Math.Max(0, profile?.crashExplosionRadius ?? 1);
            List<IntVec3> explosionCells = new();
            foreach (IntVec3 cell in CellRect.CenteredOn(center, explosionRadius).ClipInsideMap(map))
            {
                explosionCells.Add(cell);
            }
            GenExplosion.DoExplosion(center, map, explosionRadius + 0.5f,
                DamageDefOf.Bomb, pawn, damAmount: damage,
                damageFalloff: false, overrideCells: explosionCells);

            List<Thing> centerThings = new(center.GetThingList(map));
            for (int i = 0; i < centerThings.Count; i++)
            {
                Thing thing = centerThings[i];
                if (thing is Building && !thing.Destroyed
                    && thing.def.useHitPoints && thing.def.destroyable)
                {
                    thing.Kill();
                }
            }

            if (MechanicalFlightRoofUtility.HasThickRoof(center, map))
            {
                map.roofGrid.SetRoof(center, null);
                map.roofGrid.Drawer.SetDirty();
                map.mapDrawer.MapMeshDirty(center, MapMeshFlagDefOf.Roofs);
                map.GetComponent<MechanicalFlightRoofLightingRefresh>().Request(center);

                int roofRadius = Math.Max(0, profile?.crashRoofCollapseRadius ?? 2);
                List<IntVec3> collapseCells = new();
                foreach (IntVec3 cell in CellRect.CenteredOn(center, roofRadius).ClipInsideMap(map))
                {
                    if (MechanicalFlightRoofUtility.HasThickRoof(cell, map)
                        && cell.GetEdifice(map) == null)
                    {
                        collapseCells.Add(cell);
                    }
                }
                RoofCollapserImmediate.DropRoofInCells(collapseCells, map);
            }

            if (pawn.CurJobDef == MAPMechanitor_JobDefOf.MAP_MechanicalFlightEmergencyLanding)
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
            record.ResetRuntimeState();
            MechanicalFlightPresentationUtility.NotifyFlightEnded(pawn);
            GameComponent_MechanicalFlightRegistry.NotifyRuntimeStateChanged(record);
            if (!pawn.Dead)
            {
                pawn.needs?.energy?.NeedInterval();
            }
        }

        private static int GetWeightClassMultiplier(Pawn pawn)
        {
            MechWeightClassDef? weightClass = pawn.RaceProps?.mechWeightClass;
            if (weightClass == MechWeightClassDefOf.Medium)
            {
                return 2;
            }
            if (weightClass == MechWeightClassDefOf.Heavy)
            {
                return 3;
            }
            if (weightClass == MechWeightClassDefOf.UltraHeavy)
            {
                return 4;
            }
            // null、Light 与第三方自定义重量级均按轻型处理。
            return 1;
        }
    }
}
