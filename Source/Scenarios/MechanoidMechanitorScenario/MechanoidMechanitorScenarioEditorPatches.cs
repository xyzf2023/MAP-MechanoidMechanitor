using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械族机械师剧本启用时，隐藏原版普通开局角色词条的开局人数/候选人数编辑控件。
    /// 不修改 pawnCount / pawnChoiceCount，不删除 ScenPart，不影响摘要与生成逻辑。
    /// </summary>
    [HarmonyPatch(
        typeof(ScenPart_ConfigPage_ConfigureStartingPawns),
        nameof(ScenPart_ConfigPage_ConfigureStartingPawns.DoEditInterface))]
    public static class MechanoidMechanitorScenario_HideStartingPawnCountEditor_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            Page_ScenarioEditor? editor =
                Find.WindowStack?.WindowOfType<Page_ScenarioEditor>();
            Scenario? scenario = editor?.EditingScenario ?? Find.Scenario;
            return !MechanoidMechanitorScenarioUtility.ScenarioContainsMarker(scenario);
        }
    }
}
