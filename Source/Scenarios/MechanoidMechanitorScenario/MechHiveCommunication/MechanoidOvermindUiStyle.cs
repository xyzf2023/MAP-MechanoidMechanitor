using System;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class MechanoidOvermindUiStyle
    {
        public static readonly Color Background = new Color(0.07f, 0.08f, 0.09f);

        public static readonly Color Panel = new Color(0.13f, 0.14f, 0.16f);

        public static readonly Color PanelAlt = new Color(0.16f, 0.17f, 0.19f);

        public static readonly Color Border = new Color(0.24f, 0.34f, 0.44f);

        public static readonly Color Accent = new Color(0.20f, 0.42f, 0.66f);

        public static readonly Color AccentBright = new Color(0.32f, 0.68f, 0.96f);

        public static readonly Color TextPrimary = new Color(0.88f, 0.90f, 0.92f);

        public static readonly Color TextSecondary = new Color(0.58f, 0.60f, 0.64f);

        public static readonly Color Warning = new Color(0.95f, 0.55f, 0.18f);

        public static readonly Color Error = new Color(0.72f, 0.16f, 0.16f);

        public static readonly Color Disabled = new Color(0.34f, 0.35f, 0.38f);

        public static readonly Color NavSelectedFill = new Color(0.10f, 0.16f, 0.22f);

        public static readonly Color CoreGraphite = new Color(0.06f, 0.07f, 0.08f);

        public static readonly Color CoreArmorBorder = new Color(0.42f, 0.50f, 0.40f);

        public static readonly Color CoreIdentify = new Color(0.62f, 0.72f, 0.58f);

        public static readonly Color CoreStatus = new Color(0.55f, 0.12f, 0.12f);

        public const float CornerMarkLength = 8f;

        public const float NavAccentWidth = 3f;

        public readonly struct GuiState : IDisposable
        {
            private readonly Color color;

            private readonly GameFont font;

            private readonly TextAnchor anchor;

            private readonly bool wordWrap;

            public GuiState(Color color, GameFont font, TextAnchor anchor, bool wordWrap)
            {
                this.color = color;
                this.font = font;
                this.anchor = anchor;
                this.wordWrap = wordWrap;
            }

            public void Dispose()
            {
                GUI.color = color;
                Text.Font = font;
                Text.Anchor = anchor;
                Text.WordWrap = wordWrap;
            }
        }

        public static GuiState Push()
        {
            return new GuiState(GUI.color, Text.Font, Text.Anchor, Text.WordWrap);
        }

        public static void DrawBackground(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, Background);
        }

        public static void DrawPanel(Rect rect, bool alt = false, bool cornerMarks = true)
        {
            Widgets.DrawBoxSolid(rect, alt ? PanelAlt : Panel);
            DrawBorder(rect);
            if (cornerMarks)
            {
                DrawCornerMarks(rect);
            }
        }

        public static void DrawBorder(Rect rect)
        {
            Color previous = GUI.color;
            GUI.color = Border;
            Widgets.DrawBox(rect, 1);
            GUI.color = previous;
        }

        /// <summary>
        /// 连接进度条：底槽、连续填充、填充内扫光、低透明刻度，最后重绘边框。
        /// </summary>
        public static void DrawAccentProgressBar(
            Rect rect,
            float fill01,
            double realtime,
            bool confirmPhase)
        {
            fill01 = Mathf.Clamp01(fill01);
            Widgets.DrawBoxSolid(rect, Background);

            float fillWidth = rect.width * fill01;
            if (fillWidth > 0.5f)
            {
                Rect fillRect = new Rect(rect.x, rect.y, fillWidth, rect.height);
                Widgets.DrawBoxSolid(fillRect, Accent);

                double sweepPeriod = confirmPhase ? 0.55d : 1.15d;
                double cycle = realtime % sweepPeriod;
                if (cycle < 0d)
                {
                    cycle += sweepPeriod;
                }

                float sweepCenter = (float)(cycle / sweepPeriod) * fillWidth;
                float sweepHalf = Mathf.Min(8f, fillWidth * 0.5f);
                float sweepLeft = Mathf.Max(fillRect.x, fillRect.x + sweepCenter - sweepHalf);
                float sweepRight = Mathf.Min(fillRect.xMax, fillRect.x + sweepCenter + sweepHalf);
                if (sweepRight > sweepLeft)
                {
                    Color sweep = new Color(
                        AccentBright.r,
                        AccentBright.g,
                        AccentBright.b,
                        confirmPhase ? 0.32f : 0.16f);
                    Widgets.DrawBoxSolid(
                        new Rect(sweepLeft, rect.y, sweepRight - sweepLeft, rect.height),
                        sweep);
                }

                float edgeWidth = Mathf.Min(3f, fillWidth);
                Widgets.DrawBoxSolid(
                    new Rect(fillRect.xMax - edgeWidth, rect.y, edgeWidth, rect.height),
                    AccentBright);
            }

            Color previous = GUI.color;
            GUI.color = new Color(TextSecondary.r, TextSecondary.g, TextSecondary.b, 0.18f);
            const float tickStep = 8f;
            for (float x = rect.x + tickStep; x < rect.xMax - 1f; x += tickStep)
            {
                Widgets.DrawLineVertical(x, rect.y + 2f, Mathf.Max(1f, rect.height - 4f));
            }

            GUI.color = previous;
            DrawBorder(rect);
        }

        public static void DrawCornerMarks(Rect rect)
        {
            DrawCornerMarks(rect, Accent);
        }

        public static void DrawCornerMarks(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            float len = CornerMarkLength;
            Widgets.DrawLineHorizontal(rect.x, rect.y, len);
            Widgets.DrawLineVertical(rect.x, rect.y, len);
            Widgets.DrawLineHorizontal(rect.xMax - len, rect.y, len);
            Widgets.DrawLineVertical(rect.xMax - 1f, rect.y, len);
            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, len);
            Widgets.DrawLineVertical(rect.x, rect.yMax - len, len);
            Widgets.DrawLineHorizontal(rect.xMax - len, rect.yMax - 1f, len);
            Widgets.DrawLineVertical(rect.xMax - 1f, rect.yMax - len, len);
            GUI.color = previous;
        }

        public static bool DrawNavButton(Rect rect, string label, bool selected, bool enabled = true)
        {
            using (Push())
            {
                Widgets.DrawBoxSolid(rect, selected ? NavSelectedFill : Panel);
                DrawBorder(rect);

                if (selected)
                {
                    Widgets.DrawBoxSolid(
                        new Rect(rect.x, rect.y, NavAccentWidth, rect.height),
                        AccentBright);
                }

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = !enabled
                    ? Disabled
                    : selected
                        ? AccentBright
                        : TextPrimary;
                Widgets.Label(
                    new Rect(rect.x + 12f, rect.y, rect.width - 16f, rect.height),
                    label);

                if (!enabled)
                {
                    return false;
                }

                bool pressed = Widgets.ButtonInvisible(rect);
                if (pressed && !selected)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.08f);
                    Widgets.DrawBoxSolid(rect, GUI.color);
                }

                return pressed;
            }
        }

        public static bool DrawActionButton(Rect rect, string label, bool enabled = true, bool emphasized = false)
        {
            using (Push())
            {
                bool hovered = enabled && Mouse.IsOver(rect);
                Widgets.DrawBoxSolid(rect, !enabled ? Panel
                    : hovered ? NavSelectedFill : emphasized ? Accent : PanelAlt);
                Color previous = GUI.color;
                GUI.color = !enabled ? Disabled : hovered || emphasized ? AccentBright : Accent;
                Widgets.DrawBox(rect, 1);
                GUI.color = previous;

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;
                GUI.color = enabled ? TextPrimary : Disabled;
                Widgets.Label(rect, label);

                if (!enabled)
                {
                    return false;
                }

                return Widgets.ButtonInvisible(rect);
            }
        }

        private static GUIStyle? numberInputStyle;

        /// <summary>通讯面板共用的整数调节器：统一底色、边框、焦点与加减按钮。</summary>
        public static bool DrawNumberStepper(Rect rect, string controlName, ref int value,
            ref string buffer, int step = 1, int max = int.MaxValue)
        {
            using (Push())
            {
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                if (numberInputStyle == null)
                {
                    numberInputStyle = new GUIStyle(Text.CurTextFieldStyle)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        padding = new RectOffset(6, 6, 0, 0)
                    };
                    numberInputStyle.normal.background = null;
                    numberInputStyle.hover.background = null;
                    numberInputStyle.active.background = null;
                    numberInputStyle.focused.background = null;
                    numberInputStyle.normal.textColor = TextPrimary;
                    numberInputStyle.hover.textColor = TextPrimary;
                    numberInputStyle.active.textColor = AccentBright;
                    numberInputStyle.focused.textColor = AccentBright;
                }
                float buttonWidth = rect.height;
                Rect field = new Rect(rect.x + buttonWidth + 4f, rect.y,
                    rect.width - buttonWidth * 2f - 8f, rect.height);
                // 点击空白/其他控件也必须释放文本框焦点。
                if (Event.current.rawType == EventType.MouseDown
                    && GUI.GetNameOfFocusedControl() == controlName
                    && !field.Contains(Event.current.mousePosition))
                    GUI.FocusControl(null);
                if (DrawActionButton(new Rect(rect.x, rect.y, buttonWidth, rect.height), "−", value > 0))
                {
                    value = Math.Max(0, value - step);
                    buffer = value.ToString();
                    GUI.FocusControl(null);
                }
                Widgets.DrawBoxSolid(field, Background);
                DrawBorder(field);
                if (GUI.GetNameOfFocusedControl() == controlName)
                    Widgets.DrawBoxSolid(new Rect(field.x, field.yMax - 2f, field.width, 2f), AccentBright);
                GUI.SetNextControlName(controlName);
                buffer = GUI.TextField(field.ContractedBy(2f), buffer, 10, numberInputStyle);
                bool valid = int.TryParse(buffer, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int parsed);
                if (valid)
                {
                    value = Math.Min(parsed, max);
                    if (parsed > max) buffer = value.ToString();
                }
                if (DrawActionButton(new Rect(rect.xMax - buttonWidth, rect.y, buttonWidth, rect.height),
                    "+", value < max))
                {
                    value = (int)Math.Min(max, (long)value + step);
                    buffer = value.ToString();
                    valid = true;
                    GUI.FocusControl(null);
                }
                return valid;
            }
        }

        public static void DrawLabel(
            Rect rect,
            string text,
            GameFont font = GameFont.Small,
            TextAnchor anchor = TextAnchor.MiddleLeft,
            Color? color = null,
            bool wordWrap = false)
        {
            using (Push())
            {
                Text.Font = font;
                Text.Anchor = anchor;
                Text.WordWrap = wordWrap;
                GUI.color = color ?? TextPrimary;
                Widgets.Label(rect, text);
            }
        }

        public static void DrawSecondaryLabel(
            Rect rect,
            string text,
            TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            DrawLabel(rect, text, GameFont.Tiny, anchor, TextSecondary);
        }

        public static void DrawCoreFrame(Rect rect)
        {
            Color previous = GUI.color;
            Widgets.DrawBoxSolid(rect, CoreGraphite);

            GUI.color = CoreArmorBorder;
            Widgets.DrawBox(rect, 1);
            Rect inset = rect.ContractedBy(3f);
            GUI.color = CoreIdentify;
            Widgets.DrawBox(inset, 1);

            GUI.color = CoreStatus;
            Widgets.DrawBoxSolid(new Rect(rect.x + 8f, rect.y + 6f, 18f, 3f), CoreStatus);
            Widgets.DrawBoxSolid(new Rect(rect.xMax - 26f, rect.y + 6f, 18f, 3f), CoreStatus);
            Widgets.DrawBoxSolid(new Rect(rect.x + 8f, rect.yMax - 9f, 18f, 3f), CoreStatus);

            DrawCoreGrid(rect.ContractedBy(10f));
            DrawCornerMarks(rect);
            GUI.color = previous;
        }

        private static void DrawCoreGrid(Rect rect)
        {
            if (rect.width < 20f || rect.height < 20f)
            {
                return;
            }

            Color previous = GUI.color;
            GUI.color = new Color(CoreIdentify.r, CoreIdentify.g, CoreIdentify.b, 0.12f);
            float stepX = Mathf.Max(18f, rect.width / 8f);
            float stepY = Mathf.Max(18f, rect.height / 8f);
            for (float x = rect.x; x <= rect.xMax; x += stepX)
            {
                Widgets.DrawLineVertical(x, rect.y, rect.height);
            }

            for (float y = rect.y; y <= rect.yMax; y += stepY)
            {
                Widgets.DrawLineHorizontal(rect.x, y, rect.width);
            }

            GUI.color = new Color(CoreIdentify.r, CoreIdentify.g, CoreIdentify.b, 0.18f);
            float scanY = rect.y + (rect.height * 0.5f);
            Widgets.DrawLineHorizontal(rect.x, scanY, rect.width);
            GUI.color = previous;
        }

        public static bool DrawMenuCard(
            Rect rect,
            string nodeLabel,
            string title,
            bool enabled,
            bool compactLayout = false)
        {
            using (Push())
            {
                Widgets.DrawBoxSolid(rect, Panel);
                Color previous = GUI.color;
                GUI.color = enabled ? Accent : Disabled;
                Widgets.DrawBox(rect, 1);
                GUI.color = previous;

                if (Mouse.IsOver(rect) && enabled)
                {
                    Widgets.DrawBoxSolid(
                        rect,
                        new Color(AccentBright.r, AccentBright.g, AccentBright.b, 0.08f));
                }

                if (compactLayout)
                {
                    DrawMenuCardCompactContent(rect, nodeLabel, title, enabled);
                }
                else
                {
                    DrawMenuCardTallContent(rect, nodeLabel, title, enabled);
                }

                if (!enabled)
                {
                    return false;
                }

                return Widgets.ButtonInvisible(rect);
            }
        }

        private static void DrawMenuCardTallContent(
            Rect rect,
            string nodeLabel,
            string title,
            bool enabled)
        {
            Color previous = GUI.color;
            GUI.color = CoreIdentify;
            Widgets.DrawLineHorizontal(rect.x + 10f, rect.y + 8f, Mathf.Min(48f, rect.width - 20f));
            GUI.color = previous;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = false;
            GUI.color = TextSecondary;
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 12f, rect.width - 24f, 18f), nodeLabel);

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = enabled ? TextPrimary : Disabled;
            Widgets.Label(
                new Rect(rect.x + 12f, rect.y + 28f, rect.width - 24f, rect.height - 40f),
                title);
        }

        private static void DrawMenuCardCompactContent(
            Rect rect,
            string nodeLabel,
            string title,
            bool enabled)
        {
            float nodeW = Mathf.Clamp(rect.width * 0.22f, 72f, 82f);
            nodeW = Mathf.Min(nodeW, Mathf.Max(24f, rect.width - 48f));

            Color previous = GUI.color;
            GUI.color = enabled ? Accent : Disabled;
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 4f, 3f, Mathf.Max(1f, rect.height - 8f)), GUI.color);
            GUI.color = previous;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.WordWrap = false;
            GUI.color = TextSecondary;
            Widgets.Label(new Rect(rect.x + 6f, rect.y, nodeW - 6f, rect.height), nodeLabel);

            float dividerX = rect.x + nodeW;
            GUI.color = new Color(Border.r, Border.g, Border.b, 0.7f);
            Widgets.DrawLineVertical(dividerX, rect.y + 8f, Mathf.Max(1f, rect.height - 16f));
            GUI.color = previous;

            float titleX = dividerX + 12f;
            float titleW = Mathf.Max(0f, rect.xMax - titleX - 12f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = enabled ? TextPrimary : Disabled;
            Widgets.Label(new Rect(titleX, rect.y, titleW, rect.height), title);
        }
    }
}
