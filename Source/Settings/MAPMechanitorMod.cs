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

            DrawProductivityCoreWorkSpeedSetting(listing);

            listing.CheckboxLabeled(
                "MAP_Settings_SyntheticOffspringInheritXenogenes_Label".Translate(),
                ref Settings.syntheticOffspringInheritXenogenes,
                "MAP_Settings_SyntheticOffspringInheritXenogenes_Description".Translate());

            listing.End();
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
