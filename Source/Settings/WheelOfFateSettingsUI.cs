using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>主题选择与数值编辑；展开状态只属于界面，不作为玩法开关。</summary>
    internal static class WheelOfFateSettingsUI
    {
        // 主题默认收起；只记录当前界面的展开状态，不写入玩家设置或存档。
        private static readonly HashSet<string> expandedThemes = new HashSet<string>();
        private sealed class DayField
        {
            internal int Value;
            internal string Buffer = "";
        }
        private static readonly Dictionary<string, DayField> dayFields = new Dictionary<string, DayField>();

        internal static void Draw(Listing_Standard listing)
        {
            MAPMechanitorModSettings? settings = MAPMechanitorMod.Settings;
            if (settings == null) return;
            listing.CheckboxLabeled("MAP_WheelOfFate.Settings.Details.Label".Translate(),
                ref settings.showWheelOfFateThemeDetails,
                "MAP_WheelOfFate.Settings.Details.Description".Translate());
            listing.CheckboxLabeled("MAP_WheelOfFate.Settings.InitialTheme.Label".Translate(),
                ref settings.lockWheelOfFateInitialTheme,
                "MAP_WheelOfFate.Settings.InitialTheme.Description".Translate());
            listing.GapLine();
            listing.Label("MAP_WheelOfFate.Settings.ThemeSelection.Label".Translate());
            listing.Label("MAP_WheelOfFate.Settings.ThemeSelection.Description".Translate());

            WheelOfFateThemeExtension? extension = DefDatabase<StorytellerDef>
                .GetNamedSilentFail("MAP_WheelOfFate")?.GetModExtension<WheelOfFateThemeExtension>();
            if (extension == null) return;
            // 使用 XML 的天数排序，拖动最早出现天数时不会改变控件次序。
            var themes = DefDatabase<StoryThemeDef>.AllDefsListForReading
                .Where(theme => theme.themePoolTag == extension.themePoolTag)
                .OrderBy(theme => theme.minDaysPassed).ThenBy(theme => theme.defName).ToList();
            foreach (StoryThemeDef theme in themes)
                DrawTheme(listing, theme, extension, settings);
        }

        private static void DrawTheme(Listing_Standard listing, StoryThemeDef theme,
            WheelOfFateThemeExtension extension, MAPMechanitorModSettings settings)
        {
            bool enabled = settings.IsWheelOfFateThemeEnabled(theme.defName);
            bool previous = enabled;
            string tooltip = theme.description;
            if (theme == extension.initialTheme)
                tooltip += "\n\n" + "MAP_WheelOfFate.Settings.ThemeSelection.InitialOnly".Translate();

            bool expanded = expandedThemes.Contains(theme.defName);
            string title = (expanded ? "▼ " : "▶ ") + theme.LabelCap;
            float titleWidth = Mathf.Max(1f, listing.ColumnWidth - 42f);
            Rect header = listing.GetRect(Mathf.Max(32f, Text.CalcHeight(title, titleWidth) + 8f));
            Rect toggleRect = new Rect(header.x, header.y, Mathf.Max(1f, header.width - 36f), header.height);
            Rect checkboxRect = new Rect(header.xMax - 30f, header.y + (header.height - 24f) / 2f, 24f, 24f);
            // 沿用设置分组的扁平标题样式；展开标题与启用勾选使用独立点击区域。
            Widgets.DrawHighlight(header);
            Widgets.DrawHighlightIfMouseover(toggleRect);
            Widgets.Label(new Rect(header.x + 6f, header.y + 4f, titleWidth, header.height - 4f), title);
            TooltipHandler.TipRegion(toggleRect, tooltip);
            Widgets.Checkbox(checkboxRect.position, ref enabled);
            TooltipHandler.TipRegion(checkboxRect, "MAP_WheelOfFate.Settings.ThemeSelection.Description".Translate());
            if (Widgets.ButtonInvisible(toggleRect))
            {
                GUI.FocusControl(null);
                if (expanded) expandedThemes.Remove(theme.defName);
                else expandedThemes.Add(theme.defName);
                expanded = !expanded;
            }
            if (expanded)
            {
                listing.Gap(6f);
                float originalWidth = listing.ColumnWidth;
                listing.Indent(12f);
                listing.ColumnWidth = Mathf.Max(1f, originalWidth - 12f);
                try
                {
                    DrawThemeValues(listing, theme, settings.wheelOfFate);
                }
                finally
                {
                    listing.ColumnWidth = originalWidth;
                    listing.Outdent(12f);
                }
            }

            if (enabled != previous) settings.SetWheelOfFateThemeEnabled(theme.defName, enabled);
            listing.Gap(6f);
        }

        private static void DrawSelectionWeight(Listing_Standard listing, StoryThemeDef theme) =>
            DrawValue(listing, theme, "selectionWeight",
                "MAP_WheelOfFate.Settings.SelectionWeight".Translate(),
                theme.selectionWeight, 0f, 10f, 0.05f);

        private static void DrawThemeValues(Listing_Standard listing, StoryThemeDef theme,
            WheelOfFateSettings settings)
        {
            if (listing.ButtonText("MAP_WheelOfFate.Settings.ResetTheme".Translate()))
            {
                settings.ResetTheme(theme);
                dayFields.Remove(theme.defName);
                GUI.FocusControl(null);
            }
            DrawSelectionWeight(listing, theme);
            listing.Gap(6f);

            FloatRange previous = theme.EffectiveDurationDays;
            FloatRange duration = previous;
            Rect durationRect = listing.GetRect(42f);
            Widgets.FloatRange(durationRect, theme.shortHash + 178000, ref duration, 0.5f, 20f,
                labelKey: "MAP_WheelOfFate.Settings.Duration",
                valueStyle: ToStringStyle.FloatMaxOne);
            // 原版双端控件处理拖动与交叉，这里只将变动端吸附到 0.5、1、2……20 天。
            // 没有交互时不量化、不写入，因此不会把 XML 默认值变成永久覆盖值。
            if (duration.min != previous.min || duration.max != previous.max)
            {
                if (duration.min != previous.min) duration.min = SnapDay(duration.min);
                if (duration.max != previous.max) duration.max = SnapDay(duration.max);
                duration.max = Mathf.Max(duration.min, duration.max);
                settings.SetValue(theme, "durationMin", duration.min, theme.durationDays.min);
                settings.SetValue(theme, "durationMax", duration.max, theme.durationDays.max);
            }
            listing.Gap();
            DrawMinDays(listing, theme, settings);
            DrawValue(listing, theme, "incidentIntervalFactor",
                "MAP_WheelOfFate.Settings.IncidentInterval".Translate(), theme.incidentIntervalFactor,
                0.05f, 10f, 0.05f, "MAP_WheelOfFate.Settings.IncidentInterval.Description".Translate());

            // 因果扰动等权抽选时，这些倍率不参与事件/任务选择，只保留说明。
            // 上面的主题选择权重、持续时间、出现天数和事件间隔仍然有效。
            if (theme.equalIncidentWeights)
            {
                listing.Label("MAP_WheelOfFate.Settings.EqualWeightsHint".Translate());
                return;
            }
            listing.Label("MAP_WheelOfFate.Settings.WeightFactors".Translate());
            if (theme.invertIncidentWeights)
                listing.Label("MAP_WheelOfFate.Settings.InvertedWeightsHint".Translate());
            DrawFactor(listing, theme, "positiveRandomIncidentWeightFactor",
                "MAP_WheelOfFate.Settings.PositiveWeight".Translate(), theme.positiveRandomIncidentWeightFactor);
            var shownLetters = new HashSet<LetterDef>();
            foreach (LetterDef letter in new[] { LetterDefOf.NegativeEvent, LetterDefOf.ThreatSmall,
                LetterDefOf.ThreatBig, LetterDefOf.NeutralEvent })
            {
                DrawLetterFactor(listing, theme, letter);
                shownLetters.Add(letter);
            }
            // 其余 XML 信件规则也开放倍率，不新增或编辑匹配条件。
            if (theme.incidentWeightRules != null)
                foreach (LetterDef letter in theme.incidentWeightRules
                    .Where(rule => rule != null && rule.IsValid && rule.incident == null)
                    .Select(rule => rule.letterDef!).Distinct())
                    if (shownLetters.Add(letter)) DrawLetterFactor(listing, theme, letter);

            DrawFactor(listing, theme, "charityWeightFactor",
                "MAP_WheelOfFate.Settings.CharityWeight".Translate(), theme.charityWeightFactor);
            DrawFactor(listing, theme, "permanentEnemyRaidFactionFactor",
                "MAP_WheelOfFate.Settings.EnemyWeight".Translate(), theme.permanentEnemyRaidFactionFactor);

            if (theme.categoryWeightFactors != null)
                foreach (var entry in theme.categoryWeightFactors)
                    if (entry?.category != null)
                        DrawFactor(listing, theme, "category:" + entry.category.defName,
                            "MAP_WheelOfFate.Settings.CategoryWeight".Translate(CategoryDisplayName(entry.category)),
                            entry.weight);
            foreach (var group in theme.SpecificIncidentRuleGroups())
            {
                StoryThemeIncidentWeightRule rule = group.Rule;
                string name = rule.incident!.LabelCap;
                if (rule.letterDef != null)
                    name += " / " + rule.letterDef.LabelCap;
                DrawFactor(listing, theme, group.Key,
                    "MAP_WheelOfFate.Settings.IncidentWeight".Translate(name),
                    group.XmlFactor);
            }
            if (theme.raidFactionWeightFactors != null)
                foreach (var entry in theme.raidFactionWeightFactors)
                    if (entry?.factionDef != null)
                        DrawFactor(listing, theme, "faction:" + entry.factionDef.defName,
                            "MAP_WheelOfFate.Settings.FactionWeight".Translate(entry.factionDef.LabelCap),
                            entry.factor);
        }

        private static string CategoryDisplayName(IncidentCategoryDef category)
        {
            string key = "MAP_WheelOfFate.Settings.Category." + category.defName;
            if (key.CanTranslate()) return key.Translate().ToString();
            // 第三方类别优先保留自己的名称；未配置 label 时显示标识，避免空括号。
            return category.label.NullOrEmpty() ? category.defName : category.LabelCap.ToString();
        }

        private static void DrawMinDays(Listing_Standard listing, StoryThemeDef theme, WheelOfFateSettings settings)
        {
            int current = theme.EffectiveMinDaysPassed;
            if (!dayFields.TryGetValue(theme.defName, out var field))
            {
                field = new DayField();
                dayFields.Add(theme.defName, field);
            }
            // 恢复默认或 XML 值变化后同步输入，避免旧缓冲将重置结果写回。
            if (field.Value != current || field.Buffer == "")
            {
                field.Value = current;
                field.Buffer = current.ToString();
            }
            string label = "MAP_WheelOfFate.Settings.MinDays".Translate();
            float labelWidth = Mathf.Max(1f, listing.ColumnWidth - 112f);
            Rect row = listing.GetRect(Mathf.Max(30f, Text.CalcHeight(label, labelWidth)));
            Widgets.Label(new Rect(row.x, row.y, labelWidth, row.height), label);
            Widgets.TextFieldNumeric(new Rect(row.xMax - 100f, row.y, 100f, 30f),
                ref field.Value, ref field.Buffer, 0f, 1000000f);
            if (field.Value != current)
                settings.SetValue(theme, "minDaysPassed", field.Value, theme.minDaysPassed);
            listing.Gap();
        }

        private static float SnapDay(float value) =>
            value < 0.75f ? 0.5f : Mathf.Clamp(Mathf.Round(value), 1f, 20f);

        private static void DrawLetterFactor(Listing_Standard listing, StoryThemeDef theme, LetterDef letter)
        {
            string label = letter == LetterDefOf.NegativeEvent ? "NegativeWeight"
                : letter == LetterDefOf.ThreatSmall ? "SmallThreatWeight"
                : letter == LetterDefOf.ThreatBig ? "BigThreatWeight"
                : letter == LetterDefOf.NeutralEvent ? "NeutralWeight" : "LetterWeight";
            DrawFactor(listing, theme, "letter:" + letter.defName,
                ("MAP_WheelOfFate.Settings." + label).Translate(letter.LabelCap),
                theme.XmlLetterFactor(letter));
        }

        private static void DrawFactor(Listing_Standard listing, StoryThemeDef theme,
            string key, string label, float xmlDefault) =>
            DrawValue(listing, theme, key, label, xmlDefault, 0f, 10f, 0.05f);

        private static void DrawValue(Listing_Standard listing, StoryThemeDef theme,
            string key, string label, float xmlDefault, float min, float max, float step,
            string? tooltip = null)
        {
            WheelOfFateSettings? settings = MAPMechanitorMod.Settings?.wheelOfFate;
            if (settings == null) return;
            float value = settings.Value(theme, key, xmlDefault, min);
            string display = key == "minDaysPassed" ? value.ToString("0") : value.ToString("0.###");
            float raw = MAPMechanitorMod.DrawSettingsSlider(listing, label + "：" + display,
                value, Mathf.Min(min, xmlDefault), Mathf.Max(max, Mathf.Max(xmlDefault, value)),
                tooltip: tooltip);
            if (raw == value) return;
            float next = Mathf.Max(min, Mathf.Round(raw / step) * step);
            settings.SetValue(theme, key, next, xmlDefault);
        }
    }
}
