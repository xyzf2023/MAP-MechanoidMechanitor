using System;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class DormantJusticeActivationUtility
    {
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
                PawnKindDef? justiceKind = JusticeScenarioUtility.JusticePawnKind;
                if (justiceKind == null)
                {
                    LogActivationFailure(
                        phase,
                        building,
                        map,
                        originalPosition,
                        pawn,
                        buildingTemporarilyDespawned,
                        rollbackSucceeded: null,
                        "未找到 MAP_Mech_Justice PawnKindDef。");
                    return false;
                }

                phase = "generate-pawn";
                pawn = PawnGenerator.GeneratePawn(BuildJusticeGenerationRequest(justiceKind));
                if (pawn.Destroyed)
                {
                    LogActivationFailure(
                        phase,
                        building,
                        map,
                        originalPosition,
                        pawn,
                        buildingTemporarilyDespawned,
                        rollbackSucceeded: null,
                        "Pawn 生成失败。");
                    CleanupFailedPawn(pawn);
                    pawn = null;
                    return false;
                }

                phase = "initialize-pawn";
                if (!EnsureJusticePawnReady(pawn))
                {
                    LogActivationFailure(
                        phase,
                        building,
                        map,
                        originalPosition,
                        pawn,
                        buildingTemporarilyDespawned,
                        rollbackSucceeded: null,
                        "机械师记录或角色状态未就绪。");
                    CleanupFailedPawn(pawn);
                    pawn = null;
                    return false;
                }

                phase = "despawn-building";
                building.DeSpawn(DestroyMode.Vanish);
                buildingTemporarilyDespawned = true;

                phase = "find-spawn-cell";
                IntVec3 spawnCell = FindSpawnCell(map, preferredCell, pawn);
                if (!spawnCell.IsValid)
                {
                    bool rollbackSucceeded = RollbackActivation(
                        building,
                        map,
                        originalPosition,
                        originalRotation,
                        ref pawn,
                        ref buildingTemporarilyDespawned);
                    LogActivationFailure(
                        phase,
                        building,
                        map,
                        originalPosition,
                        pawn,
                        buildingTemporarilyDespawned,
                        rollbackSucceeded,
                        $"未找到有效生成格，preferred={preferredCell}。");
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
                    bool rollbackSucceeded = RollbackActivation(
                        building,
                        map,
                        originalPosition,
                        originalRotation,
                        ref pawn,
                        ref buildingTemporarilyDespawned);
                    LogActivationFailure(
                        phase,
                        building,
                        map,
                        originalPosition,
                        pawn,
                        buildingTemporarilyDespawned,
                        rollbackSucceeded,
                        "GenSpawn.Spawn 未将正义 Pawn 成功落地。");
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
                Exception? rollbackException = null;
                bool? rollbackSucceeded = null;

                try
                {
                    if (map != null)
                    {
                        rollbackSucceeded = RollbackActivation(
                            building,
                            map,
                            originalPosition,
                            originalRotation,
                            ref pawn,
                            ref buildingTemporarilyDespawned);
                    }
                    else
                    {
                        CleanupFailedPawn(pawn);
                        pawn = null;
                        rollbackSucceeded = true;
                    }
                }
                catch (Exception rollbackEx)
                {
                    rollbackException = rollbackEx;
                    Log.Error(
                        "[MAP-机械族机械师] 未启动正义启动回滚异常：" +
                        $"failedPhase={failedPhase}，" +
                        $"building={building.LabelShort}（{building.ThingID}），" +
                        $"originalPosition={originalPosition}，map={map?.ToString() ?? "null"}，" +
                        $"buildingTemporarilyDespawned={buildingTemporarilyDespawned}，" +
                        $"buildingSpawned={building.Spawned}，buildingDestroyed={building.Destroyed}：" +
                        rollbackEx);
                }

                LogActivationException(
                    failedPhase,
                    building,
                    map,
                    originalPosition,
                    pawn,
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

        private static bool RollbackActivation(
            Building building,
            Map map,
            IntVec3 originalPosition,
            Rot4 originalRotation,
            ref Pawn? pawn,
            ref bool buildingTemporarilyDespawned)
        {
            CleanupFailedPawn(pawn);
            pawn = null;

            if (!buildingTemporarilyDespawned)
            {
                return true;
            }

            if (building.Destroyed)
            {
                buildingTemporarilyDespawned = false;
                Log.Error(
                    "[MAP-机械族机械师] 未启动正义启动回滚失败：建筑已销毁，无法恢复，" +
                    $"building={building.LabelShort}（{building.ThingID}），" +
                    $"map={map}，originalPosition={originalPosition}。");
                return false;
            }

            if (building.Spawned)
            {
                buildingTemporarilyDespawned = false;
                return true;
            }

            if (TryRestoreDormantBuilding(building, map, originalPosition, originalRotation))
            {
                buildingTemporarilyDespawned = false;
                return true;
            }

            Log.Error(
                "[MAP-机械族机械师] 未启动正义启动回滚失败：建筑仍处于未生成状态，" +
                $"building={building.LabelShort}（{building.ThingID}），" +
                $"map={map}，originalPosition={originalPosition}，" +
                $"buildingPosition={building.Position}，" +
                $"buildingSpawned={building.Spawned}，buildingDestroyed={building.Destroyed}。");
            return false;
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
                if (!cell.InBounds(map))
                {
                    continue;
                }

                if (cell == originalPosition)
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
                WipeMode.VanishOrMoveAside,
                respawningAfterLoad: false);

            bool restoredSuccessfully =
                ReferenceEquals(spawnedThing, building)
                && building.Spawned
                && building.Map == map
                && !building.Destroyed
                && building.Position == position;
            return restoredSuccessfully;
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
                    thing => thing.def.IsEdifice() && !ReferenceEquals(thing, building)))
            {
                return false;
            }

            return true;
        }

        private static void CleanupFailedPawn(Pawn? pawn)
        {
            if (pawn == null)
            {
                return;
            }

            try
            {
                GameComponent_MechanoidMechanitorRegistry.RemoveFailedGeneratedMechanitor(pawn);

                if (pawn.Spawned)
                {
                    pawn.DeSpawn(DestroyMode.Vanish);
                }

                if (!pawn.Destroyed)
                {
                    pawn.Destroy(DestroyMode.Vanish);
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 未启动正义失败 Pawn 清理异常：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                    $"pawnSpawned={pawn.Spawned}，pawnDestroyed={pawn.Destroyed}：{ex}");
            }
        }

        private static void LogActivationFailure(
            string phase,
            Building? building,
            Map? map,
            IntVec3 originalPosition,
            Pawn? pawn,
            bool buildingTemporarilyDespawned,
            bool? rollbackSucceeded,
            string reason)
        {
            Log.Error(
                "[MAP-机械族机械师] 未启动正义启动失败：" +
                $"phase={phase}，reason={reason}，" +
                $"rollbackSucceeded={rollbackSucceeded?.ToString() ?? "n/a"}，" +
                $"building={building?.LabelShort ?? "null"}（{building?.ThingID ?? "null"}），" +
                $"originalPosition={originalPosition}，buildingPosition={building?.Position.ToString() ?? "null"}，" +
                $"map={map?.ToString() ?? "null"}，" +
                $"pawn={pawn?.LabelShort ?? "null"}（{pawn?.ThingID ?? "null"}），" +
                $"pawnSpawned={pawn?.Spawned.ToString() ?? "null"}，" +
                $"pawnDestroyed={pawn?.Destroyed.ToString() ?? "null"}，" +
                $"buildingSpawned={building?.Spawned.ToString() ?? "null"}，" +
                $"buildingDestroyed={building?.Destroyed.ToString() ?? "null"}，" +
                $"buildingTemporarilyDespawned={buildingTemporarilyDespawned}。");
        }

        private static void LogActivationException(
            string failedPhase,
            Building? building,
            Map? map,
            IntVec3 originalPosition,
            Pawn? pawn,
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
                $"pawn={pawn?.LabelShort ?? "null"}（{pawn?.ThingID ?? "null"}），" +
                $"pawnSpawned={pawn?.Spawned.ToString() ?? "null"}，" +
                $"pawnDestroyed={pawn?.Destroyed.ToString() ?? "null"}，" +
                $"buildingSpawned={building?.Spawned.ToString() ?? "null"}，" +
                $"buildingDestroyed={building?.Destroyed.ToString() ?? "null"}，" +
                $"buildingTemporarilyDespawned={buildingTemporarilyDespawned}，" +
                $"originalException={originalException}" +
                (rollbackException != null ? $"，rollbackException={rollbackException}" : string.Empty));
        }
    }
}
