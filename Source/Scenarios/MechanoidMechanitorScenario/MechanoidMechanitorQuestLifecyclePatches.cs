using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(typeof(Quest), nameof(Quest.QuestTick))]
    internal static class MechanoidMechanitorQuestTickPatch
    {
        public static bool Prefix(Quest __instance)
        {
            for (int i = 0; i < __instance.PartsListForReading.Count; i++)
            {
                QuestPart part = __instance.PartsListForReading[i];
                if (__instance.Historical)
                {
                    // 原版在清理 30 天后清空全部 Part；撤离尚未完成时必须保留唯一状态源。
                    // 普通历史计时、首次 Cleanup 和已完成任务仍完全沿用原版。
                    if (__instance.TicksSinceCleanup >= 1800000
                        && part is QuestPart_SymbiosisCovenantCerebrexSupport support
                        && support.NeedsPostQuestContinuation)
                    {
                        return false;
                    }
                }
                else
                {
                    try
                    {
                        if (part is PurgeDirectiveQuestPart purge)
                        {
                            purge.EnsureTicking();
                        }
                        else if (part is QuestPart_SymbiosisCovenantJointOperation jointOperation)
                        {
                            jointOperation.EnsureTicking();
                        }
                    }
                    catch (Exception ex)
                    {
                        // 与原版逐 Part Tick 的隔离边界一致，单个恢复失败不阻断其它任务。
                        Log.ErrorOnce("[MAP-机械族机械师] 恢复任务计时失败：" + ex,
                            __instance.id ^ 0x51414354);
                    }
                }
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(QuestManager), nameof(QuestManager.Remove))]
    internal static class MechanoidMechanitorQuestRemovedPatch
    {
        public static void Prefix(QuestManager __instance, Quest quest)
        {
            if (quest == null || !__instance.Contains(quest))
            {
                return;
            }

            foreach (QuestPart part in quest.PartsListForReading)
            {
                if (part is PurgeDirectiveQuestPart
                    || part is QuestPart_SymbiosisCovenantJointOperation
                    || part is QuestPart_SymbiosisCovenantCerebrexSupport
                    || part is QuestPart_JusticeBossGroup)
                {
                    // 仅处理承载本 MOD 生命周期的任务，复用原版幂等清理及逐 Part 异常隔离。
                    // 删除不是成功，不调用 End(Success)，不补发任何任务奖励。
                    quest.CleanupQuestParts();
                    return;
                }
            }
        }
    }
}
