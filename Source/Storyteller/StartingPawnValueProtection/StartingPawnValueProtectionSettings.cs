using HarmonyLib;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 开局 Pawn 价值保护的全局设置。
    /// 这些字段通过 MAPMechanitorModSettings.ExposeData 的 Harmony 补丁写入同一份 MOD 设置文件；
    /// 运行中的存档不会直接读取这些字段，而是由 GameComponent 在新开局/读档时建立快照。
    /// </summary>
    public static class StartingPawnValueProtectionSettings
    {
        public const bool DefaultEnableMechanitorStartingValueProtection = true;
        public const bool DefaultProtectOtherStartingMechs = false;

        public const int MinDurationDays = 0;
        public const int MaxDurationDays = 180;
        public const int DurationStepDays = 5;
        public const int DefaultDurationDays = 60;

        public const int MinMinimumFactorPercent = 0;
        public const int MaxMinimumFactorPercent = 100;
        public const int MinimumFactorStepPercent = 5;
        public const int DefaultMinimumFactorPercent = 0;

        public static bool enableMechanitorStartingValueProtection =
            DefaultEnableMechanitorStartingValueProtection;

        public static bool protectOtherStartingMechs =
            DefaultProtectOtherStartingMechs;

        public static int durationDays = DefaultDurationDays;
        public static int minimumFactorPercent = DefaultMinimumFactorPercent;

        public static bool ShouldProtectStartingMechanitor =>
            enableMechanitorStartingValueProtection && durationDays > 0;

        public static bool ShouldProtectOtherStartingMechs =>
            ShouldProtectStartingMechanitor && protectOtherStartingMechs;

        public static void Normalize()
        {
            durationDays = NormalizeSteppedInt(
                durationDays,
                MinDurationDays,
                MaxDurationDays,
                DurationStepDays);

            minimumFactorPercent = NormalizeSteppedInt(
                minimumFactorPercent,
                MinMinimumFactorPercent,
                MaxMinimumFactorPercent,
                MinimumFactorStepPercent);
        }

        private static int NormalizeSteppedInt(
            int value,
            int min,
            int max,
            int step)
        {
            int clamped = Mathf.Clamp(value, min, max);
            int safeStep = Mathf.Max(1, step);
            int stepped = Mathf.RoundToInt(clamped / (float)safeStep) * safeStep;
            return Mathf.Clamp(stepped, min, max);
        }
    }

    [HarmonyPatch(typeof(MAPMechanitorModSettings), nameof(MAPMechanitorModSettings.ExposeData))]
    internal static class StartingPawnValueProtectionSettings_ExposeData_Patch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            Scribe_Values.Look(
                ref StartingPawnValueProtectionSettings.enableMechanitorStartingValueProtection,
                "enableMechanitorStartingValueProtection",
                StartingPawnValueProtectionSettings.DefaultEnableMechanitorStartingValueProtection);

            Scribe_Values.Look(
                ref StartingPawnValueProtectionSettings.protectOtherStartingMechs,
                "protectOtherStartingMechs",
                StartingPawnValueProtectionSettings.DefaultProtectOtherStartingMechs);

            Scribe_Values.Look(
                ref StartingPawnValueProtectionSettings.durationDays,
                "startingPawnValueProtectionDurationDays",
                StartingPawnValueProtectionSettings.DefaultDurationDays);

            Scribe_Values.Look(
                ref StartingPawnValueProtectionSettings.minimumFactorPercent,
                "startingPawnValueProtectionMinimumFactorPercent",
                StartingPawnValueProtectionSettings.DefaultMinimumFactorPercent);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                StartingPawnValueProtectionSettings.Normalize();
            }
        }
    }

    [HarmonyPatch(typeof(MAPMechanitorMod), nameof(MAPMechanitorMod.WriteSettings))]
    internal static class StartingPawnValueProtectionSettings_WriteSettings_Patch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            StartingPawnValueProtectionSettings.Normalize();
        }
    }

    /// <summary>
    /// 将本功能设置段插入现有设置列表。挂在 DrawRecreationSettings 前缀上，
    /// 以复用原设置页的 Listing 与滚动高度计算，不改动原设置文件。
    /// </summary>
    [HarmonyPatch(typeof(MAPMechanitorMod), "DrawRecreationSettings")]
    internal static class StartingPawnValueProtectionSettings_Draw_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Listing_Standard listing)
        {
            DrawSettings(listing);
        }

        private static void DrawSettings(Listing_Standard listing)
        {
            StartingPawnValueProtectionSettings.Normalize();

            listing.GapLine();
            listing.Label(
                "MAP_MechanoidMechanitor.Settings.StartingPawnValueProtection.Section"
                    .Translate());

            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.StartingPawnValueProtection.EnableMechanitor.Label"
                    .Translate(),
                ref StartingPawnValueProtectionSettings.enableMechanitorStartingValueProtection,
                "MAP_MechanoidMechanitor.Settings.StartingPawnValueProtection.EnableMechanitor.Description"
                    .Translate());

            Rect otherMechsRow = listing.GetRect(30f);
            Widgets.CheckboxLabeled(
                otherMechsRow,
                "MAP_MechanoidMechanitor.Settings.StartingPawnValueProtection.OtherMechs.Label"
                    .Translate(),
                ref StartingPawnValueProtectionSettings.protectOtherStartingMechs,
                disabled: !StartingPawnValueProtectionSettings.enableMechanitorStartingValueProtection);
            TooltipHandler.TipRegion(
                otherMechsRow,
                "MAP_MechanoidMechanitor.Settings.StartingPawnValueProtection.OtherMechs.Description"
                    .Translate());

            StartingPawnValueProtectionSettings.durationDays = DrawSteppedSlider(
                listing,
                "MAP_MechanoidMechanitor.Settings.StartingPawnValueProtection.Duration.Label",
                StartingPawnValueProtectionSettings.durationDays,
                StartingPawnValueProtectionSettings.MinDurationDays,
                StartingPawnValueProtectionSettings.MaxDurationDays,
                StartingPawnValueProtectionSettings.DurationStepDays);

            StartingPawnValueProtectionSettings.minimumFactorPercent = DrawSteppedSlider(
                listing,
                "MAP_MechanoidMechanitor.Settings.StartingPawnValueProtection.MinimumFactor.Label",
                StartingPawnValueProtectionSettings.minimumFactorPercent,
                StartingPawnValueProtectionSettings.MinMinimumFactorPercent,
                StartingPawnValueProtectionSettings.MaxMinimumFactorPercent,
                StartingPawnValueProtectionSettings.MinimumFactorStepPercent);
        }

        private static int DrawSteppedSlider(
            Listing_Standard listing,
            string labelKey,
            int value,
            int min,
            int max,
            int step)
        {
            int clamped = Mathf.Clamp(value, min, max);
            int safeStep = Mathf.Max(1, step);

            float sliderValue = listing.SliderLabeled(
                labelKey.Translate(clamped).ToString(),
                clamped,
                min,
                max,
                0.62f);

            int stepped = Mathf.RoundToInt(sliderValue / safeStep) * safeStep;
            return Mathf.Clamp(stepped, min, max);
        }
    }
}
