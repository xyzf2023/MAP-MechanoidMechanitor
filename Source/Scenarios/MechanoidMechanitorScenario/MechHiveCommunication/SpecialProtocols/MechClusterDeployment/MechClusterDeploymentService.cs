using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;
using Verse.Sound;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechClusterDeploymentSession
    {
        public readonly Map Map;

        public readonly MechClusterSketch Sketch;

        public readonly ThingDef? ConditionCauser;

        public readonly int ThreatPoints;

        public readonly int Cost;

        public readonly int OrderRevision;

        private Rot4 placementRotation = Rot4.North;

        public Rot4 PlacementRotation => placementRotation;

        public MechClusterDeploymentSession(
            Map map,
            MechClusterSketch sketch,
            ThingDef? conditionCauser,
            int threatPoints,
            int cost,
            int orderRevision)
        {
            Map = map;
            Sketch = sketch;
            ConditionCauser = conditionCauser;
            ThreatPoints = threatPoints;
            Cost = cost;
            OrderRevision = orderRevision;
            placementRotation = Rot4.North;
        }

        public void Rotate(RotationDirection direction)
        {
            if (direction == RotationDirection.None
                || Sketch?.buildingsSketch == null
                || Sketch.pawns == null)
            {
                return;
            }

            ApplyAbsoluteRotation(placementRotation.Rotated(direction));
        }

        public void ResetPlacementRotationToNorth()
        {
            ApplyAbsoluteRotation(Rot4.North);
        }

        private void ApplyAbsoluteRotation(Rot4 next)
        {
            if (Sketch?.buildingsSketch == null || Sketch.pawns == null)
            {
                return;
            }

            if (placementRotation == next)
            {
                return;
            }

            RotationDirection relative = Rot4.GetRelativeRotation(placementRotation, next);
            // 复用原版 Sketch.Rotate，含非正方形/偶尺寸偏移修正。
            Sketch.buildingsSketch.Rotate(next);

            List<MechClusterSketch.Mech> pawns = Sketch.pawns;
            for (int i = 0; i < pawns.Count; i++)
            {
                MechClusterSketch.Mech mech = pawns[i];
                mech.position = mech.position.RotatedBy(relative);
                pawns[i] = mech;
            }

            placementRotation = next;
        }
    }

    public readonly struct MechClusterDeploymentResult
    {
        public readonly bool Success;

        public readonly bool Committed;

        public readonly string? ErrorKey;

        public MechClusterDeploymentResult(bool success, bool committed, string? errorKey)
        {
            Success = success;
            Committed = committed;
            ErrorKey = errorKey;
        }

        public static MechClusterDeploymentResult Succeeded =>
            new MechClusterDeploymentResult(true, true, null);

        public static MechClusterDeploymentResult Failed(string errorKey) =>
            new MechClusterDeploymentResult(false, false, errorKey);

        public static MechClusterDeploymentResult FailedAfterCommit(string errorKey) =>
            new MechClusterDeploymentResult(false, true, errorKey);
    }

    public static class MechClusterDeploymentService
    {
        public const int DefaultThreatPoints =
            MechClusterDeploymentOrder.DefaultThreatPoints;

        public const string ErrorUnavailable =
            "MAP_MechanoidMechanitor.MechHiveCommunication.SpecialProtocols.Cluster.Error.Unavailable";

        public const string ErrorInvalidRequest =
            "MAP_MechanoidMechanitor.MechHiveCommunication.SpecialProtocols.Cluster.Error.InvalidRequest";

        public const string ErrorGenerationFailed =
            "MAP_MechanoidMechanitor.MechHiveCommunication.SpecialProtocols.Cluster.Error.GenerationFailed";

        public const string ErrorConditionCauserPlacement =
            "MAP_MechanoidMechanitor.MechHiveCommunication.SpecialProtocols.Cluster.Error.ConditionCauserPlacement";

        public const string ErrorInsufficientCredits =
            "MAP_MechanoidMechanitor.MechHiveCommunication.SpecialProtocols.Cluster.Error.InsufficientCredits";

        public const string ErrorChargeFailed =
            "MAP_MechanoidMechanitor.MechHiveCommunication.SpecialProtocols.Cluster.Error.ChargeFailed";

        public const string ErrorCommittedFailure =
            "MAP_MechanoidMechanitor.MechHiveCommunication.SpecialProtocols.Cluster.Error.CommittedFailure";

        private static readonly List<IntVec3> PlacementEdgeCells = new List<IntVec3>();

        public static Map? ResolveFirstPlayerHomeColonyMap()
        {
            List<Map> maps = Find.Maps;
            if (maps == null)
            {
                return null;
            }

            for (int i = 0; i < maps.Count; i++)
            {
                Map candidate = maps[i];
                if (candidate != null && !candidate.Disposed && candidate.IsPlayerHome)
                {
                    return candidate;
                }
            }

            return null;
        }

        public static bool IsDeploymentTargetMapValid(Map? map)
        {
            return map != null && !map.Disposed && map.IsPlayerHome;
        }

        public static bool TryResolveAvailableMap(out Map? map)
        {
            map = null;
            if (!ModsConfig.RoyaltyActive
                || !MechanoidMechanitorMechHiveCommunicationUtility
                    .TryGetContactableMechHive(out _))
            {
                return false;
            }

            map = ResolveFirstPlayerHomeColonyMap();
            return map != null;
        }

        public static List<ThingDef> GetConditionCausers(int threatPoints)
        {
            return MechClusterBuildingUtility.GetConditionCausers(threatPoints);
        }

        public static bool IsConditionCauser(ThingDef? def, int threatPoints)
        {
            return MechClusterBuildingUtility.IsConditionCauser(def, threatPoints);
        }

        public static bool TryPrepare(
            MechClusterDeploymentOrder? order,
            out MechClusterDeploymentSession? session,
            out string errorKey)
        {
            session = null;
            errorKey = ErrorInvalidRequest;
            if (order == null)
            {
                return false;
            }

            if (!TryResolveAvailableMap(out Map? map) || map == null)
            {
                errorKey = ErrorUnavailable;
                return false;
            }

            int threatPoints = MechClusterDeploymentOrder.ClampThreatPoints(order.ThreatPoints);
            ThingDef? selectedConditionCauser = order.ConditionCauser;
            if (selectedConditionCauser != null
                && !IsConditionCauser(selectedConditionCauser, threatPoints))
            {
                return false;
            }

            int expectedCost = MechClusterDeploymentOrder.ComputeCost(
                threatPoints,
                selectedConditionCauser != null);
            if (order.Cost != expectedCost)
            {
                return false;
            }

            try
            {
                MechClusterSketch sketch = MechClusterGenerator.GenerateClusterSketch(
                    threatPoints,
                    map,
                    startDormant: true,
                    forceNoConditionCauser: true);
                if (sketch?.buildingsSketch == null)
                {
                    errorKey = ErrorGenerationFailed;
                    return false;
                }

                // 原版允许不生成额外机械族；null表示本次没有额外机械族，不是草图生成失败。
                if (sketch.pawns == null)
                {
                    sketch.pawns = new List<MechClusterSketch.Mech>();
                }

                MechClusterBuildingUtility.RemoveGeneratedProblemCausers(sketch.buildingsSketch);
                if (selectedConditionCauser != null
                    && !MechClusterBuildingUtility.TryAddConditionCauser(
                        sketch,
                        selectedConditionCauser))
                {
                    errorKey = ErrorConditionCauserPlacement;
                    return false;
                }

                session = new MechClusterDeploymentSession(
                    map,
                    sketch,
                    selectedConditionCauser,
                    threatPoints,
                    expectedCost,
                    order.Revision);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP] Failed to prepare requested mech cluster: " + ex);
                errorKey = ErrorGenerationFailed;
                return false;
            }
        }

        public static bool IsSessionValidForOrder(
            MechClusterDeploymentSession? session,
            MechClusterDeploymentOrder order)
        {
            if (session == null || order == null)
            {
                return false;
            }

            return session.OrderRevision == order.Revision
                && session.ThreatPoints == order.ThreatPoints
                && session.Cost == order.Cost
                && session.ConditionCauser == order.ConditionCauser
                && IsDeploymentTargetMapValid(session.Map);
        }

        public static bool TryHandlePlacementRotation(MechClusterDeploymentSession session)
        {
            RotationDirection direction = RotationDirection.None;
            if (KeyBindingDefOf.Designator_RotateRight.KeyDownEvent)
            {
                direction = RotationDirection.Clockwise;
            }
            else if (KeyBindingDefOf.Designator_RotateLeft.KeyDownEvent)
            {
                direction = RotationDirection.Counterclockwise;
            }

            if (direction == RotationDirection.None)
            {
                return false;
            }

            session.Rotate(direction);
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            Event.current.Use();
            return true;
        }

        public static AcceptanceReport ValidatePlacement(
            MechClusterDeploymentSession? session,
            IntVec3 center)
        {
            if (session == null
                || !center.IsValid
                || !IsDeploymentTargetMapValid(session.Map))
            {
                return ErrorUnavailable.Translate();
            }

            Map map = session.Map;
            Sketch buildings = session.Sketch.buildingsSketch;
            if (Find.CurrentMap != map)
            {
                return "MAP_MechanoidMechanitor.MechHiveCommunication.SpecialProtocols.Cluster.Error.WrongMap"
                    .Translate();
            }

            if (buildings.AnyThingOutOfBounds(map, center))
            {
                return "MAP_MechanoidMechanitor.MechHiveCommunication.SpecialProtocols.Cluster.Error.OutOfBounds"
                    .Translate();
            }

            List<SketchEntity> entities = buildings.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                SketchEntity entity = entities[i];
                IntVec3 at = entity.pos + center;
                if (entity is SketchBuildable buildable)
                {
                    if (!buildable.CanBuildOnTerrain(at, map)
                        || buildable.FirstPermanentBlockerAt(at, map) != null)
                    {
                        return "MAP_MechanoidMechanitor.MechHiveCommunication.SpecialProtocols.Cluster.Error.Blocked"
                            .Translate();
                    }
                }

                foreach (IntVec3 cell in entity.OccupiedRect.MovedBy(center))
                {
                    AcceptanceReport report = ValidateOccupiedCell(map, cell);
                    if (!report.Accepted)
                    {
                        return report;
                    }
                }
            }

            List<MechClusterSketch.Mech> pawns = session.Sketch.pawns;
            for (int i = 0; i < pawns.Count; i++)
            {
                AcceptanceReport report = ValidateMechDropCell(
                    map,
                    pawns[i].position + center);
                if (!report.Accepted)
                {
                    return report;
                }
            }

            return AcceptanceReport.WasAccepted;
        }

        public static void DrawPlacementBounds(
            MechClusterDeploymentSession session,
            IntVec3 center)
        {
            AcceptanceReport report = ValidatePlacement(session, center);
            CellRect bounds = GetPlacementBounds(session, center);
            PlacementEdgeCells.Clear();
            PlacementEdgeCells.AddRange(bounds.Cells);
            GenDraw.DrawFieldEdges(
                PlacementEdgeCells,
                report.Accepted ? Color.green : Color.red);
        }

        public static MechClusterDeploymentResult TryDeploy(
            MechClusterDeploymentSession? session,
            IntVec3 center)
        {
            if (session == null
                || !IsDeploymentTargetMapValid(session.Map)
                || (session.ConditionCauser != null
                    && !IsConditionCauser(session.ConditionCauser, session.ThreatPoints)))
            {
                return MechClusterDeploymentResult.Failed(ErrorUnavailable);
            }

            Map map = session.Map;
            AcceptanceReport placement = ValidatePlacement(session, center);
            if (!placement.Accepted)
            {
                return MechClusterDeploymentResult.Failed(ErrorInvalidRequest);
            }

            int expectedCost = MechClusterDeploymentOrder.ComputeCost(
                session.ThreatPoints,
                session.ConditionCauser != null);
            if (session.Cost != expectedCost)
            {
                return MechClusterDeploymentResult.Failed(ErrorInvalidRequest);
            }

            int credits = GameComponent_MechanoidMechanitorStoryState
                .GetPurgeDirectiveRewardPoints();
            if (credits < session.Cost)
            {
                return MechClusterDeploymentResult.Failed(ErrorInsufficientCredits);
            }

            if (!GameComponent_MechanoidMechanitorStoryState
                    .TrySpendPurgeDirectiveCredits(session.Cost))
            {
                return MechClusterDeploymentResult.Failed(ErrorChargeFailed);
            }

            List<Thing> spawned;
            try
            {
                // 扣款后进入生成；异常记为已提交失败，避免部分生成后白嫖。
                spawned = SpawnRequestedCluster(center, map, session);
            }
            catch (Exception ex)
            {
                Log.Error("[MAP] Requested mech cluster failed after commit: " + ex);
                return MechClusterDeploymentResult.FailedAfterCommit(
                    ErrorCommittedFailure);
            }

            if (spawned == null || spawned.Count == 0)
            {
                Log.Error("[MAP] Requested mech cluster produced no spawned things.");
                return MechClusterDeploymentResult.FailedAfterCommit(
                    ErrorCommittedFailure);
            }

            SendFriendlyClusterLetter(spawned);
            return MechClusterDeploymentResult.Succeeded;
        }

        private static CellRect GetPlacementBounds(
            MechClusterDeploymentSession session,
            IntVec3 center)
        {
            CellRect bounds = session.Sketch.buildingsSketch.OccupiedRect.MovedBy(center);
            List<MechClusterSketch.Mech> pawns = session.Sketch.pawns;
            for (int i = 0; i < pawns.Count; i++)
            {
                bounds = bounds.Encapsulate(pawns[i].position + center);
            }

            return bounds;
        }

        private static List<Thing> SpawnRequestedCluster(
            IntVec3 center,
            Map map,
            MechClusterDeploymentSession session)
        {
            List<Thing> spawnedThings = new List<Thing>();
            MechClusterSketch sketch = session.Sketch;
            if (Faction.OfMechanoids == null)
            {
                Log.Warning("[MAP] Could not spawn mech cluster, no world mech faction found.");
                return spawnedThings;
            }

            // 与原版 MechClusterUtility.SpawnCluster 一致：仅在正式提交后清理蓝图。
            foreach (IntVec3 item in sketch.buildingsSketch.OccupiedRect)
            {
                IntVec3 c = item + center;
                if (!c.InBounds(map))
                {
                    continue;
                }

                List<Thing> thingList = c.GetThingList(map);
                Thing? blueprint = null;
                for (int i = 0; i < thingList.Count; i++)
                {
                    if (thingList[i].def.IsBlueprint)
                    {
                        blueprint = thingList[i];
                        break;
                    }
                }

                blueprint?.Destroy();
            }

            // wipeIfCollides:true → SketchThing.TransportPod 使用 WipeMode.VanishOrMoveAside。
            Sketch.SpawnMode spawnMode = Sketch.SpawnMode.TransportPod;
            sketch.buildingsSketch.Spawn(
                map,
                center,
                Faction.OfMechanoids,
                Sketch.SpawnPosType.Unchanged,
                spawnMode,
                wipeIfCollides: true,
                forceTerrainAffordance: false,
                clearEdificeWhereFloor: false,
                spawnedThings,
                sketch.startDormant,
                buildRoofsInstantly: false,
                canSpawnThing: null,
                onFailedToSpawnThing: (IntVec3 spot, SketchEntity entity) =>
                {
                    if (entity is SketchThing sketchThing
                        && sketchThing.def != ThingDefOf.Wall
                        && sketchThing.def != ThingDefOf.Barricade)
                    {
                        entity.SpawnNear(
                            spot,
                            map,
                            12f,
                            Faction.OfMechanoids,
                            spawnMode,
                            wipeIfCollides: true,
                            forceTerrainAffordance: false,
                            spawnedThings,
                            sketch.startDormant);
                    }
                });

            float defendRadius = Mathf.Sqrt(
                    sketch.buildingsSketch.OccupiedSize.x
                        * sketch.buildingsSketch.OccupiedSize.x
                        + sketch.buildingsSketch.OccupiedSize.z
                            * sketch.buildingsSketch.OccupiedSize.z)
                / 2f
                + 6f;
            LordJob_MechanoidDefendBase lordJob = sketch.startDormant
                ? new LordJob_SleepThenMechanoidsDefend(
                    spawnedThings,
                    Faction.OfMechanoids,
                    defendRadius,
                    center,
                    canAssaultColony: false,
                    isMechCluster: true)
                : new LordJob_MechanoidsDefend(
                    spawnedThings,
                    Faction.OfMechanoids,
                    defendRadius,
                    center,
                    canAssaultColony: false,
                    isMechCluster: true);
            Lord lord = LordMaker.MakeNewLord(Faction.OfMechanoids, lordJob, map);

            // 建筑初始化与完整节点共用；机械族 Pawn 另行落地，避免被建筑初始化重复处理。
            List<Thing> buildingsOnly = new List<Thing>(spawnedThings);
            MechClusterBuildingInitUtility.InitializeSpawnedBuildings(
                buildingsOnly,
                Faction.OfMechanoids,
                lordJob,
                lord,
                MechClusterBuildingInitUtility.InitOptions.ForClusterDeployment(
                    session.ConditionCauser,
                    sketch.startDormant));

            if (!sketch.pawns.NullOrEmpty())
            {
                for (int i = 0; i < sketch.pawns.Count; i++)
                {
                    MechClusterSketch.Mech pawnSketch = sketch.pawns[i];
                    IntVec3 result = pawnSketch.position + center;
                    if (!result.Standable(map)
                        && !CellFinder.TryFindRandomCellNear(
                            result,
                            map,
                            12,
                            x => x.Standable(map),
                            out result))
                    {
                        continue;
                    }

                    Pawn pawn = PawnGenerator.GeneratePawn(
                        pawnSketch.kindDef,
                        Faction.OfMechanoids);
                    CompCanBeDormant? dormant = pawn.TryGetComp<CompCanBeDormant>();
                    if (dormant != null)
                    {
                        if (sketch.startDormant)
                        {
                            dormant.ToSleep();
                        }
                        else
                        {
                            dormant.WakeUp();
                        }
                    }

                    lord.AddPawn(pawn);
                    spawnedThings.Add(pawn);

                    ActiveTransporterInfo info = new ActiveTransporterInfo();
                    info.innerContainer.TryAdd(pawn, 1);
                    info.openDelay = 60;
                    info.leaveSlag = false;
                    info.despawnPodBeforeSpawningThing = true;
                    info.spawnWipeMode = WipeMode.Vanish;
                    DropPodUtility.MakeDropPodAt(result, map, info, Faction.OfMechanoids);
                }
            }

            // 非休眠时：建筑已由公共初始化唤醒；此处仅唤醒草图额外机械族（若尚未唤醒）。
            if (!sketch.startDormant)
            {
                for (int i = 0; i < spawnedThings.Count; i++)
                {
                    if (spawnedThings[i] is Pawn)
                    {
                        spawnedThings[i]
                            .TryGetComp<CompWakeUpDormant>()
                            ?.Activate(null, sendSignal: true, silent: true);
                    }
                }
            }

            return spawnedThings;
        }

        private static AcceptanceReport ValidateOccupiedCell(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map))
            {
                return "MAP_MechanoidMechanitor.MechHiveCommunication.SpecialProtocols.Cluster.Error.OutOfBounds"
                    .Translate();
            }

            if (cell.Roofed(map))
            {
                return "MAP_MechanoidMechanitor.MechHiveCommunication.SpecialProtocols.Cluster.Error.Roofed"
                    .Translate();
            }

            return AcceptanceReport.WasAccepted;
        }

        private static AcceptanceReport ValidateMechDropCell(Map map, IntVec3 cell)
        {
            AcceptanceReport baseReport = ValidateOccupiedCell(map, cell);
            if (!baseReport.Accepted)
            {
                return baseReport;
            }

            // 只校验基础地形可通行性；建筑/物品由落地时 WipeMode.Vanish 处理，预览不拒绝。
            TerrainDef terrain = cell.GetTerrain(map);
            if (terrain == null || terrain.passability == Traversability.Impassable)
            {
                return "MAP_MechanoidMechanitor.MechHiveCommunication.SpecialProtocols.Cluster.Error.Blocked"
                    .Translate();
            }

            return AcceptanceReport.WasAccepted;
        }

        private static void SendFriendlyClusterLetter(List<Thing> spawned)
        {
            try
            {
                IEnumerable<Thing> targets = spawned.Where(
                    thing => thing != null
                        && thing.def != ThingDefOf.Wall
                        && thing.def != ThingDefOf.Barricade);
                Find.LetterStack.ReceiveLetter(
                    "MAP_MechanoidMechanitor.MechHiveCommunication.SpecialProtocols.Cluster.LetterLabel"
                        .Translate(),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.SpecialProtocols.Cluster.LetterText"
                        .Translate(),
                    LetterDefOf.PositiveEvent,
                    new LookTargets(targets),
                    Faction.OfMechanoids);
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP] Failed to send requested mech cluster letter: " + ex);
            }
        }
    }
}
