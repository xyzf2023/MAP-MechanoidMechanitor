using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed partial class Dialog_DataProcessingAllocationDashboard : Window
    {
        private void DrawGlobalSettings(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            Rect backRect = new Rect(rect.x, rect.y, 126f, 30f);
            if (DrawSecondaryButton(
                    backRect,
                    "MAP_MechanoidMechanitor.DataProcessing.Dashboard.BackMonitor".Translate()))
            {
                mode = DashboardMode.Monitor;
                detailScrollPosition = Vector2.zero;
                return;
            }

            Text.Font = GameFont.Medium;
            GUI.color = TextMain;
            Widgets.Label(
                new Rect(backRect.xMax + 12f, rect.y, rect.width - backRect.width - 12f, 30f),
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.GlobalSettings".Translate());

            Rect listRect = new Rect(rect.x, rect.y + 44f, rect.width, rect.height - 44f);

            DataProcessingDynamicAllocationRecord? globalForHeight =
                registry.FindDynamicAllocationRecordForUI(overseer);

            float minimumHeight;
            if (globalForHeight == null)
            {
                minimumHeight = 90f;
            }
            else if (globalDefaultsTab == GlobalDefaultsTab.Advanced)
            {
                minimumHeight = 1050f;
            }
            else if (globalDefaultsTab == GlobalDefaultsTab.Rules)
            {
                minimumHeight = 940f;
            }
            else
            {
                minimumHeight = 980f;
            }

            Rect viewRect = new Rect(
                0f,
                0f,
                listRect.width - 16f,
                Mathf.Max(minimumHeight, listRect.height));
            Widgets.BeginScrollView(listRect, ref detailScrollPosition, viewRect);
            try
            {
                DrawGlobalSettingsContent(viewRect, registry);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawGlobalSettingsContent(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            float y = rect.y;
            DataProcessingDynamicAllocationRecord? global =
                registry.FindDynamicAllocationRecordForUI(overseer);
            if (global == null)
            {
                DrawPlainNotice(
                    new Rect(rect.x, y, rect.width, 70f),
                    "MAP_MechanoidMechanitor.DataProcessing.Dashboard.EnableDynamicFirst".Translate());
                return;
            }

            int threshold = global.minConsciousnessPercent;

            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicMinConsciousness".Translate());
            y += 30f;

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Warning;
            Widgets.Label(
                new Rect(rect.x, y, rect.width, 34f),
                threshold + "%");
            y += 42f;

            string[] presetLabels =
            {
                "MAP_MechanoidMechanitor.DataProcessing.MatrixPresetConservative".Translate(),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixPresetBalanced".Translate(),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixPresetAggressive".Translate()
            };
            int[] presetValues = { 100, 75, 55 };
            float presetWidth = (rect.width - 12f) / 3f;
            for (int i = 0; i < presetValues.Length; i++)
            {
                Rect buttonRect = new Rect(
                    rect.x + i * (presetWidth + 6f),
                    y,
                    presetWidth,
                    32f);
                bool selected = threshold == presetValues[i];
                if (DrawTabButton(buttonRect, presetLabels[i], selected) && !selected)
                {
                    registry.SetDynamicMinConsciousnessPercent(overseer, presetValues[i]);
                }
            }
            y += 44f;

            Rect minusRect = new Rect(rect.x, y, 76f, 30f);
            Rect plusRect = new Rect(minusRect.xMax + 8f, y, 76f, 30f);
            if (DrawMiniButton(minusRect, "-5%", threshold > 55))
            {
                registry.SetDynamicMinConsciousnessPercent(overseer, threshold - 5);
            }
            if (DrawMiniButton(plusRect, "+5%", threshold < 1000))
            {
                registry.SetDynamicMinConsciousnessPercent(overseer, threshold + 5);
            }
            y += 46f;

            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixBudgetStatistics".Translate());
            y += 32f;

            DrawGlobalBudgetRows(rect, ref y, registry, threshold);

            DrawGlobalDefaultsSection(
                new Rect(rect.x, y, rect.width, rect.yMax - y),
                registry);
        }

        private void DrawGlobalBudgetRows(
            Rect rect,
            ref float y,
            GameComponent_DataProcessingAllocationRegistry registry,
            int thresholdPercent)
        {
            float current = DataProcessingAllocationUtility.GetCurrentConsciousness(overseer);
            int totalSteps = registry.GetTotalStepsForOverseer(overseer);
            int selfSteps = registry.GetStepsForOverseerTarget(overseer, overseer);
            float baseProcessing = current
                + totalSteps * DataProcessingAllocationUtility.StepPercent
                - selfSteps * DataProcessingAllocationUtility.StepPercent * 0.5f;

            int fixedSteps = 0;
            int dynamicSteps = 0;
            int unmetSteps = 0;
            List<Pawn> targets = CollectTargets(registry);
            for (int i = 0; i < targets.Count; i++)
            {
                TargetSnapshot snapshot = BuildSnapshot(registry, targets[i]);
                if (snapshot.dynamicManaged)
                {
                    dynamicSteps += snapshot.actualSteps;
                }
                else
                {
                    fixedSteps += snapshot.actualSteps;
                }
                unmetSteps += Mathf.Max(0, snapshot.requestedSteps - snapshot.actualSteps);
            }

            DrawInfoRow(rect, ref y,
                DataProcessingTerminologyUtility.GetBaseProcessingKey(overseer).Translate(),
                baseProcessing.ToStringPercent(),
                TextMain);
            DrawInfoRow(rect, ref y,
                DataProcessingTerminologyUtility.GetCurrentKey(overseer).Translate(),
                current.ToStringPercent(),
                Accent);
            DrawInfoRow(rect, ref y,
                DataProcessingTerminologyUtility.GetThresholdKey(overseer).Translate(),
                thresholdPercent + "%",
                Warning);
            DrawInfoRow(rect, ref y,
                DataProcessingTerminologyUtility.GetFixedUsageKey(overseer).Translate(),
                DataProcessingAllocationUtility.StepsToPercent(fixedSteps).ToStringPercent(),
                TextMain);
            DrawInfoRow(rect, ref y,
                DataProcessingTerminologyUtility.GetDynamicUsageKey(overseer).Translate(),
                DataProcessingAllocationUtility.StepsToPercent(dynamicSteps).ToStringPercent(),
                TextMain);
            DrawInfoRow(rect, ref y,
                DataProcessingTerminologyUtility.GetUnmetRequestKey(overseer).Translate(),
                DataProcessingAllocationUtility.StepsToPercent(unmetSteps).ToStringPercent(),
                unmetSteps > 0 ? Warning : TextMain);
        }
    }
}
