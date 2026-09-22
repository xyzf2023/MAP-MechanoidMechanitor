using System;
using System.Collections.Generic;
using System.Globalization;
using MAP_MechanoidMechanitor.Scenarios;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class MAPMechanitorMod : Mod
    {
        private const string ProductivityCoreWorkSpeedControlName =
            "MAP_ProductivityCoreWorkSpeedOffsetPercentPerLevel";

        private const float SettingsScrollbarReserve = 20f;
        private const float SettingsBottomPadding = 12f;

        public static MAPMechanitorModSettings? Settings;

        private string? productivityCoreWorkSpeedBuffer;
        private bool productivityCoreWorkSpeedFieldWasFocused;
        private enum SettingsPage
        {
            Interface,
            Mech,
            Start,
            World,
            Boss,
            Diagnostics
        }

        private static readonly string[] SettingsPageKeys =
        {
            "MAP_Settings.Page.Interface",
            "MAP_Settings.Page.Mech",
            "MAP_Settings.Page.Start",
            "MAP_Settings.Page.World",
            "MAP_Settings.Page.Boss",
            "MAP_Settings.Page.Diagnostics"
        };

        // 仅保存界面状态，不写入 MOD 设置或存档。
        private SettingsPage settingsPage;
        private readonly Vector2[] settingsScrollPositions = new Vector2[SettingsPageKeys.Length];
        private readonly float[] settingsContentHeights = new float[SettingsPageKeys.Length];
        private readonly HashSet<string> collapsedSettingsGroups = new HashSet<string>();

        public MAPMechanitorMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<MAPMechanitorModSettings>();

            LongEventHandler.ExecuteWhenFinished(
                JusticeBossDiagnosticsRuntime.Refresh);
        }

        public override string SettingsCategory() => "[MAP]机械族机械师";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            if (Settings == null)
            {
                return;
            }

            float navigationWidth = Mathf.Min(150f, inRect.width * 0.24f);
            for (int i = 0; i < SettingsPageKeys.Length; i++)
            {
                Rect row = new Rect(inRect.x, inRect.y + i * 40f, navigationWidth, 36f);
                if (i == (int)settingsPage)
                {
                    Widgets.DrawHighlight(row);
                }
                Widgets.DrawHighlightIfMouseover(row);
                Widgets.Label(new Rect(row.x + 8f, row.y + 6f, row.width - 16f, row.height),
                    SettingsPageKeys[i].Translate());
                if (Widgets.ButtonInvisible(row) && i != (int)settingsPage)
                {
                    CommitProductivityCoreWorkSpeedBuffer();
                    productivityCoreWorkSpeedFieldWasFocused = false;
                    GUI.FocusControl(null);
                    settingsPage = (SettingsPage)i;
                }
            }

            Widgets.DrawLineVertical(inRect.x + navigationWidth + 6f, inRect.y, inRect.height);
            Rect contentRect = new Rect(
                inRect.x + navigationWidth + 18f, inRect.y,
                Mathf.Max(1f, inRect.width - navigationWidth - 18f), inRect.height);
            int pageIndex = (int)settingsPage;
            float viewWidth = Mathf.Max(1f, contentRect.width - SettingsScrollbarReserve);
            Rect viewRect = new Rect(0f, 0f, viewWidth,
                Mathf.Max(contentRect.height, settingsContentHeights[pageIndex]));

            Widgets.BeginScrollView(contentRect, ref settingsScrollPositions[pageIndex], viewRect);
            Listing_Standard listing = new Listing_Standard { maxOneColumn = true };
            listing.Begin(viewRect);

            switch (settingsPage)
            {
                case SettingsPage.Interface:
                    DrawInterfaceSettings(listing);
                    break;
                case SettingsPage.Mech:
                    DrawSettingsGroup(listing, "MAP_Settings.Group.Autonomy", DrawAutonomySettings);
                    DrawSettingsGroup(listing, "MAP_Settings.Group.Implants", DrawImplantSettings);
                    DrawSettingsGroup(listing, "MAP_Settings.Group.Cores", DrawCoreSettings);
                    DrawSettingsGroup(listing, "MAP_Settings.Group.SkillsAndOffspring", DrawSkillsAndOffspringSettings);
                    DrawJusticeAbilitySettings(listing);
                    DrawSettingsGroup(listing,
                        "MAP_MechanoidMechanitor.Settings.Recreation.Section", DrawRecreationSettings);
                    break;
                case SettingsPage.Start:
                    DrawSettingsGroup(listing,
                        "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.Section",
                        group => MechanoidMechanitorGeneralScenarioDefaultSettingsUI.Draw(group, false));
                    DrawSettingsGroup(listing,
                        "MAP_MechanoidMechanitor.Settings.StartingPawnValueProtection.Section",
                        StartingPawnValueProtectionSettingsUI.Draw);
                    break;
                case SettingsPage.World:
                    DrawSettingsGroup(listing,
                        "MAP_MechanoidMechanitor.Settings.StrategicNodes.Section", DrawStrategicNodeSettings);
                    DrawSettingsGroup(listing,
                        "MAP_MechanoidMechanitor.Settings.Insects.Section", DrawInsectStorySettings);
                    DrawSymbiosisCovenantSettings(listing);
                    DrawSettingsGroup(listing, "MAP_OvermindEconomy.Settings.Title",
                        group => OvermindEconomySettings.Draw(group, Settings.overmindEconomy, false));
                    break;
                case SettingsPage.Boss:
                    DrawSettingsGroup(listing,
                        "MAP_MechanoidMechanitor.Settings.JusticeBoss.Section", DrawJusticeBossDifficultySettings);
                    if (ModsConfig.OdysseyActive)
                    {
                        DrawSettingsGroup(listing,
                            "MAP_MechanoidMechanitor.Settings.CerebrexBoss.Section", DrawCerebrexBossDifficultySettings);
                    }
                    break;
                case SettingsPage.Diagnostics:
                    DrawDiagnosticsSettings(listing);
                    break;
            }

            settingsContentHeights[pageIndex] =
                Mathf.Max(contentRect.height, listing.CurHeight + SettingsBottomPadding);
            listing.End();
            Widgets.EndScrollView();

            // 收起分组后立即收回多余滚动距离，避免停在内容下方的空白区域。
            settingsScrollPositions[pageIndex].y = Mathf.Clamp(
                settingsScrollPositions[pageIndex].y, 0f,
                Mathf.Max(0f, settingsContentHeights[pageIndex] - contentRect.height));
        }

        private void DrawSettingsGroup(
            Listing_Standard listing, string titleKey, Action<Listing_Standard> drawContents)
        {
            bool expanded = !collapsedSettingsGroups.Contains(titleKey);
            string title = (expanded ? "▼ " : "▶ ") + titleKey.Translate();
            Rect header = listing.GetRect(
                Mathf.Max(30f, Text.CalcHeight(title, listing.ColumnWidth - 12f) + 8f));
            Widgets.DrawHighlight(header);
            Widgets.DrawHighlightIfMouseover(header);
            Widgets.Label(new Rect(header.x + 6f, header.y + 4f,
                header.width - 12f, header.height - 4f), title);
            if (Widgets.ButtonInvisible(header))
            {
                CommitProductivityCoreWorkSpeedBuffer();
                productivityCoreWorkSpeedFieldWasFocused = false;
                GUI.FocusControl(null);
                if (expanded)
                {
                    collapsedSettingsGroups.Add(titleKey);
                }
                else
                {
                    collapsedSettingsGroups.Remove(titleKey);
                }
                expanded = !expanded;
            }

            listing.Gap(4f);
            if (expanded)
            {
                drawContents(listing);
            }
            listing.Gap(8f);
        }

        private void DrawInterfaceSettings(Listing_Standard listing)
        {
            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.WorkTab.Label".Translate(),
                ref Settings!.addMechanoidMechanitorsToWorkTab,
                "MAP_MechanoidMechanitor.Settings.WorkTab.Description".Translate());

            listing.CheckboxLabeled(
                "MAP_MechanoidPrioritizedWorkOrders_Label".Translate(),
                ref Settings!.enableMechanoidPrioritizedWorkOrders,
                "MAP_MechanoidPrioritizedWorkOrders_Description".Translate());

            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.DataProcessing.ImmediateDraftRefresh.Label"
                    .Translate(),
                ref Settings.enableImmediateDraftStateRefresh,
                "MAP_MechanoidMechanitor.Settings.DataProcessing.ImmediateDraftRefresh.Description"
                    .Translate());

            bool previousEnablePortraitDisplayForAllSaves =
                Settings.enablePortraitDisplayForAllSaves;
            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.PortraitDisplayForAllSaves.Label".Translate(),
                ref Settings.enablePortraitDisplayForAllSaves,
                "MAP_MechanoidMechanitor.Settings.PortraitDisplayForAllSaves.Description".Translate());
            if (Settings.enablePortraitDisplayForAllSaves
                != previousEnablePortraitDisplayForAllSaves)
            {
                MechanoidMechanitorScenarioFreeColonistUtility.NotifyColonistDisplaysDirtyIfReady();
            }

            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.PurgeDirective.UiLoadingScreen.Label".Translate(),
                ref Settings.enablePurgeDirectiveUiLoadingScreen,
                "MAP_MechanoidMechanitor.Settings.PurgeDirective.UiLoadingScreen.Description".Translate());
        }

        private static void DrawAutonomySettings(Listing_Standard listing)
        {
            if (Settings == null) return;
            bool previousSun = Settings.enableSunAutonomy;
            bool previousHermit = Settings.enableHermitAutonomy;
            listing.CheckboxLabeled(
                "MAP_Settings.Autonomy.Sun.Label".Translate(),
                ref Settings.enableSunAutonomy,
                "MAP_Settings.Autonomy.Sun.Description".Translate());
            listing.CheckboxLabeled(
                "MAP_Settings.Autonomy.Hermit.Label".Translate(),
                ref Settings.enableHermitAutonomy,
                "MAP_Settings.Autonomy.Hermit.Description".Translate());
            if (previousSun != Settings.enableSunAutonomy || previousHermit != Settings.enableHermitAutonomy)
                GameComponent_AutonomousMechRegistry.NotifySettingsChanged();
        }

        private void DrawImplantSettings(Listing_Standard listing)
        {
            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.BrainImplants.Label".Translate(),
                ref Settings!.enableMechanoidMechanitorBrainImplants,
                "MAP_MechanoidMechanitor.Settings.BrainImplants.Description".Translate());
            if (MechanoidMechanitorBrainImplantFeatureState.RestartRequired)
            {
                listing.Label(
                    "MAP_MechanoidMechanitor.Settings.BrainImplants.RestartRequired"
                        .Translate());
            }

            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.MoonImplants.Label".Translate(),
                ref Settings!.enableMoonImplants,
                "MAP_MechanoidMechanitor.Settings.MoonImplants.Description".Translate());
            if (HumanImplantFeatureState.RestartRequired)
            {
                listing.Label(
                    "MAP_MechanoidMechanitor.Settings.MoonImplants.RestartRequired".Translate());
            }
        }

        private void DrawCoreSettings(Listing_Standard listing)
        {
            DrawProductivityCoreWorkSpeedSetting(listing);

            listing.CheckboxLabeled(
                "MAP_Settings.AnnihilationCannonCrater.Label".Translate(),
                ref Settings!.enableAnnihilationCannonCrater,
                "MAP_Settings.AnnihilationCannonCrater.Description".Translate());

            listing.CheckboxLabeled(
                "允许满级心灵中枢自动清除精神状态",
                ref Settings!.enableMaxLevelPsychicCoreMentalStateRecovery,
                "开启后，每10秒将尝试清除安装了满级心灵中枢的角色的精神状态。");
        }

        private void DrawSkillsAndOffspringSettings(Listing_Standard listing)
        {
            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.MinimumSkillPassion.Label".Translate(),
                ref Settings!.ensureMechanoidMechanitorMinimumMinorPassion,
                "MAP_MechanoidMechanitor.Settings.MinimumSkillPassion.Description".Translate());

            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.SyntheticOffspring.InheritXenogenes.Label".Translate(),
                ref Settings.syntheticOffspringInheritXenogenes,
                "MAP_MechanoidMechanitor.Settings.SyntheticOffspring.InheritXenogenes.Description".Translate());
        }

        private void DrawDiagnosticsSettings(Listing_Standard listing)
        {
            MAPMechanitorModSettings settings = Settings!;

            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.PreventLoadDeath.Label".Translate(),
                ref settings.preventMechanoidMechanitorDeathDuringLoad,
                "MAP_MechanoidMechanitor.Settings.PreventLoadDeath.Description"
                    .Translate());

            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.LoadDeathDiagnostics.Label"
                    .Translate(),
                ref settings.enableLoadDeathDiagnosticLogging,
                "MAP_MechanoidMechanitor.Settings.LoadDeathDiagnostics.Description"
                    .Translate());

            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.Compatibility.DetailedLogging.Label"
                    .Translate(),
                ref settings.enableCompatibilityDetailedLogging,
                "MAP_MechanoidMechanitor.Settings.Compatibility.DetailedLogging.Description"
                    .Translate());

            bool diagnosticLoggingBefore =
                settings.enableJusticeBossDiagnosticLogging;
            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.JusticeBoss.DiagnosticLogging.Label"
                    .Translate(),
                ref settings.enableJusticeBossDiagnosticLogging,
                "MAP_MechanoidMechanitor.Settings.JusticeBoss.DiagnosticLogging.Description"
                    .Translate());
            if (diagnosticLoggingBefore
                != settings.enableJusticeBossDiagnosticLogging)
            {
                JusticeBossDiagnosticsRuntime.Refresh();
            }
        }

        private static void DrawInsectStorySettings(Listing_Standard listing)
        {
            if (Settings == null)
            {
                return;
            }

            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.Insects.BlockAlliedInfestations.Label"
                    .Translate(),
                ref Settings.blockInfestationIncidentsWhenInsectsAllied,
                "MAP_MechanoidMechanitor.Settings.Insects.BlockAlliedInfestations.Description"
                    .Translate());

            listing.Gap(6f);

            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.Insects.Pursuit.AllowNoThickRoof.Label"
                    .Translate(),
                ref Settings.pursuitAllowInfestationWithoutThickRoof,
                "MAP_MechanoidMechanitor.Settings.Insects.Pursuit.AllowNoThickRoof.Description"
                    .Translate());

            Settings.pursuitGracePeriodDays =
                DrawIntSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.Insects.Pursuit.GracePeriod.Label",
                    "MAP_MechanoidMechanitor.Settings.Insects.Pursuit.GracePeriod.Description",
                    Settings.pursuitGracePeriodDays,
                    0,
                    30);

            Settings.pursuitHuntDailyChancePercent =
                DrawIntSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.Insects.Pursuit.HuntChance.Label",
                    "MAP_MechanoidMechanitor.Settings.Insects.Pursuit.HuntChance.Description",
                    Settings.pursuitHuntDailyChancePercent,
                    0,
                    100);

            Settings.pursuitExtraInfestationDailyChancePercent =
                DrawIntSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.Insects.Pursuit.ExtraInfestationChance.Label",
                    "MAP_MechanoidMechanitor.Settings.Insects.Pursuit.ExtraInfestationChance.Description",
                    Settings.pursuitExtraInfestationDailyChancePercent,
                    0,
                    100);

            Settings.pursuitSharedCooldownDays =
                DrawIntSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.Insects.Pursuit.Cooldown.Label",
                    "MAP_MechanoidMechanitor.Settings.Insects.Pursuit.Cooldown.Description",
                    Settings.pursuitSharedCooldownDays,
                    0,
                    15);

            Settings.pursuitHuntSurfacePointsPercent =
                DrawIntSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.Insects.Pursuit.SurfacePoints.Label",
                    "MAP_MechanoidMechanitor.Settings.Insects.Pursuit.SurfacePoints.Description",
                    Settings.pursuitHuntSurfacePointsPercent,
                    0,
                    1000);

            Settings.pursuitHuntInfestationPointsPercent =
                DrawIntSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.Insects.Pursuit.InfestationPoints.Label",
                    "MAP_MechanoidMechanitor.Settings.Insects.Pursuit.InfestationPoints.Description",
                    Settings.pursuitHuntInfestationPointsPercent,
                    0,
                    1000);
        }

        private static void DrawJusticeAbilitySettings(Listing_Standard listing)
        {
            if (Settings == null)
            {
                return;
            }

            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.JusticeAbilities.RestrictBossHackTargets.Label"
                    .Translate(),
                ref Settings.restrictMechHackBossTargets,
                "MAP_MechanoidMechanitor.Settings.JusticeAbilities.RestrictBossHackTargets.Description"
                    .Translate());
        }

        private static void DrawSymbiosisCovenantSettings(Listing_Standard listing)
        {
            if (Settings == null)
            {
                return;
            }

            int tenths = Mathf.Clamp(
                Settings.symbiosisCovenantGrowthMultiplierTenths,
                MAPMechanitorModSettings.MinSymbiosisCovenantGrowthMultiplierTenths,
                MAPMechanitorModSettings.MaxSymbiosisCovenantGrowthMultiplierTenths);

            // 滑块直接走整数档位 5..20，显示时才 /10，
            // 绝不保存 Slider 返回的 float，避免留下无法稳定复现的小数档位。
            float sliderValue = DrawSettingsSlider(
                listing,
                "MAP_MechanoidMechanitor.Settings.SymbiosisCovenant.GrowthMultiplier.Label"
                    .Translate(
                        (tenths / 10f).ToString("0.0", CultureInfo.CurrentCulture))
                    .ToString(),
                tenths,
                MAPMechanitorModSettings.MinSymbiosisCovenantGrowthMultiplierTenths,
                MAPMechanitorModSettings.MaxSymbiosisCovenantGrowthMultiplierTenths,
                0.62f,
                "MAP_MechanoidMechanitor.Settings.SymbiosisCovenant.GrowthMultiplier.Description"
                    .Translate()
                    .ToString());

            Settings.symbiosisCovenantGrowthMultiplierTenths = Mathf.Clamp(
                Mathf.RoundToInt(sliderValue),
                MAPMechanitorModSettings.MinSymbiosisCovenantGrowthMultiplierTenths,
                MAPMechanitorModSettings.MaxSymbiosisCovenantGrowthMultiplierTenths);
        }

        private static void DrawJusticeBossDifficultySettings(
            Listing_Standard listing)
        {
            if (Settings == null)
            {
                return;
            }

            MAPMechanitorModSettings settings = Settings;

            listing.Label(
                "MAP_MechanoidMechanitor.Settings.JusticeBoss.Description".Translate());

            if (ModsConfig.RoyaltyActive)
            {
                listing.CheckboxLabeled(
                    "MAP_MechanoidMechanitor.Settings.JusticeBoss.MortarShield.Label"
                        .Translate(),
                    ref settings.justiceBossEnableMortarShield,
                    "MAP_MechanoidMechanitor.Settings.JusticeBoss.MortarShield.Description"
                        .Translate());

                listing.CheckboxLabeled(
                    "MAP_MechanoidMechanitor.Settings.JusticeBoss.BulletShield.Label"
                        .Translate(),
                    ref settings.justiceBossEnableBulletShield,
                    "MAP_MechanoidMechanitor.Settings.JusticeBoss.BulletShield.Description"
                        .Translate());
            }

            settings.justiceBossAutoMortarCount =
                DrawIntSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.JusticeBoss.AutoMortar.Label",
                    "MAP_MechanoidMechanitor.Settings.JusticeBoss.AutoMortar.Description",
                    settings.justiceBossAutoMortarCount,
                    JusticeBossDifficultyValues.MinTurretCount,
                    JusticeBossDifficultyValues.MaxTurretCount);

            settings.justiceBossAutoChargeBlasterCount =
                DrawIntSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.JusticeBoss.AutoChargeBlaster.Label",
                    "MAP_MechanoidMechanitor.Settings.JusticeBoss.AutoChargeBlaster.Description",
                    settings.justiceBossAutoChargeBlasterCount,
                    JusticeBossDifficultyValues.MinTurretCount,
                    JusticeBossDifficultyValues.MaxTurretCount);

            settings.justiceBossAutoInfernoCount =
                DrawIntSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.JusticeBoss.AutoInferno.Label",
                    "MAP_MechanoidMechanitor.Settings.JusticeBoss.AutoInferno.Description",
                    settings.justiceBossAutoInfernoCount,
                    JusticeBossDifficultyValues.MinTurretCount,
                    JusticeBossDifficultyValues.MaxTurretCount);

            settings.justiceBossTotalWaves =
                DrawIntSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.JusticeBoss.TotalWaves.Label",
                    "MAP_MechanoidMechanitor.Settings.JusticeBoss.TotalWaves.Description",
                    settings.justiceBossTotalWaves,
                    JusticeBossDifficultyValues.MinTotalWaves,
                    JusticeBossDifficultyValues.MaxTotalWaves);

            settings.justiceBossWaveIntervalTicks =
                DrawTickIntervalSliderSetting(
                    listing,
                    settings.justiceBossWaveIntervalTicks);

            settings.justiceBossMechsPerWave =
                DrawIntSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.JusticeBoss.MechsPerWave.Label",
                    "MAP_MechanoidMechanitor.Settings.JusticeBoss.MechsPerWave.Description",
                    settings.justiceBossMechsPerWave,
                    JusticeBossDifficultyValues.MinMechsPerWave,
                    JusticeBossDifficultyValues.MaxMechsPerWave);

            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.JusticeBoss.ApplyMobileCombat.Label"
                    .Translate(),
                ref settings.justiceBossApplyMobileCombatToSummons,
                "MAP_MechanoidMechanitor.Settings.JusticeBoss.ApplyMobileCombat.Description"
                    .Translate());

            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.JusticeBoss.AllowBossReplacement.Label"
                    .Translate(),
                ref settings.justiceBossAllowBossReplacement,
                "MAP_MechanoidMechanitor.Settings.JusticeBoss.AllowBossReplacement.Description"
                    .Translate());
        }

        private static void DrawCerebrexBossDifficultySettings(
            Listing_Standard listing)
        {
            if (Settings == null || !ModsConfig.OdysseyActive)
            {
                return;
            }

            MAPMechanitorModSettings settings = Settings;

            // 进入方法前保存原始 GUI.enabled，方法结束前恢复，避免影响后续其他 MOD 设置整体变灰。
            bool outerEnabled = GUI.enabled;

            listing.Label(
                "MAP_MechanoidMechanitor.Settings.CerebrexBoss.Description".Translate());

            // 总开关自身始终可操作。
            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.CerebrexBoss.EnableExtraSkills.Label"
                    .Translate(),
                ref settings.cerebrexBossEnableExtraSkills,
                "MAP_MechanoidMechanitor.Settings.CerebrexBoss.EnableExtraSkills.Description"
                    .Translate());

            bool extraSkills = settings.cerebrexBossEnableExtraSkills;

            listing.Gap(6f);
            listing.Label(
                "MAP_MechanoidMechanitor.Settings.CerebrexBoss.Summoning.Subsection"
                    .Translate());

            // 子开关在总开关开启时可操作；子开关自身不因自己关闭而被锁死。
            GUI.enabled = outerEnabled && extraSkills;
            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.CerebrexBoss.EnableSummoning.Label"
                    .Translate(),
                ref settings.cerebrexBossEnableSummoning,
                "MAP_MechanoidMechanitor.Settings.CerebrexBoss.EnableSummoning.Description"
                    .Translate());

            GUI.enabled = outerEnabled && extraSkills && settings.cerebrexBossEnableSummoning;
            settings.cerebrexBossSummonIntervalTicks =
                DrawNamedTickIntervalSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.SummonInterval.Label",
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.SummonInterval.Description",
                    settings.cerebrexBossSummonIntervalTicks,
                    CerebrexBossDifficultyValues.MinSummonIntervalTicks,
                    CerebrexBossDifficultyValues.MaxSummonIntervalTicks,
                    CerebrexBossDifficultyValues.SummonIntervalStepTicks);

            settings.cerebrexBossMechsPerWave =
                DrawIntSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.MechsPerWave.Label",
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.MechsPerWave.Description",
                    settings.cerebrexBossMechsPerWave,
                    CerebrexBossDifficultyValues.MinMechsPerWave,
                    CerebrexBossDifficultyValues.MaxMechsPerWave);

            settings.cerebrexBossMaxLivingSummonedMechs =
                DrawIntSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.MaxLivingSummonedMechs.Label",
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.MaxLivingSummonedMechs.Description",
                    settings.cerebrexBossMaxLivingSummonedMechs,
                    CerebrexBossDifficultyValues.MinMaxLivingSummonedMechs,
                    CerebrexBossDifficultyValues.MaxMaxLivingSummonedMechs);

            GUI.enabled = outerEnabled && extraSkills && settings.cerebrexBossEnableSummoning;
            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.CerebrexBoss.ApplyMobileCombat.Label"
                    .Translate(),
                ref settings.cerebrexBossApplyMobileCombatToSummons,
                "MAP_MechanoidMechanitor.Settings.CerebrexBoss.ApplyMobileCombat.Description"
                    .Translate());

            GUI.enabled = outerEnabled && extraSkills;
            listing.Gap(6f);
            listing.Label(
                "MAP_MechanoidMechanitor.Settings.CerebrexBoss.Bandwidth.Subsection"
                    .Translate());

            GUI.enabled = outerEnabled && extraSkills;
            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.CerebrexBoss.EnableBandwidth.Label"
                    .Translate(),
                ref settings.cerebrexBossEnableBandwidthInterference,
                "MAP_MechanoidMechanitor.Settings.CerebrexBoss.EnableBandwidth.Description"
                    .Translate());

            GUI.enabled = outerEnabled && extraSkills && settings.cerebrexBossEnableBandwidthInterference;
            settings.cerebrexBossBandwidthCooldownMinTicks =
                DrawNamedTickIntervalSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.BandwidthCooldownMin.Label",
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.BandwidthCooldownMin.Description",
                    settings.cerebrexBossBandwidthCooldownMinTicks,
                    CerebrexBossDifficultyValues.MinBandwidthCooldownTicks,
                    CerebrexBossDifficultyValues.MaxBandwidthCooldownTicks,
                    CerebrexBossDifficultyValues.BandwidthCooldownStepTicks);

            settings.cerebrexBossBandwidthCooldownMaxTicks =
                DrawNamedTickIntervalSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.BandwidthCooldownMax.Label",
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.BandwidthCooldownMax.Description",
                    settings.cerebrexBossBandwidthCooldownMaxTicks,
                    CerebrexBossDifficultyValues.MinBandwidthCooldownTicks,
                    CerebrexBossDifficultyValues.MaxBandwidthCooldownTicks,
                    CerebrexBossDifficultyValues.BandwidthCooldownStepTicks);

            (settings.cerebrexBossBandwidthCooldownMinTicks,
                settings.cerebrexBossBandwidthCooldownMaxTicks) =
                CerebrexBossDifficultyValues.ClampBandwidthCooldownRange(
                    settings.cerebrexBossBandwidthCooldownMinTicks,
                    settings.cerebrexBossBandwidthCooldownMaxTicks);

            settings.cerebrexBossBandwidthDurationTicks =
                DrawNamedTickIntervalSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.BandwidthDuration.Label",
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.BandwidthDuration.Description",
                    settings.cerebrexBossBandwidthDurationTicks,
                    CerebrexBossDifficultyValues.MinBandwidthDurationTicks,
                    CerebrexBossDifficultyValues.MaxBandwidthDurationTicks,
                    CerebrexBossDifficultyValues.BandwidthDurationStepTicks);

            settings.cerebrexBossBandwidthMaxTargets =
                DrawIntSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.BandwidthMaxTargets.Label",
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.BandwidthMaxTargets.Description",
                    settings.cerebrexBossBandwidthMaxTargets,
                    CerebrexBossDifficultyValues.MinBandwidthMaxTargets,
                    CerebrexBossDifficultyValues.MaxBandwidthMaxTargets);

            GUI.enabled = outerEnabled && extraSkills;
            listing.Gap(6f);
            listing.Label(
                "MAP_MechanoidMechanitor.Settings.CerebrexBoss.Emp.Subsection"
                    .Translate());

            GUI.enabled = outerEnabled && extraSkills;
            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.CerebrexBoss.EnableEmp.Label"
                    .Translate(),
                ref settings.cerebrexBossEnableEmp,
                "MAP_MechanoidMechanitor.Settings.CerebrexBoss.EnableEmp.Description"
                    .Translate());

            GUI.enabled = outerEnabled && extraSkills && settings.cerebrexBossEnableEmp;
            settings.cerebrexBossEmpCooldownMinTicks =
                DrawNamedTickIntervalSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.EmpCooldownMin.Label",
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.EmpCooldownMin.Description",
                    settings.cerebrexBossEmpCooldownMinTicks,
                    CerebrexBossDifficultyValues.MinEmpCooldownTicks,
                    CerebrexBossDifficultyValues.MaxEmpCooldownTicks,
                    CerebrexBossDifficultyValues.EmpCooldownStepTicks);

            settings.cerebrexBossEmpCooldownMaxTicks =
                DrawNamedTickIntervalSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.EmpCooldownMax.Label",
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.EmpCooldownMax.Description",
                    settings.cerebrexBossEmpCooldownMaxTicks,
                    CerebrexBossDifficultyValues.MinEmpCooldownTicks,
                    CerebrexBossDifficultyValues.MaxEmpCooldownTicks,
                    CerebrexBossDifficultyValues.EmpCooldownStepTicks);

            (settings.cerebrexBossEmpCooldownMinTicks,
                settings.cerebrexBossEmpCooldownMaxTicks) =
                CerebrexBossDifficultyValues.ClampEmpCooldownRange(
                    settings.cerebrexBossEmpCooldownMinTicks,
                    settings.cerebrexBossEmpCooldownMaxTicks);

            settings.cerebrexBossEmpBaseDurationTicks =
                DrawNamedTickIntervalSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.EmpBaseDuration.Label",
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.EmpBaseDuration.Description",
                    settings.cerebrexBossEmpBaseDurationTicks,
                    CerebrexBossDifficultyValues.MinEmpBaseDurationTicks,
                    CerebrexBossDifficultyValues.MaxEmpBaseDurationTicks,
                    CerebrexBossDifficultyValues.EmpBaseDurationStepTicks);

            settings.cerebrexBossEmpRadius =
                DrawFloatSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.EmpRadius.Label",
                    "MAP_MechanoidMechanitor.Settings.CerebrexBoss.EmpRadius.Description",
                    settings.cerebrexBossEmpRadius,
                    CerebrexBossDifficultyValues.MinEmpRadius,
                    CerebrexBossDifficultyValues.MaxEmpRadius,
                    CerebrexBossDifficultyValues.EmpRadiusStep);

            GUI.enabled = outerEnabled;
        }

        private static void DrawStrategicNodeSettings(
            Listing_Standard listing)
        {
            if (Settings == null)
            {
                return;
            }

            MAPMechanitorModSettings settings = Settings;

            listing.Label(
                "MAP_MechanoidMechanitor.Settings.StrategicNodes.Description"
                    .Translate());

            listing.Gap(6f);

            listing.Label(
                "MAP_MechanoidMechanitor.Settings.StrategicNodes.FactionOutpost"
                    .Translate());

            settings.factionOutpostRaidChancePercent =
                DrawIntSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.StrategicNodes.FactionOutpost.RaidChance.Label",
                    "MAP_MechanoidMechanitor.Settings.StrategicNodes.FactionOutpost.RaidChance.Description",
                    settings.factionOutpostRaidChancePercent,
                    0,
                    100);

            settings.factionOutpostSupportChancePercent =
                DrawIntSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.StrategicNodes.FactionOutpost.SupportChance.Label",
                    "MAP_MechanoidMechanitor.Settings.StrategicNodes.FactionOutpost.SupportChance.Description",
                    settings.factionOutpostSupportChancePercent,
                    0,
                    100);

            settings.factionOutpostGarrisonThreatScalePercent =
                DrawSteppedIntSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.StrategicNodes.FactionOutpost.GarrisonThreatScale.Label",
                    "MAP_MechanoidMechanitor.Settings.StrategicNodes.FactionOutpost.GarrisonThreatScale.Description",
                    FactionOutpostThreatPointsUtility.ClampScalePercent(
                        settings.factionOutpostGarrisonThreatScalePercent),
                    FactionOutpostThreatPointsUtility.MinScalePercent,
                    FactionOutpostThreatPointsUtility.MaxScalePercent,
                    FactionOutpostThreatPointsUtility.ScaleStepPercent);

            listing.Gap(6f);

            listing.Label(
                "MAP_MechanoidMechanitor.Settings.StrategicNodes.MechHiveNode"
                    .Translate());

            settings.mechHiveNodeRaidChancePercent =
                DrawIntSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.StrategicNodes.MechHiveNode.RaidChance.Label",
                    "MAP_MechanoidMechanitor.Settings.StrategicNodes.MechHiveNode.RaidChance.Description",
                    settings.mechHiveNodeRaidChancePercent,
                    0,
                    100);

            settings.mechHiveNodeSupportChancePercent =
                DrawIntSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.StrategicNodes.MechHiveNode.SupportChance.Label",
                    "MAP_MechanoidMechanitor.Settings.StrategicNodes.MechHiveNode.SupportChance.Description",
                    settings.mechHiveNodeSupportChancePercent,
                    0,
                    100);
        }

        private static void DrawRecreationSettings(Listing_Standard listing)
        {
            if (Settings == null)
            {
                return;
            }

            listing.Label(
                "MAP_MechanoidMechanitor.Settings.Recreation.Description".Translate());

            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.Recreation.Enable.Label".Translate(),
                ref Settings.enableMechanoidMechanitorRecreation,
                "MAP_MechanoidMechanitor.Settings.Recreation.Enable.Description"
                    .Translate());

            Settings.mechanoidMechanitorIdleRecreationChancePercent =
                DrawIntSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.Recreation.IdleChance.Label",
                    "MAP_MechanoidMechanitor.Settings.Recreation.IdleChance.Description",
                    Settings.mechanoidMechanitorIdleRecreationChancePercent,
                    0,
                    100);

            Settings.mechanoidMechanitorInspirationChancePercent =
                DrawIntSliderSetting(
                    listing,
                    "MAP_MechanoidMechanitor.Settings.Recreation.InspirationChance.Label",
                    "MAP_MechanoidMechanitor.Settings.Recreation.InspirationChance.Description",
                    Settings.mechanoidMechanitorInspirationChancePercent,
                    0,
                    100);
        }

        // 左侧导航会缩小内容区，标签按实际高度换行，保留原有文字和滑块取值规则。
        internal static float DrawSettingsSlider(
            Listing_Standard listing, string label, float value, float min, float max,
            float labelRatio = 0.62f, string? tooltip = null)
        {
            float labelWidth = Mathf.Max(1f, listing.ColumnWidth * labelRatio - 8f);
            Rect row = listing.GetRect(Mathf.Max(30f, Text.CalcHeight(label, labelWidth)));
            Rect labelRect = new Rect(row.x, row.y, labelWidth, row.height);
            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, label);
            Text.Anchor = previousAnchor;
            if (tooltip != null)
            {
                TooltipHandler.TipRegion(labelRect, tooltip);
            }
            Rect sliderRect = new Rect(row.x + listing.ColumnWidth * labelRatio,
                row.y, listing.ColumnWidth * (1f - labelRatio), row.height);
            float result = Widgets.HorizontalSlider(
                sliderRect, value, min, max, middleAlignment: true);
            listing.Gap(listing.verticalSpacing);
            return result;
        }

        private static int DrawIntSliderSetting(
            Listing_Standard listing,
            string labelKey,
            string descriptionKey,
            int value,
            int min,
            int max)
        {
            int clamped = Mathf.Clamp(value, min, max);

            float sliderValue = DrawSettingsSlider(
                listing,
                labelKey.Translate(clamped).ToString(),
                clamped,
                min,
                max,
                0.62f,
                descriptionKey.Translate().ToString());

            return Mathf.Clamp(
                Mathf.RoundToInt(sliderValue),
                min,
                max);
        }

        /// <summary>
        /// 与 DrawIntSliderSetting 相同的展示风格，但最终值按 step 对齐后再钳制到合法范围。
        /// 用于“按固定百分比步进”的设置（如普通派系前哨防卫强度 10% 步进）。
        /// </summary>
        private static int DrawSteppedIntSliderSetting(
            Listing_Standard listing,
            string labelKey,
            string descriptionKey,
            int value,
            int min,
            int max,
            int step)
        {
            int clamped = Mathf.Clamp(value, min, max);

            float sliderValue = DrawSettingsSlider(
                listing,
                labelKey.Translate(clamped).ToString(),
                clamped,
                min,
                max,
                0.62f,
                descriptionKey.Translate().ToString());

            int stepped =
                Mathf.RoundToInt(sliderValue / step) * step;
            return Mathf.Clamp(stepped, min, max);
        }

        private static int DrawTickIntervalSliderSetting(
            Listing_Standard listing,
            int value)
        {
            int clamped =
                JusticeBossDifficultyValues.ClampWaveIntervalTicks(value);

            string secondsText =
                (clamped / 60f).ToString(
                    "0.##",
                    CultureInfo.CurrentCulture);

            float sliderValue = DrawSettingsSlider(
                listing,
                "MAP_MechanoidMechanitor.Settings.JusticeBoss.WaveInterval.Label"
                    .Translate(secondsText)
                    .ToString(),
                clamped,
                JusticeBossDifficultyValues.MinWaveIntervalTicks,
                JusticeBossDifficultyValues.MaxWaveIntervalTicks,
                0.62f,
                "MAP_MechanoidMechanitor.Settings.JusticeBoss.WaveInterval.Description"
                    .Translate()
                    .ToString());

            int stepped =
                Mathf.RoundToInt(
                    sliderValue
                    / JusticeBossDifficultyValues.WaveIntervalStepTicks)
                * JusticeBossDifficultyValues.WaveIntervalStepTicks;

            return JusticeBossDifficultyValues.ClampWaveIntervalTicks(
                stepped);
        }

        /// <summary>
        /// 通用“按秒显示”的 tick 间隔滑块：内部以游戏刻为单位存储与钳制，
        /// 标签显示换算后的秒数（保留两位有效数字）。
        /// </summary>
        private static int DrawNamedTickIntervalSliderSetting(
            Listing_Standard listing,
            string labelKey,
            string descriptionKey,
            int value,
            int min,
            int max,
            int step)
        {
            int clamped = Mathf.Clamp(value, min, max);
            int safeStep = step <= 0 ? 1 : step;

            string secondsText =
                (clamped / 60f).ToString(
                    "0.##",
                    CultureInfo.CurrentCulture);

            float sliderValue = DrawSettingsSlider(
                listing,
                labelKey.Translate(secondsText).ToString(),
                clamped,
                min,
                max,
                0.62f,
                descriptionKey.Translate().ToString());

            int stepped =
                Mathf.RoundToInt(sliderValue / safeStep) * safeStep;
            return Mathf.Clamp(stepped, min, max);
        }

        /// <summary>
        /// 通用浮点滑块：标签显示当前浮点值（保留一位小数），最终值按 step 对齐后再钳制。
        /// </summary>
        private static float DrawFloatSliderSetting(
            Listing_Standard listing,
            string labelKey,
            string descriptionKey,
            float value,
            float min,
            float max,
            float step)
        {
            float clamped = Mathf.Clamp(value, min, max);
            float safeStep = step <= 0f ? 1f : step;

            float sliderValue = DrawSettingsSlider(
                listing,
                labelKey.Translate(clamped.ToString("0.#", CultureInfo.CurrentCulture))
                    .ToString(),
                clamped,
                min,
                max,
                0.62f,
                descriptionKey.Translate().ToString());

            float stepped =
                Mathf.Round(sliderValue / safeStep) * safeStep;
            return Mathf.Clamp(stepped, min, max);
        }

        public override void WriteSettings()
        {
            CommitProductivityCoreWorkSpeedBuffer();
            JusticeBossDiagnosticsRuntime.Refresh();
            Settings?.NormalizeJusticeBossSettings();
            Settings?.NormalizeCerebrexBossSettings();
            Settings?.NormalizeSymbiosisCovenantSettings();
            Settings?.NormalizeStrategicNodeSettings();
            base.WriteSettings();
        }

        private void DrawProductivityCoreWorkSpeedSetting(Listing_Standard listing)
        {
            productivityCoreWorkSpeedBuffer ??= FormatWorkSpeedPercent(
                Settings!.productivityCoreWorkSpeedOffsetPercentPerLevel);

            string label =
                "MAP_MechanoidMechanitor.Settings.ProductivityCore.WorkSpeedOffset.Label".Translate();
            Rect row = listing.GetRect(Mathf.Max(30f,
                Text.CalcHeight(label, Mathf.Max(1f, listing.ColumnWidth - 150f))));
            Rect labelRect = new Rect(row.x, row.y, row.width - 150f, row.height);
            Widgets.Label(labelRect, label);
            TooltipHandler.TipRegion(
                labelRect,
                "MAP_MechanoidMechanitor.Settings.ProductivityCore.WorkSpeedOffset.Description".Translate());

            Rect fieldRect = new Rect(row.xMax - 140f, row.y, 110f, 30f);
            GUI.SetNextControlName(ProductivityCoreWorkSpeedControlName);
            productivityCoreWorkSpeedBuffer = Widgets.TextField(
                fieldRect,
                productivityCoreWorkSpeedBuffer);
            Widgets.Label(new Rect(fieldRect.xMax + 5f, row.y, 25f, row.height), "%");

            bool focused =
                GUI.GetNameOfFocusedControl() == ProductivityCoreWorkSpeedControlName;
            if (productivityCoreWorkSpeedFieldWasFocused && !focused)
            {
                CommitProductivityCoreWorkSpeedBuffer();
            }

            productivityCoreWorkSpeedFieldWasFocused = focused;
        }

        private void CommitProductivityCoreWorkSpeedBuffer()
        {
            if (Settings == null || productivityCoreWorkSpeedBuffer == null)
            {
                return;
            }

            string trimmed = productivityCoreWorkSpeedBuffer.Trim();
            if (!TryParseWorkSpeedPercent(trimmed, out float parsed))
            {
                productivityCoreWorkSpeedBuffer = FormatWorkSpeedPercent(
                    Settings.productivityCoreWorkSpeedOffsetPercentPerLevel);
                return;
            }

            float clamped = Mathf.Clamp(
                parsed,
                ProductivityCoreUtility.MinWorkSpeedOffsetPercentPerLevel,
                ProductivityCoreUtility.MaxWorkSpeedOffsetPercentPerLevel);
            Settings.productivityCoreWorkSpeedOffsetPercentPerLevel = clamped;
            productivityCoreWorkSpeedBuffer = FormatWorkSpeedPercent(clamped);
        }

        private static bool TryParseWorkSpeedPercent(string text, out float value)
        {
            value = 0f;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            bool parsed = float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out value)
                || float.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);
            return parsed && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string FormatWorkSpeedPercent(float value)
        {
            return value.ToString("0.##", CultureInfo.CurrentCulture);
        }
    }
}
