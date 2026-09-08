using MAP_MechanoidMechanitor;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 为未手动进入剧情风格选择页的普通新游戏创建静默剧情配置。
    /// 这些默认值与 Classic 预设仅在初始设计上保持一致；运行时不读取 Classic Def，
    /// 后续修改 Classic 也不会改变玩家已保存的 MOD 设置或既有存档。
    /// </summary>
    public static class MechanoidMechanitorGeneralScenarioDefaultConfigurationUtility
    {
        public static MechanoidMechanitorStoryConfiguration CreateForNewGame()
        {
            MAPMechanitorModSettings? settings = MAPMechanitorMod.Settings;
            settings?.NormalizeGeneralScenarioDefaultSettings();

            MechanoidMechanitorStoryConfiguration configuration =
                new MechanoidMechanitorStoryConfiguration
                {
                    ordinaryFactionRelationsMode =
                        MechanoidMechanitorOrdinaryFactionRelationsMode.Default,
                    factionOutpostFrequency =
                        settings?.generalScenarioFactionOutpostFrequency
                        ?? MAPMechanitorModSettings
                            .DefaultGeneralScenarioFactionOutpostFrequency,
                    hostileFactionOutpostWeight = 100,
                    neutralFactionOutpostWeight = 100,
                    allyFactionOutpostWeight = 100,
                    mechHiveRelationMode =
                        MechanoidMechanitorMechHiveRelationMode.Default,
                    insectRelationMode =
                        MechanoidMechanitorInsectRelationMode.Default,
                    mechHiveNodeFrequency =
                        settings?.generalScenarioMechHiveNodeFrequency
                        ?? MAPMechanitorModSettings
                            .DefaultGeneralScenarioMechHiveNodeFrequency,
                    purgeDirectiveEnabled = false,
                    symbiosisCovenantEnabled = false,
                    ideologyAdaptationLevel =
                        MechanoidMechanitorIdeologyAdaptationLevel.Disabled
                };

            MechanoidMechanitorStoryConfigurationContext context =
                MechanoidMechanitorStoryConfigurationContext.Create(configuration);
            configuration.Normalize(context);
            return configuration;
        }
    }
}
