using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
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

            // 在处理信任变化前，确保关系恶化时声明前信任锁定能够立即收紧；
            // 即使本次好感变化来自自然好感目标，其导致的关系类别变化也应立即同步。
            GameComponent_SymbiosisCovenantState.CurrentComponent
                ?.NotifyGoodwillChangedRelationMayHaveShifted(__state.ordinaryFaction);

            // 由自然好感目标逐步推动的好感变化不能再增加信任，避免自我循环。
            if (reason == HistoryEventDefOf.ReachNaturalGoodwill)
            {
                return;
            }

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
                // 大约每 +4 派系好感提供 +1 信任：+1→+1，+4→+1，+5→+2，+8→+2，+9→+3。
                // 这里只给「基础值」，成长倍率统一由 AdjustTrust 内部应用。
                int trust = Mathf.CeilToInt(goodwillDelta / 4f);
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
                2,
                "MAP_MechanoidMechanitor.Symbiosis.TrustReason.Trade".Translate(),
                SymbiosisCovenantTrustSource.Trade);
        }
    }

    /// <summary>
    /// 普通派系委托成功奖励。
    /// 联合军事行动与 Gravcore_Mechhive 主脑任务各有专用 Trust / Unity 奖励，
    /// 必须在这里明确分流，绝不能让它们再叠加一次普通任务奖励。
    /// 其它独立世界事件（例如摧毁共同敌人据点）仍按各自入口结算，不受本分流影响。
    /// </summary>
    [HarmonyPatch(typeof(Quest), nameof(Quest.End))]
    public static class SymbiosisCovenant_QuestEnded_Patch
    {
        /// <summary>普通派系委托成功的基础信任奖励（×1.0 基础值）。</summary>
        private const int NormalQuestTrustBase = 20;

        // 必须是 public：作为 public Patch 方法的 __state 参数类型，
        // 私有嵌套类型会导致可访问性不一致的编译错误。
        public enum QuestRewardKind : byte
        {
            None = 0,
            Normal = 1,
            JointOperation = 2,
            GravcoreMechhive = 3
        }

        public struct QuestRewardState
        {
            public QuestRewardKind Kind;

            /// <summary>仅 Normal 分支使用：本次应获得普通任务奖励的普通派系。</summary>
            public List<Faction>? OrdinaryFactions;
        }

        [HarmonyPrefix]
        public static void Prefix(
            Quest __instance,
            QuestEndOutcome outcome,
            out QuestRewardState __state)
        {
            __state = default;
            if (!GameComponent_SymbiosisCovenantState.IsActive
                || outcome != QuestEndOutcome.Success
                || __instance.Historical
                || !__instance.EverAccepted)
            {
                return;
            }

            // 联合军事行动：由 QuestPart_SymbiosisCovenantJointOperation 自己结算
            // Unity +35 与有效参与派系 Trust +35，这里必须跳过普通奖励。
            if (SymbiosisCovenantJointOperationUtility.HasJointOperationPart(__instance))
            {
                __state.Kind = QuestRewardKind.JointOperation;
                return;
            }

            // 主脑任务：由共生盟约专用入口结算盟约成员 Trust +75 与 Unity +100。
            // 严格按 root defName 识别，不依赖任务标题或名称文本。
            if (SymbiosisCovenantCerebrexSupportUtility.IsGravcoreMechhiveQuest(__instance))
            {
                __state.Kind = QuestRewardKind.GravcoreMechhive;
                return;
            }

            __state.Kind = QuestRewardKind.Normal;
            __state.OrdinaryFactions = __instance.InvolvedFactions
                .Where(MechanoidMechanitorOrdinaryFactionUtility.IsOrdinaryFaction)
                .Distinct()
                .ToList();
        }

        [HarmonyPostfix]
        public static void Postfix(Quest __instance, QuestRewardState __state)
        {
            // 只有本次调用真正把任务结束为成功才结算，兼容其他补丁跳过原方法的情况。
            if (__instance.State != QuestState.EndedSuccess)
            {
                return;
            }

            switch (__state.Kind)
            {
                case QuestRewardKind.Normal:
                    for (int i = 0; i < __state.OrdinaryFactions!.Count; i++)
                    {
                        GameComponent_SymbiosisCovenantState.TryAdjustTrust(
                            __state.OrdinaryFactions[i],
                            NormalQuestTrustBase,
                            "MAP_MechanoidMechanitor.Symbiosis.TrustReason.Quest".Translate(),
                            SymbiosisCovenantTrustSource.Quest);
                    }

                    break;

                case QuestRewardKind.GravcoreMechhive:
                    SymbiosisCovenantCerebrexSupportUtility
                        .TryApplyGravcoreMechhiveCompletionReward(__instance);
                    break;
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
                    30,
                    "MAP_MechanoidMechanitor.Symbiosis.TrustReason.MechHiveNode"
                        .Translate(),
                    SymbiosisCovenantTrustSource.MechHiveNode);
                return;
            }

            // 只奖励与该被摧毁派系敌对的记录。这里沿用既有语义：
            // 不要求对方已经是 CovenantMember，本次不得偷偷加上该限制。
            IReadOnlyList<SymbiosisCovenantFactionRecord> records =
                state.GetRecordsSorted();
            for (int i = 0; i < records.Count; i++)
            {
                Faction? covenantFaction = records[i].Faction;
                if (covenantFaction != null && covenantFaction.HostileTo(__state))
                {
                    GameComponent_SymbiosisCovenantState.TryAdjustTrust(
                        covenantFaction,
                        25,
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

            // 与「摧毁机械巢 Settlement」是两条不同的 Harmony 入口，
            // 基础值同为 +30，但触发条件不同，不得合并。
            GameComponent_SymbiosisCovenantState.TryAdjustTrustForAll(
                30,
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
