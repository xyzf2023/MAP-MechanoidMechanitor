using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(
        typeof(MechanoidMechanitorStoryConfiguration),
        nameof(MechanoidMechanitorStoryConfiguration.Normalize))]
    public static class SymbiosisCovenant_RouteMutualExclusion_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(MechanoidMechanitorStoryConfiguration __instance)
        {
            if (__instance.purgeDirectiveEnabled
                && __instance.symbiosisCovenantEnabled)
            {
                __instance.symbiosisCovenantEnabled = false;
            }
        }
    }

    [HarmonyPatch(typeof(Faction), nameof(Faction.TryAffectGoodwillWith))]
    public static class SymbiosisCovenant_GoodwillChanged_Patch
    {
        public struct GoodwillChangeState
        {
            public Faction? ordinaryFaction;
            public int previousGoodwill;
            public bool trackTrust;
        }

        [HarmonyPrefix]
        public static bool Prefix(
            Faction __instance,
            Faction other,
            ref bool __result,
            out GoodwillChangeState __state)
        {
            __state = default;
            if (GameComponent_SymbiosisCovenantState.IsMechHiveHostileLocked(
                    __instance,
                    other)
                && !MechanoidMechanitorMechHiveRelationApplier.IsApplying)
            {
                __result = false;
                return false;
            }

            if (!GameComponent_SymbiosisCovenantState.IsActive
                || !MechanoidMechanitorOrdinaryFactionUtility.TryGetPlayerAndOrdinary(
                    __instance,
                    other,
                    out Faction player,
                    out Faction ordinary))
            {
                return true;
            }

            __state.ordinaryFaction = ordinary;
            __state.previousGoodwill = player.BaseGoodwillWith(ordinary);
            __state.trackTrust = true;
            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(
            bool __result,
            HistoryEventDef? reason,
            GoodwillChangeState __state)
        {
            if (!__result
                || !__state.trackTrust
                || __state.ordinaryFaction == null)
            {
                return;
            }

            // 由自然好感目标逐步推动的好感变化不能再增加信任，避免自我循环。
            if (reason == HistoryEventDefOf.ReachNaturalGoodwill)
            {
                return;
            }

            // 在处理信任变化前，确保关系恶化时声明前信任锁定能够立即收紧。
            GameComponent_SymbiosisCovenantState.CurrentComponent
                ?.NotifyGoodwillChangedRelationMayHaveShifted(__state.ordinaryFaction);

            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null)
            {
                return;
            }

            int goodwillDelta =
                player.BaseGoodwillWith(__state.ordinaryFaction)
                - __state.previousGoodwill;
            if (goodwillDelta > 0)
            {
                int trust = Math.Max(1, Math.Min(10, (goodwillDelta + 4) / 5));
                GameComponent_SymbiosisCovenantState.TryAdjustTrust(
                    __state.ordinaryFaction,
                    trust,
                    "MAP_MechanoidMechanitor.Symbiosis.TrustReason.Goodwill".Translate(),
                    SymbiosisCovenantTrustSource.Goodwill);
                return;
            }

            if (goodwillDelta < 0 && IsBetrayalReason(reason))
            {
                int trustLoss = -Math.Max(
                    5,
                    Math.Min(30, (Math.Abs(goodwillDelta) + 1) / 2));
                GameComponent_SymbiosisCovenantState.TryAdjustTrust(
                    __state.ordinaryFaction,
                    trustLoss,
                    "MAP_MechanoidMechanitor.Symbiosis.TrustReason.Betrayal".Translate(),
                    SymbiosisCovenantTrustSource.Betrayal);
            }
        }

        private static bool IsBetrayalReason(HistoryEventDef? reason)
        {
            string name = reason?.defName ?? string.Empty;
            if (name.Length == 0)
            {
                return false;
            }

            string[] betrayalTokens =
            {
                "Attack",
                "Attacked",
                "Kill",
                "Killed",
                "Kidnap",
                "Enslave",
                "Capture",
                "Prisoner",
                "Betray",
                "Abandon",
                "Harm"
            };
            for (int i = 0; i < betrayalTokens.Length; i++)
            {
                if (name.IndexOf(
                        betrayalTokens[i],
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.TryExecute))]
    public static class SymbiosisCovenant_TradeExecuted_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(out Faction? __state)
        {
            __state = null;
            if (!GameComponent_SymbiosisCovenantState.IsActive
                || TradeSession.giftMode)
            {
                return;
            }

            Faction? faction = TradeSession.trader?.Faction;
            if (faction != null
                && MechanoidMechanitorOrdinaryFactionUtility.IsOrdinaryFaction(faction))
            {
                __state = faction;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(bool __result, bool actuallyTraded, Faction? __state)
        {
            if (!__result || !actuallyTraded || __state == null)
            {
                return;
            }

            GameComponent_SymbiosisCovenantState.TryAdjustTrust(
                __state,
                1,
                "MAP_MechanoidMechanitor.Symbiosis.TrustReason.Trade".Translate(),
                SymbiosisCovenantTrustSource.Trade);
        }
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.End))]
    public static class SymbiosisCovenant_QuestEnded_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(
            Quest __instance,
            QuestEndOutcome outcome,
            out List<Faction>? __state)
        {
            __state = null;
            if (!GameComponent_SymbiosisCovenantState.IsActive
                || outcome != QuestEndOutcome.Success
                || !__instance.EverAccepted)
            {
                return;
            }

            __state = __instance.InvolvedFactions
                .Where(MechanoidMechanitorOrdinaryFactionUtility.IsOrdinaryFaction)
                .Distinct()
                .ToList();
        }

        [HarmonyPostfix]
        public static void Postfix(List<Faction>? __state)
        {
            if (__state == null)
            {
                return;
            }

            for (int i = 0; i < __state.Count; i++)
            {
                GameComponent_SymbiosisCovenantState.TryAdjustTrust(
                    __state[i],
                    10,
                    "MAP_MechanoidMechanitor.Symbiosis.TrustReason.Quest".Translate(),
                    SymbiosisCovenantTrustSource.Quest);
            }
        }
    }

    [HarmonyPatch(typeof(SettlementDefeatUtility), nameof(SettlementDefeatUtility.CheckDefeated))]
    public static class SymbiosisCovenant_SettlementDefeated_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Settlement factionBase, out Faction? __state)
        {
            __state = factionBase != null && !factionBase.Destroyed
                ? factionBase.Faction
                : null;
        }

        [HarmonyPostfix]
        public static void Postfix(Settlement factionBase, Faction? __state)
        {
            if (__state == null
                || factionBase == null
                || !factionBase.Destroyed
                || !GameComponent_SymbiosisCovenantState.IsActive)
            {
                return;
            }

            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (state == null)
            {
                return;
            }

            if (MechanoidMechanitorOrdinaryFactionUtility.IsOrdinaryFaction(__state))
            {
                state.RegisterSevereBetrayal(
                    __state,
                    "MAP_MechanoidMechanitor.Symbiosis.TrustReason.DestroyedSettlement"
                        .Translate());
            }

            Faction? mechHive = MechanoidMechanitorOrdinaryFactionUtility.TryGetMechHive();
            if (__state == mechHive)
            {
                GameComponent_SymbiosisCovenantState.TryAdjustTrustForAll(
                    8,
                    "MAP_MechanoidMechanitor.Symbiosis.TrustReason.MechHiveNode"
                        .Translate(),
                    SymbiosisCovenantTrustSource.MechHiveNode);
                return;
            }

            IReadOnlyList<SymbiosisCovenantFactionRecord> records =
                state.GetRecordsSorted();
            for (int i = 0; i < records.Count; i++)
            {
                Faction? covenantFaction = records[i].Faction;
                if (covenantFaction != null && covenantFaction.HostileTo(__state))
                {
                    GameComponent_SymbiosisCovenantState.TryAdjustTrust(
                        covenantFaction,
                        8,
                        "MAP_MechanoidMechanitor.Symbiosis.TrustReason.SharedEnemy"
                            .Translate(),
                        SymbiosisCovenantTrustSource.Other);
                }
            }
        }
    }

    [HarmonyPatch(typeof(MAPMechHiveNode), "MarkCleaned")]
    public static class SymbiosisCovenant_MechHiveNodeCleaned_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(MAPMechHiveNode __instance, out bool __state)
        {
            __state = __instance != null && !__instance.Cleaned;
        }

        [HarmonyPostfix]
        public static void Postfix(bool __state)
        {
            if (!__state)
            {
                return;
            }

            GameComponent_SymbiosisCovenantState.TryAdjustTrustForAll(
                8,
                "MAP_MechanoidMechanitor.Symbiosis.TrustReason.MechHiveNode".Translate(),
                SymbiosisCovenantTrustSource.MechHiveNode);
        }
    }

    [HarmonyPatch(typeof(Faction), nameof(Faction.SetRelationDirect))]
    public static class SymbiosisCovenant_MechHiveSetRelationDirect_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Faction __instance, Faction other)
        {
            return MechanoidMechanitorMechHiveRelationApplier.IsApplying
                || !GameComponent_SymbiosisCovenantState.IsMechHiveHostileLocked(
                    __instance,
                    other);
        }
    }

    [HarmonyPatch(
        typeof(MechanoidMechanitorMechHiveRelationApplier),
        nameof(MechanoidMechanitorMechHiveRelationApplier.ApplyExactMechHiveRelation))]
    public static class SymbiosisCovenant_MechHiveExactRelation_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(
            Faction mechHive,
            ref FactionRelationKind relationKind,
            ref bool hostileOnHarmByPlayer)
        {
            if (!GameComponent_SymbiosisCovenantState.IsMechHiveHostileLocked(
                    Faction.OfPlayerSilentFail,
                    mechHive))
            {
                return;
            }

            relationKind = FactionRelationKind.Hostile;
            hostileOnHarmByPlayer = false;
        }
    }

    [HarmonyPatch(typeof(Faction), nameof(Faction.SetRelation))]
    public static class SymbiosisCovenant_MechHiveSetRelation_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Faction __instance, FactionRelation relation)
        {
            if (MechanoidMechanitorMechHiveRelationApplier.IsApplying
                || relation?.other == null
                || !GameComponent_SymbiosisCovenantState.IsMechHiveHostileLocked(
                    __instance,
                    relation.other))
            {
                return;
            }

            Faction? mechHive = MechanoidMechanitorOrdinaryFactionUtility.TryGetMechHive();
            if (mechHive != null)
            {
                MechanoidMechanitorMechHiveRelationApplier.ApplyExactMechHiveRelation(
                    mechHive,
                    FactionRelationKind.Hostile,
                    hostileOnHarmByPlayer: false);
            }
        }
    }

    [HarmonyPatch(
        typeof(FactionDialogMaker),
        nameof(FactionDialogMaker.FactionDialogFor))]
    public static class SymbiosisCovenant_FactionDialogSecretContact_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            Pawn negotiator,
            Faction faction,
            DiaNode __result)
        {
            if (__result == null)
            {
                return;
            }

            if (!SymbiosisCovenantDiplomacyUtility.ShouldShowSecretContact(faction))
            {
                return;
            }

            DiaOption option = SymbiosisCovenantDiplomacyUtility.CreateSecretContactOption(
                negotiator,
                faction);

            // 插入在原版“断开连接”选项之前。
            int insertIndex = __result.options.FindLastIndex(o => o.resolveTree);
            if (insertIndex >= 0)
            {
                __result.options.Insert(insertIndex, option);
            }
            else
            {
                __result.options.Add(option);
            }
        }
    }
}
