using System;
using MAP_MechanoidMechanitor.Scenarios;

namespace MAP_MechanoidMechanitor
{
    // 与设置页中的可独立恢复区域对应；仅用于界面操作，不写入设置或存档。
    internal enum MAPSettingsSection
    {
        All,
        Interface,
        Autonomy,
        Implants,
        Cores,
        SkillsAndOffspring,
        JusticeAbilities,
        Recreation,
        GeneralScenario,
        StartingPawnValueProtection,
        StrategicNodes,
        Insects,
        SymbiosisCovenant,
        OvermindEconomy,
        JusticeBoss,
        CerebrexBoss,
        Diagnostics,
    }

    /// <summary>
    /// 原位恢复设置，保留 Mod 关联和独立设置对象的保存入口。
    /// 默认值统一取自各设置类型的字段初始化，避免另维护一份默认数值。
    /// </summary>
    internal static class MAPMechanitorSettingsResetUtility
    {
        internal static void Reset(MAPSettingsSection section)
        {
            MAPMechanitorModSettings? settings = MAPMechanitorMod.Settings;
            if (settings == null) return;

            bool previousSun = settings.enableSunAutonomy;
            bool previousHermit = settings.enableHermitAutonomy;
            bool previousPortraits = settings.enablePortraitDisplayForAllSaves;
            bool previousWorkTabDisplay = settings.addMechanoidMechanitorsToWorkTab;
            bool previousDiagnostics = settings.enableJusticeBossDiagnosticLogging;
            bool resetEconomy = section == MAPSettingsSection.All
                || section == MAPSettingsSection.OvermindEconomy;
            // 先按旧速率结算，恢复后通过原有入口应用新速率；不清空存档经济账本。
            if (resetEconomy) GameComponent_OvermindEconomy.Current?.Prepare();

            var defaults = new MAPMechanitorModSettings();
            if (section == MAPSettingsSection.All)
            {
                foreach (MAPSettingsSection item in Enum.GetValues(typeof(MAPSettingsSection)))
                {
                    if (item != MAPSettingsSection.All)
                        ResetSection(settings, defaults, item);
                }
                // 旧设置兼容占位也恢复默认，但不赋予其新的运行语义。
                settings.enableStoryStylesForGeneralScenarios =
                    defaults.enableStoryStylesForGeneralScenarios;
            }
            else
            {
                ResetSection(settings, defaults, section);
            }

            if (previousSun != settings.enableSunAutonomy
                || previousHermit != settings.enableHermitAutonomy)
                GameComponent_AutonomousMechRegistry.NotifySettingsChanged();
            if (previousPortraits != settings.enablePortraitDisplayForAllSaves)
                MechanoidMechanitorScenarioFreeColonistUtility.NotifyColonistDisplaysDirtyIfReady();
            else if (previousWorkTabDisplay != settings.addMechanoidMechanitorsToWorkTab)
                WorkTabPawnListUtility.NotifyPawnsChangedIfReady();
            if (previousDiagnostics != settings.enableJusticeBossDiagnosticLogging)
                JusticeBossDiagnosticsRuntime.Refresh();
            if (resetEconomy) GameComponent_OvermindEconomy.Current?.Prepare();
        }

        private static void ResetSection(
            MAPMechanitorModSettings settings, MAPMechanitorModSettings defaults,
            MAPSettingsSection section)
        {
            switch (section)
            {
                case MAPSettingsSection.Interface:
                    settings.showWheelOfFateThemeDetails = defaults.showWheelOfFateThemeDetails;
                    settings.addMechanoidMechanitorsToWorkTab = defaults.addMechanoidMechanitorsToWorkTab;
                    settings.enableMechanoidPrioritizedWorkOrders = defaults.enableMechanoidPrioritizedWorkOrders;
                    settings.enableImmediateDraftStateRefresh = defaults.enableImmediateDraftStateRefresh;
                    settings.enablePortraitDisplayForAllSaves = defaults.enablePortraitDisplayForAllSaves;
                    settings.enablePurgeDirectiveUiLoadingScreen = defaults.enablePurgeDirectiveUiLoadingScreen;
                    break;
                case MAPSettingsSection.Autonomy:
                    settings.enableSunAutonomy = defaults.enableSunAutonomy;
                    settings.enableHermitAutonomy = defaults.enableHermitAutonomy;
                    break;
                case MAPSettingsSection.Implants:
                    settings.enableMechanoidMechanitorBrainImplants = defaults.enableMechanoidMechanitorBrainImplants;
                    settings.enableMoonImplants = defaults.enableMoonImplants;
                    break;
                case MAPSettingsSection.Cores:
                    settings.productivityCoreWorkSpeedOffsetPercentPerLevel = defaults.productivityCoreWorkSpeedOffsetPercentPerLevel;
                    settings.enableAnnihilationCannonCrater = defaults.enableAnnihilationCannonCrater;
                    settings.enableMaxLevelPsychicCoreMentalStateRecovery = defaults.enableMaxLevelPsychicCoreMentalStateRecovery;
                    break;
                case MAPSettingsSection.SkillsAndOffspring:
                    settings.ensureMechanoidMechanitorMinimumMinorPassion = defaults.ensureMechanoidMechanitorMinimumMinorPassion;
                    settings.syntheticOffspringInheritXenogenes = defaults.syntheticOffspringInheritXenogenes;
                    break;
                case MAPSettingsSection.JusticeAbilities:
                    settings.restrictMechHackBossTargets = defaults.restrictMechHackBossTargets;
                    break;
                case MAPSettingsSection.Recreation:
                    settings.enableMechanoidMechanitorRecreation = defaults.enableMechanoidMechanitorRecreation;
                    settings.mechanoidMechanitorIdleRecreationChancePercent = defaults.mechanoidMechanitorIdleRecreationChancePercent;
                    settings.mechanoidMechanitorInspirationChancePercent = defaults.mechanoidMechanitorInspirationChancePercent;
                    break;
                case MAPSettingsSection.GeneralScenario:
                    settings.generalScenarioFactionOutpostFrequency = defaults.generalScenarioFactionOutpostFrequency;
                    settings.generalScenarioMechHiveNodeFrequency = defaults.generalScenarioMechHiveNodeFrequency;
                    ResetGeneralScenarioTemplate(settings);
                    break;
                case MAPSettingsSection.StartingPawnValueProtection:
                    ResetStartingPawnValueProtection();
                    break;
                case MAPSettingsSection.StrategicNodes:
                    settings.factionOutpostRaidChancePercent = defaults.factionOutpostRaidChancePercent;
                    settings.factionOutpostSupportChancePercent = defaults.factionOutpostSupportChancePercent;
                    settings.factionOutpostGarrisonThreatScalePercent = defaults.factionOutpostGarrisonThreatScalePercent;
                    settings.mechHiveNodeRaidChancePercent = defaults.mechHiveNodeRaidChancePercent;
                    settings.mechHiveNodeSupportChancePercent = defaults.mechHiveNodeSupportChancePercent;
                    break;
                case MAPSettingsSection.Insects:
                    settings.blockInfestationIncidentsWhenInsectsAllied = defaults.blockInfestationIncidentsWhenInsectsAllied;
                    settings.pursuitAllowInfestationWithoutThickRoof = defaults.pursuitAllowInfestationWithoutThickRoof;
                    settings.pursuitGracePeriodDays = defaults.pursuitGracePeriodDays;
                    settings.pursuitHuntDailyChancePercent = defaults.pursuitHuntDailyChancePercent;
                    settings.pursuitExtraInfestationDailyChancePercent = defaults.pursuitExtraInfestationDailyChancePercent;
                    settings.pursuitSharedCooldownDays = defaults.pursuitSharedCooldownDays;
                    settings.pursuitHuntSurfacePointsPercent = defaults.pursuitHuntSurfacePointsPercent;
                    settings.pursuitHuntInfestationPointsPercent = defaults.pursuitHuntInfestationPointsPercent;
                    break;
                case MAPSettingsSection.SymbiosisCovenant:
                    settings.symbiosisCovenantGrowthMultiplierTenths = defaults.symbiosisCovenantGrowthMultiplierTenths;
                    break;
                case MAPSettingsSection.OvermindEconomy:
                    settings.overmindEconomy ??= new OvermindEconomySettings();
                    settings.overmindEconomy.ResetToDefaults();
                    break;
                case MAPSettingsSection.JusticeBoss:
                    settings.justiceBossEnableMortarShield = defaults.justiceBossEnableMortarShield;
                    settings.justiceBossEnableBulletShield = defaults.justiceBossEnableBulletShield;
                    settings.justiceBossAutoMortarCount = defaults.justiceBossAutoMortarCount;
                    settings.justiceBossAutoChargeBlasterCount = defaults.justiceBossAutoChargeBlasterCount;
                    settings.justiceBossAutoInfernoCount = defaults.justiceBossAutoInfernoCount;
                    settings.justiceBossTotalWaves = defaults.justiceBossTotalWaves;
                    settings.justiceBossWaveIntervalTicks = defaults.justiceBossWaveIntervalTicks;
                    settings.justiceBossMechsPerWave = defaults.justiceBossMechsPerWave;
                    settings.justiceBossAllowBossReplacement = defaults.justiceBossAllowBossReplacement;
                    settings.justiceBossApplyMobileCombatToSummons = defaults.justiceBossApplyMobileCombatToSummons;
                    break;
                case MAPSettingsSection.CerebrexBoss:
                    settings.cerebrexBossEnableExtraSkills = defaults.cerebrexBossEnableExtraSkills;
                    settings.cerebrexBossEnableSummoning = defaults.cerebrexBossEnableSummoning;
                    settings.cerebrexBossSummonIntervalTicks = defaults.cerebrexBossSummonIntervalTicks;
                    settings.cerebrexBossMechsPerWave = defaults.cerebrexBossMechsPerWave;
                    settings.cerebrexBossMaxLivingSummonedMechs = defaults.cerebrexBossMaxLivingSummonedMechs;
                    settings.cerebrexBossApplyMobileCombatToSummons = defaults.cerebrexBossApplyMobileCombatToSummons;
                    settings.cerebrexBossEnableBandwidthInterference = defaults.cerebrexBossEnableBandwidthInterference;
                    settings.cerebrexBossBandwidthCooldownMinTicks = defaults.cerebrexBossBandwidthCooldownMinTicks;
                    settings.cerebrexBossBandwidthCooldownMaxTicks = defaults.cerebrexBossBandwidthCooldownMaxTicks;
                    settings.cerebrexBossBandwidthDurationTicks = defaults.cerebrexBossBandwidthDurationTicks;
                    settings.cerebrexBossBandwidthMaxTargets = defaults.cerebrexBossBandwidthMaxTargets;
                    settings.cerebrexBossEnableEmp = defaults.cerebrexBossEnableEmp;
                    settings.cerebrexBossEmpCooldownMinTicks = defaults.cerebrexBossEmpCooldownMinTicks;
                    settings.cerebrexBossEmpCooldownMaxTicks = defaults.cerebrexBossEmpCooldownMaxTicks;
                    settings.cerebrexBossEmpBaseDurationTicks = defaults.cerebrexBossEmpBaseDurationTicks;
                    settings.cerebrexBossEmpRadius = defaults.cerebrexBossEmpRadius;
                    break;
                case MAPSettingsSection.Diagnostics:
                    settings.enableStartupDetailedLogging = defaults.enableStartupDetailedLogging;
                    settings.preventMechanoidMechanitorDeathDuringLoad = defaults.preventMechanoidMechanitorDeathDuringLoad;
                    settings.enableLoadDeathDiagnosticLogging = defaults.enableLoadDeathDiagnosticLogging;
                    settings.enableCompatibilityDetailedLogging = defaults.enableCompatibilityDetailedLogging;
                    settings.enableJusticeBossDiagnosticLogging = defaults.enableJusticeBossDiagnosticLogging;
                    break;
            }
        }

        private static void ResetGeneralScenarioTemplate(MAPMechanitorModSettings settings)
        {
            var template = MechanoidMechanitorGeneralScenarioTemplateSettings.Current;
            var defaults = new MechanoidMechanitorGeneralScenarioTemplateModSettings();
            template.generalScenarioOrdinaryFactionRelationsMode = defaults.generalScenarioOrdinaryFactionRelationsMode;
            template.generalScenarioHostileFactionOutpostWeight = defaults.generalScenarioHostileFactionOutpostWeight;
            template.generalScenarioAllyFactionOutpostWeight = defaults.generalScenarioAllyFactionOutpostWeight;
            template.generalScenarioNeutralFactionOutpostWeight = defaults.generalScenarioNeutralFactionOutpostWeight;
            template.generalScenarioMechHiveRelationMode = defaults.generalScenarioMechHiveRelationMode;
            template.generalScenarioInsectRelationMode = defaults.generalScenarioInsectRelationMode;
            template.generalScenarioPurgeDirectiveEnabled = defaults.generalScenarioPurgeDirectiveEnabled;
            template.generalScenarioSymbiosisCovenantEnabled = defaults.generalScenarioSymbiosisCovenantEnabled;
            template.generalScenarioIdeologyAdaptationLevel = defaults.generalScenarioIdeologyAdaptationLevel;
            MechanoidMechanitorGeneralScenarioTemplateSettings.Normalize(settings, template);
        }

        private static void ResetStartingPawnValueProtection()
        {
            var settings = StartingPawnValueProtectionSettings.Data;
            var defaults = new StartingPawnValueProtectionModSettings();
            settings.enableMechanitorStartingValueProtection = defaults.enableMechanitorStartingValueProtection;
            settings.protectOtherStartingMechs = defaults.protectOtherStartingMechs;
            settings.durationDays = defaults.durationDays;
            settings.minimumFactorPercent = defaults.minimumFactorPercent;
            settings.Normalize();
        }
    }
}
