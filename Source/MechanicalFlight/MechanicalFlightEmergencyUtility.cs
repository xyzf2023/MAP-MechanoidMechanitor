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
        private const int EmergencyTargetValidationIntervalTicks = 30;

        internal struct DeathCrashState
        {
            internal bool ShouldCrash;
            internal MechanicalFlightAuthorizationRecord? Record;
            internal MechanicalFlightProfileDef? Profile;
            internal Map? Map;
            internal IntVec3 Position;
        }

        internal static void CaptureDeathCrash(
            Pawn? pawn,
            out DeathCrashState state)
        {
            state = default;
            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(
                    pawn, out MechanicalFlightAuthorizationRecord? record)
                || record == null || pawn == null || pawn.Dead
                || !pawn.Spawned || pawn.Map == null
                || !record.IsRuntimeActive
                || record.Phase == MechanicalFlightPhase.Crashing
                || !MechanicalFlightUtility.HasHoverVisual(pawn))
            {
                return;
            }

            state.ShouldCrash = true;
            state.Record = record;
            state.Profile = record.Profile;
            state.Map = pawn.Map;
            state.Position = pawn.Position;
        }

        internal static void ResolveDeathCrash(
            Pawn pawn,
            DeathCrashState state)
        {
            if (!state.ShouldCrash || !pawn.Dead || state.Record == null
                || state.Map == null || !state.Position.InBounds(state.Map))
            {
                return;
            }

            state.Record.Phase = MechanicalFlightPhase.Crashing;
            List<Thing> ignoredThings = new() { pawn };
            Corpse? corpse = pawn.Corpse;
            if (corpse != null && !corpse.Destroyed)
            {
                ignoredThings.Add(corpse);
            }

            try
            {
                ResolveCrashImpact(
                    pawn,
                    state.Profile,
                    state.Map,
                    state.Position,
                    ignoredThings);
            }
            catch (Exception exception)
            {
                Log.Error("[MAP-机械族机械师] 死亡坠毁结算异常：" + exception);
            }
            finally
            {
                FinishCrash(pawn, state.Record, resumeEnergyShutdown: false);
            }
        }

        internal static bool TryCrashFromDowned(Pawn? pawn)
        {
            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(
                    pawn, out MechanicalFlightAuthorizationRecord? record)
                || record == null || pawn == null || !pawn.Downed || pawn.Dead
                || !pawn.Spawned || pawn.Map == null
                || !record.IsRuntimeActive
                || record.Phase == MechanicalFlightPhase.Crashing
                || !MechanicalFlightUtility.HasHoverVisual(pawn))
            {
                return false;
            }

            Crash(pawn, record);
            return true;
        }

        /// <summary>
        /// 窄范围立即坠毁入口：只接受当前真实处于活动飞行状态、且已显示悬浮
        /// 视觉的 Pawn，复用现有 Crash 结算。不搜索安全落点，不进入普通 Landing，
        /// 也不创建第二套爆炸、塌方或飞行清理代码。
        /// </summary>
        internal static bool TryCrashImmediately(Pawn? pawn)
        {
            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(
                    pawn, out MechanicalFlightAuthorizationRecord? record)
                || record == null || pawn == null || pawn.Dead || pawn.Downed
                || !pawn.Spawned || pawn.Map == null
                || !record.IsRuntimeActive
                || record.Phase == MechanicalFlightPhase.Crashing
                || !MechanicalFlightUtility.HasHoverVisual(pawn))
            {
                return false;
            }

            Crash(pawn, record);
            return true;
        }

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
            if (record.Phase == MechanicalFlightPhase.Landing)
            {
                // 普通降落已经进入原版着陆倒计时，不允许改判为迫降。
                return true;
            }

            MechanicalFlightEnergyUtility.TrySetEnergyFraction(pawn, 0f);
            if (!TryFindSafeLandingCell(pawn, record.Profile, out IntVec3 target))
            {
                Crash(pawn, record);
                return true;
            }

            record.EmergencyLandingTarget = target;
            record.EmergencyTargetMap = pawn.Map;
            record.NextEmergencyTargetValidationTick =
                GenTicks.TicksGame + EmergencyTargetValidationIntervalTicks;
            record.TicksUntilNextEnergyDrain = 0;
            record.Phase = pawn.Position == target
                ? MechanicalFlightPhase.EmergencyLanding
                : MechanicalFlightPhase.EmergencyApproach;
            GameComponent_MechanicalFlightRegistry.NotifyRuntimeStateChanged(record);

            if (pawn.Drafted)
            {
                pawn.drafter.Drafted = false;
            }
            // 唯一入口：就地进入迫降或创建/修复迫降移动任务。
            StartOrRepairEmergencyJob(pawn, record);
            return true;
        }

        internal static void Tick(MechanicalFlightAuthorizationRecord record)
        {
            Pawn? pawn = record.Pawn;
            if (pawn?.Map == null || pawn.Dead || !pawn.Spawned)
            {
                MechanicalFlightUtility.ClearRuntimeState(record, forceLand: false);
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
                MechanicalFlightUtility.VanillaFlightState approachState =
                    MechanicalFlightUtility.GetVanillaFlightState(pawn);
                if (approachState == MechanicalFlightUtility.VanillaFlightState.Grounded)
                {
                    Crash(pawn, record);
                    return;
                }
                if (approachState == MechanicalFlightUtility.VanillaFlightState.Unknown
                    && !pawn.flight.Flying)
                {
                    pawn.flight.StartFlying();
                    if (!pawn.flight.Flying)
                    {
                        Crash(pawn, record);
                        return;
                    }
                }
                if (!TryValidateOrReplanEmergencyTarget(
                        pawn, record, forceValidation: true))
                {
                    Crash(pawn, record);
                    return;
                }
                // 即使已经站在落点，也必须先创建迫降 Job 并真实持有预留。
                StartOrRepairEmergencyJob(pawn, record);
            }
            else if (record.Phase == MechanicalFlightPhase.EmergencyLanding)
            {
                MechanicalFlightUtility.VanillaFlightState state =
                    MechanicalFlightUtility.GetVanillaFlightState(pawn);
                if (state == MechanicalFlightUtility.VanillaFlightState.Grounded)
                {
                    CompleteLandingAndShutdown(pawn, record);
                    return;
                }
                if (state == MechanicalFlightUtility.VanillaFlightState.Landing)
                {
                    // 保留原版降落进度，但清除旧版本可能保存下来的残留路径，
                    // 并补建迫降 Job/预留。
                    pawn.pather?.StopDead();
                    MechanicalFlightStraightPathPatch.ClearMotion(pawn);
                    StartOrRepairEmergencyJob(pawn, record);
                    return;
                }
                if (!TryValidateOrReplanEmergencyTarget(
                        pawn, record, forceValidation: true))
                {
                    Crash(pawn, record);
                    return;
                }
                StartOrRepairEmergencyJob(pawn, record);
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

        private static bool TryValidateOrReplanEmergencyTarget(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record,
            bool forceValidation)
        {
            Map? map = pawn.Map;
            if (map == null)
            {
                return false;
            }

            IntVec3 target = record.EmergencyLandingTarget;
            if (!target.IsValid
                || !target.InBounds(map)
                || !ReferenceEquals(record.EmergencyTargetMap, map))
            {
                // 目标越界或 Map 变化必须立即强制验证。
                forceValidation = true;
            }

            if (!forceValidation
                && GenTicks.TicksGame < record.NextEmergencyTargetValidationTick)
            {
                // 未到检查时间且目标基础状态正常：复用上一次的安全判定。
                return true;
            }

            if (target.IsValid && target.InBounds(map)
                && IsSafeLandingCell(target, pawn, map))
            {
                record.EmergencyTargetMap = map;
                record.NextEmergencyTargetValidationTick =
                    GenTicks.TicksGame + EmergencyTargetValidationIntervalTicks;
                return true;
            }

            if (!TryFindSafeLandingCell(pawn, record.Profile, out IntVec3 newTarget))
            {
                return false;
            }
            record.EmergencyLandingTarget = newTarget;
            record.EmergencyTargetMap = map;
            record.NextEmergencyTargetValidationTick =
                GenTicks.TicksGame + EmergencyTargetValidationIntervalTicks;
            return true;
        }

        private static void StartOrRepairEmergencyJob(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record)
        {
            Map? map = pawn.Map;
            if (map == null)
            {
                Crash(pawn, record);
                return;
            }

            // 预约丢失或任务丢失时立即强制重新验证目标；正常情况下按30 Tick节流。
            IntVec3 previousTarget = record.EmergencyLandingTarget;
            bool jobOrReservationMissing =
                !previousTarget.IsValid
                || pawn.CurJobDef
                    != MAPMechanitor_JobDefOf.MAP_MechanicalFlightEmergencyLanding
                || pawn.CurJob?.targetA.Cell != previousTarget
                || !map.reservationManager.ReservedBy(
                    new LocalTargetInfo(previousTarget), pawn);
            if (!TryValidateOrReplanEmergencyTarget(
                    pawn, record, jobOrReservationMissing))
            {
                Crash(pawn, record);
                return;
            }

            IntVec3 target = record.EmergencyLandingTarget;
            bool alreadyAtTarget = pawn.Position == target;
            if (!alreadyAtTarget
                && record.Phase != MechanicalFlightPhase.EmergencyApproach)
            {
                record.Phase = MechanicalFlightPhase.EmergencyApproach;
                GameComponent_MechanicalFlightRegistry.NotifyRuntimeStateChanged(record);
            }

            bool hasEmergencyJob =
                pawn.CurJobDef == MAPMechanitor_JobDefOf.MAP_MechanicalFlightEmergencyLanding
                && pawn.CurJob?.targetA.Cell == target;
            bool holdsReservation = hasEmergencyJob
                && pawn.Map.reservationManager.ReservedBy(
                    new LocalTargetInfo(target), pawn);
            if (!hasEmergencyJob || !holdsReservation)
            {
                Job job = JobMaker.MakeJob(
                    MAPMechanitor_JobDefOf.MAP_MechanicalFlightEmergencyLanding, target);
                job.flying = !alreadyAtTarget;
                pawn.jobs.StartJob(job, JobCondition.InterruptForced, null,
                    resumeCurJobAfterwards: false, cancelBusyStances: true,
                    thinkTree: null, tag: JobTag.SatisfyingNeeds,
                    fromQueue: false, canReturnCurJobToPool: false,
                    keepCarryingThingOverride: null, continueSleeping: false,
                    addToJobsThisTick: true, preToilReservationsCanFail: true);
                return;
            }

            if (alreadyAtTarget)
            {
                pawn.pather?.StopDead();
                MechanicalFlightStraightPathPatch.ClearMotion(pawn);
                if (record.Phase != MechanicalFlightPhase.EmergencyLanding)
                {
                    BeginEmergencyLanding(pawn, record);
                }
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
            if (pawn.Map == null)
            {
                Crash(pawn, record);
                return;
            }
            if (!TryValidateOrReplanEmergencyTarget(
                    pawn, record, forceValidation: true))
            {
                Crash(pawn, record);
                return;
            }
            if (pawn.Position != record.EmergencyLandingTarget
                || pawn.CurJobDef
                    != MAPMechanitor_JobDefOf.MAP_MechanicalFlightEmergencyLanding
                || pawn.CurJob?.targetA.Cell != record.EmergencyLandingTarget
                || !pawn.Map.reservationManager.ReservedBy(
                    new LocalTargetInfo(record.EmergencyLandingTarget), pawn))
            {
                StartOrRepairEmergencyJob(pawn, record);
                return;
            }

            record.Phase = MechanicalFlightPhase.EmergencyLanding;
            record.TicksUntilNextEnergyDrain = 0;
            pawn.pather?.StopDead();
            MechanicalFlightStraightPathPatch.ClearMotion(pawn);
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
            MechanicalFlightStraightPathPatch.ClearMotion(pawn);
            if (pawn.CurJobDef == MAPMechanitor_JobDefOf.MAP_MechanicalFlightEmergencyLanding)
            {
                pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
            }
            MechanicalFlightUtility.FinalizeRuntimeState(pawn, record);

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
            // 先套用普通降落共享的基础物理规则，再追加迫降专属的战略安全条件。
            if (!MechanicalFlightUtility.IsBaseLandingCellValid(cell, pawn, map)
                || !pawn.CanReserve(cell))
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
                MechanicalFlightUtility.ClearRuntimeState(record, forceLand: false);
                return;
            }

            record.Phase = MechanicalFlightPhase.Crashing;
            IntVec3 center = pawn.Position;
            try
            {
                pawn.pather?.StopDead();
                MechanicalFlightStraightPathPatch.ClearMotion(pawn);
                pawn.flight?.ForceLand();

                ResolveCrashImpact(pawn, record.Profile, map, center, null);
            }
            catch (Exception exception)
            {
                Log.Error("[MAP-机械族机械师] 坠毁结算异常：" + exception);
            }
            finally
            {
                // 一次坠毁最多执行一次爆炸；无论结算是否异常都完成状态清理。
                FinishCrash(pawn, record, resumeEnergyShutdown: true);
            }
        }

        private static void ResolveCrashImpact(
            Pawn pawn,
            MechanicalFlightProfileDef? profile,
            Map map,
            IntVec3 center,
            List<Thing>? ignoredThings)
        {
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
                damageFalloff: false, ignoredThings: ignoredThings,
                overrideCells: explosionCells);

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

        }

        private static void FinishCrash(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record,
            bool resumeEnergyShutdown)
        {
            try
            {
                if (pawn.jobs != null
                    && pawn.CurJobDef == MAPMechanitor_JobDefOf.MAP_MechanicalFlightEmergencyLanding)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
            }
            catch (Exception exception)
            {
                Log.Error("[MAP-机械族机械师] 坠毁任务清理异常：" + exception);
            }

            bool pendingShutdown = record.PendingShutdownAfterLanding;
            try
            {
                MechanicalFlightUtility.FinalizeRuntimeState(pawn, record);
            }
            catch (Exception exception)
            {
                Log.Error("[MAP-机械族机械师] 坠毁表现状态清理异常：" + exception);
            }

            if ((resumeEnergyShutdown || pendingShutdown) && !pawn.Dead)
            {
                try
                {
                    pawn.needs?.energy?.NeedInterval();
                }
                catch (Exception exception)
                {
                    Log.Error("[MAP-机械族机械师] 坠毁后能量处理异常：" + exception);
                }
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
