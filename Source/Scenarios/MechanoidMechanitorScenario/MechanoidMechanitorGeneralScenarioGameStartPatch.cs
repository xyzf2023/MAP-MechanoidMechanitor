using HarmonyLib;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 普通新游戏在原版正式开始新游戏前静默建立一份 GeneralScenario 来源的默认剧情配置。
    /// 只作用于新游戏入口；读档不会经过此方法，因而不会为历史普通存档补写配置。
    /// </summary>
    [HarmonyPatch(typeof(PageUtility), nameof(PageUtility.InitGameStart))]
    public static class MechanoidMechanitorGeneralScenario_PageUtility_InitGameStart_Patch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (Current.Game == null || Find.Scenario == null)
            {
                return;
            }

            // 机械族机械师专用剧本始终继续使用现有剧情风格流程。
            if (MechanoidMechanitorScenarioUtility.ScenarioContainsMarker(Find.Scenario))
            {
                return;
            }

            // 防止异常页面链或重复调用覆盖已经提交的配置。
            if (GameComponent_MechanoidMechanitorStoryState.HasActiveConfiguration)
            {
                return;
            }

            GameComponent_MechanoidMechanitorStoryState? storyState =
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            if (storyState == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 普通新游戏静默初始化失败：缺少剧情状态组件。"
                    + "已放行原版新游戏流程。");
                return;
            }

            MechanoidMechanitorStoryConfiguration configuration =
                MechanoidMechanitorGeneralScenarioDefaultConfigurationUtility
                    .CreateForNewGame();

            // 普通静默初始化没有“玩家选择的剧情风格”。现有新游戏入口只保存该引用，
            // 不会解引用 storyStyle；显式传入 null 可让 SelectedStoryStyle 保持为空，
            // 同时复用其完整的新游戏状态初始化与 GeneralScenario 来源判定。
            storyState.SetStoryStyleForNewGame(null!, configuration);
        }
    }
}
