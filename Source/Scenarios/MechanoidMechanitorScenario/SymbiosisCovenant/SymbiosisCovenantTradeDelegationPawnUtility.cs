using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class SymbiosisCovenantTradeDelegationPawnUtility
    {
        public static bool CanGenerateRepresentative(Faction faction, PlanetTile tile)
        {
            return GetRepresentativeOptions(faction).Any(option => option.kind != null);
        }

        public static Pawn GenerateTrader(
            Faction faction,
            PlanetTile tile,
            PawnGroupMaker groupMaker,
            TraderKindDef traderKind,
            Ideo? ideo = null)
        {
            PawnGenOption traderOption = groupMaker.traders
                .Where(option => option.kind != null && option.kind.trader)
                .RandomElementByWeight(option => option.selectionWeight);

            Pawn pawn = PawnGenerator.GeneratePawn(
                new PawnGenerationRequest(
                    traderOption.kind,
                    faction,
                    PawnGenerationContext.NonPlayer,
                    tile,
                    forceGenerateNewPawn: false,
                    allowDead: false,
                    allowDowned: false,
                    canGeneratePawnRelations: true,
                    mustBeCapableOfViolence: false,
                    colonistRelationChanceFactor: 1f,
                    forceAddFreeWarmLayerIfNeeded: false,
                    allowGay: true,
                    allowPregnant: false,
                    allowFood: true,
                    allowAddictions: true,
                    fixedIdeo: ideo));
            pawn.mindState.wantsToTradeWithColony = true;
            PawnComponentsUtility.AddAndRemoveDynamicComponents(pawn, actAsIfSpawned: true);
            pawn.trader.traderKind = traderKind;
            TradeUtility.CheckGiveTraderQuest(pawn);
            return pawn;
        }

        public static List<Pawn> GenerateFactionMembers(
            Faction faction,
            PlanetTile tile,
            int desiredCount,
            float pointBudget,
            bool preferTraderGuards)
        {
            List<Pawn> result = new List<Pawn>();
            if (desiredCount <= 0)
            {
                return result;
            }

            List<PawnGenOption> options = preferTraderGuards
                ? GetTraderGuardOptions(faction)
                : GetRepresentativeOptions(faction);
            if (options.Count == 0 && preferTraderGuards)
            {
                options = GetRepresentativeOptions(faction);
            }
            if (options.Count == 0)
            {
                return result;
            }

            float remainingPoints = Math.Max(0f, pointBudget);
            for (int i = 0; i < desiredCount; i++)
            {
                List<PawnGenOption> affordable = remainingPoints > 0f
                    ? options
                        .Where(option => option.kind != null
                            && option.kind != PawnKindDefOf.Slave
                            && !option.kind.trader
                            && !option.kind.RaceProps.Animal
                            && option.Cost <= remainingPoints)
                        .ToList()
                    : new List<PawnGenOption>();

                PawnGenOption selected;
                if (affordable.Count > 0)
                {
                    selected = affordable.RandomElementByWeight(option => option.selectionWeight);
                }
                else
                {
                    if (result.Count > 0)
                    {
                        break;
                    }

                    selected = options
                        .Where(option => option.kind != null
                            && option.kind != PawnKindDefOf.Slave
                            && !option.kind.trader
                            && !option.kind.RaceProps.Animal)
                        .OrderBy(option => option.Cost)
                        .FirstOrDefault();
                    if (selected == null)
                    {
                        break;
                    }
                }

                Pawn pawn = PawnGenerator.GeneratePawn(
                    new PawnGenerationRequest(
                        selected.kind,
                        faction,
                        PawnGenerationContext.NonPlayer,
                        tile,
                        forceGenerateNewPawn: false,
                        allowDead: false,
                        allowDowned: false,
                        canGeneratePawnRelations: true,
                        mustBeCapableOfViolence: selected.kind.isFighter,
                        colonistRelationChanceFactor: 1f,
                        forceAddFreeWarmLayerIfNeeded: false,
                        allowGay: true,
                        allowPregnant: false,
                        allowFood: true,
                        allowAddictions: true));
                result.Add(pawn);
                remainingPoints = Math.Max(0f, remainingPoints - selected.Cost);
            }

            return result;
        }

        public static List<Pawn> GenerateCarriers(
            Faction faction,
            PlanetTile tile,
            PawnGroupMaker groupMaker,
            List<Thing> wares,
            int thingDivisor,
            int maxCarriers)
        {
            List<PawnGenOption> carrierOptions = groupMaker.carriers
                .Where(option => option.kind != null
                    && option.kind.RaceProps.packAnimal
                    && (!tile.Valid
                        || Find.WorldGrid[tile].PrimaryBiome.IsPackAnimalAllowed(option.kind.race)))
                .ToList();
            if (carrierOptions.Count == 0)
            {
                return new List<Pawn>();
            }

            List<Thing> cargo = wares.Where(thing => !(thing is Pawn)).ToList();
            int desired = Mathf.Clamp(
                Mathf.CeilToInt(cargo.Count / (float)Math.Max(1, thingDivisor)),
                1,
                Math.Max(1, maxCarriers));
            PawnKindDef kind = carrierOptions
                .RandomElementByWeight(option => option.selectionWeight).kind;

            List<Pawn> carriers = new List<Pawn>();
            for (int i = 0; i < desired; i++)
            {
                Pawn carrier = PawnGenerator.GeneratePawn(
                    new PawnGenerationRequest(
                        kind,
                        faction,
                        PawnGenerationContext.NonPlayer,
                        tile,
                        forceGenerateNewPawn: false,
                        allowDead: false,
                        allowDowned: false,
                        canGeneratePawnRelations: true,
                        mustBeCapableOfViolence: false,
                        colonistRelationChanceFactor: 1f,
                        forceAddFreeWarmLayerIfNeeded: false,
                        allowGay: true,
                        allowPregnant: false,
                        allowFood: true,
                        allowAddictions: true));
                carriers.Add(carrier);
            }

            for (int i = 0; i < cargo.Count; i++)
            {
                carriers[i % carriers.Count].inventory.innerContainer.TryAdd(cargo[i]);
            }

            return carriers;
        }

        public static IEnumerable<Pawn> ExtractPawnWares(
            List<Thing> wares,
            Faction leadFaction)
        {
            for (int i = 0; i < wares.Count; i++)
            {
                if (wares[i] is Pawn pawn)
                {
                    if (pawn.Faction != leadFaction)
                    {
                        pawn.SetFaction(leadFaction);
                    }
                    yield return pawn;
                }
            }
        }

        public static Dictionary<Faction, int> AllocateMemberSlots(
            IReadOnlyList<Faction> participants,
            int totalMemberPawns)
        {
            Dictionary<Faction, int> result = new Dictionary<Faction, int>();
            if (participants.Count == 0)
            {
                return result;
            }

            int total = Math.Max(participants.Count, totalMemberPawns);
            Faction lead = participants[0];
            int leadSlots = Mathf.Clamp(
                Mathf.RoundToInt(total * 0.45f),
                1,
                Math.Max(1, total - (participants.Count - 1)));
            result[lead] = leadSlots;
            for (int i = 1; i < participants.Count; i++)
            {
                result[participants[i]] = 1;
            }

            int assigned = result.Values.Sum();
            int cursor = 1;
            while (assigned < total)
            {
                Faction target;
                if (participants.Count == 1)
                {
                    target = lead;
                }
                else
                {
                    target = participants[cursor % participants.Count];
                    cursor++;
                }
                result[target]++;
                assigned++;
            }

            return result;
        }

        private static List<PawnGenOption> GetTraderGuardOptions(Faction faction)
        {
            if (faction.def.pawnGroupMakers.NullOrEmpty())
            {
                return new List<PawnGenOption>();
            }

            return faction.def.pawnGroupMakers
                .Where(maker => maker.kindDef == PawnGroupKindDefOf.Trader && !maker.guards.NullOrEmpty())
                .SelectMany(maker => maker.guards)
                .Where(IsRepresentativeOption)
                .ToList();
        }

        private static List<PawnGenOption> GetRepresentativeOptions(Faction faction)
        {
            List<PawnGenOption> traderGuards = GetTraderGuardOptions(faction);
            if (traderGuards.Count > 0)
            {
                return traderGuards;
            }

            if (faction.def.pawnGroupMakers.NullOrEmpty())
            {
                return new List<PawnGenOption>();
            }

            List<PawnGenOption> peaceful = faction.def.pawnGroupMakers
                .Where(maker => maker.kindDef == PawnGroupKindDefOf.Peaceful)
                .SelectMany(maker => maker.options)
                .Where(IsRepresentativeOption)
                .ToList();
            if (peaceful.Count > 0)
            {
                return peaceful;
            }

            // 没有贸易护卫或和平访问成员时，不退化到袭击/特殊 PawnGroupMaker。
            // 次要成员可以被跳过，避免为“联合代表”生成首领、Boss或袭击专用单位。
            return new List<PawnGenOption>();
        }

        private static bool IsRepresentativeOption(PawnGenOption option)
        {
            return option?.kind != null
                && option.kind != PawnKindDefOf.Slave
                && !option.kind.trader
                && !option.kind.RaceProps.Animal;
        }
    }
}
