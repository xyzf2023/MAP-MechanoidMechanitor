using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 极窄范围诊断 Patch：仅观察「当前进行中的联合军事行动」的目标 MapParent 的
    /// 原版 PostMapGenerate 调用时机与上下文。
    ///
    /// 设计要点：
    /// - 只 Patch RimWorld.Planet.MapParent.PostMapGenerate 的 Prefix / Postfix。
    /// - 绝不跳过原版方法、绝不修改返回值、绝不修改 questTags、绝不修改 map。
    /// - 只有「正在进行的联合行动 Part 存在」且「__instance 正是该行动的目标」时才输出日志。
    /// - 其他所有 MapParent（玩家基地、其他任务地点、其他 MOD 地图）零日志、零影响。
    ///
    /// 自然顺序通常应为：
    /// TargetMapParentPostMapGeneratePrefix
    /// → RelevantSignalReceivedBeforeBase(MapGenerated)
    /// → RelevantSignalReceivedAfterBase(MapGenerated)
    /// → MapGeneratedSignalReceived
    /// → TargetMapRegistered
    /// → TargetMapParentPostMapGeneratePostfix
    /// 该顺序本身可用于定位信号链断点。
    /// </summary>
    [HarmonyPatch(typeof(MapParent), nameof(MapParent.PostMapGenerate))]
    internal static class SymbiosisCovenantJointOperation_MapParent_PostMapGenerate_Diagnostics
    {
        [HarmonyPrefix]
        public static void Prefix(MapParent __instance)
        {
            if (__instance == null || !Prefs.DevMode)
            {
                return;
            }

            QuestPart_SymbiosisCovenantJointOperation? part =
                SymbiosisCovenantJointOperationUtility.FindActiveOperationPart();
            if (part == null || part.targetWorldObject == null)
            {
                return;
            }

            if (!ReferenceEquals(part.targetWorldObject, __instance))
            {
                return;
            }

            Map? map = __instance.Map;
            string mapId = map != null ? map.GetUniqueLoadID() : "-";

            SymbiosisCovenantJointOperationDiagnostics.Log(
                "TargetMapParentPostMapGeneratePrefix",
                "actionId=" + (part.actionId ?? "-")
                + " | questId=" + (part.quest != null ? part.quest.id.ToString() : "-")
                + " | target=" + (__instance.Label ?? "-")
                + " | targetLoadId=" + __instance.GetUniqueLoadID()
                + " | targetQuestTag=" + (part.targetQuestTag ?? "-")
                + " | currentTick=" + (Find.TickManager?.TicksGame ?? 0)
                + " | hasMap=" + __instance.HasMap
                + " | mapId=" + mapId
                + " | findMapsContains=" + (map != null && Find.Maps.Contains(map))
                + " | state=" + part.State
                + " | stage=" + part.stage
                + " | inSignalEnable=" + (part.inSignalEnable ?? "-")
                + " | questInitiateSignal=" + (part.quest?.InitiateSignal ?? "-")
                + " | questTags=" + SymbiosisCovenantJointOperationDiagnostics.FormatQuestTags(__instance.questTags)
                + " | targetTagPresent=" + SymbiosisCovenantJointOperationDiagnostics.QuestTagContains(
                    __instance.questTags, part.targetQuestTag));
        }

        [HarmonyPostfix]
        public static void Postfix(MapParent __instance)
        {
            if (__instance == null || !Prefs.DevMode)
            {
                return;
            }

            QuestPart_SymbiosisCovenantJointOperation? part =
                SymbiosisCovenantJointOperationUtility.FindActiveOperationPart();
            if (part == null || part.targetWorldObject == null)
            {
                return;
            }

            if (!ReferenceEquals(part.targetWorldObject, __instance))
            {
                return;
            }

            Map? map = __instance.Map;
            string mapId = map != null ? map.GetUniqueLoadID() : "-";

            SymbiosisCovenantJointOperationDiagnostics.Log(
                "TargetMapParentPostMapGeneratePostfix",
                "actionId=" + (part.actionId ?? "-")
                + " | questId=" + (part.quest != null ? part.quest.id.ToString() : "-")
                + " | target=" + (__instance.Label ?? "-")
                + " | targetLoadId=" + __instance.GetUniqueLoadID()
                + " | targetQuestTag=" + (part.targetQuestTag ?? "-")
                + " | currentTick=" + (Find.TickManager?.TicksGame ?? 0)
                + " | state=" + part.State
                + " | stage=" + part.stage
                + " | targetTagStillPresent=" + SymbiosisCovenantJointOperationDiagnostics.QuestTagContains(
                    __instance.questTags, part.targetQuestTag)
                + " | questTags=" + SymbiosisCovenantJointOperationDiagnostics.FormatQuestTags(__instance.questTags)
                + " | mapId=" + mapId);
        }
    }
}
