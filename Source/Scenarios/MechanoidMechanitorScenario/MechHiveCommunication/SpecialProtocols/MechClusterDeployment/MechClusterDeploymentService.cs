using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

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
            "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Error.Unavailable";

        public const string ErrorInvalidRequest =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Error.InvalidRequest";

        public const string ErrorGenerationFailed =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Error.GenerationFailed";

        public const string ErrorConditionCauserPlacement =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Error.ConditionCauserPlacement";

        public const string ErrorInsufficientCredits =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Error.InsufficientCredits";

        public const string ErrorChargeFailed =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Error.ChargeFailed";

        public const string ErrorCommittedFailure =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Error.CommittedFailure";

        private const string MemberTag = "MechClusterMember";

        private const string ProblemCauserTag = "MechClusterProblemCauser";

        private const float InitiationChance = 0.6f;

        private static readonly FloatRange InitiationDelay = new FloatRange(0.1f, 15f);

        private static readonly FloatRange MechAssemblerInitialDelayDays =
            new FloatRange(0.5f, 1.5f);

        private static readonly List<IntVec3> PlacementEdgeCells = new List<IntVec3>();

        public static bool TryResolveAvailableMap(Map? preferredMap, out Map? map)
        {
            map = null;
            if (!ModsConfig.RoyaltyActive
                || !MechanoidMechanitorMechHiveCommunicationUtility
                    .TryGetContactableMechHive(out _))
            {
                return false;
            }

            map = MechanoidOvermindDeliveryService.ResolvePlayerDeliveryMap(preferredMap);
            return map != null;
        }

        public static List<ThingDef> GetConditionCausers(int threatPoints)
        {
            List<ThingDef> result = new List<ThingDef>();
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (IsConditionCauser(def, threatPoints))
                {
                    result.Add(def);
                }
            }

            result.Sort((a, b) =>
                string.Compare(a.LabelCap, b.LabelCap, StringComparison.CurrentCulture));
            return result;
        }

        public static bool IsConditionCauser(ThingDef? def, int threatPoints)
        {
            if (def?.building?.buildingTags == null
                || def.category != ThingCategory.Building
                || def.thingClass == null
                || !typeof(Building).IsAssignableFrom(def.thingClass)
                || def.building.minMechClusterPoints > threatPoints)
            {
                return false;
            }

            List<string> tags = def.building.buildingTags;
            return tags.Contains(MemberTag) && tags.Contains(ProblemCauserTag);
        }

        public static bool TryPrepare(
            MechClusterDeploymentOrder? order,
            Map? preferredMap,
            out MechClusterDeploymentSession? session,
            out string errorKey)
        {
            session = null;
            errorKey = ErrorInvalidRequest;
            if (order == null || !order.Selected)
            {
                return false;
            }

            if (!TryResolveAvailableMap(preferredMap, out Map? map) || map == null)
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
                if (sketch?.buildingsSketch == null || sketch.pawns == null)
                {
                    errorKey = ErrorGenerationFailed;
                    return false;
                }

                RemoveGeneratedProblemCausers(sketch.buildingsSketch);
                if (selectedConditionCauser != null
                    && !TryAddConditionCauser(sketch, selectedConditionCauser))
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
            if (session == null || order == null || !order.Selected)
            {
                return false;
            }

            return session.OrderRevision == order.Revision
                && session.ThreatPoints == order.ThreatPoints
                && session.Cost == order.Cost
                && session.ConditionCauser == order.ConditionCauser
                && session.Map != null
                && !session.Map.Disposed;
        }

        public static AcceptanceReport ValidatePlacement(
            MechClusterDeploymentSession? session,
            IntVec3 center)
        {
            if (session == null
                || !center.IsValid
                || session.Map == null
                || session.Map.Disposed
                || !MechanoidOvermindDeliveryService.IsPlayerOwnedMap(session.Map))
            {
                return ErrorUnavailable.Translate();
            }

            Map map = session.Map;
            Sketch buildings = session.Sketch.buildingsSketch;
            if (Find.CurrentMap != map)
            {
                return "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Error.WrongMap"
                    .Translate();
            }

            if (buildings.AnyThingOutOfBounds(map, center))
            {
                return "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Error.OutOfBounds"
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
                        return "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Error.Blocked"
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
                || !TryResolveAvailableMap(session.Map, out Map? map)
                || map != session.Map
                || (session.ConditionCauser != null
                    && !IsConditionCauser(session.ConditionCauser, session.ThreatPoints)))
            {
                return MechClusterDeploymentResult.Failed(ErrorUnavailable);
            }

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
                spawned = SpawnRequestedCluster(center, map!, session);
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

            bool applyRandomInitiation = Rand.Chance(InitiationChance);
            float randomInitiationDays = InitiationDelay.RandomInRange;
            int assemblerDelay = (int)(MechAssemblerInitialDelayDays.RandomInRange * 60000f);
            ThingDef? requestedConditionCauser = session.ConditionCauser;

            for (int i = 0; i < spawnedThings.Count; i++)
            {
                Thing thing = spawnedThings[i];
                thing.TryGetComp<CompSpawnerPawn>()?.CalculateNextPawnSpawnTick(assemblerDelay);
                if (thing.TryGetComp<CompProjectileInterceptor>() != null)
                {
                    lordJob.AddThingToNotifyOnDefeat(thing);
                }

                CompInitiatable? initiatable = thing.TryGetComp<CompInitiatable>();
                if (initiatable != null)
                {
                    if (requestedConditionCauser != null
                        && thing.def == requestedConditionCauser)
                    {
                        // 落地后 1 tick 即视为初始化完成；不套用原版 0.1–15 天随机延迟。
                        initiatable.initiationDelayTicksOverride = 1;
                    }
                    else if (applyRandomInitiation)
                    {
                        initiatable.initiationDelayTicksOverride =
                            (int)(60000f * randomInitiationDays);
                    }
                }

                if (thing is Building building && IsBuildingThreat(building))
                {
                    lord.AddBuilding(building);
                }

                thing.SetFaction(Faction.OfMechanoids);
            }

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

            if (!sketch.startDormant)
            {
                for (int i = 0; i < spawnedThings.Count; i++)
                {
                    spawnedThings[i]
                        .TryGetComp<CompWakeUpDormant>()
                        ?.Activate(null, sendSignal: true, silent: true);
                }
            }

            return spawnedThings;
        }

        private static bool IsBuildingThreat(Thing b)
        {
            CompPawnSpawnOnWakeup? spawnOnWakeup = b.TryGetComp<CompPawnSpawnOnWakeup>();
            if (spawnOnWakeup != null && spawnOnWakeup.CanSpawn)
            {
                return true;
            }

            CompSpawnerPawn? spawnerPawn = b.TryGetComp<CompSpawnerPawn>();
            if (spawnerPawn != null && spawnerPawn.pawnsLeftToSpawn != 0)
            {
                return true;
            }

            if (!b.def.building.IsTurret)
            {
                return b.TryGetComp<CompCauseGameCondition>() != null;
            }

            return true;
        }

        private static void RemoveGeneratedProblemCausers(Sketch sketch)
        {
            List<SketchThing> things = sketch.Things;
            for (int i = things.Count - 1; i >= 0; i--)
            {
                SketchThing thing = things[i];
                if (IsConditionCauser(thing.def, MechClusterDeploymentOrder.MaxThreatPoints))
                {
                    sketch.Remove(thing);
                }
            }
        }

        private static bool TryAddConditionCauser(
            MechClusterSketch cluster,
            ThingDef conditionCauser)
        {
            Sketch sketch = cluster.buildingsSketch;
            CellRect searchRect = sketch.OccupiedRect.ExpandedBy(6);
            List<IntVec3> cells = searchRect.Cells.ToList();
            IntVec3 center = searchRect.CenterCell;
            cells.Sort((a, b) =>
                DistanceSquared(a, center).CompareTo(DistanceSquared(b, center)));

            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                if (sketch.WouldCollide(conditionCauser, cell, Rot4.North)
                    || OverlapsMechSpawn(cluster, conditionCauser, cell))
                {
                    continue;
                }

                ThingDef? stuff = null;
                try
                {
                    stuff = GenStuff.RandomStuffByCommonalityFor(conditionCauser);
                }
                catch (Exception)
                {
                    if (conditionCauser.MadeFromStuff)
                    {
                        continue;
                    }
                }

                if (sketch.AddThing(
                        conditionCauser,
                        cell,
                        Rot4.North,
                        stuff,
                        wipeIfCollides: false))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool OverlapsMechSpawn(
            MechClusterSketch cluster,
            ThingDef def,
            IntVec3 position)
        {
            CellRect occupied = GenAdj.OccupiedRect(position, Rot4.North, def.Size);
            for (int i = 0; i < cluster.pawns.Count; i++)
            {
                if (occupied.Contains(cluster.pawns[i].position))
                {
                    return true;
                }
            }

            return false;
        }

        private static int DistanceSquared(IntVec3 a, IntVec3 b)
        {
            int dx = a.x - b.x;
            int dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static AcceptanceReport ValidateOccupiedCell(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map))
            {
                return "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Error.OutOfBounds"
                    .Translate();
            }

            if (cell.Roofed(map))
            {
                return "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Error.Roofed"
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
                return "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Error.Blocked"
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
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.LetterLabel"
                        .Translate(),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.LetterText"
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
