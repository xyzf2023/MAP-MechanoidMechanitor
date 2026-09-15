using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed partial class Dialog_DataProcessingAllocationDashboard : Window
    {
        private void DrawSpecializationButtons(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target,
            DataProcessingSpecialization current)
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
                TooltipHandler.TipRegion(buttonRect, DataProcessingAllocationUtility.GetSpecializationTip(value));
                if (clicked)
                {
                    registry.SetDashboardSpecialization(overseer, target, value);
                }
            }
        }

        private void DrawScrollable(Rect rect, string key, Func<Rect, float> draw)
        {
            if (rect.width <= 20f || rect.height <= 0f) return;
            float height;
            if (!scrollHeights.TryGetValue(key, out height)) height = rect.height;
            height = Mathf.Max(height, rect.height);
            detailScrollPosition.y = Mathf.Clamp(detailScrollPosition.y, 0f, Mathf.Max(0f, height - rect.height));
            Rect view = new Rect(0f, 0f, rect.width - 16f, height);
            Widgets.BeginScrollView(rect, ref detailScrollPosition, view);
            try { scrollHeights[key] = Mathf.Max(rect.height, draw(view) + 12f); }
            finally { Widgets.EndScrollView(); }
        }

        private static void Paragraph(Rect rect, ref float y, string text, Color color)
        {
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true; GUI.color = color;
            float height = Mathf.Max(Text.LineHeight, Text.CalcHeight(text, rect.width));
            Widgets.Label(new Rect(rect.x, y, rect.width, height), text);
            y += height + 6f;
        }

        private static void Section(Rect rect, ref float y, string title)
        {
            y += 6f;
            DrawSectionTitle(new Rect(rect.x, y, rect.width, 26f), title);
            y += 34f;
        }

        private static bool Foldout(Rect rect, ref float y, string label, ref bool open)
        {
            if (DrawSecondaryButton(new Rect(rect.x, y, rect.width, ControlHeight), (open ? "▼ " : "▶ ") + label)) open = !open;
            y += ControlRow;
            return open;
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

                if (clicked)
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
                new Rect(rect.x, y, rect.width, ControlHeight),
                label,
                value,
                out bool next);
            if (changed)
            {
                setter(next);
            }
            y += ControlRow;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = TextSecondary;
            float descriptionHeight = Text.CalcHeight(description, rect.width - 34f);
            Widgets.Label(
                new Rect(rect.x + 34f, y, rect.width - 34f, descriptionHeight),
                description);
            y += descriptionHeight + 12f;
        }

    }
}
