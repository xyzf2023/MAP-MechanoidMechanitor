using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed partial class Dialog_DataProcessingAllocationDashboard
    {
        /// <summary>
        /// 绘制“新加入机械族默认设置”区域：说明、标签（基础/自动规则/高级）、
        /// 当前标签对应的设置页，以及下方的危险操作区（重置为默认）。
        /// 修改默认模板不会覆盖任何现有机械族。
        /// </summary>
        private void DrawGlobalDefaultsSection(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            float y = rect.y;

            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.DefaultsHeader".Translate());
            y += 32f;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = TextSecondary;
            string description =
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.DefaultsDescription".Translate();
            float descHeight = Text.CalcHeight(description, rect.width);
            Widgets.Label(
                new Rect(rect.x, y, rect.width, descHeight),
                description);
            y += descHeight + 12f;

            // 标签：基础 | 自动规则 | 高级
            string[] tabLabels =
            {
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.DefaultsTabBasic".Translate(),
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.DefaultsTabRules".Translate(),
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.DefaultsTabAdvanced".Translate()
            };
            float tabWidth = (rect.width - 16f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                Rect tabRect = new Rect(
                    rect.x + i * (tabWidth + 8f),
                    y,
                    tabWidth,
                    28f);
                if (DrawTabButton(tabRect, tabLabels[i], (int)globalDefaultsTab == i)
                    && (int)globalDefaultsTab != i)
                {
                    globalDefaultsTab = (GlobalDefaultsTab)i;
                }
            }
            y += 38f;

            switch (globalDefaultsTab)
            {
                case GlobalDefaultsTab.Basic:
                    DrawGlobalDefaultsBasic(rect, ref y, registry);
                    break;
                case GlobalDefaultsTab.Rules:
                    DrawGlobalDefaultsRules(rect, ref y, registry);
                    break;
                case GlobalDefaultsTab.Advanced:
                    DrawGlobalDefaultsAdvanced(rect, ref y, registry);
                    break;
            }

            // 危险操作区：重置为默认
            y += 14f;
            Rect dangerRect = new Rect(rect.x, y, rect.width, 32f);
            if (DrawDangerButton(
                dangerRect,
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.ResetAll".Translate()))
            {
                Find.WindowStack.Add(
                    Dialog_MessageBox.CreateConfirmation(
                        "MAP_MechanoidMechanitor.DataProcessing.Dashboard.ResetAllConfirm".Translate(),
                        () =>
                        {
                            int count =
                                registry.ResetAllMechanoidDynamicSettingsToDefaults(overseer);
                            Messages.Message(
                                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.ResetAllComplete"
                                    .Translate(count),
                                overseer,
                                MessageTypeDefOf.NeutralEvent,
                                historical: false);
                        }));
            }
        }

        private void DrawGlobalDefaultsBasic(
            Rect rect,
            ref float y,
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            DataProcessingDynamicTargetDefaults? defaults =
                registry.GetDynamicTargetDefaultsForUI(overseer);
            if (defaults == null)
            {
                return;
            }

            bool enabledChanged = DrawCheckboxRow(
                new Rect(rect.x, y, rect.width, 30f),
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.DefaultTargetEnabled".Translate(),
                defaults.enabled,
                out bool nextEnabled);
            if (enabledChanged)
            {
                registry.TryUpdateDynamicTargetDefaults(
                    overseer,
                    d => d.enabled = nextEnabled);
            }
            y += 36f;

            DrawStepEditor(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicNormalSteps".Translate(),
                defaults.normalSteps,
                next => registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.normalSteps = next));
            DrawStepEditor(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicCommonMaxSteps".Translate(),
                defaults.commonMaxSteps,
                next => registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.commonMaxSteps = next));

            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicPriority".Translate());
            y += 32f;
            DrawPriorityButtons(
                new Rect(rect.x, y, rect.width, 32f),
                defaults.priority,
                next => registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.priority = next));
            y += 42f;

            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicCheckInterval".Translate());
            y += 32f;
            DrawIntervalButtons(
                new Rect(rect.x, y, rect.width, 34f),
                defaults.checkIntervalTicks / 60,
                next => registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.checkIntervalTicks = next * 60));
            y += 42f;
        }

        private void DrawGlobalDefaultsRules(
            Rect rect,
            ref float y,
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            DataProcessingDynamicTargetDefaults? defaults =
                registry.GetDynamicTargetDefaultsForUI(overseer);
            if (defaults == null)
            {
                return;
            }

            DrawRuleCheckbox(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicRuleWork".Translate(),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixRuleWork.Description".Translate(),
                defaults.switchForWork,
                next => registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.switchForWork = next));

            DrawRuleCheckbox(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicRuleDraftedWeapon".Translate(),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixRuleDrafted.Description".Translate(),
                defaults.switchForDraftedWeapon,
                next => registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.switchForDraftedWeapon = next));

            DrawRuleCheckbox(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicRuleCloseMelee".Translate(),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixRuleMelee.Description".Translate(),
                defaults.switchForCloseMelee,
                next => registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.switchForCloseMelee = next));

            DrawRuleCheckbox(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicRuleUndraftedFallback".Translate(),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixRuleFallback.Description".Translate(),
                defaults.applyUndraftedFallback,
                next => registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.applyUndraftedFallback = next));
        }

        private void DrawGlobalDefaultsAdvanced(
            Rect rect,
            ref float y,
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            DataProcessingDynamicTargetDefaults? defaults =
                registry.GetDynamicTargetDefaultsForUI(overseer);
            if (defaults == null)
            {
                return;
            }

            bool advancedChanged = DrawCheckboxRow(
                new Rect(rect.x, y, rect.width, 30f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicAdvancedMax".Translate(),
                defaults.advancedMaxEnabled,
                out bool nextAdvanced);
            if (advancedChanged)
            {
                registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.advancedMaxEnabled = nextAdvanced);
            }
            y += 36f;

            DrawStepEditor(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicGeneralMax".Translate(),
                defaults.generalMaxSteps,
                next => registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.generalMaxSteps = next));
            DrawStepEditor(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicProductionMax".Translate(),
                defaults.productionMaxSteps,
                next => registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.productionMaxSteps = next));
            DrawStepEditor(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicFireControlMax".Translate(),
                defaults.fireControlMaxSteps,
                next => registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.fireControlMaxSteps = next));
            DrawStepEditor(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicAssaultMax".Translate(),
                defaults.assaultMaxSteps,
                next => registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.assaultMaxSteps = next));
        }
    }
}
