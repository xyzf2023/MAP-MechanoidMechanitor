using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    internal delegate bool MechanoidMechanitorRuntimeStoryAction(out string message);

    /// <summary>
    /// 为原本没有活动剧情配置的既有存档提供 DEV/AutoTest 运行时接入。
    /// 打开控制台菜单时只返回中性预览配置，不写入存档；真正执行叶子动作时才临时建立配置。
    /// 若动作失败、被拒绝或最终仍等于中性基线，则完整回滚，不让单纯查看/无效操作激活剧情系统。
    /// </summary>
    internal static class MechanoidMechanitorStoryRuntimeBootstrapUtility
    {
        private static readonly AccessTools.FieldRef<
            GameComponent_MechanoidMechanitorStoryState,
            MechanoidMechanitorStoryConfiguration> ActiveConfigurationField =
                AccessTools.FieldRefAccess<
                    GameComponent_MechanoidMechanitorStoryState,
                    MechanoidMechanitorStoryConfiguration>("activeConfiguration");

        private static readonly AccessTools.FieldRef<
            GameComponent_MechanoidMechanitorStoryState,
            MechanoidMechanitorStoryConfigurationOrigin> ConfigurationOriginField =
                AccessTools.FieldRefAccess<
                    GameComponent_MechanoidMechanitorStoryState,
                    MechanoidMechanitorStoryConfigurationOrigin>("storyConfigurationOrigin");

        private static readonly AccessTools.FieldRef<
            GameComponent_MechanoidMechanitorStoryState,
            bool> InitialOrdinaryRelationsAppliedField =
                AccessTools.FieldRefAccess<
                    GameComponent_MechanoidMechanitorStoryState,
                    bool>("initialOrdinaryFactionRelationsApplied");

        private static readonly AccessTools.FieldRef<
            GameComponent_MechanoidMechanitorStoryState,
            bool> InitialMechHiveRelationAppliedField =
                AccessTools.FieldRefAccess<
                    GameComponent_MechanoidMechanitorStoryState,
                    bool>("initialMechHiveRelationApplied");

        private static readonly AccessTools.FieldRef<
            GameComponent_MechanoidMechanitorStoryState,
            MechanoidMechanitorPurgeDirectiveRuntimeState> PurgeRuntimeStateField =
                AccessTools.FieldRefAccess<
                    GameComponent_MechanoidMechanitorStoryState,
                    MechanoidMechanitorPurgeDirectiveRuntimeState>("purgeDirectiveRuntimeState");

        public static MechanoidMechanitorStoryConfiguration CreateMenuConfigurationSnapshot()
        {
            MechanoidMechanitorStoryConfiguration? current =
                GameComponent_MechanoidMechanitorStoryState.CurrentConfiguration;
            return current ?? CreateNeutralExistingGameConfiguration();
        }

        public static bool TryExecuteWithRuntimeConfiguration(
            MechanoidMechanitorRuntimeStoryAction action,
            out string message)
        {
            if (action == null)
            {
                message = "无法切换剧本状态：运行时动作为空。";
                return false;
            }

            if (Current.Game == null || Current.ProgramState != ProgramState.Playing)
            {
                message = "无法切换剧本状态：当前不处于可编辑的游戏运行状态。";
                return false;
            }

            if (GameComponent_MechanoidMechanitorStoryState.HasActiveConfiguration)
            {
                return action(out message);
            }

            GameComponent_MechanoidMechanitorStoryState? storyState =
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            if (storyState == null)
            {
                message = "无法切换剧本状态：剧情状态组件不可用。";
                return false;
            }

            MechanoidMechanitorStoryConfiguration? originalConfiguration =
                ActiveConfigurationField(storyState);
            MechanoidMechanitorStoryConfigurationOrigin originalOrigin =
                ConfigurationOriginField(storyState);
            bool originalInitialOrdinary = InitialOrdinaryRelationsAppliedField(storyState);
            bool originalInitialMechHive = InitialMechHiveRelationAppliedField(storyState);
            MechanoidMechanitorPurgeDirectiveRuntimeState? originalPurgeRuntime =
                PurgeRuntimeStateField(storyState);

            MechanoidMechanitorStoryConfiguration baseline =
                originalConfiguration?.CreateCopy()
                ?? CreateNeutralExistingGameConfiguration();
            MechanoidMechanitorStoryConfigurationContext context =
                MechanoidMechanitorStoryConfigurationContext.Create(baseline);
            baseline.SyncOrdinaryFactionEntries(context);
            baseline.Normalize(context);

            MechanoidMechanitorStoryConfigurationOrigin runtimeOrigin =
                MechanoidMechanitorScenarioUtility.IsScenarioActive
                    ? MechanoidMechanitorStoryConfigurationOrigin.MechanoidMechanitorScenario
                    : MechanoidMechanitorStoryConfigurationOrigin.GeneralScenario;

            ConfigurationOriginField(storyState) = runtimeOrigin;
            ActiveConfigurationField(storyState) = baseline.CreateCopy();

            // 这是一个已经运行中的世界：当前外交关系就是接管前的既有基线。
            // 不能把它重新当成“新游戏初始关系”执行一遍。
            InitialOrdinaryRelationsAppliedField(storyState) = true;
            InitialMechHiveRelationAppliedField(storyState) = true;

            // 原本没有剧情配置的存档不应继承任何潜在的旧肃清运行痕迹。
            // 临时引导失败时会恢复原引用；只有实际配置变更成功后才保留这份干净状态。
            MechanoidMechanitorPurgeDirectiveRuntimeState freshPurgeRuntime =
                new MechanoidMechanitorPurgeDirectiveRuntimeState();
            freshPurgeRuntime.InitializeForNewGame(purgeDirectiveEnabled: false);
            PurgeRuntimeStateField(storyState) = freshPurgeRuntime;
            storyState.RebuildRuntimeCaches();

            bool succeeded;
            string actionMessage;
            try
            {
                succeeded = action(out actionMessage);
            }
            catch
            {
                RestoreOriginalState(
                    storyState,
                    originalConfiguration,
                    originalOrigin,
                    originalInitialOrdinary,
                    originalInitialMechHive,
                    originalPurgeRuntime);
                throw;
            }

            if (!succeeded)
            {
                RestoreOriginalState(
                    storyState,
                    originalConfiguration,
                    originalOrigin,
                    originalInitialOrdinary,
                    originalInitialMechHive,
                    originalPurgeRuntime);
                message = actionMessage;
                return false;
            }

            MechanoidMechanitorStoryConfiguration? resultingConfiguration =
                GameComponent_MechanoidMechanitorStoryState.CurrentConfiguration;
            if (resultingConfiguration == null)
            {
                RestoreOriginalState(
                    storyState,
                    originalConfiguration,
                    originalOrigin,
                    originalInitialOrdinary,
                    originalInitialMechHive,
                    originalPurgeRuntime);
                message = "切换剧本状态失败：运行时配置在动作完成后丢失。";
                return false;
            }

            if (ConfigurationsEqual(baseline, resultingConfiguration))
            {
                // 用户选择的是中性默认值，或该选择被 Normalize 归一化回中性状态。
                // 行为上没有发生剧情配置变化，因此继续保持这个存档“未启用剧情配置”。
                RestoreOriginalState(
                    storyState,
                    originalConfiguration,
                    originalOrigin,
                    originalInitialOrdinary,
                    originalInitialMechHive,
                    originalPurgeRuntime);
                message = actionMessage;
                return true;
            }

            Log.Message(
                "[MAP-StoryStateDebug] BOOTSTRAP 已为既有存档建立运行时剧情配置。来源="
                + runtimeOrigin);
            message = actionMessage;
            return true;
        }

        private static MechanoidMechanitorStoryConfiguration
            CreateNeutralExistingGameConfiguration()
        {
            return new MechanoidMechanitorStoryConfiguration
            {
                ordinaryFactionRelationsMode =
                    MechanoidMechanitorOrdinaryFactionRelationsMode.Default,
                factionOutpostFrequency = MechanoidMechanitorFactionOutpostFrequency.Off,
                hostileFactionOutpostWeight = 100,
                neutralFactionOutpostWeight = 100,
                allyFactionOutpostWeight = 100,
                mechHiveRelationMode = MechanoidMechanitorMechHiveRelationMode.Default,
                mechHiveNodeFrequency = MechanoidMechanitorMechHiveNodeFrequency.Off,
                purgeDirectiveEnabled = false,
                symbiosisCovenantEnabled = false,
                ideologyAdaptationLevel =
                    MechanoidMechanitorIdeologyAdaptationLevel.Disabled
            };
        }

        private static void RestoreOriginalState(
            GameComponent_MechanoidMechanitorStoryState storyState,
            MechanoidMechanitorStoryConfiguration? originalConfiguration,
            MechanoidMechanitorStoryConfigurationOrigin originalOrigin,
            bool originalInitialOrdinary,
            bool originalInitialMechHive,
            MechanoidMechanitorPurgeDirectiveRuntimeState? originalPurgeRuntime)
        {
            ActiveConfigurationField(storyState) = originalConfiguration!;
            ConfigurationOriginField(storyState) = originalOrigin;
            InitialOrdinaryRelationsAppliedField(storyState) = originalInitialOrdinary;
            InitialMechHiveRelationAppliedField(storyState) = originalInitialMechHive;
            PurgeRuntimeStateField(storyState) = originalPurgeRuntime!;
            storyState.RebuildRuntimeCaches();
        }

        private static bool ConfigurationsEqual(
            MechanoidMechanitorStoryConfiguration left,
            MechanoidMechanitorStoryConfiguration right)
        {
            if (left.ordinaryFactionRelationsMode != right.ordinaryFactionRelationsMode
                || left.factionOutpostFrequency != right.factionOutpostFrequency
                || left.hostileFactionOutpostWeight != right.hostileFactionOutpostWeight
                || left.neutralFactionOutpostWeight != right.neutralFactionOutpostWeight
                || left.allyFactionOutpostWeight != right.allyFactionOutpostWeight
                || left.mechHiveRelationMode != right.mechHiveRelationMode
                || left.mechHiveNodeFrequency != right.mechHiveNodeFrequency
                || left.purgeDirectiveEnabled != right.purgeDirectiveEnabled
                || left.symbiosisCovenantEnabled != right.symbiosisCovenantEnabled
                || left.ideologyAdaptationLevel != right.ideologyAdaptationLevel)
            {
                return false;
            }

            List<MechanoidMechanitorFactionRelationSetting> leftSettings =
                left.ordinaryFactionRelationSettings;
            List<MechanoidMechanitorFactionRelationSetting> rightSettings =
                right.ordinaryFactionRelationSettings;
            if (leftSettings.Count != rightSettings.Count)
            {
                return false;
            }

            for (int i = 0; i < leftSettings.Count; i++)
            {
                MechanoidMechanitorFactionRelationSetting? leftSetting = leftSettings[i];
                if (leftSetting?.faction == null)
                {
                    continue;
                }

                MechanoidMechanitorFactionRelationSetting? rightSetting =
                    right.FindSettingFor(leftSetting.faction);
                if (rightSetting == null
                    || rightSetting.relationOption != leftSetting.relationOption)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
