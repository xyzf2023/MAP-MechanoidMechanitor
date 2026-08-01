using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed partial class Dialog_DataProcessingAllocationDashboard : Window
    {
        private static bool DrawCheckboxRow(
            Rect rect,
            string label,
            bool value,
            out bool next,
            bool active = true)
        {
            next = value;
            Color oldColor = GUI.color;
            if (!active)
            {
                GUI.color = Widgets.InactiveColor;
            }

            Rect checkboxRect = new Rect(rect.x, rect.y + 2f, 24f, 24f);
            Widgets.Checkbox(
                checkboxRect.x,
                checkboxRect.y,
                ref next,
                24f,
                !active);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(
                new Rect(checkboxRect.xMax + 8f, rect.y, rect.width - 32f, rect.height),
                label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = oldColor;
            return active && next != value;
        }

        private static void DrawInfoRow(
            Rect rect,
            ref float y,
            string label,
            string value,
            Color valueColor)
        {
            Rect rowRect = new Rect(rect.x, y, rect.width, 28f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = TextSecondary;
            Widgets.Label(
                new Rect(rowRect.x, rowRect.y, rowRect.width * 0.46f, rowRect.height),
                label);
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = valueColor;
            Widgets.Label(
                new Rect(rowRect.x + rowRect.width * 0.46f, rowRect.y, rowRect.width * 0.54f, rowRect.height),
                value);
            Text.Anchor = TextAnchor.UpperLeft;
            y += 30f;
        }

        private static void DrawSectionTitle(Rect rect, string label)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = TextMain;
            Widgets.Label(rect, label);
            Widgets.DrawBoxSolid(
                new Rect(rect.x, rect.yMax - 1f, rect.width, 1f),
                Border);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawPlainNotice(Rect rect, string text)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = TextSecondary;
            Widgets.Label(rect, text);
        }

        private static void DrawWorkRecognitionDiagnostic(Rect rect, Pawn target, ref float y)
        {
            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.WorkRecognition.Diagnostic".Translate());
            y += 30f;

            DataProcessingWorkRecognitionResult result =
                DataProcessingDynamicWorkRecognitionUtility.Analyze(target);
            string jobDefName = result.jobDef?.defName ?? "-";
            string workGiverName = result.workGiverDef?.defName ?? "-";

            DrawInfoRow(rect, ref y,
                "MAP_MechanoidMechanitor.DataProcessing.WorkRecognition.JobDef".Translate(),
                jobDefName,
                TextMain);
            DrawInfoRow(rect, ref y,
                "MAP_MechanoidMechanitor.DataProcessing.WorkRecognition.WorkGiver".Translate(),
                workGiverName,
                TextMain);
            DrawInfoRow(rect, ref y,
                "MAP_MechanoidMechanitor.DataProcessing.WorkRecognition.Result".Translate(),
                result.countsAsWork
                    ? "MAP_MechanoidMechanitor.DataProcessing.WorkRecognition.Yes".Translate()
                    : "MAP_MechanoidMechanitor.DataProcessing.WorkRecognition.No".Translate(),
                result.countsAsWork ? Accent : TextSecondary);
            DrawInfoRow(rect, ref y,
                "MAP_MechanoidMechanitor.DataProcessing.WorkRecognition.Source".Translate(),
                DataProcessingDynamicWorkRecognitionUtility.GetSourceLabel(result.source),
                TextMain);
        }

        private static bool DrawTabButton(Rect rect, string label, bool selected)
        {
            Color background = selected
                ? new Color(0.13f, 0.29f, 0.32f, 1f)
                : new Color(0.075f, 0.105f, 0.12f, 1f);
            Widgets.DrawBoxSolid(rect, background);
            GUI.color = selected ? Accent : Border;
            Widgets.DrawBox(rect, selected ? 2 : 1);
            GUI.color = selected ? Accent : TextMain;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            bool oldWrap = Text.WordWrap;
            Text.WordWrap = false;
            Widgets.Label(rect, label);
            Text.WordWrap = oldWrap;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            return Widgets.ButtonInvisible(rect);
        }

        private static void DrawPanel(Rect rect, Color background, Color border)
        {
            Widgets.DrawBoxSolid(rect, background);
            GUI.color = border;
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;
        }

        private static void DrawCenteredMessage(Rect rect, string message)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = TextSecondary;
            Widgets.Label(rect, message);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private static Rect Inset(Rect rect, float horizontal, float vertical)
        {
            return new Rect(
                rect.x + horizontal,
                rect.y + vertical,
                Mathf.Max(0f, rect.width - horizontal * 2f),
                Mathf.Max(0f, rect.height - vertical * 2f));
        }

        private static void DrawPortrait(Rect rect, Pawn target)
        {
            try
            {
                float zoom = target.kindDef != null
                    ? target.kindDef.controlGroupPortraitZoom
                    : 1f;
                RenderTexture image = PortraitsCache.Get(
                    target,
                    rect.size,
                    Rot4.East,
                    PortraitCameraOffset,
                    zoom);
                GUI.DrawTexture(rect, image);
            }
            catch
            {
                Widgets.DrawBoxSolid(rect, Background);
            }

            GUI.color = Border;
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;
        }

        private static bool DrawCheckboxRow(
            Rect rect,
            string label,
            bool value,
            bool active,
            out bool changed)
        {
            bool result = DrawCheckboxRow(rect, label, value, out _, active);
            changed = result;
            return result;
        }

        private static void DrawPanel(Rect rect, Color border)
        {
            DrawPanel(rect, Panel, border);
        }

        private static bool DrawPrimaryButton(Rect rect, string label, bool selected = false)
        {
            Color oldColor = GUI.color;
            GUI.color = selected ? Accent : Color.white;
            bool clicked = Widgets.ButtonText(rect, label);
            GUI.color = oldColor;
            return clicked;
        }

        private static bool DrawSecondaryButton(Rect rect, string label)
        {
            return Widgets.ButtonText(rect, label);
        }

        private static void Solid(Rect rect, Color color)
        {
            Widgets.DrawBoxSolid(rect, color);
        }
    }
}
