using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed partial class Dialog_DataProcessingAllocationDashboard : Window
    {
        private enum DashboardButtonStyle
        {
            Primary,
            Secondary,
            Compact,
            Danger
        }

        private static readonly Color ButtonPrimaryBackground =
            new Color(0.095f, 0.255f, 0.285f, 0.98f);
        private static readonly Color ButtonPrimarySelectedBackground =
            new Color(0.12f, 0.35f, 0.38f, 0.98f);
        private static readonly Color ButtonSecondaryBackground =
            new Color(0.072f, 0.105f, 0.12f, 0.98f);
        private static readonly Color ButtonCompactBackground =
            new Color(0.065f, 0.095f, 0.108f, 0.98f);
        private static readonly Color ButtonDangerBackground =
            new Color(0.24f, 0.085f, 0.085f, 0.98f);
        private static readonly Color ButtonDisabledBackground =
            new Color(0.065f, 0.075f, 0.08f, 0.90f);

        private static bool DrawCheckboxRow(
            Rect rect,
            string label,
            bool value,
            out bool next,
            bool active = true)
        {
            next = value;
            if (!DrawToggleRow(rect, label, value, active))
            {
                return false;
            }

            next = !value;
            return true;
        }

        private static bool DrawToggleRow(
            Rect rect,
            string label,
            bool value,
            bool active = true)
        {
            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;

            try
            {
                const float toggleWidth = 78f;
                Rect labelRect = new Rect(
                    rect.x,
                    rect.y,
                    Mathf.Max(0f, rect.width - toggleWidth - 10f),
                    rect.height);
                Rect toggleRect = new Rect(
                    rect.xMax - toggleWidth,
                    rect.y + 1f,
                    toggleWidth,
                    Mathf.Max(24f, rect.height - 2f));

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = active ? TextMain : Disabled;
                Widgets.Label(labelRect, label);

                return DrawFlatButton(
                    toggleRect,
                    value
                        ? "MAP_MechanoidMechanitor.DataProcessing.Dashboard.ToggleOn".Translate()
                        : "MAP_MechanoidMechanitor.DataProcessing.Dashboard.ToggleOff".Translate(),
                    value ? DashboardButtonStyle.Primary : DashboardButtonStyle.Secondary,
                    selected: value,
                    enabled: active,
                    font: GameFont.Tiny);
            }
            finally
            {
                GUI.color = oldColor;
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWrap;
            }
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
            return DrawFlatButton(
                rect,
                label,
                DashboardButtonStyle.Secondary,
                selected,
                enabled: true,
                font: GameFont.Tiny);
        }

        private static bool DrawFlatButton(
            Rect rect,
            string label,
            DashboardButtonStyle style,
            bool selected = false,
            bool enabled = true,
            GameFont font = GameFont.Small)
        {
            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;

            try
            {
                Color background;
                Color borderColor;
                Color textColor;

                if (!enabled)
                {
                    background = ButtonDisabledBackground;
                    borderColor = Disabled;
                    textColor = Disabled;
                }
                else
                {
                    switch (style)
                    {
                        case DashboardButtonStyle.Primary:
                            background = selected
                                ? ButtonPrimarySelectedBackground
                                : ButtonPrimaryBackground;
                            borderColor = Accent;
                            textColor = TextMain;
                            break;
                        case DashboardButtonStyle.Danger:
                            background = ButtonDangerBackground;
                            borderColor = Danger;
                            textColor = TextMain;
                            break;
                        case DashboardButtonStyle.Compact:
                            background = ButtonCompactBackground;
                            borderColor = selected ? Accent : Border;
                            textColor = selected ? Accent : TextMain;
                            break;
                        default:
                            background = selected
                                ? ButtonPrimarySelectedBackground
                                : ButtonSecondaryBackground;
                            borderColor = selected ? Accent : Border;
                            textColor = selected ? Accent : TextMain;
                            break;
                    }
                }

                Widgets.DrawBoxSolid(rect, background);
                if (enabled)
                {
                    Widgets.DrawHighlightIfMouseover(rect);
                }

                GUI.color = borderColor;
                Widgets.DrawBox(rect, selected ? 2 : 1);

                Text.Font = font;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;
                GUI.color = textColor;
                Widgets.Label(rect, label);

                return enabled && Widgets.ButtonInvisible(rect);
            }
            finally
            {
                GUI.color = oldColor;
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWrap;
            }
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
            bool clicked = DrawCheckboxRow(rect, label, value, out bool next, active);
            changed = clicked && next != value;
            return clicked;
        }

        private static void DrawPanel(Rect rect, Color border)
        {
            DrawPanel(rect, Panel, border);
        }

        private static bool DrawPrimaryButton(Rect rect, string label, bool selected = false)
        {
            return DrawFlatButton(
                rect,
                label,
                DashboardButtonStyle.Primary,
                selected);
        }

        private static bool DrawSecondaryButton(
            Rect rect,
            string label,
            bool selected = false,
            bool enabled = true)
        {
            return DrawFlatButton(
                rect,
                label,
                DashboardButtonStyle.Secondary,
                selected,
                enabled);
        }

        private static bool DrawMiniButton(Rect rect, string label, bool enabled = true)
        {
            return DrawFlatButton(
                rect,
                label,
                DashboardButtonStyle.Compact,
                selected: false,
                enabled,
                font: GameFont.Small);
        }

        private static bool DrawDangerButton(Rect rect, string label, bool enabled = true)
        {
            return DrawFlatButton(
                rect,
                label,
                DashboardButtonStyle.Danger,
                selected: false,
                enabled);
        }

        private static void Solid(Rect rect, Color color)
        {
            Widgets.DrawBoxSolid(rect, color);
        }
    }
}
