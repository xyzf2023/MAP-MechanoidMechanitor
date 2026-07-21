using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechClusterDeploymentSession
    {
        public readonly Map Map;

        public readonly MechClusterSketch Sketch;

        public readonly ThingDef? ConditionCauser;

        public readonly int Cost;

        public readonly int OrderRevision;

        public MechClusterDeploymentSession(
            Map map,
            MechClusterSketch sketch,
            ThingDef? conditionCauser,
            int cost,
            int orderRevision)
        {
            Map = map;
            Sketch = sketch;
            ConditionCauser = conditionCauser;
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
        public const float ThreatPoints = 10000f;

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

        public static List<ThingDef> GetConditionCausers()
        {
            List<ThingDef> result = new List<ThingDef>();
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (IsConditionCauser(def))
                {
                    result.Add(def);
                }
            }

            result.Sort((a, b) =>
                string.Compare(a.LabelCap, b.LabelCap, StringComparison.CurrentCulture));
            return result;
        }

        public static bool IsConditionCauser(ThingDef? def)
        {
            if (def?.building?.buildingTags == null
                || def.category != ThingCategory.Building
                || def.thingClass == null
                || !typeof(Building).IsAssignableFrom(def.thingClass)
                || def.building.minMechClusterPoints > ThreatPoints)
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

            ThingDef? selectedConditionCauser = order.ConditionCauser;
            if (selectedConditionCauser != null
                && !IsConditionCauser(selectedConditionCauser))
            {
                return false;
            }

            try
            {
                MechClusterSketch sketch = MechClusterGenerator.GenerateClusterSketch(
                    ThreatPoints,
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
                    order.Cost,
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

            if (buildings.IsSpawningBlocked(map, center, Faction.OfMechanoids))
            {
                return "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Error.Blocked"
                    .Translate();
            }

            List<SketchEntity> entities = buildings.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                foreach (IntVec3 cell in entities[i].OccupiedRect.MovedBy(center))
                {
                    AcceptanceReport report = ValidateDropCell(map, cell, requireStandable: false);
                    if (!report.Accepted)
                    {
                        return report;
                    }
                }
            }

            List<MechClusterSketch.Mech> pawns = session.Sketch.pawns;
            for (int i = 0; i < pawns.Count; i++)
            {
                AcceptanceReport report = ValidateDropCell(
                    map,
                    pawns[i].position + center,
                    requireStandable: true);
                if (!report.Accepted)
                {
                    return report;
                }
            }

            return AcceptanceReport.WasAccepted;
        }

        public static void DrawPlacementGhost(
            MechClusterDeploymentSession session,
            IntVec3 center)
        {
            AcceptanceReport report = ValidatePlacement(session, center);
            session.Sketch.buildingsSketch.DrawGhost(
                center,
                Sketch.SpawnPosType.Unchanged,
                placingMode: true,
                thingToIgnore: null,
                validator: (_, __, ___, ____) => report.Accepted);
        }

        public static MechClusterDeploymentResult TryDeploy(
            MechClusterDeploymentSession? session,
            IntVec3 center)
        {
            if (session == null
                || !TryResolveAvailableMap(session.Map, out Map? map)
                || map != session.Map
                || (session.ConditionCauser != null
                    && !IsConditionCauser(session.ConditionCauser)))
            {
                return MechClusterDeploymentResult.Failed(ErrorUnavailable);
            }

            AcceptanceReport placement = ValidatePlacement(session, center);
            if (!placement.Accepted)
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
                // 从进入原版生成流程起视为已提交；异常时不退款，避免部分生成后免费保留。
                spawned = MechClusterUtility.SpawnCluster(
                    center,
                    map!,
                    session.Sketch,
                    dropInPods: true,
                    canAssaultColony: false,
                    questTag: null);
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

        private static void RemoveGeneratedProblemCausers(Sketch sketch)
        {
            List<SketchThing> things = sketch.Things;
            for (int i = things.Count - 1; i >= 0; i--)
            {
                SketchThing thing = things[i];
                if (IsConditionCauser(thing.def))
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

        private static AcceptanceReport ValidateDropCell(
            Map map,
            IntVec3 cell,
            bool requireStandable)
        {
            if (!cell.InBounds(map))
            {
                return "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Error.OutOfBounds"
                    .Translate();
            }

            RoofDef roof = map.roofGrid.RoofAt(cell);
            if (roof != null && roof.isThickRoof)
            {
                return "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Error.ThickRoof"
                    .Translate();
            }

            if (requireStandable && !cell.Standable(map))
            {
                return "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Error.Blocked"
                    .Translate();
            }

            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing is Building
                    || thing.def.IsBlueprint
                    || thing.def.IsFrame
                    || thing.def.preventSkyfallersLandingOn)
                {
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Error.Blocked"
                        .Translate();
                }
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
