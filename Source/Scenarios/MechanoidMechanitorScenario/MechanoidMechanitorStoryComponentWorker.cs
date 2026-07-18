using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public abstract class MechanoidMechanitorStoryComponentWorker
    {
        public MechanoidMechanitorStoryComponentDef def = null!;

        protected const float SectionGap = 8f;

        protected const float FactionRowHeight = 30f;

        protected const float DropdownButtonWidth = 200f;

        protected const float DropdownButtonHeight = 30f;

        protected const float TitleButtonGap = 12f;

        protected const float TitleDescriptionGap = 4f;

        protected const float SeparatorTopGap = 8f;

        protected const float SeparatorBottomGap = 10f;

        protected const float FactionListIndent = 20f;

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

        public abstract float GetHeight(
            MechanoidMechanitorStoryConfigurationContext context,
            float width);

        public abstract void Draw(
            Rect rect,
            MechanoidMechanitorStoryConfigurationContext context);

        protected virtual void NormalizeAfterChange(
            MechanoidMechanitorStoryConfigurationContext context)
        {
            MechanoidMechanitorStoryConfigurationContext freshContext =
                MechanoidMechanitorStoryConfigurationContext.Create(context.Configuration);
            context.Configuration.Normalize(freshContext);
        }

        protected virtual TaggedString GetDescription()
        {
            return def.description;
        }

        protected float MeasureDropdownSectionHeight(float width, bool includeSeparator = true)
        {
            MeasureDropdownSectionHeights(width, out float titleRowHeight, out float descriptionHeight);
            float height = titleRowHeight + TitleDescriptionGap + descriptionHeight;
            if (includeSeparator)
            {
                height += SeparatorTopGap + SeparatorBottomGap;
            }

            return height;
        }

        protected void MeasureDropdownSectionHeights(
            float width,
            out float titleRowHeight,
            out float descriptionHeight)
        {
            float titleWidth = Mathf.Max(0f, width - DropdownButtonWidth - TitleButtonGap);
            GameFont previousFont = Text.Font;

            Text.Font = GameFont.Medium;
            float titleHeight = Text.CalcHeight(def.LabelCap, titleWidth);
            titleRowHeight = Mathf.Max(titleHeight, DropdownButtonHeight);

            Text.Font = GameFont.Small;
            descriptionHeight = Text.CalcHeight(GetDescription(), width);

            Text.Font = previousFont;
        }

        protected float DrawDropdownSection<T>(
            Rect rect,
            MechanoidMechanitorStoryConfigurationContext context,
            string currentLabel,
            bool enabled,
            IEnumerable<T> options,
            Func<T, string> labelGetter,
            Action<T> onSelected,
            bool includeSeparator = true)
        {
            MeasureDropdownSectionHeights(
                rect.width,
                out float titleRowHeight,
                out float descriptionHeight);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            float y = rect.y;
            float titleWidth = Mathf.Max(0f, rect.width - DropdownButtonWidth - TitleButtonGap);

            Rect buttonRect = new Rect(
                rect.xMax - DropdownButtonWidth,
                y + Mathf.Max(0f, (titleRowHeight - DropdownButtonHeight) / 2f),
                DropdownButtonWidth,
                DropdownButtonHeight);

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x, y, titleWidth, titleRowHeight), def.LabelCap);
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            DrawDropdownButton(
                buttonRect,
                currentLabel,
                enabled,
                options,
                labelGetter,
                onSelected);

            if (!enabled)
            {
                DrawDisabledTip(
                    new Rect(rect.x, y, rect.width, titleRowHeight + TitleDescriptionGap + descriptionHeight),
                    context);
            }

            y += titleRowHeight + TitleDescriptionGap;

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x, y, rect.width, descriptionHeight), GetDescription());
            Text.Font = previousFont;
            y += descriptionHeight;

            if (includeSeparator)
            {
                DrawSectionSeparator(rect, ref y);
            }

            return y;
        }

        protected void DrawSectionSeparator(Rect rect, ref float y)
        {
            y += SeparatorTopGap;
            Color previousColor = GUI.color;
            try
            {
                GUI.color = new Color(1f, 1f, 1f, 0.2f);
                Widgets.DrawLineHorizontal(rect.x, y, rect.width);
            }
            finally
            {
                GUI.color = previousColor;
            }

            y += SeparatorBottomGap;
        }

        protected void DrawDropdownButton<T>(
            Rect buttonRect,
            string currentLabel,
            bool enabled,
            IEnumerable<T> options,
            Func<T, string> labelGetter,
            Action<T> onSelected)
        {
            bool previousEnabled = GUI.enabled;
            if (!enabled)
            {
                GUI.enabled = false;
            }

            try
            {
                if (!Widgets.ButtonText(buttonRect, currentLabel) || !enabled)
                {
                    return;
                }

                List<FloatMenuOption> menuOptions = new List<FloatMenuOption>();
                foreach (T option in options)
                {
                    T local = option;
                    menuOptions.Add(
                        new FloatMenuOption(
                            labelGetter(local),
                            () => onSelected(local)));
                }

                if (menuOptions.Count == 0)
                {
                    return;
                }

                Find.WindowStack.Add(new FloatMenu(menuOptions));
            }
            finally
            {
                GUI.enabled = previousEnabled;
            }
        }

        protected void DrawDisabledTip(
            Rect rect,
            MechanoidMechanitorStoryConfigurationContext context)
        {
            if (CanInteract(context))
            {
                return;
            }

            string? reason = GetDisabledReason(context);
            if (!reason.NullOrEmpty())
            {
                TooltipHandler.TipRegion(rect, reason);
            }
        }
    }
}
