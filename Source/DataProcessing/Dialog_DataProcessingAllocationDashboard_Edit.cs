using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed partial class Dialog_DataProcessingAllocationDashboard : Window
    {
        private void DrawTargetEditor(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target)
        {
            Rect backRect = new Rect(rect.x, rect.y, 126f, 30f);
            if (Widgets.ButtonText(
                    backRect,
                    "MAP_MechanoidMechanitor.DataProcessing.Dashboard.BackMonitor".Translate()))
            {
                mode = DashboardMode.Monitor;
                detailScrollPosition = Vector2.zero;
                return;
            }

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = TextMain;
            Widgets.Label(
                new Rect(backRect.xMax + 12f, rect.y, rect.width - backRect.width - 12f, 30f),
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.Editing".Translate(
                    target.LabelShortCap));

            Rect tabsRect = new Rect(rect.x, rect.y + 42f, rect.width, 32f);
            DrawEditTabs(tabsRect);

            Rect listRect = new Rect(rect.x, tabsRect.yMax + 10f, rect.width, rect.yMax - tabsRect.yMax - 10f);
            float viewHeight = editTab == EditTab.Advanced
                ? 600f
                : editTab == EditTab.Rules && Prefs.DevMode
                    ? 700f
                    : 500f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, Mathf.Max(viewHeight, listRect.height));
            Widgets.BeginScrollView(listRect, ref detailScrollPosition, viewRect);
            try
            {
                DataProcessingDynamicTargetRecord config =
                    registry.GetOrCreateDynamicTargetRecord(overseer, target);
                switch (editTab)
                {
                    case EditTab.Rules:
                        DrawRulesEdit(viewRect, registry, target, config);
                        break;
                    case EditTab.Advanced:
                        DrawAdvancedEdit(viewRect, registry, target, config);
                        break;
                    default:
                        DrawBasicEdit(viewRect, registry, target, config);
                        break;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawEditTabs(Rect rect)
        {
            string[] labels =
            {
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.TabBasic".Translate(),
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.TabRules".Translate(),
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.TabAdvanced".Translate()
            };
            EditTab[] tabs =
            {
                EditTab.Basic,
                EditTab.Rules,
                EditTab.Advanced
            };

            float width = (rect.width - 12f) / 3f;
            for (int i = 0; i < tabs.Length; i++)
            {
                Rect buttonRect = new Rect(
                    rect.x + i * (width + 6f),
                    rect.y,
                    width,
                    rect.height);
                if (DrawTabButton(buttonRect, labels[i], editTab == tabs[i]))
                {
                    editTab = tabs[i];
                    detailScrollPosition = Vector2.zero;
                }
            }
        }

        private void DrawBasicEdit(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target,
            DataProcessingDynamicTargetRecord config)
        {
            float y = rect.y;
            bool globalEnabled = registry.IsDynamicAllocationEnabled(overseer);
            bool changed = DrawCheckboxRow(
                new Rect(rect.x, y, rect.width, 30f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicTargetEnabled".Translate(),
                globalEnabled && config.enabled,
                out bool enabled,
                globalEnabled);
            if (changed)
            {
                registry.SetDynamicAllocationEnabledForTarget(overseer, target, enabled);
            }
            y += 42f;

            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicDefaultMode".Translate());
            y += 30f;
            bool dynamicManaged = registry.IsDynamicAllocationEnabledForTarget(overseer, target);
            DataProcessingSpecialization shownSpecialization = dynamicManaged
                ? config.defaultSpecialization
                : registry.GetSpecializationForOverseerTarget(overseer, target);
            DrawSpecializationButtons(
                new Rect(rect.x, y, rect.width, 72f),
                registry,
                target,
                shownSpecialization,
                dynamicManaged);
            y += 84f;

            DrawStepEditor(
                rect,
                ref y,
                dynamicManaged
                    ? "MAP_MechanoidMechanitor.DataProcessing.DynamicNormalSteps".Translate()
                    : "MAP_MechanoidMechanitor.DataProcessing.DynamicCurrentActualSteps".Translate(),
                dynamicManaged
                    ? config.normalSteps
                    : registry.GetStepsForOverseerTarget(overseer, target),
                next =>
                {
                    if (dynamicManaged)
                    {
                        registry.SetDynamicTargetNormalSteps(overseer, target, next);
                    }
                    else
                    {
                        registry.SetSteps(overseer, target, next);
                    }
                });
            DrawStepEditor(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicCommonMaxSteps".Translate(),
                config.commonMaxSteps,
                next => registry.SetDynamicTargetCommonMaxSteps(overseer, target, next),
                config.normalSteps);

            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicPriority".Translate());
            y += 30f;
            DrawPriorityButtons(
                new Rect(rect.x, y, rect.width, 32f),
                config.priority,
                next => registry.SetDynamicTargetPriority(overseer, target, next));
        }

        private void DrawRulesEdit(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target,
            DataProcessingDynamicTargetRecord config)
        {
            float y = rect.y;
            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixAutomaticRules".Translate());
            y += 30f;

            DrawRuleCheckbox(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicRuleWork".Translate(),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixRuleWork.Description".Translate(),
                config.switchForWork,
                next => registry.SetDynamicTargetRule(overseer, target, "Work", next));
            DrawRuleCheckbox(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicRuleDraftedWeapon".Translate(),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixRuleDrafted.Description".Translate(),
                config.switchForDraftedWeapon,
                next => registry.SetDynamicTargetRule(overseer, target, "DraftedWeapon", next));
            DrawRuleCheckbox(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicRuleCloseMelee".Translate(),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixRuleMelee.Description".Translate(),
                config.switchForCloseMelee,
                next => registry.SetDynamicTargetRule(overseer, target, "CloseMelee", next));
            DrawRuleCheckbox(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicRuleUndraftedFallback".Translate(),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixRuleFallback.Description".Translate(),
                config.applyUndraftedFallback,
                next => registry.SetDynamicTargetRule(overseer, target, "UndraftedFallback", next));

            y += 6f;
            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicCheckInterval".Translate());
            y += 30f;
            DrawIntervalButtons(
                new Rect(rect.x, y, rect.width, 34f),
                config.checkIntervalTicks / 60,
                next => registry.SetDynamicTargetCheckInterval(overseer, target, next));

            if (Prefs.DevMode)
            {
                y += 54f;
                DrawWorkRecognitionDiagnostic(rect, target, ref y);
            }
        }

        private void DrawAdvancedEdit(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target,
            DataProcessingDynamicTargetRecord config)
        {
            float y = rect.y;
            bool changed = DrawCheckboxRow(
                new Rect(rect.x, y, rect.width, 30f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicAdvancedMax".Translate(),
                config.advancedMaxEnabled,
                out bool advanced);
            if (changed)
            {
                registry.SetDynamicTargetAdvancedMaxEnabled(overseer, target, advanced);
                config = registry.GetOrCreateDynamicTargetRecord(overseer, target);
            }
            y += 42f;

            if (!config.advancedMaxEnabled)
            {
                DrawPlainNotice(
                    new Rect(rect.x, y, rect.width, 54f),
                    "MAP_MechanoidMechanitor.DataProcessing.Dashboard.AdvancedDisabled".Translate());
                return;
            }

            DrawPlainNotice(
                new Rect(rect.x, y, rect.width, 54f),
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.CommonMaxUnused".Translate(
                    DataProcessingAllocationUtility.StepsToPercent(config.commonMaxSteps)
                        .ToStringPercent()));
            y += 66f;

            DrawStepEditor(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicGeneralMax".Translate(),
                config.generalMaxSteps,
                next => registry.SetDynamicTargetMaxStepsForSpecialization(
                    overseer,
                    target,
                    DataProcessingSpecialization.GeneralTuning,
                    next),
                config.normalSteps);
            DrawStepEditor(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicProductionMax".Translate(),
                config.productionMaxSteps,
                next => registry.SetDynamicTargetMaxStepsForSpecialization(
                    overseer,
                    target,
                    DataProcessingSpecialization.ProductionCoordination,
                    next),
                config.normalSteps);
            DrawStepEditor(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicFireControlMax".Translate(),
                config.fireControlMaxSteps,
                next => registry.SetDynamicTargetMaxStepsForSpecialization(
                    overseer,
                    target,
                    DataProcessingSpecialization.FireControlCalculation,
                    next),
                config.normalSteps);
            DrawStepEditor(
                rect,
                ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicAssaultMax".Translate(),
                config.assaultMaxSteps,
                next => registry.SetDynamicTargetMaxStepsForSpecialization(
                    overseer,
                    target,
                    DataProcessingSpecialization.AssaultProtocol,
                    next),
                config.normalSteps);
        }
    }
}
