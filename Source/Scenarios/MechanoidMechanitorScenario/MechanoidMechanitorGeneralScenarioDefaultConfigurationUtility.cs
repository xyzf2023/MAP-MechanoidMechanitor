using MAP_MechanoidMechanitor;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 为普通新游戏创建静默剧情配置。
    /// 模板默认值最初参考 Classic，但运行时不读取 Classic Def；玩家保存后的模板和
    /// 已创建存档都不会跟随 Classic 或之后的 MOD 设置变化。
    /// </summary>
    public static class MechanoidMechanitorGeneralScenarioDefaultConfigurationUtility
    {
        public static MechanoidMechanitorStoryConfiguration CreateForNewGame()
        {
            MAPMechanitorModSettings? settings = MAPMechanitorMod.Settings;
            MechanoidMechanitorGeneralScenarioTemplateModSettings template =
                MechanoidMechanitorGeneralScenarioTemplateSettings.Current;
            MechanoidMechanitorGeneralScenarioTemplateSettings.Normalize(settings, template);

            MechanoidMechanitorStoryConfiguration configuration =
                new MechanoidMechanitorStoryConfiguration
                {
                    ordinaryFactionRelationsMode =
                        template.generalScenarioOrdinaryFactionRelationsMode,
                    factionOutpostFrequency =
                        settings?.generalScenarioFactionOutpostFrequency
                        ?? MAPMechanitorModSettings
                            .DefaultGeneralScenarioFactionOutpostFrequency,
                    hostileFactionOutpostWeight =
                        template.generalScenarioHostileFactionOutpostWeight,
                    allyFactionOutpostWeight =
                        template.generalScenarioAllyFactionOutpostWeight,
                    neutralFactionOutpostWeight =
                        template.generalScenarioNeutralFactionOutpostWeight,
                    mechHiveRelationMode =
                        template.generalScenarioMechHiveRelationMode,
                    insectRelationMode =
                        template.generalScenarioInsectRelationMode,
                    mechHiveNodeFrequency =
                        settings?.generalScenarioMechHiveNodeFrequency
                        ?? MAPMechanitorModSettings
                            .DefaultGeneralScenarioMechHiveNodeFrequency,
                    purgeDirectiveEnabled =
                        template.generalScenarioPurgeDirectiveEnabled,
                    symbiosisCovenantEnabled =
                        template.generalScenarioSymbiosisCovenantEnabled,
                    ideologyAdaptationLevel =
                        template.generalScenarioIdeologyAdaptationLevel
                };

            MechanoidMechanitorStoryConfigurationContext context =
                MechanoidMechanitorStoryConfigurationContext.Create(configuration);
            configuration.Normalize(context);
            return configuration;
        }
    }
}
