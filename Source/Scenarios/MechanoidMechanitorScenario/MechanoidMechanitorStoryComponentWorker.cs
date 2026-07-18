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

        protected const float OptionRowHeight = 28f;

        protected const float FactionRowHeight = 30f;

        protected const float DropdownButtonWidth = 180f;

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

        protected float DrawHeaderAndDescription(Rect rect, float width)
        {
            float y = rect.y;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, y, width, Text.LineHeight), def.LabelCap);
            y += Text.LineHeight + 2f;
            Text.Font = GameFont.Small;

            TaggedString description = GetDescription();
            float descriptionHeight = Text.CalcHeight(description, width);
            Widgets.Label(new Rect(rect.x, y, width, descriptionHeight), description);
            y += descriptionHeight + SectionGap;
            return y;
        }

        protected virtual TaggedString GetDescription()
        {
            return def.description;
        }

        protected float MeasureHeaderAndDescription(float width)
        {
            float height = Text.LineHeight + 2f;
            height += Text.CalcHeight(GetDescription(), width);
            height += SectionGap;
            return height;
        }

        protected bool DrawRadioOption(
            Rect rect,
            string label,
            bool active,
            bool enabled,
            out Rect drawnRect)
        {
            drawnRect = new Rect(rect.x, rect.y, rect.width, OptionRowHeight);
            bool clicked = Widgets.RadioButtonLabeled(
                drawnRect,
                label,
                active,
                disabled: !enabled);
            return enabled && clicked && !active;
        }

        protected void DrawDropdownButton<T>(
            Rect buttonRect,
            string currentLabel,
            bool enabled,
            IEnumerable<T> options,
            System.Func<T, string> labelGetter,
            System.Action<T> onSelected)
        {
            bool previousEnabled = GUI.enabled;
            if (!enabled)
            {
                GUI.enabled = false;
            }

            if (Widgets.ButtonText(buttonRect, currentLabel) && enabled)
            {
                List<FloatMenuOption> menuOptions = new List<FloatMenuOption>();
                foreach (T option in options)
                {
                    T local = option;
                    menuOptions.Add(
                        new FloatMenuOption(
                            labelGetter(local),
                            () => onSelected(local)));
                }

                Find.WindowStack.Add(new FloatMenu(menuOptions));
            }

            GUI.enabled = previousEnabled;
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
