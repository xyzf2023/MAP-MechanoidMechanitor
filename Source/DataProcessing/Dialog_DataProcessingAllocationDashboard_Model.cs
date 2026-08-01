using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed partial class Dialog_DataProcessingAllocationDashboard : Window
    {
        private struct TargetSnapshot
        {
            public DataProcessingDynamicTargetRecord? config;
            public bool dynamicManaged;
            public DataProcessingDynamicState state;
            public DataProcessingSpecialization specialization;
            public int actualSteps;
            public int normalSteps;
            public int requestedSteps;
            public string quotaSource;

            public bool IsLimited => actualSteps < requestedSteps;
        }

        private TargetSnapshot BuildSnapshot(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target)
        {
            bool globalEnabled = registry.IsDynamicAllocationEnabled(overseer);
            DataProcessingDynamicTargetRecord? config =
                registry.GetDynamicTargetRecord(overseer, target);
            if (globalEnabled && config == null)
            {
                config = registry.GetOrCreateDynamicTargetRecord(overseer, target);
            }

            int actual = registry.GetStepsForOverseerTarget(overseer, target);
            int normal = config?.normalSteps ?? actual;
            DataProcessingSpecialization specialization =
                registry.GetSpecializationForOverseerTarget(overseer, target);
            bool dynamicManaged = globalEnabled && (config?.enabled ?? true);
            DataProcessingDynamicState state = dynamicManaged
                ? registry.GetCachedDynamicStateForTarget(target)
                : DataProcessingDynamicState.Idle;

            int requested = normal;
            string source;
            if (!dynamicManaged || config == null)
            {
                source = "MAP_MechanoidMechanitor.DataProcessing.Dashboard.Source.Fixed".Translate();
            }
            else if (state == DataProcessingDynamicState.Idle)
            {
                source = "MAP_MechanoidMechanitor.DataProcessing.Dashboard.Source.Normal".Translate();
            }
            else if (config.advancedMaxEnabled)
            {
                requested = config.GetMaxStepsForSpecialization(specialization);
                source = "MAP_MechanoidMechanitor.DataProcessing.Dashboard.Source.SpecializationMax"
                    .Translate(DataProcessingAllocationUtility.GetSpecializationLabel(specialization));
            }
            else
            {
                requested = config.commonMaxSteps;
                source = "MAP_MechanoidMechanitor.DataProcessing.Dashboard.Source.CommonMax".Translate();
            }

            return new TargetSnapshot
            {
                config = config,
                dynamicManaged = dynamicManaged,
                state = state,
                specialization = specialization,
                actualSteps = actual,
                normalSteps = normal,
                requestedSteps = Mathf.Max(normal, requested),
                quotaSource = source
            };
        }

        private void DrawSpecializationButtons(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target,
            DataProcessingSpecialization current,
            bool dynamicManaged)
        {
            DataProcessingSpecialization[] values =
            {
                DataProcessingSpecialization.GeneralTuning,
                DataProcessingSpecialization.ProductionCoordination,
                DataProcessingSpecialization.FireControlCalculation,
                DataProcessingSpecialization.AssaultProtocol
            };

            float width = (rect.width - 6f) / 2f;
            float height = (rect.height - 6f) / 2f;
            for (int i = 0; i < values.Length; i++)
            {
                DataProcessingSpecialization value = values[i];
                int row = i / 2;
                int column = i % 2;
                Rect buttonRect = new Rect(
                    rect.x + column * (width + 6f),
                    rect.y + row * (height + 6f),
                    width,
                    height);
                bool clicked = DrawTabButton(
                    buttonRect,
                    DataProcessingAllocationUtility.GetSpecializationLabel(value),
                    current == value);
                if (clicked && current != value)
                {
                    if (dynamicManaged)
                    {
                        registry.SetDynamicTargetDefaultSpecialization(
                            overseer,
                            target,
                            value);
                    }
                    else
                    {
                        registry.TrySetManualSpecialization(
                            overseer,
                            target,
                            value);
                    }
                }
            }
        }

        private static void DrawStepEditor(
            Rect rect,
            ref float y,
            string label,
            int steps,
            Action<int> setter,
            int minimum = 0)
        {
            Rect lineRect = new Rect(rect.x, y, rect.width, 34f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = TextMain;
            Widgets.Label(
                new Rect(lineRect.x, lineRect.y, lineRect.width - 170f, lineRect.height),
                label);

            Rect minusRect = new Rect(lineRect.xMax - 164f, lineRect.y + 2f, 38f, 30f);
            Rect valueRect = new Rect(minusRect.xMax + 6f, lineRect.y, 76f, lineRect.height);
            Rect plusRect = new Rect(valueRect.xMax + 6f, minusRect.y, 38f, 30f);

            if (DrawMiniButton(minusRect, "-", steps > minimum))
            {
                setter(Mathf.Max(minimum, steps - 1));
            }

            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Accent;
            Widgets.Label(
                valueRect,
                DataProcessingAllocationUtility.StepsToPercent(steps).ToStringPercent());

            if (DrawMiniButton(plusRect, "+"))
            {
                setter(steps + 1);
            }

            Text.Anchor = TextAnchor.UpperLeft;
            y += 42f;
        }

        private static void DrawPriorityButtons(Rect rect, int current, Action<int> setter)
        {
            string[] labels =
            {
                "MAP_MechanoidMechanitor.DataProcessing.MatrixPriorityCritical".Translate(),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixPriorityHigh".Translate(),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixPriorityStandard".Translate(),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixPriorityLow".Translate()
            };
            float width = (rect.width - 18f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                Rect buttonRect = new Rect(rect.x + i * (width + 6f), rect.y, width, rect.height);
                bool clicked = DrawTabButton(
                    buttonRect,
                    labels[i],
                    current == i + 1);
                if (clicked && current != i + 1)
                {
                    setter(i + 1);
                }
            }
        }

        private static void DrawIntervalButtons(Rect rect, int current, Action<int> setter)
        {
            int[] values = { 1, 5, 10, 30 };
            float width = (rect.width - 18f) / 4f;
            for (int i = 0; i < values.Length; i++)
            {
                int value = values[i];
                Rect buttonRect = new Rect(rect.x + i * (width + 6f), rect.y, width, rect.height);
                bool clicked = DrawTabButton(
                    buttonRect,
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicSeconds".Translate(value),
                    current == value);
                if (clicked && current != value)
                {
                    setter(value);
                }
                if (value == 1)
                {
                    TooltipHandler.TipRegion(
                        buttonRect,
                        "MAP_MechanoidMechanitor.DataProcessing.MatrixIntervalHighFrequency".Translate());
                }
            }
        }

        private static void DrawRuleCheckbox(
            Rect rect,
            ref float y,
            string label,
            string description,
            bool value,
            Action<bool> setter)
        {
            bool changed = DrawCheckboxRow(
                new Rect(rect.x, y, rect.width, 28f),
                label,
                value,
                out bool next);
            if (changed)
            {
                setter(next);
            }
            y += 30f;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = TextSecondary;
            float descriptionHeight = Text.CalcHeight(description, rect.width - 34f);
            Widgets.Label(
                new Rect(rect.x + 34f, y, rect.width - 34f, descriptionHeight),
                description);
            y += descriptionHeight + 12f;
        }

        private int GetRequestedSteps(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target,
            out string quotaSource)
        {
            TargetSnapshot snapshot = BuildSnapshot(registry, target);
            quotaSource = snapshot.quotaSource;
            return snapshot.requestedSteps;
        }
    }
}
