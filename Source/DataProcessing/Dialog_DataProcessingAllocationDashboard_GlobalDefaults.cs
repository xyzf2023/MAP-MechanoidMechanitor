using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed partial class Dialog_DataProcessingAllocationDashboard
    {
        /// <summary>
        /// 绘制“新加入机械族默认设置”：基础、自动规则、高级依次排列，
        /// 所有设置共用同一个滚动区域。
        /// 修改默认模板不会覆盖任何现有机械族。
        /// </summary>
        private void DrawGlobalDefaultsSection(
            Rect rect,
            ref float y,
            GameComponent_DataProcessingAllocationRegistry registry)
        {

            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.DefaultsHeader".Translate());
            y += 32f;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = TextSecondary;
            string description =
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.DefaultsDescription".Translate();
            float descHeight = Text.CalcHeight(description, rect.width);
            Widgets.Label(
                new Rect(rect.x, y, rect.width, descHeight),
                description);
            y += descHeight + 12f;

            Section(rect, ref y, "MAP_MechanoidMechanitor.DataProcessing.Dashboard.DefaultsTabBasic".Translate());
            DrawGlobalDefaultsBasic(rect, ref y, registry);
            Section(rect, ref y, L("Rules"));
            DrawGlobalDefaultsRules(rect, ref y, registry);
            Section(rect, ref y, L("AdvancedOptions"));
            DrawGlobalDefaultsAdvanced(rect, ref y, registry);
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
                new Rect(rect.x, y, rect.width, ControlHeight),
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.DefaultTargetEnabled".Translate(),
                defaults.enabled,
                out bool nextEnabled);
            if (enabledChanged)
            {
                registry.TryUpdateDynamicTargetDefaults(
                    overseer,
                    d => d.enabled = nextEnabled);
            }
            y += ControlRow;

            DrawStepEditor(
                rect,
                ref y,
                L("NormalRequest"),
                defaults.normalSteps,
                next => registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.normalSteps = next), readValue: () => defaults.normalSteps);
            if (defaults.advancedMaxEnabled) Paragraph(rect, ref y, L("CommonUnused"), TextSecondary);
            else DrawStepEditor(
                rect,
                ref y,
                L("TaskMaximum"),
                defaults.commonMaxSteps,
                next => registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.commonMaxSteps = next), defaults.normalSteps, () => defaults.commonMaxSteps);

            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicPriority".Translate());
            y += 32f;
            DrawPriorityButtons(
                new Rect(rect.x, y, rect.width, ControlHeight),
                defaults.priority,
                next => registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.priority = next));
            y += ControlRow;

            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicCheckInterval".Translate());
            y += 32f;
            DrawIntervalButtons(
                new Rect(rect.x, y, rect.width, ControlHeight),
                defaults.checkIntervalTicks / 60,
                next => registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.checkIntervalTicks = next * 60));
            y += ControlRow;
        }

        private void DrawGlobalDefaultsRules(Rect rect, ref float y, GameComponent_DataProcessingAllocationRegistry registry)
        {
            var defaults = registry.GetDynamicTargetDefaultsForUI(overseer);
            if (defaults == null) return;
            Paragraph(rect, ref y, L("RuleOrder"), TextSecondary);
            DrawRuleCheckbox(rect, ref y, L("RuleMelee"), L("RuleMeleeHelp"), defaults.switchForCloseMelee,
                next => registry.TryUpdateDynamicTargetDefaults(overseer, d => d.switchForCloseMelee = next));
            DrawRuleCheckbox(rect, ref y, L("RuleDraft"), L("RuleDraftHelp"), defaults.switchForDraftedWeapon,
                next => registry.TryUpdateDynamicTargetDefaults(overseer, d => d.switchForDraftedWeapon = next));
            DrawRuleCheckbox(rect, ref y, L("RuleWork"), L("RuleWorkHelp"), defaults.switchForWork,
                next => registry.TryUpdateDynamicTargetDefaults(overseer, d => d.switchForWork = next));
            DrawRuleCheckbox(rect, ref y, L("RuleFallback"), L("RuleFallbackHelp"), defaults.applyUndraftedFallback,
                next => registry.TryUpdateDynamicTargetDefaults(overseer, d => d.applyUndraftedFallback = next));
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
                new Rect(rect.x, y, rect.width, ControlHeight),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicAdvancedMax".Translate(),
                defaults.advancedMaxEnabled,
                out bool nextAdvanced);
            if (advancedChanged)
            {
                registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.advancedMaxEnabled = nextAdvanced);
            }
            y += ControlRow;

            if (!defaults.advancedMaxEnabled)
            { Paragraph(rect, ref y, L("SeparateDisabled"), TextSecondary); return; }

            DrawStepEditor(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicGeneralMax".Translate(),
                defaults.generalMaxSteps,
                next => registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.generalMaxSteps = next), defaults.normalSteps, () => defaults.generalMaxSteps);
            DrawStepEditor(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicProductionMax".Translate(),
                defaults.productionMaxSteps,
                next => registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.productionMaxSteps = next), defaults.normalSteps, () => defaults.productionMaxSteps);
            DrawStepEditor(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicFireControlMax".Translate(),
                defaults.fireControlMaxSteps,
                next => registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.fireControlMaxSteps = next), defaults.normalSteps, () => defaults.fireControlMaxSteps);
            DrawStepEditor(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicAssaultMax".Translate(),
                defaults.assaultMaxSteps,
                next => registry.TryUpdateDynamicTargetDefaults(
                    overseer, d => d.assaultMaxSteps = next), defaults.normalSteps, () => defaults.assaultMaxSteps);
        }
    }
}
