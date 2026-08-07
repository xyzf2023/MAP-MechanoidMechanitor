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
            return TryGetRepresentativeOptions(
                faction,
                preferTraderGuards: false,
                out _,
                out _);
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
            if (desiredCount <= 0
                || !TryGetRepresentativeOptions(
                    faction,
                    preferTraderGuards,
                    out List<PawnGenOption> options,
                    out PawnGroupKindDef groupKind))
            {
                return result;
            }

            float effectiveBudget = Math.Max(1f, pointBudget);
            PawnGroupMakerParms groupParms = new PawnGroupMakerParms
            {
                groupKind = groupKind,
                tile = tile,
                faction = faction,
                points = effectiveBudget
            };

            // 具体 PawnKind / Xenotype 的选择继续交给原版点数选择器处理，
            // 从而保留 maxPerGroup、Bossgroup 预留、儿童限制、xenotype 战力倍率等约束。
            List<PawnGenOptionWithXenotype> selectedOptions =
                PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints(
                        effectiveBudget,
                        options,
                        groupParms)
                    .Take(desiredCount)
                    .ToList();

            // 分给某个参与派系的点数份额可能低于其最廉价成员。
            // 为保证“已选中的参与派系至少有一名真实代表”，仅在原版选择器一个都选不出时
            // 放宽一次首名代表的点数限制；仍通过 GetOptions 保留原版合法性与 Xenotype 筛选。
            if (selectedOptions.Count == 0)
            {
                const float fallbackPoints = 100000f;
                groupParms.points = fallbackPoints;
                List<PawnGenOptionWithXenotype> fallbackOptions =
                    PawnGroupMakerUtility.GetOptions(
                        groupParms,
                        faction.def,
                        options,
                        fallbackPoints,
                        fallbackPoints,
                        fallbackPoints);
                if (fallbackOptions.Count > 0)
                {
                    selectedOptions.Add(
                        fallbackOptions
                            .OrderBy(option => option.Cost)
                            .ThenByDescending(option => option.SelectionWeight)
                            .First());
                }
            }

            for (int i = 0; i < selectedOptions.Count; i++)
            {
                PawnGenOptionWithXenotype selected = selectedOptions[i];
                PawnKindDef kind = selected.Option.kind;
                Pawn pawn = PawnGenerator.GeneratePawn(
                    new PawnGenerationRequest(
                        kind,
                        faction,
                        PawnGenerationContext.NonPlayer,
                        tile,
                        forceGenerateNewPawn: false,
                        allowDead: false,
                        allowDowned: false,
                        canGeneratePawnRelations: true,
                        mustBeCapableOfViolence: kind.isFighter,
                        colonistRelationChanceFactor: 1f,
                        forceAddFreeWarmLayerIfNeeded: false,
                        allowGay: true,
                        allowPregnant: false,
                        allowFood: true,
                        allowAddictions: true,
                        forcedXenotype: selected.Xenotype));
                result.Add(pawn);
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

        private static bool TryGetRepresentativeOptions(
            Faction faction,
            bool preferTraderGuards,
            out List<PawnGenOption> options,
            out PawnGroupKindDef groupKind)
        {
            options = new List<PawnGenOption>();
            groupKind = PawnGroupKindDefOf.Peaceful;
            if (faction.def.pawnGroupMakers.NullOrEmpty())
            {
                return false;
            }

            List<PawnGenOption> traderGuards = GetTraderGuardOptions(faction);
            if (traderGuards.Count > 0)
            {
                options = traderGuards;
                groupKind = PawnGroupKindDefOf.Trader;
                return true;
            }

            List<PawnGenOption> peaceful = faction.def.pawnGroupMakers
                .Where(maker => maker.kindDef == PawnGroupKindDefOf.Peaceful)
                .SelectMany(maker => maker.options)
                .Where(IsRepresentativeOption)
                .ToList();
            if (peaceful.Count > 0)
            {
                options = peaceful;
                groupKind = PawnGroupKindDefOf.Peaceful;
                return true;
            }

            // 没有贸易护卫或和平访问成员时，不退化到袭击/特殊 PawnGroupMaker。
            // 次要成员可以被跳过，避免为“联合代表”生成首领、Boss或袭击专用单位。
            return false;
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

        private static bool IsRepresentativeOption(PawnGenOption option)
        {
            return option?.kind != null
                && option.kind != PawnKindDefOf.Slave
                && !option.kind.trader
                && !option.kind.factionLeader
                && !option.kind.RaceProps.Animal;
        }
    }
}
