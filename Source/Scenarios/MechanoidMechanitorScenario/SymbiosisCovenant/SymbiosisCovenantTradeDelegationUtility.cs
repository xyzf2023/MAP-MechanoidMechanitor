using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class SymbiosisCovenantTradeDelegationUtility
    {
        public static bool IsAvailableNow(
            GameComponent_SymbiosisCovenantState? state,
            out SymbiosisCovenantDelegationLevelSettings? levelSettings)
        {
            levelSettings = null;
            if (!GameComponent_SymbiosisCovenantState.IsActive || state == null)
            {
                return false;
            }

            if (state.CovenantLevel < 2 || state.CovenantMemberCount <= 0)
            {
                return false;
            }

            levelSettings = SymbiosisCovenantLevelEffectUtility
                .GetTradeDelegationSettingsForLevel(state.CovenantLevel);
            return levelSettings != null;
        }

        public static bool IsMapEligible(Map? map)
        {
            if (map == null || !map.IsPlayerHome || map.mapPawns.FreeColonistsSpawnedCount <= 0)
            {
                return false;
            }

            foreach (GameCondition condition in map.GameConditionManager.ActiveConditions)
            {
                if (condition.def.preventNeutralVisitors || condition.def.preventIncidents)
                {
                    return false;
                }
            }

            return RCellFinder.TryFindRandomPawnEntryCell(
                out _,
                map,
                CellFinder.EdgeRoadChance_Neutral);
        }

        public static IEnumerable<Map> EligiblePlayerHomeMapsInRandomOrder()
        {
            return Find.Maps.Where(IsMapEligible).InRandomOrder();
        }

        public static bool MeetsParticipantArrivalRules(Faction faction, Map map, bool desperate = false)
        {
            if (faction == null
                || faction.IsPlayer
                || faction.defeated
                || faction.temporary
                || faction.Hidden
                || faction.HostileTo(Faction.OfPlayer))
            {
                return false;
            }

            if (!desperate
                && (!faction.def.allowedArrivalTemperatureRange.Includes(map.mapTemperature.OutdoorTemp)
                    || !faction.def.allowedArrivalTemperatureRange.Includes(map.mapTemperature.SeasonalTemp)))
            {
                return false;
            }

            if (!faction.def.arrivalLayerWhitelist.NullOrEmpty()
                && !faction.def.arrivalLayerWhitelist.Contains(map.Tile.LayerDef))
            {
                return false;
            }
            if (!faction.def.arrivalLayerBlacklist.NullOrEmpty()
                && faction.def.arrivalLayerBlacklist.Contains(map.Tile.LayerDef))
            {
                return false;
            }
            if (map.Tile.LayerDef.onlyAllowWhitelistedArrivals
                && (faction.def.arrivalLayerWhitelist.NullOrEmpty()
                    || !faction.def.arrivalLayerWhitelist.Contains(map.Tile.LayerDef)))
            {
                return false;
            }
            if (!faction.def.neutralArrivalLayerWhitelist.NullOrEmpty()
                && !faction.def.neutralArrivalLayerWhitelist.Contains(map.Tile.LayerDef))
            {
                return false;
            }
            if (!faction.def.neutralArrivalLayerBlacklist.NullOrEmpty()
                && faction.def.neutralArrivalLayerBlacklist.Contains(map.Tile.LayerDef))
            {
                return false;
            }
            if (NeutralGroupIncidentUtility.AnyBlockingHostileLord(map, faction))
            {
                return false;
            }

            return true;
        }

        public static bool TryGetLeadContext(
            Faction faction,
            Map map,
            IncidentParms parms,
            out PawnGroupMaker? groupMaker,
            out TraderKindDef? traderKind)
        {
            groupMaker = null;
            traderKind = null;

            if (!MeetsParticipantArrivalRules(faction, map))
            {
                return false;
            }

            List<TraderKindDef> eligibleTraderKinds = GetEligibleTraderKinds(faction, map);
            if (eligibleTraderKinds.Count == 0)
            {
                return false;
            }

            TraderKindDef? requested = parms.traderKind;
            if (requested != null)
            {
                if (!eligibleTraderKinds.Contains(requested))
                {
                    return false;
                }
                traderKind = requested;
            }
            else if (!eligibleTraderKinds.TryRandomElementByWeight(
                         kind => kind != null
                             ? Math.Max(0f, kind.CalculatedCommonality)
                             : 0f,
                         out traderKind))
            {
                return false;
            }

            IncidentParms groupIncidentParms = new IncidentParms
            {
                target = map,
                faction = faction,
                traderKind = traderKind,
                points = Math.Max(1f, parms.points),
                pawnIdeo = parms.pawnIdeo,
                pawnGroupMakerSeed = parms.pawnGroupMakerSeed
            };
            PawnGroupMakerParms makerParms = IncidentParmsUtility.GetDefaultPawnGroupMakerParms(
                PawnGroupKindDefOf.Trader,
                groupIncidentParms,
                ensureCanGenerateAtLeastOnePawn: true);

            if (!PawnGroupMakerUtility.TryGetRandomPawnGroupMaker(makerParms, out groupMaker)
                || groupMaker == null
                || !groupMaker.traders.Any(option => option.kind != null && option.kind.trader)
                || !groupMaker.carriers.Any(option => option.kind != null
                    && option.kind.RaceProps.packAnimal
                    && (!map.Tile.Valid
                        || Find.WorldGrid[map.Tile].PrimaryBiome.IsPackAnimalAllowed(option.kind.race))))
            {
                groupMaker = null;
                traderKind = null;
                return false;
            }

            return true;
        }

        public static List<TraderKindDef> GetEligibleTraderKinds(Faction faction, Map map)
        {
            List<TraderKindDef> result = new List<TraderKindDef>();
            if (faction.def.caravanTraderKinds.NullOrEmpty())
            {
                return result;
            }

            for (int i = 0; i < faction.def.caravanTraderKinds.Count; i++)
            {
                TraderKindDef kind = faction.def.caravanTraderKinds[i];
                if (kind == null
                    || !kind.requestable
                    || kind.tradeCurrency != TradeCurrency.Silver
                    || string.Equals(kind.category, "Slaver", StringComparison.OrdinalIgnoreCase)
                    || kind.CalculatedCommonality <= 0f)
                {
                    continue;
                }

                if (kind.faction != null && kind.faction != faction.def)
                {
                    continue;
                }

                if (kind.permitRequiredForTrading != null
                    && !map.mapPawns.FreeColonists.Any(
                        pawn => pawn.royalty != null
                            && pawn.royalty.HasPermit(kind.permitRequiredForTrading, faction)))
                {
                    continue;
                }

                result.Add(kind);
            }

            return result;
        }

        public static List<Faction> SelectParticipants(
            GameComponent_SymbiosisCovenantState state,
            Map map,
            Faction leadFaction,
            SymbiosisCovenantDelegationLevelSettings settings)
        {
            int desired = Math.Max(1, settings.participantFactionCount.RandomInRange);
            List<Faction> candidates = new List<Faction>();
            IReadOnlyList<SymbiosisCovenantFactionRecord> records = state.GetRecordsSorted();
            for (int i = 0; i < records.Count; i++)
            {
                Faction? faction = records[i].Faction;
                if (records[i].CovenantMember
                    && faction != null
                    && faction != leadFaction
                    && MeetsParticipantArrivalRules(faction, map)
                    && SymbiosisCovenantTradeDelegationPawnUtility.CanGenerateRepresentative(faction, map.Tile))
                {
                    candidates.Add(faction);
                }
            }

            List<Faction> result = new List<Faction> { leadFaction };
            foreach (Faction candidate in candidates.InRandomOrder())
            {
                if (result.Count >= desired)
                {
                    break;
                }
                result.Add(candidate);
            }

            return result;
        }

        public static Faction? SelectLeadFaction(
            IEnumerable<Faction> candidates,
            Faction? lastLeadFaction)
        {
            List<Faction> list = candidates.ToList();
            if (list.Count == 0)
            {
                return null;
            }

            float repeatWeight = SymbiosisCovenantTradeDelegationDefOf
                .MAP_SymbiosisCovenant_TradeDelegationConfig.repeatedLeadWeight;
            return list.TryRandomElementByWeight(
                faction => faction == lastLeadFaction ? repeatWeight : 1f,
                out Faction selected)
                    ? selected
                    : list.RandomElement();
        }

        public static bool TryExecuteOnMap(
            Map? map,
            bool forced,
            Faction? forcedLead = null)
        {
            if (!IsMapEligible(map) || map == null)
            {
                return false;
            }

            IncidentWorker worker = SymbiosisCovenantTradeDelegationDefOf
                .MAP_SymbiosisCovenant_TradeDelegation.Worker;
            IncidentParms parms = new IncidentParms
            {
                target = map,
                faction = forcedLead,
                forced = forced,
                sendLetter = true,
                points = TraderCaravanUtility.GenerateGuardPoints()
            };
            return HasEligibleLead(map, parms, forcedLead)
                && worker.CanFireNow(parms)
                && worker.TryExecute(parms);
        }

        public static bool TryExecuteOnAnyEligibleMap(bool forced, Faction? forcedLead = null)
        {
            foreach (Map map in EligiblePlayerHomeMapsInRandomOrder())
            {
                if (TryExecuteOnMap(map, forced, forcedLead))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasEligibleLead(Map map, IncidentParms parms, Faction? forcedLead)
        {
            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (!IsAvailableNow(state, out _))
            {
                return false;
            }

            if (forcedLead != null)
            {
                return state!.GetRecord(forcedLead)?.CovenantMember == true
                    && TryGetLeadContext(forcedLead, map, parms, out _, out _);
            }

            IReadOnlyList<SymbiosisCovenantFactionRecord> records = state!.GetRecordsSorted();
            for (int i = 0; i < records.Count; i++)
            {
                Faction? faction = records[i].Faction;
                if (records[i].CovenantMember
                    && faction != null
                    && TryGetLeadContext(faction, map, parms, out _, out _))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
