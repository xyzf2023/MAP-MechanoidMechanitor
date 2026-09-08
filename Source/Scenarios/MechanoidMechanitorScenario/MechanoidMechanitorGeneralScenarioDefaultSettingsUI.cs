using System.Collections.Generic;
using HarmonyLib;
using MAP_MechanoidMechanitor;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// MOD 设置页中的“普通剧本默认世界内容”。这些值只作为之后新建普通游戏的
    /// 配置快照来源，不实时控制已经建立的存档。
    /// </summary>
    public static class MechanoidMechanitorGeneralScenarioDefaultSettingsUI
    {
        private static readonly MechanoidMechanitorFactionOutpostFrequency[]
            FactionOutpostFrequencies =
            {
                MechanoidMechanitorFactionOutpostFrequency.Off,
                MechanoidMechanitorFactionOutpostFrequency.Low,
                MechanoidMechanitorFactionOutpostFrequency.Medium,
                MechanoidMechanitorFactionOutpostFrequency.High
            };

        private static readonly MechanoidMechanitorMechHiveNodeFrequency[]
            MechHiveNodeFrequencies =
            {
                MechanoidMechanitorMechHiveNodeFrequency.Off,
                MechanoidMechanitorMechHiveNodeFrequency.Low,
                MechanoidMechanitorMechHiveNodeFrequency.Medium,
                MechanoidMechanitorMechHiveNodeFrequency.High
            };

        public static void Draw(Listing_Standard listing)
        {
            MAPMechanitorModSettings? settings = MAPMechanitorMod.Settings;
            if (settings == null)
            {
                return;
            }

            settings.NormalizeGeneralScenarioDefaultSettings();

            listing.Label(
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.Section"
                    .Translate());
            listing.Label(
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.Description"
                    .Translate());

            bool outerEnabled = GUI.enabled;
            GUI.enabled = outerEnabled && !settings.enableStoryStylesForGeneralScenarios;

            DrawFactionOutpostFrequency(listing, settings);
            DrawMechHiveNodeFrequency(listing, settings);

            GUI.enabled = outerEnabled;
            if (settings.enableStoryStylesForGeneralScenarios)
            {
                listing.Label(
                    "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.DisabledNotice"
                        .Translate());
            }
        }

        private static void DrawFactionOutpostFrequency(
            Listing_Standard listing,
            MAPMechanitorModSettings settings)
        {
            Rect row = listing.GetRect(30f);
            DrawRowLabel(
                row,
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.FactionOutpost.Label",
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.FactionOutpost.Description",
                out Rect buttonRect);

            if (!Widgets.ButtonText(
                    buttonRect,
                    LabelFor(settings.generalScenarioFactionOutpostFrequency)))
            {
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = 0; i < FactionOutpostFrequencies.Length; i++)
            {
                MechanoidMechanitorFactionOutpostFrequency frequency =
                    FactionOutpostFrequencies[i];
                options.Add(
                    new FloatMenuOption(
                        LabelFor(frequency),
                        () => settings.generalScenarioFactionOutpostFrequency = frequency));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void DrawMechHiveNodeFrequency(
            Listing_Standard listing,
            MAPMechanitorModSettings settings)
        {
            Rect row = listing.GetRect(30f);
            DrawRowLabel(
                row,
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.MechHiveNode.Label",
                "MAP_MechanoidMechanitor.Settings.GeneralScenarioDefaults.MechHiveNode.Description",
                out Rect buttonRect);

            if (!Widgets.ButtonText(
                    buttonRect,
                    MechanoidMechanitorStoryConfigurationLabels.LabelFor(
                        settings.generalScenarioMechHiveNodeFrequency)))
            {
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = 0; i < MechHiveNodeFrequencies.Length; i++)
            {
                MechanoidMechanitorMechHiveNodeFrequency frequency =
                    MechHiveNodeFrequencies[i];
                options.Add(
                    new FloatMenuOption(
                        MechanoidMechanitorStoryConfigurationLabels.LabelFor(frequency),
                        () => settings.generalScenarioMechHiveNodeFrequency = frequency));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void DrawRowLabel(
            Rect row,
            string labelKey,
            string descriptionKey,
            out Rect buttonRect)
        {
            const float gap = 8f;
            float labelWidth = row.width * 0.62f;
            Rect labelRect = new Rect(row.x, row.y, labelWidth, row.height);
            buttonRect = new Rect(
                labelRect.xMax + gap,
                row.y,
                Mathf.Max(1f, row.xMax - labelRect.xMax - gap),
                row.height);

            Widgets.Label(labelRect, labelKey.Translate());
            TooltipHandler.TipRegion(row, descriptionKey.Translate());
        }

        private static string LabelFor(
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
    /// 将普通剧本默认世界内容插入现有设置页。补丁仅扩展本 MOD 自己的战略节点设置绘制入口，
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
