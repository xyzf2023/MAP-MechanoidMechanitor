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

        public static readonly Color Border = new Color(0.30f, 0.32f, 0.34f);

        public static readonly Color Accent = new Color(0.70f, 0.40f, 0.16f);

        public static readonly Color AccentBright = new Color(0.92f, 0.58f, 0.22f);

        public static readonly Color TextPrimary = new Color(0.88f, 0.90f, 0.92f);

        public static readonly Color TextSecondary = new Color(0.58f, 0.60f, 0.64f);

        public static readonly Color Warning = new Color(0.95f, 0.55f, 0.18f);

        public static readonly Color Error = new Color(0.72f, 0.16f, 0.16f);

        public static readonly Color Disabled = new Color(0.34f, 0.35f, 0.38f);

        public static readonly Color NavSelectedFill = new Color(0.18f, 0.16f, 0.13f);

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

        public static void DrawCornerMarks(Rect rect)
        {
            Color previous = GUI.color;
            GUI.color = Accent;
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

        public static bool DrawActionButton(Rect rect, string label, bool enabled = true)
        {
            using (Push())
            {
                Widgets.DrawBoxSolid(rect, enabled ? PanelAlt : Panel);
                Color previous = GUI.color;
                GUI.color = enabled ? Accent : Disabled;
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
    }
}
