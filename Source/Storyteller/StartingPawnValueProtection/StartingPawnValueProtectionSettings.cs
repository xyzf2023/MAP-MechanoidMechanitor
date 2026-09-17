using HarmonyLib;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 开局 Pawn 价值保护的独立设置数据。
    /// 使用 LoadedModManager 的原版设置读写 API 单独持久化，避免依赖 Harmony 安装时序去劫持
    /// MAPMechanitorModSettings 的首次加载。
    /// </summary>
    public sealed class StartingPawnValueProtectionModSettings : ModSettings
    {
        public bool enableMechanitorStartingValueProtection =
            StartingPawnValueProtectionSettings.DefaultEnableMechanitorStartingValueProtection;

        public bool protectOtherStartingMechs =
            StartingPawnValueProtectionSettings.DefaultProtectOtherStartingMechs;

        public int durationDays = StartingPawnValueProtectionSettings.DefaultDurationDays;
        public int minimumFactorPercent =
            StartingPawnValueProtectionSettings.DefaultMinimumFactorPercent;

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(
                ref enableMechanitorStartingValueProtection,
                "enableMechanitorStartingValueProtection",
                StartingPawnValueProtectionSettings.DefaultEnableMechanitorStartingValueProtection);

            Scribe_Values.Look(
                ref protectOtherStartingMechs,
                "protectOtherStartingMechs",
                StartingPawnValueProtectionSettings.DefaultProtectOtherStartingMechs);

            Scribe_Values.Look(
                ref durationDays,
                "startingPawnValueProtectionDurationDays",
                StartingPawnValueProtectionSettings.DefaultDurationDays);

            Scribe_Values.Look(
                ref minimumFactorPercent,
                "startingPawnValueProtectionMinimumFactorPercent",
                StartingPawnValueProtectionSettings.DefaultMinimumFactorPercent);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Normalize();
            }
        }

        public void Normalize()
        {
            durationDays = StartingPawnValueProtectionSettings.NormalizeSteppedInt(
                durationDays,
                StartingPawnValueProtectionSettings.MinDurationDays,
                StartingPawnValueProtectionSettings.MaxDurationDays,
                StartingPawnValueProtectionSettings.DurationStepDays);

            minimumFactorPercent = StartingPawnValueProtectionSettings.NormalizeSteppedInt(
                minimumFactorPercent,
                StartingPawnValueProtectionSettings.MinMinimumFactorPercent,
                StartingPawnValueProtectionSettings.MaxMinimumFactorPercent,
                StartingPawnValueProtectionSettings.MinimumFactorStepPercent);
        }
    }

    /// <summary>
    /// 价值保护设置访问入口。
    /// 运行中的存档不会直接读取实时设置，而是由 GameComponent 在新开局/读档时建立快照。
    /// </summary>
    public static class StartingPawnValueProtectionSettings
    {
        private const string SettingsHandleName =
            "MAPMechanitorMod_StartingPawnValueProtection";

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

        private static StartingPawnValueProtectionModSettings? settings;
        private static readonly StartingPawnValueProtectionModSettings FallbackSettings =
            new StartingPawnValueProtectionModSettings();

        public static StartingPawnValueProtectionModSettings Data
        {
            get
            {
                EnsureLoaded();
                return settings ?? FallbackSettings;
            }
        }

        public static int durationDays => Data.durationDays;
        public static int minimumFactorPercent => Data.minimumFactorPercent;

        public static bool ShouldProtectStartingMechanitor =>
            Data.enableMechanitorStartingValueProtection && Data.durationDays > 0;

        public static bool ShouldProtectOtherStartingMechs =>
            ShouldProtectStartingMechanitor && Data.protectOtherStartingMechs;

        public static void Normalize()
        {
            Data.Normalize();
        }

        public static void Write()
        {
            EnsureLoaded();
            if (settings == null)
            {
                return;
            }

            settings.Normalize();
            string? modIdentifier = ResolveModIdentifier();
            if (modIdentifier.NullOrEmpty())
            {
                Log.Warning(
                    "[MAP-机械族机械师] 开局价值保护设置：无法确认 MOD 标识，设置未写入磁盘。");
                return;
            }

            LoadedModManager.WriteModSettings(
                modIdentifier,
                SettingsHandleName,
                settings);
        }

        internal static int NormalizeSteppedInt(
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

        internal static void EnsureLoaded()
        {
            if (settings != null)
            {
                return;
            }

            // 严禁在游戏/设置 Scribe 正在读写时嵌套启动另一份设置文件的 Scribe。
            // 正常流程会在 StaticConstructorOnStartup 阶段预载；若预载异常，LoadedGame 后会重试。
            if (Scribe.mode != LoadSaveMode.Inactive)
            {
                return;
            }

            string? modIdentifier = ResolveModIdentifier();
            if (modIdentifier.NullOrEmpty())
            {
                return;
            }

            settings = LoadedModManager.ReadModSettings<StartingPawnValueProtectionModSettings>(
                    modIdentifier,
                    SettingsHandleName)
                ?? new StartingPawnValueProtectionModSettings();
            settings.Normalize();
        }

        private static string? ResolveModIdentifier()
        {
            return MAPMechanitorMod.Settings?.Mod?.Content?.FolderName;
        }
    }

    [StaticConstructorOnStartup]
    internal static class StartingPawnValueProtectionSettingsBootstrap
    {
        static StartingPawnValueProtectionSettingsBootstrap()
        {
            StartingPawnValueProtectionSettings.EnsureLoaded();
        }
    }

    [HarmonyPatch(typeof(MAPMechanitorMod), nameof(MAPMechanitorMod.WriteSettings))]
    internal static class StartingPawnValueProtectionSettings_WriteSettings_Patch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            StartingPawnValueProtectionSettings.Write();
        }
    }

    /// <summary>由 MOD 设置页直接绘制开局价值保护选项。</summary>
    internal static class StartingPawnValueProtectionSettingsUI
    {
        internal static void Draw(Listing_Standard listing)
        {
            StartingPawnValueProtectionModSettings settings =
                StartingPawnValueProtectionSettings.Data;
            settings.Normalize();

            listing.CheckboxLabeled(
                "MAP_MechanoidMechanitor.Settings.StartingPawnValueProtection.EnableMechanitor.Label"
                    .Translate(),
                ref settings.enableMechanitorStartingValueProtection,
                "MAP_MechanoidMechanitor.Settings.StartingPawnValueProtection.EnableMechanitor.Description"
                    .Translate());

            string otherMechsLabel =
                "MAP_MechanoidMechanitor.Settings.StartingPawnValueProtection.OtherMechs.Label".Translate();
            Rect otherMechsRow = listing.GetRect(Mathf.Max(30f,
                Text.CalcHeight(otherMechsLabel, Mathf.Max(1f, listing.ColumnWidth - 30f))));
            Widgets.CheckboxLabeled(
                otherMechsRow,
                otherMechsLabel,
                ref settings.protectOtherStartingMechs,
                disabled: !settings.enableMechanitorStartingValueProtection);
            TooltipHandler.TipRegion(
                otherMechsRow,
                "MAP_MechanoidMechanitor.Settings.StartingPawnValueProtection.OtherMechs.Description"
                    .Translate());

            settings.durationDays = DrawSteppedSlider(
                listing,
                "MAP_MechanoidMechanitor.Settings.StartingPawnValueProtection.Duration.Label",
                settings.durationDays,
                StartingPawnValueProtectionSettings.MinDurationDays,
                StartingPawnValueProtectionSettings.MaxDurationDays,
                StartingPawnValueProtectionSettings.DurationStepDays);

            settings.minimumFactorPercent = DrawSteppedSlider(
                listing,
                "MAP_MechanoidMechanitor.Settings.StartingPawnValueProtection.MinimumFactor.Label",
                settings.minimumFactorPercent,
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

            float sliderValue = MAPMechanitorMod.DrawSettingsSlider(
                listing,
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
