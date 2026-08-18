using HarmonyLib;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 任务生成层：阻止原版标准任务型虫灾节点 QuestNode_Infestation 在当前不可生成时
    /// 通过 TestRunInt 报告可用，从而避免新生成“盟友虫巢袭击”任务。
    /// 不 Patch QuestNode_Infestation.RunInt，已有任务的真正生成交给 QuestPart_Infestation 保险。
    /// </summary>
    [HarmonyPatch(typeof(QuestNode_Infestation), "TestRunInt")]
    public static class MechanoidMechanitorInsectQuest_QuestNodeInfestation_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result)
        {
            if (!MechanoidMechanitorInsectIncidentPolicy
                .ShouldBlockQuestInfestation())
            {
                return true;
            }

            MechanoidMechanitorInsectBlockLogUtility
                .WarnQuestInfestationBlocked(
                    "QuestNode_Infestation",
                    "QuestNode_Infestation.TestRunInt",
                    throttled: true);

            __result = false;
            return false;
        }
    }

    /// <summary>
    /// 已存在任务保险：当任务真正收到触发信号（精确命中 inSignal）且该任务型虫灾将被阻止时，
    /// 跳过 QuestPart_Infestation 的 Notify_QuestSignalReceived，避免其执行 SpawnTunnels。
    /// QuestPart 基类实现为空，精确命中 inSignal 时跳过 override 不会漏掉基类副作用。
    /// </summary>
    [HarmonyPatch(
        typeof(QuestPart_Infestation),
        nameof(QuestPart_Infestation.Notify_QuestSignalReceived))]
    public static class MechanoidMechanitorInsectQuest_QuestPartInfestation_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            QuestPart_Infestation __instance,
            Signal signal)
        {
            if (signal.tag != __instance.inSignal)
            {
                return true;
            }

            if (!MechanoidMechanitorInsectIncidentPolicy
                .ShouldBlockQuestInfestation())
            {
                return true;
            }

            MechanoidMechanitorInsectBlockLogUtility
                .WarnQuestInfestationBlocked(
                    "QuestPart_Infestation",
                    "QuestPart_Infestation.Notify_QuestSignalReceived",
                    throttled: false);

            return false;
        }
    }
}
