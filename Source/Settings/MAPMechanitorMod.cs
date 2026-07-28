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

        public static MAPMechanitorModSettings? Settings;

        private string? productivityCoreWorkSpeedBuffer;
        private bool productivityCoreWorkSpeedFieldWasFocused;

        public MAPMechanitorMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<MAPMechanitorModSettings>();
        }

        public override string SettingsCategory() => "[MAP]机械族机械师";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.CheckboxLabeled(
                "将机械族机械师显示在工作标签页",
                ref Settings!.addMechanoidMechanitorsToWorkTab,
                "启用后，符合条件的机械族机械师会被追加显示到原版“工作”标签页中，方便调整工作优先级。若与修改工作标签页或工作优先级界面的 MOD 冲突，请关闭此项。");

            bool previousEnablePortraitDisplayForAllSaves =
                Settings.enablePortraitDisplayForAllSaves;
            listing.CheckboxLabeled(
                "MAP_Settings_EnablePortraitDisplayForAllSaves_Label".Translate(),
                ref Settings.enablePortraitDisplayForAllSaves,
                "MAP_Settings_EnablePortraitDisplayForAllSaves_Description".Translate());
            if (Settings.enablePortraitDisplayForAllSaves
                != previousEnablePortraitDisplayForAllSaves)
            {
                MechanoidMechanitorScenarioFreeColonistUtility.NotifyColonistDisplaysDirtyIfReady();
            }

            listing.CheckboxLabeled(
                "MAP_Settings_EnableMechanoidMechanitorBrainImplants_Label".Translate(),
                ref Settings.enableMechanoidMechanitorBrainImplants,
                "MAP_Settings_EnableMechanoidMechanitorBrainImplants_Description".Translate());
            if (MechanoidMechanitorBrainImplantFeatureState.RestartRequired)
            {
                listing.Label(
                    "MAP_Settings_EnableMechanoidMechanitorBrainImplants_RestartRequired"
                        .Translate());
            }

            listing.CheckboxLabeled(
                "MAP_Settings_EnableLoverImplants_Label".Translate(),
                ref Settings!.enableLoverImplants,
                "MAP_Settings_EnableLoverImplants_Description".Translate());
            if (LoverImplantFeatureState.RestartRequired)
            {
                listing.Label(
                    "MAP_Settings_EnableLoverImplants_RestartRequired".Translate());
            }

            DrawProductivityCoreWorkSpeedSetting(listing);

            listing.CheckboxLabeled(
                "MAP_Settings_SyntheticOffspringInheritXenogenes_Label".Translate(),
                ref Settings.syntheticOffspringInheritXenogenes,
                "MAP_Settings_SyntheticOffspringInheritXenogenes_Description".Translate());

            listing.CheckboxLabeled(
                "MAP_Settings_EnablePurgeDirectiveUiLoadingScreen_Label".Translate(),
                ref Settings.enablePurgeDirectiveUiLoadingScreen,
                "MAP_Settings_EnablePurgeDirectiveUiLoadingScreen_Description".Translate());

            DrawJusticeBossDifficultySettings(listing);

            listing.End();
        }

        private static void DrawJusticeBossDifficultySettings(
            Listing_Standard listing)
        {
            if (Settings == null)
            {
                return;
            }

            MAPMechanitorModSettings settings = Settings;

            listing.GapLine();
            listing.Label(
                "MAP_Settings_JusticeBossDifficulty_Section".Translate());

            listing.Label(
                "MAP_Settings_JusticeBossDifficulty_Description".Translate());

            if (ModsConfig.RoyaltyActive)
            {
                listing.CheckboxLabeled(
                    "MAP_Settings_JusticeBossDifficulty_MortarShield_Label"
                        .Translate(),
                    ref settings.justiceBossEnableMortarShield,
                    "MAP_Settings_JusticeBossDifficulty_MortarShield_Description"
                        .Translate());

                listing.CheckboxLabeled(
                    "MAP_Settings_JusticeBossDifficulty_BulletShield_Label"
                        .Translate(),
                    ref settings.justiceBossEnableBulletShield,
                    "MAP_Settings_JusticeBossDifficulty_BulletShield_Description"
                        .Translate());
            }

            settings.justiceBossAutoMortarCount =
                DrawIntSliderSetting(
                    listing,
                    "MAP_Settings_JusticeBossDifficulty_AutoMortar_Label",
                    "MAP_Settings_JusticeBossDifficulty_AutoMortar_Description",
                    settings.justiceBossAutoMortarCount,
                    JusticeBossDifficultyValues.MinTurretCount,
                    JusticeBossDifficultyValues.MaxTurretCount);

            settings.justiceBossAutoChargeBlasterCount =
                DrawIntSliderSetting(
                    listing,
                    "MAP_Settings_JusticeBossDifficulty_AutoChargeBlaster_Label",
                    "MAP_Settings_JusticeBossDifficulty_AutoChargeBlaster_Description",
                    settings.justiceBossAutoChargeBlasterCount,
                    JusticeBossDifficultyValues.MinTurretCount,
                    JusticeBossDifficultyValues.MaxTurretCount);

            settings.justiceBossAutoInfernoCount =
                DrawIntSliderSetting(
                    listing,
                    "MAP_Settings_JusticeBossDifficulty_AutoInferno_Label",
                    "MAP_Settings_JusticeBossDifficulty_AutoInferno_Description",
                    settings.justiceBossAutoInfernoCount,
                    JusticeBossDifficultyValues.MinTurretCount,
                    JusticeBossDifficultyValues.MaxTurretCount);

            settings.justiceBossTotalWaves =
                DrawIntSliderSetting(
                    listing,
                    "MAP_Settings_JusticeBossDifficulty_TotalWaves_Label",
                    "MAP_Settings_JusticeBossDifficulty_TotalWaves_Description",
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
                    "MAP_Settings_JusticeBossDifficulty_MechsPerWave_Label",
                    "MAP_Settings_JusticeBossDifficulty_MechsPerWave_Description",
                    settings.justiceBossMechsPerWave,
                    JusticeBossDifficultyValues.MinMechsPerWave,
                    JusticeBossDifficultyValues.MaxMechsPerWave);

            listing.CheckboxLabeled(
                "MAP_Settings_JusticeBossDifficulty_AllowBossReplacement_Label"
                    .Translate(),
                ref settings.justiceBossAllowBossReplacement,
                "MAP_Settings_JusticeBossDifficulty_AllowBossReplacement_Description"
                    .Translate());
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

            float sliderValue = listing.SliderLabeled(
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

            float sliderValue = listing.SliderLabeled(
                "MAP_Settings_JusticeBossDifficulty_WaveInterval_Label"
                    .Translate(secondsText)
                    .ToString(),
                clamped,
                JusticeBossDifficultyValues.MinWaveIntervalTicks,
                JusticeBossDifficultyValues.MaxWaveIntervalTicks,
                0.62f,
                "MAP_Settings_JusticeBossDifficulty_WaveInterval_Description"
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

        public override void WriteSettings()
        {
            CommitProductivityCoreWorkSpeedBuffer();
            base.WriteSettings();
        }

        private void DrawProductivityCoreWorkSpeedSetting(Listing_Standard listing)
        {
            productivityCoreWorkSpeedBuffer ??= FormatWorkSpeedPercent(
                Settings!.productivityCoreWorkSpeedOffsetPercentPerLevel);

            Rect row = listing.GetRect(30f);
            Rect labelRect = new Rect(row.x, row.y, row.width - 150f, row.height);
            Widgets.Label(
                labelRect,
                "MAP_Settings_ProductivityCoreWorkSpeedOffset_Label".Translate());
            TooltipHandler.TipRegion(
                labelRect,
                "MAP_Settings_ProductivityCoreWorkSpeedOffset_Description".Translate());

            Rect fieldRect = new Rect(row.xMax - 140f, row.y, 110f, row.height);
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
