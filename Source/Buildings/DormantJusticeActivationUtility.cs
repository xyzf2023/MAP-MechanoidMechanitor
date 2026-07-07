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
                    LogActivationFailure(
                        phase,
                        building,
                        map,
                        originalPosition,
                        pawn,
                        buildingTemporarilyDespawned,
                        $"未找到有效生成格，preferred={preferredCell}。");
                    RollbackActivation(
                        building,
                        map,
                        originalPosition,
                        originalRotation,
                        ref pawn,
                        ref buildingTemporarilyDespawned);
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
                    LogActivationFailure(
                        phase,
                        building,
                        map,
                        originalPosition,
                        pawn,
                        buildingTemporarilyDespawned,
                        "GenSpawn.Spawn 未将正义 Pawn 成功落地。");
                    RollbackActivation(
                        building,
                        map,
                        originalPosition,
                        originalRotation,
                        ref pawn,
                        ref buildingTemporarilyDespawned);
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
                phase = "rollback";
                if (map != null)
                {
                    RollbackActivation(
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
                }

                LogActivationException(
                    phase,
                    building,
                    map,
                    originalPosition,
                    pawn,
                    buildingTemporarilyDespawned,
                    ex);
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

        private static void RollbackActivation(
            Building building,
            Map map,
            IntVec3 originalPosition,
            Rot4 originalRotation,
            ref Pawn? pawn,
            ref bool buildingTemporarilyDespawned)
        {
            CleanupFailedPawn(pawn);
            pawn = null;

            if (!buildingTemporarilyDespawned
                || building.Destroyed
                || building.Spawned)
            {
                buildingTemporarilyDespawned = false;
                return;
            }

            if (TryRestoreDormantBuilding(building, map, originalPosition, originalRotation))
            {
                buildingTemporarilyDespawned = false;
                return;
            }

            Log.Error(
                "[MAP-机械族机械师] 未启动正义启动回滚失败：无法恢复建筑，" +
                $"building={building.LabelShort}（{building.ThingID}），" +
                $"map={map}，originalPosition={originalPosition}，" +
                $"buildingSpawned={building.Spawned}，buildingDestroyed={building.Destroyed}。");
            buildingTemporarilyDespawned = false;
        }

        private static bool TryRestoreDormantBuilding(
            Building building,
            Map map,
            IntVec3 originalPosition,
            Rot4 originalRotation)
        {
            if (building.Destroyed || building.Spawned)
            {
                return building.Spawned;
            }

            if (GenSpawn.CanSpawnAt(building.def, originalPosition, map, originalRotation))
            {
                GenSpawn.Spawn(
                    building,
                    originalPosition,
                    map,
                    originalRotation,
                    WipeMode.Vanish,
                    respawningAfterLoad: false);
                return building.Spawned;
            }

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(originalPosition, 4f, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                if (!GenSpawn.CanSpawnAt(building.def, cell, map, originalRotation))
                {
                    continue;
                }

                GenSpawn.Spawn(
                    building,
                    cell,
                    map,
                    originalRotation,
                    WipeMode.Vanish,
                    respawningAfterLoad: false);
                if (building.Spawned)
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

        private static void CleanupFailedPawn(Pawn? pawn)
        {
            if (pawn == null)
            {
                return;
            }

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

        private static void LogActivationFailure(
            string phase,
            Building? building,
            Map? map,
            IntVec3 originalPosition,
            Pawn? pawn,
            bool buildingTemporarilyDespawned,
            string reason)
        {
            Log.Error(
                "[MAP-机械族机械师] 未启动正义启动失败：" +
                $"phase={phase}，reason={reason}，" +
                $"building={building?.LabelShort ?? "null"}（{building?.ThingID ?? "null"}），" +
                $"originalPosition={originalPosition}，map={map?.ToString() ?? "null"}，" +
                $"pawn={pawn?.LabelShort ?? "null"}（{pawn?.ThingID ?? "null"}），" +
                $"pawnSpawned={pawn?.Spawned.ToString() ?? "null"}，" +
                $"pawnDestroyed={pawn?.Destroyed.ToString() ?? "null"}，" +
                $"buildingSpawned={building?.Spawned.ToString() ?? "null"}，" +
                $"buildingDestroyed={building?.Destroyed.ToString() ?? "null"}，" +
                $"buildingTemporarilyDespawned={buildingTemporarilyDespawned}。");
        }

        private static void LogActivationException(
            string phase,
            Building? building,
            Map? map,
            IntVec3 originalPosition,
            Pawn? pawn,
            bool buildingTemporarilyDespawned,
            Exception ex)
        {
            Log.Error(
                "[MAP-机械族机械师] 未启动正义启动异常：" +
                $"phase={phase}，" +
                $"building={building?.LabelShort ?? "null"}（{building?.ThingID ?? "null"}），" +
                $"originalPosition={originalPosition}，map={map?.ToString() ?? "null"}，" +
                $"pawn={pawn?.LabelShort ?? "null"}（{pawn?.ThingID ?? "null"}），" +
                $"pawnSpawned={pawn?.Spawned.ToString() ?? "null"}，" +
                $"pawnDestroyed={pawn?.Destroyed.ToString() ?? "null"}，" +
                $"buildingSpawned={building?.Spawned.ToString() ?? "null"}，" +
                $"buildingDestroyed={building?.Destroyed.ToString() ?? "null"}，" +
                $"buildingTemporarilyDespawned={buildingTemporarilyDespawned}：{ex}");
        }
    }
}
