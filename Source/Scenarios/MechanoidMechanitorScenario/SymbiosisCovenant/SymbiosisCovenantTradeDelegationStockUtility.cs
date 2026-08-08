using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class SymbiosisCovenantDelegationStockResult
    {
        public readonly List<Thing> Wares = new List<Thing>();
        public readonly HashSet<ThingDef> ExtraTradeThingDefs = new HashSet<ThingDef>();
    }

    public static class SymbiosisCovenantTradeDelegationStockUtility
    {
        public static SymbiosisCovenantDelegationStockResult GenerateStock(
            Map map,
            Faction leadFaction,
            TraderKindDef leadTraderKind,
            IReadOnlyList<Faction> participants,
            SymbiosisCovenantDelegationLevelSettings settings)
        {
            SymbiosisCovenantTradeDelegationDef config =
                SymbiosisCovenantTradeDelegationDefOf.MAP_SymbiosisCovenant_TradeDelegationConfig;
            SymbiosisCovenantDelegationStockResult result =
                new SymbiosisCovenantDelegationStockResult();

            try
            {
                ThingSetMakerParams baseParams = new ThingSetMakerParams
                {
                    traderDef = leadTraderKind,
                    tile = map.Tile,
                    makingFaction = leadFaction
                };
                result.Wares.AddRange(ThingSetMakerDefOf.TraderStock.root.Generate(baseParams));

                AdjustSilver(result.Wares, settings);
                EnsureMinimumStock(
                    result,
                    settings.minimumStock,
                    map,
                    leadFaction,
                    leadTraderKind);
                AddPoolSlots(
                    result,
                    config.logisticsPool,
                    settings.logisticsSlots,
                    settings.level,
                    map,
                    leadFaction,
                    leadTraderKind);
                AddPoolSlots(
                    result,
                    config.rarePool,
                    settings.rareSlots,
                    settings.level,
                    map,
                    leadFaction,
                    leadTraderKind);

                if (settings.level >= 3 && participants.Count > 1)
                {
                    for (int i = 1; i < participants.Count; i++)
                    {
                        AddParticipantContribution(
                            result,
                            participants[i],
                            map,
                            leadTraderKind,
                            settings,
                            config);
                    }
                }

                return result;
            }
            catch
            {
                DestroyUnheldThings(result.Wares);
                throw;
            }
        }

        private static void AdjustSilver(
            List<Thing> wares,
            SymbiosisCovenantDelegationLevelSettings settings)
        {
            List<Thing> silverStacks = wares.Where(thing => thing.def == ThingDefOf.Silver).ToList();
            int baseSilver = silverStacks.Sum(thing => thing.stackCount);
            int target = Mathf.Clamp(
                Mathf.RoundToInt(baseSilver * settings.silverMultiplier),
                settings.silverClamp.min,
                settings.silverClamp.max);
            if (baseSilver <= 0)
            {
                target = Math.Max(settings.silverClamp.min, target);
            }

            Thing primary;
            if (silverStacks.Count == 0)
            {
                primary = ThingMaker.MakeThing(ThingDefOf.Silver);
                wares.Add(primary);
            }
            else
            {
                primary = silverStacks[0];
                for (int i = 1; i < silverStacks.Count; i++)
                {
                    wares.Remove(silverStacks[i]);
                    silverStacks[i].Destroy();
                }
            }

            primary.stackCount = Math.Max(0, target);
        }

        private static void EnsureMinimumStock(
            SymbiosisCovenantDelegationStockResult result,
            List<SymbiosisCovenantDelegationMinimumStock> minimumStock,
            Map map,
            Faction leadFaction,
            TraderKindDef leadTraderKind)
        {
            for (int i = 0; i < minimumStock.Count; i++)
            {
                ThingDef? def = minimumStock[i].thingDef;
                if (def == null || minimumStock[i].count <= 0)
                {
                    continue;
                }

                int current = result.Wares
                    .Where(thing => !(thing is Pawn) && thing.def == def)
                    .Sum(thing => thing.stackCount);
                int missing = minimumStock[i].count - current;
                if (missing <= 0)
                {
                    continue;
                }

                foreach (Thing thing in StockGeneratorUtility.TryMakeForStock(def, missing, leadFaction))
                {
                    FinalizeExtraThing(result, thing, map, leadFaction, leadTraderKind);
                }
            }
        }

        private static void AddPoolSlots(
            SymbiosisCovenantDelegationStockResult result,
            List<SymbiosisCovenantDelegationStockPoolEntry> pool,
            int slots,
            int level,
            Map map,
            Faction leadFaction,
            TraderKindDef leadTraderKind)
        {
            List<SymbiosisCovenantDelegationStockPoolEntry> available = pool
                .Where(entry => entry.AvailableAt(level))
                .ToList();
            int remainingSlots = Math.Max(0, slots);
            while (remainingSlots > 0 && available.Count > 0)
            {
                if (!available.TryRandomElementByWeight(entry => entry.weight, out var selected))
                {
                    break;
                }
                available.Remove(selected);
                remainingSlots--;

                foreach (StockGenerator generator in selected.generators)
                {
                    IEnumerable<Thing> generated;
                    try
                    {
                        generated = generator.GenerateThings(map.Tile, leadFaction).ToList();
                    }
                    catch (Exception exception)
                    {
                        Log.Error($"[MAP] 共生盟约联合商队库存生成失败：{exception}");
                        continue;
                    }

                    foreach (Thing thing in generated)
                    {
                        if (thing is Pawn || !thing.def.tradeability.TraderCanSell())
                        {
                            thing.Destroy();
                            continue;
                        }
                        FinalizeExtraThing(result, thing, map, leadFaction, leadTraderKind);
                    }
                }
            }
        }

        private static void FinalizeExtraThing(
            SymbiosisCovenantDelegationStockResult result,
            Thing thing,
            Map map,
            Faction leadFaction,
            TraderKindDef leadTraderKind)
        {
            thing.PostGeneratedForTrader(leadTraderKind, map.Tile, leadFaction);
            result.Wares.Add(thing);
            if (!leadTraderKind.WillTrade(thing.def))
            {
                result.ExtraTradeThingDefs.Add(thing.def);
            }
        }

        private static void AddParticipantContribution(
            SymbiosisCovenantDelegationStockResult result,
            Faction participant,
            Map map,
            TraderKindDef leadTraderKind,
            SymbiosisCovenantDelegationLevelSettings settings,
            SymbiosisCovenantTradeDelegationDef config)
        {
            List<TraderKindDef> traderKinds =
                SymbiosisCovenantTradeDelegationUtility.GetEligibleTraderKinds(participant, map);
            if (traderKinds.Count == 0)
            {
                return;
            }

            TraderKindDef sourceKind = traderKinds.RandomElementByWeight(
                kind => Math.Max(0.01f, kind.CalculatedCommonality));
            ThingSetMakerParams sourceParams = new ThingSetMakerParams
            {
                traderDef = sourceKind,
                tile = map.Tile,
                makingFaction = participant
            };
            List<Thing> candidates = ThingSetMakerDefOf.TraderStock.root.Generate(sourceParams).ToList();
            try
            {
                float budget = Math.Max(0f, settings.contributionMarketValue.RandomInRange);
                int wantedEntries = Math.Max(0, settings.contributionItemCount.RandomInRange);
                int takenEntries = 0;
                foreach (Thing candidate in candidates.InRandomOrder().ToList())
                {
                    if (takenEntries >= wantedEntries || budget <= 0f)
                    {
                        break;
                    }
                    if (!ContributionAllowed(candidate, config))
                    {
                        continue;
                    }

                    float unitValue = Math.Max(0.01f, candidate.MarketValue);
                    int affordableCount = Mathf.FloorToInt(budget / unitValue);
                    if (affordableCount <= 0)
                    {
                        continue;
                    }

                    int takeCount = Math.Min(candidate.stackCount, affordableCount);
                    Thing selected;
                    if (takeCount >= candidate.stackCount)
                    {
                        selected = candidate;
                        candidates.Remove(candidate);
                    }
                    else
                    {
                        selected = candidate.SplitOff(takeCount);
                    }

                    result.Wares.Add(selected);
                    if (!leadTraderKind.WillTrade(selected.def))
                    {
                        result.ExtraTradeThingDefs.Add(selected.def);
                    }
                    budget -= selected.MarketValue * selected.stackCount;
                    takenEntries++;
                }
            }
            finally
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (!candidates[i].Destroyed)
                    {
                        candidates[i].Destroy();
                    }
                }
            }
        }

        private static void DestroyUnheldThings(IEnumerable<Thing> things)
        {
            foreach (Thing thing in things)
            {
                if (thing != null
                    && !thing.Destroyed
                    && !thing.Spawned
                    && thing.ParentHolder == null)
                {
                    thing.Destroy();
                }
            }
        }

        private static bool ContributionAllowed(
            Thing thing,
            SymbiosisCovenantTradeDelegationDef config)
        {
            if (thing is Pawn || thing.def == ThingDefOf.Silver)
            {
                return false;
            }
            if (config.IsContributionExplicitlyExcluded(thing.def))
            {
                return false;
            }
            if (config.IsContributionExplicitlyAllowed(thing.def))
            {
                return thing.def.tradeability.TraderCanSell();
            }

            return thing.def.tradeability.TraderCanSell()
                && thing.def.PlayerAcquirable
                && thing.MarketValue > 0f;
        }
    }
}
