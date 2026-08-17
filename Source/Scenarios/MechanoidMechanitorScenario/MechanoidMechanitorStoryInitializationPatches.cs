using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 剧情配置的通用新游戏初始化入口。
    /// 放在 Scenario.PreMapGenerate 的 Postfix，使各原版/第三方 ScenPart 先完成自己的预生成逻辑，
    /// 然后再把玩家选择的机械族机械师剧情关系作为最终开局关系应用。
    /// </summary>
    [HarmonyPatch(typeof(Scenario), nameof(Scenario.PreMapGenerate))]
    public static class MechanoidMechanitorStory_Scenario_PreMapGenerate_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            MechanoidMechanitorStoryInitializationUtility.ApplyInitialStoryConfigurationIfNeeded();
        }
    }

    public static class MechanoidMechanitorStoryInitializationUtility
    {
        public static void ApplyInitialStoryConfigurationIfNeeded()
        {
            if (Current.Game == null
                || !GameComponent_MechanoidMechanitorStoryState.IsStoryConfigurationActive)
            {
                return;
            }

            GameComponent_MechanoidMechanitorStoryState? storyState =
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            if (storyState == null)
            {
                return;
            }

            try
            {
                if (!storyState.InitialMechHiveRelationApplied)
                {
                    MechanoidMechanitorMechHiveRelationApplier
                        .ApplyInitialMechHiveRelation(
                            storyState,
                            MechanoidMechanitorFactionRelationNotificationMode.Deferred);
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] PreMapGenerate 阶段应用机械巢初始关系失败。"
                    + "已继续执行其他剧情关系初始化。\n"
                    + ex);
            }

            try
            {
                if (!storyState.InitialOrdinaryFactionRelationsApplied)
                {
                    MechanoidMechanitorOrdinaryFactionRelationApplier
                        .ApplyInitialOrdinaryFactionRelations(
                            storyState,
                            MechanoidMechanitorFactionRelationNotificationMode.Deferred);
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] PreMapGenerate 阶段应用普通派系初始关系失败。"
                    + "已阻止异常继续中断地图生成。\n"
                    + ex);
            }

            try
            {
                if (!storyState.InitialInsectRelationApplied)
                {
                    MechanoidMechanitorInsectRelationApplier
                        .ApplyInitialInsectRelation(
                            storyState,
                            MechanoidMechanitorFactionRelationNotificationMode.Deferred);
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] PreMapGenerate 阶段应用虫巢初始关系失败。"
                    + "已继续执行其他剧情关系初始化。\n"
                    + ex);
            }
        }
    }
}
