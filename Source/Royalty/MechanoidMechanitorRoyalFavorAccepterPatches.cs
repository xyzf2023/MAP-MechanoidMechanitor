using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// Royal Favor 存在两条原版领取路径：
    /// 1. chosenPawnSignal：任务完成后通过 ChoosePawn 信件选择领取者；
    /// 2. giveToAccepter：接任务时指定接受者，完成后荣誉直接发给 Quest.AccepterPawn。
    ///
    /// MechanoidMechanitorRoyalFavorPatches 已处理第一条路径。
    /// 本文件仅补齐第二条路径，不扩大普通 RequiresAccepter 任务对机械族的接受资格。
    /// </summary>
    internal static class MechanoidMechanitorRoyalFavorAccepterUtility
    {
        [ThreadStatic]
        private static Quest? currentAccepterSelectionQuest;

        internal static Quest? CurrentAccepterSelectionQuest
        {
            get => currentAccepterSelectionQuest;
            set => currentAccepterSelectionQuest = value;
        }

        internal static bool IsRoyalFavorAccepterQuest(Quest? quest)
        {
            if (!ModsConfig.RoyaltyActive || quest == null)
            {
                return false;
            }

            List<QuestPart> parts = quest.PartsListForReading;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] is QuestPart_GiveRoyalFavor { giveToAccepter: true })
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool CanSupportedMechanitorAccept(
            Pawn? pawn,
            Quest? quest)
        {
            if (!IsRoyalFavorAccepterQuest(quest)
                || !MechanoidMechanitorRoyaltyUtility
                    .IsRoyaltyEligibleMechanitor(pawn))
            {
                return false;
            }

            Pawn candidate = pawn!;
            MechanoidMechanitorRoyaltyUtility
                .EnsureRoyaltyInfrastructure(candidate);

            // 保留 QuestUtility.CanPawnAcceptQuest 除 IsFreeColonist 外的原版门槛。
            if (candidate.Destroyed
                || candidate.Downed
                || candidate.Suspended
                || candidate.IsQuestLodger())
            {
                return false;
            }

            List<QuestPart> parts = quest!.PartsListForReading;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] is QuestPart_RequirementsToAccept requirement
                    && !requirement.CanPawnAccept(candidate))
                {
                    return false;
                }
            }

            return true;
        }

        internal static List<Pawn> AppendEligibleCandidates(
            List<Pawn>? vanillaCandidates)
        {
            List<Pawn> result = vanillaCandidates != null
                ? new List<Pawn>(vanillaCandidates)
                : new List<Pawn>();

            Quest? quest = CurrentAccepterSelectionQuest;
            if (!IsRoyalFavorAccepterQuest(quest))
            {
                return result;
            }

            List<Pawn> allPlayerLocations =
                PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive;
            for (int i = 0; i < allPlayerLocations.Count; i++)
            {
                Pawn pawn = allPlayerLocations[i];
                if (result.Contains(pawn)
                    || !MechanoidMechanitorRoyaltyUtility
                        .IsRoyaltyEligibleMechanitor(pawn))
                {
                    continue;
                }

                MechanoidMechanitorRoyaltyUtility
                    .EnsureRoyaltyInfrastructure(pawn);
                result.Add(pawn);
            }

            return result;
        }
    }

    /// <summary>
    /// 原版 CanPawnAcceptQuest 最终写死 IsFreeColonist。
    /// 仅当该任务确实通过 giveToAccepter 发放 Royal Favor 时，
    /// 为机械族机械师重放其余原版资格检查并局部放行。
    /// </summary>
    [HarmonyPatch(typeof(QuestUtility), nameof(QuestUtility.CanPawnAcceptQuest))]
    internal static class Patch_QuestUtility_CanPawnAcceptQuest_RoyalFavorMechanitor
    {
        [HarmonyPostfix]
        internal static void Postfix(
            Pawn p,
            Quest quest,
            ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (MechanoidMechanitorRoyalFavorAccepterUtility
                .CanSupportedMechanitorAccept(p, quest))
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// 正常任务接受与奖励选项接受最终都会进入 AcceptQuestByInterface。
    /// 在该统一入口建立 Royal Favor accepter 上下文，从而同时覆盖：
    /// - 无 QuestPart_Choice 的普通任务；
    /// - DoRewards 中选择某一奖励方案后再指定接受者的任务。
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Quests), "AcceptQuestByInterface")]
    internal static class Patch_MainTabWindow_Quests_AcceptQuestByInterface_RoyalFavorContext
    {
        [HarmonyPrefix]
        internal static void Prefix(
            Quest ___selected,
            bool requiresAccepter,
            out Quest? __state)
        {
            __state = MechanoidMechanitorRoyalFavorAccepterUtility
                .CurrentAccepterSelectionQuest;

            MechanoidMechanitorRoyalFavorAccepterUtility
                .CurrentAccepterSelectionQuest =
                requiresAccepter
                && MechanoidMechanitorRoyalFavorAccepterUtility
                    .IsRoyalFavorAccepterQuest(___selected)
                    ? ___selected
                    : null;
        }

        [HarmonyPostfix]
        internal static void Postfix(Quest? __state)
        {
            RestoreContext(__state);
        }

        [HarmonyFinalizer]
        internal static Exception? Finalizer(
            Exception? __exception,
            Quest? __state)
        {
            RestoreContext(__state);
            return __exception;
        }

        private static void RestoreContext(Quest? previous)
        {
            MechanoidMechanitorRoyalFavorAccepterUtility
                .CurrentAccepterSelectionQuest = previous;
        }
    }

    /// <summary>
    /// DEV“立即接受”不经过 AcceptQuestByInterface，而是直接从 FreeColonists
    /// 随机选择接受者，因此单独建立相同上下文以保持纯机械殖民地调试行为一致。
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Quests), "DoAcceptButton")]
    internal static class Patch_MainTabWindow_Quests_DoAcceptButton_RoyalFavorContext
    {
        [HarmonyPrefix]
        internal static void Prefix(
            Quest ___selected,
            out Quest? __state)
        {
            __state = MechanoidMechanitorRoyalFavorAccepterUtility
                .CurrentAccepterSelectionQuest;

            MechanoidMechanitorRoyalFavorAccepterUtility
                .CurrentAccepterSelectionQuest =
                MechanoidMechanitorRoyalFavorAccepterUtility
                    .IsRoyalFavorAccepterQuest(___selected)
                    ? ___selected
                    : null;
        }

        [HarmonyPostfix]
        internal static void Postfix(Quest? __state)
        {
            RestoreContext(__state);
        }

        [HarmonyFinalizer]
        internal static Exception? Finalizer(
            Exception? __exception,
            Quest? __state)
        {
            RestoreContext(__state);
            return __exception;
        }

        private static void RestoreContext(Quest? previous)
        {
            MechanoidMechanitorRoyalFavorAccepterUtility
                .CurrentAccepterSelectionQuest = previous;
        }
    }

    /// <summary>
    /// 只在上面的 Royal Favor accepter UI 上下文存在时扩展原版候选集合。
    /// 不修改 Pawn.IsFreeColonist，也不改变 PawnsFinder 在其他系统中的语义。
    /// </summary>
    [HarmonyPatch(
        typeof(PawnsFinder),
        nameof(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoSuspended),
        MethodType.Getter)]
    internal static class Patch_PawnsFinder_RoyalFavorAccepterCandidates
    {
        [HarmonyPostfix]
        internal static void Postfix(ref List<Pawn> __result)
        {
            if (!MechanoidMechanitorRoyalFavorAccepterUtility
                .IsRoyalFavorAccepterQuest(
                    MechanoidMechanitorRoyalFavorAccepterUtility
                        .CurrentAccepterSelectionQuest))
            {
                return;
            }

            __result = MechanoidMechanitorRoyalFavorAccepterUtility
                .AppendEligibleCandidates(__result);
        }
    }
}
