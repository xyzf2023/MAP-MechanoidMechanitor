using System;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class DormantJusticeActivationUtility
    {
        private readonly struct PawnActivationFailureSnapshot
        {
            public readonly string Label;
            public readonly string ThingId;
            public readonly bool Spawned;
            public readonly bool Destroyed;
            public readonly bool Dead;
            public readonly string Map;
            public readonly IntVec3 Position;
            public readonly bool IsMechanicalConsciousnessHost;
            public readonly bool HasMechanitorRecord;

            public PawnActivationFailureSnapshot(
                string label,
                string thingId,
                bool spawned,
                bool destroyed,
                bool dead,
                string map,
                IntVec3 position,
                bool isMechanicalConsciousnessHost,
                bool hasMechanitorRecord)
            {
                Label = label;
                ThingId = thingId;
                Spawned = spawned;
                Destroyed = destroyed;
                Dead = dead;
                Map = map;
                Position = position;
                IsMechanicalConsciousnessHost = isMechanicalConsciousnessHost;
                HasMechanitorRecord = hasMechanitorRecord;
            }

            public static PawnActivationFailureSnapshot From(Pawn? pawn)
            {
                if (pawn == null)
                {
                    return new PawnActivationFailureSnapshot(
                        "null",
                        "null",
                        false,
                        false,
                        false,
                        "null",
                        IntVec3.Invalid,
                        false,
                        false);
                }

                return new PawnActivationFailureSnapshot(
                    pawn.LabelShort,
                    pawn.ThingID,
                    pawn.Spawned,
                    pawn.Destroyed,
                    pawn.Dead,
                    pawn.Map?.ToString() ?? pawn.MapHeld?.ToString() ?? "null",
                    pawn.Position,
                    GameComponent_MechanoidMechanitorRegistry.IsMechanicalConsciousnessHost(pawn),
                    QueryHasMechanitorRecordForSnapshot(pawn));
            }

            private static bool QueryHasMechanitorRecordForSnapshot(Pawn pawn)
            {
                try
                {
                    return GameComponent_MechanoidMechanitorRegistry
                        .HasRecordForPawnIncludingDestroyed(pawn);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 未启动正义失败快照查询注册记录异常：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
                    return true;
                }
            }
        }

        public static bool TryActivate(CompDormantJustice? dormantComp, out Pawn? generatedJustice)
        {
            generatedJustice = null;
            if (dormantComp == null)
            {
                return false;
            }

            if (dormantComp.parent is not Building building
                || building.Destroyed
                || !building.Spawned
                || building.Map == null
                || building.Faction == null
                || !building.Faction.IsPlayerSafe())
            {
                return false;
            }

            if (dormantComp.IsActivationInProgress)
            {
                return false;
            }

            dormantComp.PrepareForActivationAttempt();
            dormantComp.BeginActivationAttempt();

            string phase = "initial-validation";
            Map? map = null;
            IntVec3 originalPosition = IntVec3.Invalid;
            Rot4 originalRotation = Rot4.South;
            IntVec3 preferredCell = IntVec3.Invalid;
            Pawn? pawn = null;
            bool buildingTemporarilyDespawned = false;

            try
            {
                map = building.Map;
                originalPosition = building.Position;
                originalRotation = building.Rotation;
                preferredCell = building.OccupiedRect().CenterCell;

                phase = "resolve-pawn-kind";
                PawnKindDef? justiceKind = JusticePawnUtility.JusticePawnKind;
                if (justiceKind == null)
                {
                    LogActivationFailure(
                        phase,
                        building,
                        map,
                        originalPosition,
                        PawnActivationFailureSnapshot.From(pawn),
                        buildingTemporarilyDespawned,
                        rollbackSucceeded: null,
                        "未找到 MAP_Mech_Justice PawnKindDef。");
                    return false;
                }

                phase = "generate-pawn";
                pawn = PawnGenerator.GeneratePawn(BuildJusticeGenerationRequest(justiceKind));
                if (pawn.Destroyed)
                {
                    PawnActivationFailureSnapshot generateSnapshot =
                        PawnActivationFailureSnapshot.From(pawn);
                    bool pawnCleanupSucceeded = CleanupFailedPawn(pawn);
                    LogActivationFailure(
                        phase,
                        building,
                        map,
                        originalPosition,
                        generateSnapshot,
                        buildingTemporarilyDespawned,
                        pawnCleanupSucceeded,
                        "Pawn 生成失败。");
                    if (pawnCleanupSucceeded)
                    {
                        pawn = null;
                    }

                    return false;
                }

                phase = "initialize-pawn";
                if (!EnsureJusticePawnReady(pawn))
                {
                    PawnActivationFailureSnapshot initSnapshot =
                        PawnActivationFailureSnapshot.From(pawn);
                    bool pawnCleanupSucceeded = CleanupFailedPawn(pawn);
                    LogActivationFailure(
                        phase,
                        building,
                        map,
                        originalPosition,
                        initSnapshot,
                        buildingTemporarilyDespawned,
                        pawnCleanupSucceeded,
                        "机械师记录或角色状态未就绪。");
                    if (pawnCleanupSucceeded)
                    {
                        pawn = null;
                    }

                    return false;
                }

                phase = "despawn-building";
                building.DeSpawn(DestroyMode.Vanish);
                buildingTemporarilyDespawned = true;

                phase = "find-spawn-cell";
                IntVec3 spawnCell = FindSpawnCell(map, preferredCell, pawn);
                PawnActivationFailureSnapshot pawnSnapshot =
                    PawnActivationFailureSnapshot.From(pawn);
                if (!spawnCell.IsValid)
                {
                    bool rollbackSucceeded = TryRollbackActivation(
                        building,
                        map,
                        originalPosition,
                        originalRotation,
                        ref pawn,
                        ref buildingTemporarilyDespawned,
                        pawnSnapshot,
                        out Exception? rollbackException);
                    LogActivationFailure(
                        phase,
                        building,
                        map,
                        originalPosition,
                        pawnSnapshot,
                        buildingTemporarilyDespawned,
                        rollbackSucceeded,
                        $"未找到有效生成格，preferred={preferredCell}。",
                        rollbackException);
                    return false;
                }

                phase = "spawn-pawn";
                Thing? spawnedThing = GenSpawn.Spawn(pawn, spawnCell, map);
                bool pawnSpawnedSuccessfully =
                    ReferenceEquals(spawnedThing, pawn)
                    && pawn.Spawned
                    && pawn.Map == map
                    && !pawn.Destroyed;
                if (!pawnSpawnedSuccessfully)
                {
                    PawnActivationFailureSnapshot spawnFailureSnapshot =
                        PawnActivationFailureSnapshot.From(pawn);
                    bool rollbackSucceeded = TryRollbackActivation(
                        building,
                        map,
                        originalPosition,
                        originalRotation,
                        ref pawn,
                        ref buildingTemporarilyDespawned,
                        spawnFailureSnapshot,
                        out Exception? rollbackException);
                    LogActivationFailure(
                        phase,
                        building,
                        map,
                        originalPosition,
                        spawnFailureSnapshot,
                        buildingTemporarilyDespawned,
                        rollbackSucceeded,
                        "GenSpawn.Spawn 未将正义 Pawn 成功落地。",
                        rollbackException);
                    return false;
                }

                phase = "commit";
                if (!building.Destroyed)
                {
                    building.Destroy(DestroyMode.Vanish);
                }

                generatedJustice = pawn;
                pawn = null;
                return true;
            }
            catch (Exception ex)
            {
                string failedPhase = phase;
                PawnActivationFailureSnapshot pawnSnapshot =
                    PawnActivationFailureSnapshot.From(pawn);
                Exception? rollbackException = null;
                bool? rollbackSucceeded = null;

                if (map != null)
                {
                    rollbackSucceeded = TryRollbackActivation(
                        building,
                        map,
                        originalPosition,
                        originalRotation,
                        ref pawn,
                        ref buildingTemporarilyDespawned,
                        pawnSnapshot,
                        out rollbackException);
                }
                else
                {
                    rollbackSucceeded = CleanupFailedPawn(pawn);
                    if (rollbackSucceeded == true)
                    {
                        pawn = null;
                    }
                }

                LogActivationException(
                    failedPhase,
                    building,
                    map,
                    originalPosition,
                    pawnSnapshot,
                    buildingTemporarilyDespawned,
                    rollbackSucceeded,
                    ex,
                    rollbackException);
                return false;
            }
            finally
            {
                dormantComp.EndActivationAttempt();
            }
        }

        private static PawnGenerationRequest BuildJusticeGenerationRequest(PawnKindDef kind)
        {
            return new PawnGenerationRequest(
                kind,
                Faction.OfPlayer,
                PawnGenerationContext.NonPlayer,
                null,
                forceGenerateNewPawn: true,
                allowDead: false,
                allowDowned: false,
                canGeneratePawnRelations: true,
                mustBeCapableOfViolence: false,
                1f,
                forceAddFreeWarmLayerIfNeeded: false,
                allowGay: true,
                allowPregnant: false,
                allowFood: true,
                allowAddictions: true,
                inhabitant: false,
                certainlyBeenInCryptosleep: false,
                forceRedressWorldPawnIfFormerColonist: false,
                worldPawnFactionDoesntMatter: false,
                0f,
                0f,
                null,
                1f,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                forceNoIdeo: false,
                forceNoBackstory: false,
                forbidAnyTitle: false,
                forceDead: false,
                null,
                null,
                null,
                null,
                null,
                0f,
                developmentalStages: DevelopmentalStage.Adult);
        }

        private static bool EnsureJusticePawnReady(Pawn pawn)
        {
            if (pawn.Faction != Faction.OfPlayer)
            {
                pawn.SetFaction(Faction.OfPlayer);
            }

            GameComponent_MechanoidMechanitorRegistry.EnsureNativeMechanitorRecord(pawn);
            MechanoidMechanitorRoleUtility.EnsureRoleState(pawn);
            MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(pawn);

            return !pawn.Destroyed
                && !pawn.Dead
                && pawn.RaceProps.IsMechanoid
                && pawn.Faction != null
                && pawn.Faction.IsPlayerSafe()
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn)
                && GameComponent_MechanoidMechanitorRegistry.TryGetNativeMechanitorRecord(
                    pawn,
                    out _)
                && pawn.relations != null
                && pawn.mechanitor != null
                && MechanitorUtility.IsMechanitor(pawn);
        }

        private static IntVec3 FindSpawnCell(Map map, IntVec3 preferred, Pawn pawn)
        {
            if (GenSpawn.CanSpawnAt(pawn.def, preferred, map))
            {
                return preferred;
            }

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(preferred, 4f, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                if (GenSpawn.CanSpawnAt(pawn.def, cell, map))
                {
                    return cell;
                }
            }

            if (CellFinder.TryRandomClosewalkCellNear(
                    preferred,
                    map,
                    6,
                    out IntVec3 closewalk,
                    c => GenSpawn.CanSpawnAt(pawn.def, c, map)))
            {
                return closewalk;
            }

            return IntVec3.Invalid;
        }

        private static bool TryRollbackActivation(
            Building building,
            Map map,
            IntVec3 originalPosition,
            Rot4 originalRotation,
            ref Pawn? pawn,
            ref bool buildingTemporarilyDespawned,
            PawnActivationFailureSnapshot pawnSnapshot,
            out Exception? rollbackException)
        {
            rollbackException = null;
            try
            {
                return RollbackActivation(
                    building,
                    map,
                    originalPosition,
                    originalRotation,
                    ref pawn,
                    ref buildingTemporarilyDespawned);
            }
            catch (Exception ex)
            {
                rollbackException = ex;
                Log.Error(
                    "[MAP-机械族机械师] 未启动正义启动回滚异常：" +
                    $"building={building.LabelShort}（{building.ThingID}），" +
                    $"originalPosition={originalPosition}，map={map}，" +
                    $"buildingTemporarilyDespawned={buildingTemporarilyDespawned}，" +
                    $"buildingSpawned={building.Spawned}，buildingDestroyed={building.Destroyed}，" +
                    FormatPawnSnapshotForLog(pawnSnapshot) +
                    $"：{ex}");
                return false;
            }
        }

        private static bool RollbackActivation(
            Building building,
            Map map,
            IntVec3 originalPosition,
            Rot4 originalRotation,
            ref Pawn? pawn,
            ref bool buildingTemporarilyDespawned)
        {
            bool pawnCleanupSucceeded = CleanupFailedPawn(pawn);
            if (pawnCleanupSucceeded)
            {
                pawn = null;
            }

            bool buildingRestoreSucceeded;
            if (!buildingTemporarilyDespawned)
            {
                buildingRestoreSucceeded = true;
            }
            else if (building.Destroyed)
            {
                buildingTemporarilyDespawned = false;
                Log.Error(
                    "[MAP-机械族机械师] 未启动正义启动回滚失败：建筑已销毁，无法恢复，" +
                    $"building={building.LabelShort}（{building.ThingID}），" +
                    $"map={map}，originalPosition={originalPosition}，" +
                    $"pawnCleanupSucceeded={pawnCleanupSucceeded}。");
                buildingRestoreSucceeded = false;
            }
            else if (building.Spawned)
            {
                buildingTemporarilyDespawned = false;
                buildingRestoreSucceeded = true;
            }
            else if (TryRestoreDormantBuilding(building, map, originalPosition, originalRotation))
            {
                buildingTemporarilyDespawned = false;
                buildingRestoreSucceeded = true;
            }
            else
            {
                Log.Error(
                    "[MAP-机械族机械师] 未启动正义启动回滚失败：建筑仍处于未生成状态，" +
                    $"building={building.LabelShort}（{building.ThingID}），" +
                    $"map={map}，originalPosition={originalPosition}，" +
                    $"buildingPosition={building.Position}，" +
                    $"buildingSpawned={building.Spawned}，buildingDestroyed={building.Destroyed}，" +
                    $"pawnCleanupSucceeded={pawnCleanupSucceeded}。");
                buildingRestoreSucceeded = false;
            }

            return pawnCleanupSucceeded && buildingRestoreSucceeded;
        }

        private static bool TryRestoreDormantBuilding(
            Building building,
            Map map,
            IntVec3 originalPosition,
            Rot4 originalRotation)
        {
            if (building.Destroyed)
            {
                return false;
            }

            if (building.Spawned)
            {
                return building.Map == map;
            }

            if (TryRestoreDormantBuildingAt(
                    building,
                    map,
                    originalPosition,
                    originalRotation))
            {
                return true;
            }

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(originalPosition, 4f, true))
            {
                if (!cell.InBounds(map) || cell == originalPosition)
                {
                    continue;
                }

                if (TryRestoreDormantBuildingAt(building, map, cell, originalRotation))
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 未启动正义建筑已恢复到邻近格：" +
                        $"building={building.LabelShort}（{building.ThingID}），" +
                        $"originalPosition={originalPosition}，restoredPosition={cell}。");
                    return true;
                }
            }

            return false;
        }

        private static bool TryRestoreDormantBuildingAt(
            Building building,
            Map map,
            IntVec3 position,
            Rot4 rotation)
        {
            if (!CanSafelyRestoreBuilding(building, map, position, rotation))
            {
                return false;
            }

            Thing? spawnedThing = GenSpawn.Spawn(
                building,
                position,
                map,
                rotation,
                WipeMode.Vanish,
                respawningAfterLoad: false);

            return ReferenceEquals(spawnedThing, building)
                && building.Spawned
                && building.Map == map
                && building.Position == position
                && !building.Destroyed;
        }

        private static bool CanSafelyRestoreBuilding(
            Building building,
            Map map,
            IntVec3 position,
            Rot4 rotation)
        {
            if (!position.InBounds(map))
            {
                return false;
            }

            CellRect occupiedRect = GenAdj.OccupiedRect(position, rotation, building.def.Size);
            if (!occupiedRect.InBounds(map))
            {
                return false;
            }

            foreach (IntVec3 cell in occupiedRect)
            {
                Building? edifice = cell.GetEdifice(map);
                if (edifice != null && !ReferenceEquals(edifice, building))
                {
                    return false;
                }
            }

            if (!GenSpawn.CanSpawnAt(
                    building.def,
                    position,
                    map,
                    rotation,
                    canWipeEdifices: false))
            {
                return false;
            }

            if (GenSpawn.WouldWipeAnythingWith(
                    occupiedRect,
                    building.def,
                    map,
                    _ => true))
            {
                return false;
            }

            return true;
        }

        private static bool CleanupFailedPawn(Pawn? pawn)
        {
            if (pawn == null)
            {
                return true;
            }

            if (GameComponent_MechanoidMechanitorRegistry.IsMechanicalConsciousnessHost(pawn))
            {
                Log.Error(
                    "[MAP-机械族机械师] 未启动正义失败 Pawn 清理中止：其为当前机械意识宿主，" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                    $"pawnSpawned={pawn.Spawned}，pawnDestroyed={pawn.Destroyed}。");
                return false;
            }

            try
            {
                if (!GameComponent_MechanoidMechanitorRegistry.RemoveFailedGeneratedMechanitor(pawn))
                {
                    Log.Error(
                        "[MAP-机械族机械师] 未启动正义失败 Pawn 清理中止：注册表清理未成功，" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                        $"pawnSpawned={pawn.Spawned}，pawnDestroyed={pawn.Destroyed}。");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 未启动正义失败 Pawn 注册表清理异常：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
                return false;
            }

            if (pawn.Spawned)
            {
                try
                {
                    pawn.DeSpawn(DestroyMode.Vanish);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 未启动正义失败 Pawn DeSpawn 异常：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                        $"pawnSpawned={pawn.Spawned}，pawnDestroyed={pawn.Destroyed}：{ex}");
                    return false;
                }
            }

            if (!pawn.Destroyed)
            {
                try
                {
                    pawn.Destroy(DestroyMode.Vanish);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 未启动正义失败 Pawn Destroy 异常：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                        $"pawnSpawned={pawn.Spawned}，pawnDestroyed={pawn.Destroyed}：{ex}");
                    return false;
                }
            }

            try
            {
                if (GameComponent_MechanoidMechanitorRegistry.IsMechanicalConsciousnessHost(pawn))
                {
                    Log.Error(
                        "[MAP-机械族机械师] 未启动正义失败 Pawn 清理验证失败：stillHost，" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）。");
                    return false;
                }

                if (pawn.Spawned)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 未启动正义失败 Pawn 清理验证失败：stillSpawned，" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）。");
                    return false;
                }

                if (!pawn.Destroyed)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 未启动正义失败 Pawn 清理验证失败：notDestroyed，" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）。");
                    return false;
                }

                if (GameComponent_MechanoidMechanitorRegistry.HasRecordForPawnIncludingDestroyed(pawn))
                {
                    Log.Error(
                        "[MAP-机械族机械师] 未启动正义失败 Pawn 清理验证失败：mechanitorRecordStillExists，" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）。");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 未启动正义失败 Pawn 清理验证异常：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                    $"pawnSpawned={pawn.Spawned}，pawnDestroyed={pawn.Destroyed}：{ex}");
                return false;
            }
        }

        private static string FormatPawnSnapshotForLog(PawnActivationFailureSnapshot snapshot)
        {
            return
                $"pawn={snapshot.Label}（{snapshot.ThingId}），" +
                $"pawnSpawned={snapshot.Spawned}，pawnDestroyed={snapshot.Destroyed}，" +
                $"pawnDead={snapshot.Dead}，pawnMap={snapshot.Map}，pawnPosition={snapshot.Position}，" +
                $"pawnIsHost={snapshot.IsMechanicalConsciousnessHost}，" +
                $"pawnHasMechanitorRecord={snapshot.HasMechanitorRecord}，";
        }

        private static void LogActivationFailure(
            string phase,
            Building? building,
            Map? map,
            IntVec3 originalPosition,
            PawnActivationFailureSnapshot pawnSnapshot,
            bool buildingTemporarilyDespawned,
            bool? rollbackSucceeded,
            string reason,
            Exception? rollbackException = null)
        {
            Log.Error(
                "[MAP-机械族机械师] 未启动正义启动失败：" +
                $"phase={phase}，reason={reason}，" +
                $"rollbackSucceeded={rollbackSucceeded?.ToString() ?? "n/a"}，" +
                $"building={building?.LabelShort ?? "null"}（{building?.ThingID ?? "null"}），" +
                $"originalPosition={originalPosition}，buildingPosition={building?.Position.ToString() ?? "null"}，" +
                $"map={map?.ToString() ?? "null"}，" +
                FormatPawnSnapshotForLog(pawnSnapshot) +
                $"buildingSpawned={building?.Spawned.ToString() ?? "null"}，" +
                $"buildingDestroyed={building?.Destroyed.ToString() ?? "null"}，" +
                $"buildingTemporarilyDespawned={buildingTemporarilyDespawned}" +
                (rollbackException != null ? $"，rollbackException={rollbackException}" : string.Empty) +
                "。");
        }

        private static void LogActivationException(
            string failedPhase,
            Building? building,
            Map? map,
            IntVec3 originalPosition,
            PawnActivationFailureSnapshot pawnSnapshot,
            bool buildingTemporarilyDespawned,
            bool? rollbackSucceeded,
            Exception originalException,
            Exception? rollbackException)
        {
            Log.Error(
                "[MAP-机械族机械师] 未启动正义启动异常：" +
                $"failedPhase={failedPhase}，" +
                $"rollbackSucceeded={rollbackSucceeded?.ToString() ?? "n/a"}，" +
                $"building={building?.LabelShort ?? "null"}（{building?.ThingID ?? "null"}），" +
                $"originalPosition={originalPosition}，buildingPosition={building?.Position.ToString() ?? "null"}，" +
                $"map={map?.ToString() ?? "null"}，" +
                FormatPawnSnapshotForLog(pawnSnapshot) +
                $"buildingSpawned={building?.Spawned.ToString() ?? "null"}，" +
                $"buildingDestroyed={building?.Destroyed.ToString() ?? "null"}，" +
                $"buildingTemporarilyDespawned={buildingTemporarilyDespawned}，" +
                $"originalException={originalException}" +
                (rollbackException != null ? $"，rollbackException={rollbackException}" : string.Empty));
        }
    }
}
