using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public abstract class MechanoidMechanitorStoryComponentWorker
    {
        protected const float CardPadding = 13f;
        public const float CardGap = 11f;
        protected const float HeaderRowHeight = 32f;
        protected const float DropdownHeight = 29f;
        protected const float DropdownMinWidth = 130f;
        protected const float DropdownMaxWidth = 170f;
        protected const float DropdownWidthRatio = 0.36f;
        protected const float TitleDropdownGap = 10f;
        protected const float DescriptionTopGap = 4f;
        protected const float DisabledReasonTopGap = 5f;

        private static readonly Color CardBgColor = new Color(0.16f, 0.16f, 0.16f, 1f);
        private static readonly Color CardBgDisabledColor = new Color(0.12f, 0.12f, 0.12f, 1f);
        private static readonly Color CardOutlineColor = new Color(0.48f, 0.40f, 0.28f, 0.45f);
        private static readonly Color CardOutlineDisabledColor = new Color(0.35f, 0.32f, 0.28f, 0.35f);
        private static readonly Color DropdownBgColor = new Color(0.18f, 0.16f, 0.14f, 1f);
        private static readonly Color DropdownBgHoverColor = new Color(0.24f, 0.21f, 0.17f, 1f);
        private static readonly Color DropdownBgDisabledColor = new Color(0.13f, 0.12f, 0.11f, 1f);
        private static readonly Color DropdownOutlineColor = new Color(0.58f, 0.46f, 0.30f, 0.70f);
        private static readonly Color DropdownOutlineHoverColor = new Color(0.70f, 0.56f, 0.36f, 0.85f);
        private static readonly Color DropdownOutlineDisabledColor = new Color(0.40f, 0.36f, 0.30f, 0.40f);
        private static readonly Color DescriptionColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        private static readonly Color DescriptionDisabledColor = new Color(0.55f, 0.55f, 0.55f, 1f);
        private static readonly Color TitleDisabledColor = new Color(0.62f, 0.62f, 0.62f, 1f);
        private static readonly Color DisabledReasonColor = new Color(0.85f, 0.68f, 0.38f, 1f);

        public MechanoidMechanitorStoryComponentDef def = null!;

        /// <summary>
        /// 双栏布局归属。默认左栏；右栏 Worker 覆写为 true。
        /// </summary>
        public virtual bool DrawInRightColumn => false;

        public virtual bool ShouldShow(MechanoidMechanitorStoryConfigurationContext context)
        {
            return true;
        }

        public virtual bool CanInteract(MechanoidMechanitorStoryConfigurationContext context)
        {
            return true;
        }

        public virtual string? GetDisabledReason(
            MechanoidMechanitorStoryConfigurationContext context)
        {
            return null;
        }

        /// <summary>
        /// 取得该组件在指定配置中的本地化摘要显示值。
        /// 默认返回 null，表示不参与剧情风格预设的悬浮摘要。
        /// </summary>
        public virtual string? GetSummaryValue(
            MechanoidMechanitorStoryConfiguration configuration)
        {
            return null;
        }

        public abstract float GetHeight(
            MechanoidMechanitorStoryConfigurationContext context,
            float width);

        public abstract void Draw(
            Rect rect,
            MechanoidMechanitorStoryConfigurationContext context);

        protected abstract TaggedString GetDescription();

        protected static void NormalizeAfterChange(
            MechanoidMechanitorStoryConfigurationContext context)
        {
            context.Configuration.Normalize(context);
        }

        protected float MeasureCardHeight(
            MechanoidMechanitorStoryConfigurationContext context,
            float width,
            float extraContentHeight = 0f)
        {
            float innerWidth = Mathf.Max(1f, width - CardPadding * 2f);
            float headerHeight = MeasureCardHeaderHeight();
            float descriptionHeight = MeasureDescriptionHeight(innerWidth);
            float disabledReasonHeight = MeasureDisabledReasonHeight(context, innerWidth);
            float height = CardPadding
                + headerHeight
                + DescriptionTopGap
                + descriptionHeight
                + disabledReasonHeight
                + CardPadding;
            if (extraContentHeight > 0f)
            {
                height += extraContentHeight;
            }

            return height;
        }

        protected float MeasureCardHeaderHeight()
        {
            return Mathf.Max(HeaderRowHeight, DropdownHeight);
        }

        protected float MeasureDescriptionHeight(float innerWidth)
        {
            GameFont previousFont = Text.Font;
            bool previousWordWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = true;
                return Text.CalcHeight(GetDescription(), innerWidth);
            }
            finally
            {
                Text.Font = previousFont;
                Text.WordWrap = previousWordWrap;
            }
        }

        protected float MeasureDisabledReasonHeight(
            MechanoidMechanitorStoryConfigurationContext context,
            float innerWidth)
        {
            if (CanInteract(context))
            {
                return 0f;
            }

            string? reason = GetDisabledReason(context);
            if (reason.NullOrEmpty())
            {
                return 0f;
            }

            GameFont previousFont = Text.Font;
            bool previousWordWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = true;
                return DisabledReasonTopGap + Text.CalcHeight(reason, innerWidth);
            }
            finally
            {
                Text.Font = previousFont;
                Text.WordWrap = previousWordWrap;
            }
        }

        protected static float CalcDropdownWidth(float innerWidth)
        {
            return Mathf.Clamp(innerWidth * DropdownWidthRatio, DropdownMinWidth, DropdownMaxWidth);
        }

        protected void DrawCardBackground(Rect rect, bool interactive)
        {
            Color bg = interactive ? CardBgColor : CardBgDisabledColor;
            Color outline = interactive ? CardOutlineColor : CardOutlineDisabledColor;
            Widgets.DrawBoxSolidWithOutline(rect, bg, outline);
            Widgets.DrawHighlightIfMouseover(rect);
        }

        protected float DrawCardHeaderAndDropdown(
            Rect cardRect,
            MechanoidMechanitorStoryConfigurationContext context,
            string currentLabel,
            bool enabled,
            Action onDropdownClicked)
        {
            DrawCardBackground(cardRect, enabled);

            string? disabledReason = enabled ? null : GetDisabledReason(context);
            if (!disabledReason.NullOrEmpty())
            {
                TooltipHandler.TipRegion(cardRect, disabledReason);
            }

            Rect inner = cardRect.ContractedBy(CardPadding);
            float dropdownWidth = CalcDropdownWidth(inner.width);
            float headerHeight = MeasureCardHeaderHeight();

            Rect titleRect = new Rect(
                inner.x,
                inner.y,
                Mathf.Max(1f, inner.width - dropdownWidth - TitleDropdownGap),
                headerHeight);
            Rect dropdownRect = new Rect(
                inner.xMax - dropdownWidth,
                inner.y + (headerHeight - DropdownHeight) * 0.5f,
                dropdownWidth,
                DropdownHeight);

            Color previousColor = GUI.color;
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = enabled ? Color.white : TitleDisabledColor;
                Widgets.Label(titleRect, def.LabelCap);

                if (DrawFlatDropdownButton(dropdownRect, currentLabel, enabled))
                {
                    onDropdownClicked();
                }

                float y = inner.y + headerHeight + DescriptionTopGap;
                float descriptionHeight = MeasureDescriptionHeight(inner.width);
                Rect descriptionRect = new Rect(inner.x, y, inner.width, descriptionHeight);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                GUI.color = enabled ? DescriptionColor : DescriptionDisabledColor;
                Widgets.Label(descriptionRect, GetDescription());
                y += descriptionHeight;

                if (!enabled)
                {
                    y += DrawDisabledReason(inner.x, y, inner.width, disabledReason);
                }

                return y;
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWordWrap;
            }
        }

        protected float DrawDisabledReason(float x, float y, float width, string? reason)
        {
            if (reason.NullOrEmpty())
            {
                return 0f;
            }

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            Color previousColor = GUI.color;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                float height = Text.CalcHeight(reason, width);
                Rect reasonRect = new Rect(x, y + DisabledReasonTopGap, width, height);
                GUI.color = DisabledReasonColor;
                Widgets.Label(reasonRect, reason);
                return DisabledReasonTopGap + height;
            }
            finally
            {
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWordWrap;
                GUI.color = previousColor;
            }
        }

        protected bool DrawFlatDropdownButton(Rect rect, string label, bool enabled)
        {
            Color previousColor = GUI.color;
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            try
            {
                Color bg = enabled ? DropdownBgColor : DropdownBgDisabledColor;
                Color outline = enabled ? DropdownOutlineColor : DropdownOutlineDisabledColor;
                if (enabled && Mouse.IsOver(rect))
                {
                    bg = DropdownBgHoverColor;
                    outline = DropdownOutlineHoverColor;
                }

                Widgets.DrawBoxSolidWithOutline(rect, bg, outline);

                Text.Font = GameFont.Small;
                Text.WordWrap = false;
                GUI.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.40f);

                Rect labelRect = new Rect(rect.x + 8f, rect.y, Mathf.Max(1f, rect.width - 26f), rect.height);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, label.Truncate(labelRect.width));

                Rect arrowRect = new Rect(rect.xMax - 18f, rect.y, 14f, rect.height);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(arrowRect, "▼");

                if (!enabled)
                {
                    return false;
                }

                return Widgets.ButtonInvisible(rect, doMouseoverSound: true);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWordWrap;
            }
        }

        protected void OpenDropdownMenu<T>(
            T[] options,
            Func<T, string> getLabel,
            Action<T> onSelected)
        {
            List<FloatMenuOption> menuOptions = new List<FloatMenuOption>(options.Length);
            for (int i = 0; i < options.Length; i++)
            {
                T option = options[i];
                string label = getLabel(option);
                menuOptions.Add(new FloatMenuOption(label, () => onSelected(option)));
            }

            Find.WindowStack.Add(new FloatMenu(menuOptions));
        }
    }
}
