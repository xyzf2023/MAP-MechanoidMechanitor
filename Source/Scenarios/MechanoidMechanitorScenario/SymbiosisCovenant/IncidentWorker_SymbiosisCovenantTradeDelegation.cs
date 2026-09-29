using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class IncidentWorker_SymbiosisCovenantTradeDelegation : IncidentWorker_NeutralGroup
    {
        protected override PawnGroupKindDef PawnGroupKindDef => PawnGroupKindDefOf.Trader;

        public override bool FactionCanBeGroupSource(
            Faction faction,
            IncidentParms parms,
            bool desperate = false)
        {
            if (!base.FactionCanBeGroupSource(faction, parms, desperate))
            {
                return false;
            }

            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (!SymbiosisCovenantTradeDelegationUtility.IsAvailableNow(state, out _)
                || state!.GetRecord(faction)?.CovenantMember != true)
            {
                return false;
            }

            return SymbiosisCovenantTradeDelegationUtility.TryGetLeadContext(
                faction,
                (Map)parms.target,
                parms,
                out _,
                out _);
        }

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!base.CanFireNowSub(parms))
            {
                return false;
            }

            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            return SymbiosisCovenantTradeDelegationUtility.IsAvailableNow(state, out _)
                && parms.target is Map map
                && SymbiosisCovenantTradeDelegationUtility.IsMapEligible(map);
        }

        protected override void ResolveParmsPoints(IncidentParms parms)
        {
            parms.points = TraderCaravanUtility.GenerateGuardPoints();
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!(parms.target is Map map))
            {
                return false;
            }

            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (!SymbiosisCovenantTradeDelegationUtility.IsAvailableNow(
                    state,
                    out SymbiosisCovenantDelegationLevelSettings? settings)
                || settings == null)
            {
                return false;
            }

            if (parms.points <= 0f)
            {
                parms.points = TraderCaravanUtility.GenerateGuardPoints();
            }

            if (!parms.spawnCenter.IsValid
                && !RCellFinder.TryFindRandomPawnEntryCell(
                    out parms.spawnCenter,
                    map,
                    CellFinder.EdgeRoadChance_Neutral))
            {
                return false;
            }

            Faction? leadFaction = ResolveLeadFaction(state!, parms);
            if (leadFaction == null)
            {
                return false;
            }

            parms.faction = leadFaction;
            if (!SymbiosisCovenantTradeDelegationUtility.TryGetLeadContext(
                    leadFaction,
                    map,
                    parms,
                    out PawnGroupMaker? leadGroupMaker,
                    out TraderKindDef? leadTraderKind)
                || leadGroupMaker == null
                || leadTraderKind == null)
            {
                return false;
            }
            parms.traderKind = leadTraderKind;

            List<Faction> participants =
                SymbiosisCovenantTradeDelegationUtility.SelectParticipants(
                    state!,
                    map,
                    leadFaction,
                    settings);
            List<Pawn> pawns = new List<Pawn>();
            SymbiosisCovenantDelegationStockResult? stock = null;
            try
            {
                Pawn trader = SymbiosisCovenantTradeDelegationPawnUtility.GenerateTrader(
                    leadFaction,
                    map.Tile,
                    leadGroupMaker,
                    leadTraderKind,
                    parms.pawnIdeo);
                pawns.Add(trader);

                // 先生成真实参与成员，再生成库存。这样生成失败的次要派系不会
                // 在本次代表团中留下“有贡献货物但没有代表”的幽灵参与记录。
                int targetMembers = Math.Max(
                    participants.Count,
                    settings.memberPawnCount.RandomInRange);
                Dictionary<Faction, int> slots =
                    SymbiosisCovenantTradeDelegationPawnUtility.AllocateMemberSlots(
                        participants,
                        targetMembers);
                int guardSlots = Math.Max(1, targetMembers - 1);
                float guardPoints = Math.Max(
                    0f,
                    parms.points * settings.guardPointMultiplier
                        - trader.kindDef.combatPower);

                for (int i = 0; i < participants.Count; i++)
                {
                    Faction faction = participants[i];
                    int desired = slots.TryGetValue(faction, out int allocated)
                        ? allocated
                        : 0;
                    if (faction == leadFaction)
                    {
                        desired = Math.Max(0, desired - 1);
                    }
                    if (desired <= 0)
                    {
                        continue;
                    }

                    float pointsForFaction = guardPoints * desired / guardSlots;
                    List<Pawn> generated =
                        SymbiosisCovenantTradeDelegationPawnUtility.GenerateFactionMembers(
                            faction,
                            map.Tile,
                            desired,
                            pointsForFaction,
                            preferTraderGuards: faction == leadFaction);
                    if (faction != leadFaction && generated.Count == 0)
                    {
                        participants.RemoveAt(i);
                        i--;
                        continue;
                    }
                    pawns.AddRange(generated);
                }

                stock = SymbiosisCovenantTradeDelegationStockUtility.GenerateStock(
                    map,
                    leadFaction,
                    leadTraderKind,
                    participants,
                    settings);

                foreach (Pawn pawnWare in
                         SymbiosisCovenantTradeDelegationPawnUtility.ExtractPawnWares(
                             stock.Wares,
                             leadFaction))
                {
                    pawns.Add(pawnWare);
                }

                SymbiosisCovenantTradeDelegationDef config =
                    SymbiosisCovenantTradeDelegationDefOf
                        .MAP_SymbiosisCovenant_TradeDelegationConfig;
                List<Pawn> carriers =
                    SymbiosisCovenantTradeDelegationPawnUtility.GenerateCarriers(
                        leadFaction,
                        map.Tile,
                        leadGroupMaker,
                        stock.Wares,
                        config.carrierThingDivisor,
                        config.maxCarriers);
                if (carriers.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"无法为 {leadFaction} 领导的盟约贸易代表团生成有效运输角色。");
                }
                pawns.AddRange(carriers);

                for (int i = 0; i < pawns.Count; i++)
                {
                    IntVec3 spawnCell = CellFinder.RandomClosewalkCellNear(
                        parms.spawnCenter,
                        map,
                        5);
                    GenSpawn.Spawn(pawns[i], spawnCell, map);
                    parms.storeGeneratedNeutralPawns?.Add(pawns[i]);
                    if (pawns[i].needs?.food != null)
                    {
                        pawns[i].needs.food.CurLevel = pawns[i].needs.food.MaxLevel;
                    }
                }

                if (!RCellFinder.TryFindRandomSpotJustOutsideColony(
                        pawns[0].Position,
                        map,
                        pawns[0],
                        out IntVec3 chillSpot,
                        cell => pawns.All(
                            pawn => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))))
                {
                    LordMaker.MakeNewLord(
                        leadFaction,
                        new LordJob_ExitMapBest(
                            LocomotionUrgency.Jog,
                            canDig: false,
                            canDefendSelf: true),
                        map,
                        pawns);
                    return false;
                }

                LordJob_SymbiosisCovenantTradeDelegation lordJob =
                    new LordJob_SymbiosisCovenantTradeDelegation(
                        leadFaction,
                        chillSpot,
                        participants,
                        leadTraderKind,
                        stock.ExtraTradeThingDefs);
                LordMaker.MakeNewLord(leadFaction, lordJob, map, pawns);
                SendDelegationLetter(
                    parms,
                    pawns,
                    leadFaction,
                    leadTraderKind,
                    participants,
                    settings.level);
                SymbiosisCovenantTradeDelegationScheduler.NotifyLeadUsed(
                    state!,
                    leadFaction);
                return true;
            }
            catch (Exception exception)
            {
                Log.Error($"[MAP-机械族机械师] 共生盟约联合贸易代表团生成失败：{exception}");
                CleanupFailedPawns(pawns, map, leadFaction);
                CleanupUnheldStock(stock);
                return false;
            }
        }

        private Faction? ResolveLeadFaction(
            GameComponent_SymbiosisCovenantState state,
            IncidentParms parms)
        {
            if (parms.faction != null)
            {
                return FactionCanBeGroupSource(parms.faction, parms)
                    ? parms.faction
                    : null;
            }

            Faction? lastLead = SymbiosisCovenantTradeDelegationScheduler.GetLastLeadFaction(state);
            return SymbiosisCovenantTradeDelegationUtility.SelectLeadFaction(
                CandidateFactions(parms),
                lastLead);
        }

        private void SendDelegationLetter(
            IncidentParms parms,
            List<Pawn> pawns,
            Faction leadFaction,
            TraderKindDef traderKind,
            IReadOnlyList<Faction> participants,
            int level)
        {
            string participantNames = string.Join(
                ", ",
                participants.Select(faction => faction.Name));
            TaggedString label =
                "MAP_MechanoidMechanitor.Symbiosis.Delegation.Letter.Label".Translate();
            TaggedString text =
                "MAP_MechanoidMechanitor.Symbiosis.Delegation.Letter.Text"
                    .Translate(
                        leadFaction.NameColored,
                        traderKind.LabelCap,
                        level,
                        participantNames);
            text += "\n\n" + "LetterCaravanArrivalCommonWarning".Translate();
            PawnRelationUtility.Notify_PawnsSeenByPlayer_Letter(
                pawns,
                ref label,
                ref text,
                "LetterRelatedPawnsNeutralGroup".Translate(Faction.OfPlayer.def.pawnsPlural),
                informEvenIfSeenBefore: true);
            SendStandardLetter(label, text, LetterDefOf.PositiveEvent, parms, pawns[0]);
        }

        private static void CleanupFailedPawns(
            List<Pawn> pawns,
            Map map,
            Faction leadFaction)
        {
            List<Pawn> spawned = pawns.Where(pawn => !pawn.Destroyed && pawn.Spawned).ToList();
            if (spawned.Count > 0 && spawned.All(pawn => pawn.GetLord() == null))
            {
                LordMaker.MakeNewLord(
                    leadFaction,
                    new LordJob_ExitMapBest(LocomotionUrgency.Jog, canDig: false, canDefendSelf: true),
                    map,
                    spawned);
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                if (!pawns[i].Destroyed && !pawns[i].Spawned)
                {
                    pawns[i].Destroy();
                }
            }
        }

        private static void CleanupUnheldStock(
            SymbiosisCovenantDelegationStockResult? stock)
        {
            if (stock == null)
            {
                return;
            }

            for (int i = 0; i < stock.Wares.Count; i++)
            {
                Thing thing = stock.Wares[i];
                if (thing != null
                    && !thing.Destroyed
                    && !thing.Spawned
                    && thing.ParentHolder == null)
                {
                    thing.Destroy();
                }
            }
        }
    }
}
