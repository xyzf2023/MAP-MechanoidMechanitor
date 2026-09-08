using System;
using System.Collections.Generic;
using HarmonyLib;
using MAP_MechanoidMechanitor;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// MOD 设置页中的“普通剧本默认剧情配置”。这些值只作为之后新建普通游戏的
    /// 配置快照来源，不实时控制已经建立的存档；普通派系逐派系 Custom 模式不在此暴露。
    /// </summary>
    public static class MechanoidMechanitorGeneralScenarioDefaultSettingsUI
    {
        private const float RowHeight = 30f;
        private const float LabelRatio = 0.62f;
        private const float RowGap = 8f;
        private const float WeightValueWidth = 36f;

        private static readonly MechanoidMechanitorOrdinaryFactionRelationsMode[]
            OrdinaryFactionModes =
            {
                MechanoidMechanitorOrdinaryFactionRelationsMode.Default,
                MechanoidMechanitorOrdinaryFactionRelationsMode.AllHostile,
                MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentHostile,
                MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentNeutral,
                MechanoidMechanitorOrdinaryFactionRelationsMode.AllAlly,
                MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentAlly
            };

        private static readonly MechanoidMechanitorFactionOutpostFrequency[]
            FactionOutpostFrequencies =
            {
                MechanoidMechanitorFactionOutpostFrequency.Off,
                MechanoidMechanitorFactionOutpostFrequency.Low,
                MechanoidMechanitorFactionOutpostFrequency.Medium,
                MechanoidMechanitorFactionOutpostFrequency.High
            };

        private static readonly MechanoidMechanitorMechHiveRelationMode[]
            MechHiveRelationModes =
            {
                MechanoidMechanitorMechHiveRelationMode.Default,
                MechanoidMechanitorMechHiveRelationMode.Neutral,
                MechanoidMechanitorMechHiveRelationMode.PermanentNeutral,
                MechanoidMechanitorMechHiveRelationMode.Ally
            };

        private static readonly MechanoidMechanitorInsectRelationMode[]
            InsectRelationModes =
            {
                MechanoidMechanitorInsectRelationMode.Default,
                MechanoidMechanitorInsectRelationMode.PermanentNeutral,
                MechanoidMechanitorInsectRelationMode.Ally,
                MechanoidMechanitorInsectRelationMode.Pursuit
            };

        private static readonly MechanoidMechanitorMechHiveNodeFrequency[]
            MechHiveNodeFrequencies =
            {
                MechanoidMechanitorMechHiveNodeFrequency.Off,
                MechanoidMechanitorMechHiveNodeFrequency.Low,
                MechanoidMechanitorMechHiveNodeFrequency.Medium,
                MechanoidMechanitorMechHiveNodeFrequency.High
            };

        private static readonly MechanoidMechanitorIdeologyAdaptationLevel[]
            IdeologyAdaptationLevels =
            {
                MechanoidMechanitorIdeologyAdaptationLevel.Disabled,
                MechanoidMechanitorIdeologyAdaptationLevel.Partial,
                MechanoidMechanitorIdeologyAdaptationLevel.Full
            };

        public static void Draw(Listing_Standard listing)
        {
            MAPMechanitorModSettings? settings = MAPMechanitorMod.Settings;
            if (settings == null)
            {
                return;
            }

            MechanoidMechanitorGeneralScenarioTemplateModSettings template =
                MechanoidMechanitorGeneralScenarioTemplateSettings.Current;
            MechanoidMechanitorGeneralScenarioTemplateSettings.Normalize(settings, template);

            listing.Label(
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.Section"
                    .Translate());
            listing.Label(
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.Description"
                    .Translate());

            bool outerEnabled = GUI.enabled;
            bool templateEnabled = outerEnabled && !settings.enableStoryStylesForGeneralScenarios;
            GUI.enabled = templateEnabled;

            DrawSubsectionLabel(
                listing,
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.OrdinaryFaction.Subsection");
            DrawDropdownRow(
                listing,
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.OrdinaryFactionRelations.Label",
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.OrdinaryFactionRelations.Description",
                template.generalScenarioOrdinaryFactionRelationsMode,
                OrdinaryFactionModes,
                MechanoidMechanitorStoryConfigurationLabels.LabelFor,
                value =>
                {
                    template.generalScenarioOrdinaryFactionRelationsMode = value;
                    MechanoidMechanitorGeneralScenarioTemplateSettings.Normalize(settings, template);
                });

            DrawDropdownRow(
                listing,
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.FactionOutpost.Label",
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.FactionOutpost.Description",
                settings.generalScenarioFactionOutpostFrequency,
                FactionOutpostFrequencies,
                LabelForFactionOutpostFrequency,
                value =>
                {
                    settings.generalScenarioFactionOutpostFrequency = value;
                    MechanoidMechanitorGeneralScenarioTemplateSettings.Normalize(settings, template);
                });

            bool outpostWeightsEnabled = templateEnabled
                && settings.generalScenarioFactionOutpostFrequency
                    != MechanoidMechanitorFactionOutpostFrequency.Off;
            DrawWeightRow(
                listing,
                "MAP_MechanoidMechanitor.Story.FactionOutpost.Weight.Hostile",
                ref template.generalScenarioHostileFactionOutpostWeight,
                outpostWeightsEnabled);
            DrawWeightRow(
                listing,
                "MAP_MechanoidMechanitor.Story.FactionOutpost.Weight.Ally",
                ref template.generalScenarioAllyFactionOutpostWeight,
                outpostWeightsEnabled);
            DrawWeightRow(
                listing,
                "MAP_MechanoidMechanitor.Story.FactionOutpost.Weight.Neutral",
                ref template.generalScenarioNeutralFactionOutpostWeight,
                outpostWeightsEnabled);

            DrawSubsectionLabel(
                listing,
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.HostileFactions.Subsection");
            DrawDropdownRow(
                listing,
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.MechHiveRelation.Label",
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.MechHiveRelation.Description",
                template.generalScenarioMechHiveRelationMode,
                MechHiveRelationModes,
                MechanoidMechanitorStoryConfigurationLabels.LabelFor,
                value =>
                {
                    template.generalScenarioMechHiveRelationMode = value;
                    MechanoidMechanitorGeneralScenarioTemplateSettings.Normalize(settings, template);
                });

            DrawDropdownRow(
                listing,
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.MechHiveNode.Label",
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.MechHiveNode.Description",
                settings.generalScenarioMechHiveNodeFrequency,
                MechHiveNodeFrequencies,
                MechanoidMechanitorStoryConfigurationLabels.LabelFor,
                value =>
                {
                    settings.generalScenarioMechHiveNodeFrequency = value;
                    MechanoidMechanitorGeneralScenarioTemplateSettings.Normalize(settings, template);
                });

            DrawDropdownRow(
                listing,
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.InsectRelation.Label",
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.InsectRelation.Description",
                template.generalScenarioInsectRelationMode,
                InsectRelationModes,
                MechanoidMechanitorStoryConfigurationLabels.LabelFor,
                value =>
                {
                    template.generalScenarioInsectRelationMode = value;
                    MechanoidMechanitorGeneralScenarioTemplateSettings.Normalize(settings, template);
                });

            DrawSubsectionLabel(
                listing,
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.Routes.Subsection");

            bool purgeAvailable =
                MechanoidMechanitorGeneralScenarioTemplateSettings.CanEnablePurgeDirective(template);
            DrawBooleanRow(
                listing,
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.PurgeDirective.Label",
                purgeAvailable
                    ? "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.PurgeDirective.Description"
                    : "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.PurgeDirective.DisabledReason",
                template.generalScenarioPurgeDirectiveEnabled,
                templateEnabled && purgeAvailable,
                value =>
                {
                    template.generalScenarioPurgeDirectiveEnabled = value;
                    if (value)
                    {
                        template.generalScenarioSymbiosisCovenantEnabled = false;
                    }

                    MechanoidMechanitorGeneralScenarioTemplateSettings.Normalize(settings, template);
                });

            bool symbiosisAvailable =
                MechanoidMechanitorGeneralScenarioTemplateSettings.CanEnableSymbiosisCovenant(
                    template.generalScenarioOrdinaryFactionRelationsMode);
            DrawBooleanRow(
                listing,
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.SymbiosisCovenant.Label",
                symbiosisAvailable
                    ? "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.SymbiosisCovenant.Description"
                    : "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.SymbiosisCovenant.DisabledReason",
                template.generalScenarioSymbiosisCovenantEnabled,
                templateEnabled && symbiosisAvailable,
                value =>
                {
                    template.generalScenarioSymbiosisCovenantEnabled = value;
                    if (value)
                    {
                        template.generalScenarioPurgeDirectiveEnabled = false;
                    }

                    MechanoidMechanitorGeneralScenarioTemplateSettings.Normalize(settings, template);
                });

            DrawSubsectionLabel(
                listing,
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.Ideology.Subsection");
            DrawDropdownRow(
                listing,
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.IdeologyAdaptation.Label",
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.IdeologyAdaptation.Description",
                template.generalScenarioIdeologyAdaptationLevel,
                IdeologyAdaptationLevels,
                MechanoidMechanitorStoryConfigurationLabels.LabelFor,
                value =>
                {
                    template.generalScenarioIdeologyAdaptationLevel = value;
                    MechanoidMechanitorGeneralScenarioTemplateSettings.Normalize(settings, template);
                });

            GUI.enabled = outerEnabled;
            if (settings.enableStoryStylesForGeneralScenarios)
            {
                listing.Label(
                    "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.DisabledNotice"
                        .Translate());
            }
        }

        private static void DrawSubsectionLabel(Listing_Standard listing, string key)
        {
            listing.Gap(6f);
            listing.Label(key.Translate());
        }

        private static void DrawDropdownRow<T>(
            Listing_Standard listing,
            string labelKey,
            string descriptionKey,
            T currentValue,
            IReadOnlyList<T> options,
            Func<T, string> labelSelector,
            Action<T> setter)
        {
            Rect row = listing.GetRect(RowHeight);
            DrawRowLabel(row, labelKey, descriptionKey, out Rect buttonRect);
            if (!Widgets.ButtonText(buttonRect, labelSelector(currentValue)))
            {
                return;
            }

            List<FloatMenuOption> menuOptions = new List<FloatMenuOption>(options.Count);
            for (int i = 0; i < options.Count; i++)
            {
                T option = options[i];
                menuOptions.Add(
                    new FloatMenuOption(
                        labelSelector(option),
                        () => setter(option)));
            }

            Find.WindowStack.Add(new FloatMenu(menuOptions));
        }

        private static void DrawBooleanRow(
            Listing_Standard listing,
            string labelKey,
            string descriptionKey,
            bool currentValue,
            bool enabled,
            Action<bool> setter)
        {
            Rect row = listing.GetRect(RowHeight);
            DrawRowLabel(row, labelKey, descriptionKey, out Rect buttonRect);

            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && enabled;
            if (Widgets.ButtonText(
                    buttonRect,
                    currentValue
                        ? MechanoidMechanitorStoryConfigurationLabels.EnabledLabel
                        : MechanoidMechanitorStoryConfigurationLabels.DisabledLabel))
            {
                setter(!currentValue);
            }

            GUI.enabled = previousEnabled;
        }

        private static void DrawWeightRow(
            Listing_Standard listing,
            string labelKey,
            ref int value,
            bool enabled)
        {
            Rect row = listing.GetRect(RowHeight);
            TooltipHandler.TipRegion(
                row,
                "MAP_MechanoidMechanitor.Story.FactionOutpost.Weight.Tooltip".Translate());

            float labelWidth = row.width * LabelRatio;
            Rect labelRect = new Rect(row.x, row.y, labelWidth, row.height);
            Rect valueRect = new Rect(
                row.xMax - WeightValueWidth,
                row.y,
                WeightValueWidth,
                row.height);
            Rect sliderRect = new Rect(
                labelRect.xMax + RowGap,
                row.y,
                Mathf.Max(20f, valueRect.x - RowGap - (labelRect.xMax + RowGap)),
                row.height);

            Widgets.Label(labelRect, labelKey.Translate());
            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(valueRect, value.ToString());
            Text.Anchor = previousAnchor;

            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && enabled;
            float newValue = Widgets.HorizontalSlider(
                sliderRect,
                value,
                0f,
                100f,
                middleAlignment: true,
                label: null,
                leftAlignedLabel: null,
                rightAlignedLabel: null,
                roundTo: 1f);
            GUI.enabled = previousEnabled;

            if (enabled)
            {
                value = Mathf.Clamp(Mathf.RoundToInt(newValue), 0, 100);
            }
        }

        private static void DrawRowLabel(
            Rect row,
            string labelKey,
            string descriptionKey,
            out Rect buttonRect)
        {
            float labelWidth = row.width * LabelRatio;
            Rect labelRect = new Rect(row.x, row.y, labelWidth, row.height);
            buttonRect = new Rect(
                labelRect.xMax + RowGap,
                row.y,
                Mathf.Max(1f, row.xMax - labelRect.xMax - RowGap),
                row.height);

            Widgets.Label(labelRect, labelKey.Translate());
            TooltipHandler.TipRegion(row, descriptionKey.Translate());
        }

        private static string LabelForFactionOutpostFrequency(
            MechanoidMechanitorFactionOutpostFrequency frequency)
        {
            return frequency switch
            {
                MechanoidMechanitorFactionOutpostFrequency.Off =>
                    "MAP_MechanoidMechanitor.Story.FactionOutpostFrequency.Off".Translate(),
                MechanoidMechanitorFactionOutpostFrequency.Low =>
                    "MAP_MechanoidMechanitor.Story.FactionOutpostFrequency.Low".Translate(),
                MechanoidMechanitorFactionOutpostFrequency.Medium =>
                    "MAP_MechanoidMechanitor.Story.FactionOutpostFrequency.Medium".Translate(),
                MechanoidMechanitorFactionOutpostFrequency.High =>
                    "MAP_MechanoidMechanitor.Story.FactionOutpostFrequency.High".Translate(),
                _ => frequency.ToString()
            };
        }
    }

    /// <summary>
    /// 将普通剧本默认剧情配置插入现有设置页。补丁只扩展本 MOD 自己的战略节点设置绘制入口，
    /// 不修改运行期世界逻辑。
    /// </summary>
    [HarmonyPatch(typeof(MAPMechanitorMod), "DrawStrategicNodeSettings")]
    public static class MechanoidMechanitorGeneralScenarioDefaultSettingsPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Listing_Standard listing)
        {
            listing.GapLine();
            MechanoidMechanitorGeneralScenarioDefaultSettingsUI.Draw(listing);
        }
    }
}
