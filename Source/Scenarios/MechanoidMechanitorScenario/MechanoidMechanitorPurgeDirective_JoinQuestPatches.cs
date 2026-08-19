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
}
