using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// QuestPart_JoinPlayer 入口拦截：
    /// 只临时过滤会新增禁止血肉人口的 Pawn，让原版继续处理其余 Pawn，
    /// 执行结束后再恢复原始列表。不重写整个方法，也不整体 return false。
    /// </summary>
    [HarmonyPatch(
        typeof(QuestPart_JoinPlayer),
        nameof(QuestPart_JoinPlayer.Notify_QuestSignalReceived))]
    public static class
        MechanoidMechanitorPurgeDirective_QuestPartJoinPlayer_Patch
    {
        public sealed class PatchState
        {
            public List<Pawn>? OriginalPawns;
        }

        [HarmonyPrefix]
        public static void Prefix(
            QuestPart_JoinPlayer __instance,
            Signal signal,
            ref PatchState? __state)
        {
            __state = null;

            if (!MechanoidMechanitorPurgeDirectivePopulationPolicy.RestrictionActive
                || !__instance.joinPlayer
                || signal.tag != __instance.inSignal)
            {
                return;
            }

            List<Pawn> original = __instance.pawns;

            // 与原版一致的 destroyed 清理。
            original.RemoveAll(pawn => pawn == null || pawn.Destroyed);

            List<Pawn> filtered = new List<Pawn>(original.Count);
            bool blockedAny = false;

            foreach (Pawn pawn in original)
            {
                if (MechanoidMechanitorPurgeDirectivePopulationPolicy
                        .WouldAddForbiddenFreeColonist(
                            pawn,
                            Faction.OfPlayerSilentFail))
                {
                    blockedAny = true;
                    continue;
                }

                filtered.Add(pawn);
            }

            if (!blockedAny)
            {
                return;
            }

            __state = new PatchState
            {
                OriginalPawns = original
            };

            __instance.pawns = filtered;
        }

        [HarmonyPostfix]
        public static void Postfix(
            QuestPart_JoinPlayer __instance,
            PatchState? __state)
        {
            if (__state?.OriginalPawns != null)
            {
                __instance.pawns = __state.OriginalPawns;
            }
        }
    }

    /// <summary>
    /// QuestPart_PawnsArrive 入口拦截（仅白名单永久人口路线）：
    /// M03 倒地难民 / M04 囚犯救援 / M10 逃兵。
    /// 仅当 quest.root.defName 属于白名单时才临时过滤会被禁止的血肉 Pawn，
    /// 其余 Pawn（含机器人）照常走原版流程；绝不拦截临时 lodger 类 joinPlayer 任务
    /// （例如 Intro_Wimp、Hospitality_Refugee 初次到来）。
    /// </summary>
    [HarmonyPatch(
        typeof(QuestPart_PawnsArrive),
        nameof(QuestPart_PawnsArrive.Notify_QuestSignalReceived))]
    public static class
        MechanoidMechanitorPurgeDirective_QuestPartPawnsArrive_Patch
    {
        private static readonly HashSet<string> PermanentJoinQuestRoots =
            new HashSet<string>
        {
            "Intro_Deserter",
            "OpportunitySite_DownedRefugee",
            "OpportunitySite_PrisonerWillingToJoin"
        };

        public sealed class PatchState
        {
            public List<Pawn>? OriginalPawns;
        }

        [HarmonyPrefix]
        public static void Prefix(
            QuestPart_PawnsArrive __instance,
            Signal signal,
            ref PatchState? __state)
        {
            __state = null;

            if (!MechanoidMechanitorPurgeDirectivePopulationPolicy.RestrictionActive
                || !__instance.joinPlayer
                || signal.tag != __instance.inSignal)
            {
                return;
            }

            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null)
            {
                return;
            }

            string? rootDefName = __instance.quest?.root?.defName;
            if (rootDefName == null
                || !PermanentJoinQuestRoots.Contains(rootDefName))
            {
                return;
            }

            List<Pawn> original = __instance.pawns;

            // 与原版一致的 destroyed 清理。
            original.RemoveAll(pawn => pawn == null || pawn.Destroyed);

            List<Pawn> filtered = new List<Pawn>(original.Count);
            bool blockedAny = false;

            foreach (Pawn pawn in original)
            {
                if (MechanoidMechanitorPurgeDirectivePopulationPolicy
                        .WouldAddForbiddenFreeColonist(pawn, player))
                {
                    blockedAny = true;
                    continue;
                }

                filtered.Add(pawn);
            }

            if (!blockedAny)
            {
                return;
            }

            __state = new PatchState
            {
                OriginalPawns = original
            };

            __instance.pawns = filtered;
        }

        [HarmonyPostfix]
        public static void Postfix(
            QuestPart_PawnsArrive __instance,
            PatchState? __state)
        {
            if (__state?.OriginalPawns != null)
            {
                __instance.pawns = __state.OriginalPawns;
            }
        }
    }
}
