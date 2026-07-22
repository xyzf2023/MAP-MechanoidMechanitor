using System;
using System.Collections.Generic;
using System.Text;
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

        public const string ErrorNoAvailableTemplate =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.ForceSupport.Error.NoAvailableTemplate";

        public const string ErrorInsufficientCredits =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.ForceSupport.Error.InsufficientCredits";

        public const string ErrorChargeFailed =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.ForceSupport.Error.ChargeFailed";

        public const string ErrorCommittedFailure =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.ForceSupport.Error.CommittedFailure";

        private const string BreachKindDefName = "Mech_Termite_Breach";

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

        public static List<PawnGroupMaker> GetAvailableCombatTemplates(
            MechForceSupportOrder order,
            Map? map)
        {
            List<PawnGroupMaker> result = new List<PawnGroupMaker>();
            if (!TryBuildGroupParms(order, map, out _, out PawnGroupMakerParms? groupParms)
                || groupParms?.faction?.def?.pawnGroupMakers == null)
            {
                return result;
            }

            List<PawnGroupMaker> makers = groupParms.faction.def.pawnGroupMakers;
            for (int i = 0; i < makers.Count; i++)
            {
                PawnGroupMaker maker = makers[i];
                if (maker == null
                    || maker.kindDef != PawnGroupKindDefOf.Combat
                    || ContainsBreachOption(maker)
                    || !maker.CanGenerateFrom(groupParms))
                {
                    continue;
                }

                result.Add(maker);
            }

            return result;
        }

        public static void SanitizeSelectedTemplate(
            MechForceSupportOrder order,
            Map? map)
        {
            if (order.SelectedGroupMaker == null)
            {
                return;
            }

            List<PawnGroupMaker> available = GetAvailableCombatTemplates(order, map);
            if (!available.Contains(order.SelectedGroupMaker))
            {
                order.SetSelectedGroupMaker(null);
            }
        }

        public static string BuildTemplateDisplayName(PawnGroupMaker maker)
        {
            string composition = BuildTemplateCompositionLabel(maker);
            if (composition.NullOrEmpty())
            {
                composition = "—";
            }

            return "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.ForceSupport.TemplateNamed"
                .Translate(composition);
        }

        public static List<(PawnGroupMaker? maker, string label, string fullLabel)>
            BuildTemplateMenuEntries(MechForceSupportOrder order, Map? map)
        {
            List<(PawnGroupMaker? maker, string label, string fullLabel)> entries =
                new List<(PawnGroupMaker?, string, string)>();
            string randomLabel =
                "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.ForceSupport.TemplateRandom"
                    .Translate();
            entries.Add((null, randomLabel, randomLabel));

            List<PawnGroupMaker> makers = GetAvailableCombatTemplates(order, map);
            List<string> baseNames = new List<string>(makers.Count);
            Dictionary<string, int> nameCounts = new Dictionary<string, int>();
            for (int i = 0; i < makers.Count; i++)
            {
                string baseName = BuildTemplateDisplayName(makers[i]);
                baseNames.Add(baseName);
                if (nameCounts.TryGetValue(baseName, out int count))
                {
                    nameCounts[baseName] = count + 1;
                }
                else
                {
                    nameCounts[baseName] = 1;
                }
            }

            Dictionary<string, int> nameIndexes = new Dictionary<string, int>();
            Text.Font = GameFont.Small;
            for (int i = 0; i < makers.Count; i++)
            {
                string baseName = baseNames[i];
                string fullLabel = baseName;
                if (nameCounts[baseName] > 1)
                {
                    if (!nameIndexes.TryGetValue(baseName, out int index))
                    {
                        index = 0;
                    }

                    index++;
                    nameIndexes[baseName] = index;
                    fullLabel = baseName + " (" + index + ")";
                }

                string menuLabel = fullLabel.Truncate(280f);
                entries.Add((makers[i], menuLabel, fullLabel));
            }

            return entries;
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

            if (!TryBuildGroupParms(
                    order,
                    map,
                    out Faction mechHive,
                    out PawnGroupMakerParms? groupParms)
                || groupParms == null)
            {
                return MechForceSupportDeploymentResult.Failed(ErrorUnavailable);
            }

            List<PawnGroupMaker> available = GetAvailableCombatTemplates(order, map);
            if (available.Count == 0)
            {
                return MechForceSupportDeploymentResult.Failed(ErrorNoAvailableTemplate);
            }

            PawnGroupMaker? selected = order.SelectedGroupMaker;
            if (selected != null && !available.Contains(selected))
            {
                order.SetSelectedGroupMaker(null);
                selected = null;
            }

            PawnGroupMaker maker;
            if (selected != null)
            {
                maker = selected;
            }
            else if (!available.TryRandomElementByWeight(
                         gm => gm.commonality,
                         out maker)
                     || maker == null)
            {
                return MechForceSupportDeploymentResult.Failed(ErrorNoAvailableTemplate);
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
                foreach (Pawn pawn in maker.GeneratePawns(
                    groupParms,
                    errorOnZeroResults: false))
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

        private static bool TryBuildGroupParms(
            MechForceSupportOrder order,
            Map? map,
            out Faction faction,
            out PawnGroupMakerParms? groupParms)
        {
            faction = null!;
            groupParms = null;
            if (!MechanoidMechanitorMechHiveCommunicationUtility
                    .TryGetContactableMechHive(out Faction mechHive)
                || mechHive == null)
            {
                return false;
            }

            faction = mechHive;
            Map? parmsMap = map;
            if (!IsLoadedMap(parmsMap))
            {
                List<Map> maps = Find.Maps;
                if (maps != null)
                {
                    for (int i = 0; i < maps.Count; i++)
                    {
                        if (IsLoadedMap(maps[i]))
                        {
                            parmsMap = maps[i];
                            break;
                        }
                    }
                }
            }

            if (!IsLoadedMap(parmsMap))
            {
                return false;
            }

            IncidentParms incidentParms = new IncidentParms
            {
                target = parmsMap,
                points = order.ThreatPoints,
                faction = mechHive,
                pawnGroupKind = PawnGroupKindDefOf.Combat,
                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                raidArrivalMode = PawnsArrivalModeDefOf.CenterDrop,
                canKidnap = false,
                canSteal = false,
                canTimeoutOrFlee = true
            };

            groupParms = IncidentParmsUtility.GetDefaultPawnGroupMakerParms(
                PawnGroupKindDefOf.Combat,
                incidentParms,
                ensureCanGenerateAtLeastOnePawn: true);
            return groupParms != null;
        }

        private static bool ContainsBreachOption(PawnGroupMaker maker)
        {
            List<PawnGenOption>? options = maker.options;
            if (options == null)
            {
                return false;
            }

            for (int i = 0; i < options.Count; i++)
            {
                PawnKindDef? kind = options[i]?.kind;
                if (kind != null && kind.defName == BreachKindDefName)
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildTemplateCompositionLabel(PawnGroupMaker maker)
        {
            List<PawnGenOption>? options = maker.options;
            if (options == null || options.Count == 0)
            {
                return string.Empty;
            }

            List<string> labels = new List<string>();
            HashSet<PawnKindDef> seen = new HashSet<PawnKindDef>();
            for (int i = 0; i < options.Count; i++)
            {
                PawnKindDef? kind = options[i]?.kind;
                if (kind == null || !seen.Add(kind))
                {
                    continue;
                }

                labels.Add(kind.LabelCap.Resolve());
            }

            if (labels.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < labels.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append("、");
                }

                sb.Append(labels[i]);
            }

            return sb.ToString();
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
