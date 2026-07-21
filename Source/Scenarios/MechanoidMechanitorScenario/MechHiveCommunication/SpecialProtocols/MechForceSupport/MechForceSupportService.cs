using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public readonly struct MechForceSupportDeploymentResult
    {
        public readonly bool Success;

        public readonly bool Committed;

        public readonly string? ErrorKey;

        public MechForceSupportDeploymentResult(
            bool success,
            bool committed,
            string? errorKey)
        {
            Success = success;
            Committed = committed;
            ErrorKey = errorKey;
        }

        public static MechForceSupportDeploymentResult Succeeded =>
            new MechForceSupportDeploymentResult(true, true, null);

        public static MechForceSupportDeploymentResult Failed(string errorKey) =>
            new MechForceSupportDeploymentResult(false, false, errorKey);

        public static MechForceSupportDeploymentResult FailedAfterCommit(
            string errorKey) =>
            new MechForceSupportDeploymentResult(false, true, errorKey);
    }

    public static class MechForceSupportService
    {
        public const string ErrorUnavailable =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.ForceSupport.Error.Unavailable";

        public const string ErrorInvalidRequest =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.ForceSupport.Error.InvalidRequest";

        public const string ErrorGenerationFailed =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.ForceSupport.Error.GenerationFailed";

        public const string ErrorInsufficientCredits =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.ForceSupport.Error.InsufficientCredits";

        public const string ErrorChargeFailed =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.ForceSupport.Error.ChargeFailed";

        public const string ErrorCommittedFailure =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.ForceSupport.Error.CommittedFailure";

        public static bool HasAvailableMap()
        {
            if (!MechanoidMechanitorMechHiveCommunicationUtility
                    .TryGetContactableMechHive(out _))
            {
                return false;
            }

            List<Map> maps = Find.Maps;
            if (maps == null)
            {
                return false;
            }

            for (int i = 0; i < maps.Count; i++)
            {
                if (IsLoadedMap(maps[i]))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsLoadedMap(Map? map)
        {
            return map != null
                && !map.Disposed
                && Find.Maps != null
                && Find.Maps.Contains(map)
                && map.Parent != null
                && map.Parent.HasMap
                && map.Parent.Map == map;
        }

        public static bool CanSelectWorldTarget(GlobalTargetInfo target)
        {
            return TryResolveLoadedMap(target, out _);
        }

        public static bool TryResolveLoadedMap(
            GlobalTargetInfo target,
            out Map? map)
        {
            map = null;
            if (!(target.WorldObject is MapParent mapParent)
                || !mapParent.HasMap)
            {
                return false;
            }

            Map candidate = mapParent.Map;
            if (!IsLoadedMap(candidate))
            {
                return false;
            }

            map = candidate;
            return true;
        }

        public static TaggedString GetWorldTargetLabel(GlobalTargetInfo target)
        {
            if (TryResolveLoadedMap(target, out Map? map) && map != null)
            {
                return "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.ForceSupport.WorldTargetValid"
                    .Translate(map.Parent.LabelCap);
            }

            return "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.ForceSupport.WorldTargetInvalid"
                .Translate();
        }

        public static bool TryValidateOrder(
            MechForceSupportOrder? order,
            out string errorKey)
        {
            errorKey = ErrorInvalidRequest;
            if (order == null
                || !order.IsInputValid
                || order.ThreatPoints < MechForceSupportOrder.MinThreatPoints
                || order.Cost != MechForceSupportOrder.ComputeCost(order.ThreatPoints))
            {
                return false;
            }

            if (!HasAvailableMap())
            {
                errorKey = ErrorUnavailable;
                return false;
            }

            return true;
        }

        public static AcceptanceReport ValidateTargetCell(Map? map, IntVec3 cell)
        {
            if (!IsLoadedMap(map))
            {
                return ErrorUnavailable.Translate();
            }

            if (!cell.IsValid || !cell.InBounds(map))
            {
                return ErrorInvalidRequest.Translate();
            }

            return AcceptanceReport.WasAccepted;
        }

        public static MechForceSupportDeploymentResult TryDeploy(
            MechForceSupportOrder? order,
            Map? map,
            IntVec3 center)
        {
            if (!TryValidateOrder(order, out string errorKey)
                || order == null)
            {
                return MechForceSupportDeploymentResult.Failed(errorKey);
            }

            AcceptanceReport targetReport = ValidateTargetCell(map, center);
            if (!targetReport.Accepted || map == null)
            {
                return MechForceSupportDeploymentResult.Failed(
                    IsLoadedMap(map) ? ErrorInvalidRequest : ErrorUnavailable);
            }

            if (!MechanoidMechanitorMechHiveCommunicationUtility
                    .TryGetContactableMechHive(out Faction mechHive))
            {
                return MechForceSupportDeploymentResult.Failed(ErrorUnavailable);
            }

            int threatPoints = order.ThreatPoints;
            int expectedCost = MechForceSupportOrder.ComputeCost(threatPoints);
            if (order.Cost != expectedCost)
            {
                return MechForceSupportDeploymentResult.Failed(ErrorInvalidRequest);
            }

            IncidentParms parms = new IncidentParms
            {
                target = map,
                points = threatPoints,
                faction = mechHive,
                pawnGroupKind = PawnGroupKindDefOf.Combat,
                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                raidArrivalMode = PawnsArrivalModeDefOf.CenterDrop,
                spawnCenter = center,
                canKidnap = false,
                canSteal = false,
                canTimeoutOrFlee = true
            };

            List<Pawn> pawns = new List<Pawn>();
            try
            {
                PawnGroupMakerParms groupParms =
                    IncidentParmsUtility.GetDefaultPawnGroupMakerParms(
                        PawnGroupKindDefOf.Combat,
                        parms,
                        ensureCanGenerateAtLeastOnePawn: true);
                foreach (Pawn pawn in PawnGroupMakerUtility.GeneratePawns(
                    groupParms,
                    warnOnZeroResults: false))
                {
                    pawns.Add(pawn);
                }

                if (pawns.Count == 0
                    || !parms.raidArrivalMode.Worker.TryResolveRaidSpawnCenter(parms))
                {
                    DiscardGeneratedPawns(pawns);
                    return MechForceSupportDeploymentResult.Failed(
                        ErrorGenerationFailed);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP] Failed to generate requested mech force support: " + ex);
                DiscardGeneratedPawns(pawns);
                return MechForceSupportDeploymentResult.Failed(
                    ErrorGenerationFailed);
            }

            int credits = GameComponent_MechanoidMechanitorStoryState
                .GetPurgeDirectiveRewardPoints();
            if (credits < expectedCost)
            {
                DiscardGeneratedPawns(pawns);
                return MechForceSupportDeploymentResult.Failed(
                    ErrorInsufficientCredits);
            }

            if (!GameComponent_MechanoidMechanitorStoryState
                    .TrySpendPurgeDirectiveCredits(expectedCost))
            {
                DiscardGeneratedPawns(pawns);
                return MechForceSupportDeploymentResult.Failed(ErrorChargeFailed);
            }

            try
            {
                parms.raidArrivalMode.Worker.Arrive(pawns, parms);
                parms.raidStrategy.Worker.MakeLords(parms, pawns);
            }
            catch (Exception ex)
            {
                Log.Error("[MAP] Requested mech force support failed after commit: " + ex);
                return MechForceSupportDeploymentResult.FailedAfterCommit(
                    ErrorCommittedFailure);
            }

            SendSupportLetter(map, center, mechHive);
            return MechForceSupportDeploymentResult.Succeeded;
        }

        private static void DiscardGeneratedPawns(List<Pawn> pawns)
        {
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Spawned)
                {
                    continue;
                }

                try
                {
                    if (Find.WorldPawns.Contains(pawn))
                    {
                        Find.WorldPawns.RemovePawn(pawn);
                    }

                    Find.WorldPawns.PassToWorld(
                        pawn,
                        PawnDiscardDecideMode.Discard);
                }
                catch (Exception ex)
                {
                    Log.Warning("[MAP] Failed to discard unused support pawn: " + ex);
                }
            }

            pawns.Clear();
        }

        private static void SendSupportLetter(
            Map map,
            IntVec3 center,
            Faction mechHive)
        {
            try
            {
                Find.LetterStack.ReceiveLetter(
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.ForceSupport.LetterLabel"
                        .Translate(),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.ForceSupport.LetterText"
                        .Translate(),
                    LetterDefOf.PositiveEvent,
                    new LookTargets(center, map),
                    mechHive);
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP] Failed to send mech force support letter: " + ex);
            }
        }
    }
}
