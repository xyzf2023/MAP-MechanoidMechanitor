using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class SyntheticPregnancyUIUtility
    {
        public static void DrawApproachButton(Rect rect, Pawn syntheticCompanion, Pawn spouse)
        {
            if (!SyntheticCompanionStateUtility.TryGetState(syntheticCompanion, out ISyntheticCompanionState? state)
                || state == null)
            {
                return;
            }

            SyntheticPregnancyApproach current = state.PregnancyApproach;
            GUI.color = Color.white;
            GUI.DrawTexture(rect, GetIcon(current));
            if (Widgets.ButtonInvisible(rect))
            {
                Find.WindowStack.Add(new FloatMenu(BuildOptions(state)));
            }

            if (Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(
                    rect,
                    "MAP_MechanoidMechanitor.Lover.Pregnancy.Approach.Title".Translate()
                    .Colorize(ColoredText.TipSectionTitleColor)
                    + "\n"
                    + GetLabel(current)
                    + "\n\n"
                    + "ClickToChangePregnancyApproach".Translate()
                    .Colorize(ColoredText.SubtleGrayColor));
            }
        }

        private static List<FloatMenuOption> BuildOptions(ISyntheticCompanionState state)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            AddOption(options, state, SyntheticPregnancyApproach.AvoidPregnancy);
            AddOption(options, state, SyntheticPregnancyApproach.TryForBaby);
            AddOption(options, state, SyntheticPregnancyApproach.TryForBabyMale);
            AddOption(options, state, SyntheticPregnancyApproach.TryForBabyFemale);
            return options;
        }

        private static void AddOption(
            List<FloatMenuOption> options,
            ISyntheticCompanionState state,
            SyntheticPregnancyApproach approach)
        {
            options.Add(
                new FloatMenuOption(
                    GetLabel(approach),
                    () => state.SetPregnancyApproach(approach),
                    GetIcon(approach),
                    Color.white));
        }

        private static string GetLabel(SyntheticPregnancyApproach approach)
        {
            return approach switch
            {
                SyntheticPregnancyApproach.AvoidPregnancy =>
                    PregnancyApproach.AvoidPregnancy.GetLabel().CapitalizeFirst(),
                SyntheticPregnancyApproach.TryForBaby =>
                    PregnancyApproach.TryForBaby.GetLabel().CapitalizeFirst(),
                SyntheticPregnancyApproach.TryForBabyMale =>
                    "MAP_MechanoidMechanitor.Lover.Pregnancy.TryForBabyMale".Translate(),
                SyntheticPregnancyApproach.TryForBabyFemale =>
                    "MAP_MechanoidMechanitor.Lover.Pregnancy.TryForBabyFemale".Translate(),
                _ => PregnancyApproach.AvoidPregnancy.GetLabel().CapitalizeFirst(),
            };
        }

        private static Texture2D GetIcon(SyntheticPregnancyApproach approach)
        {
            return approach switch
            {
                SyntheticPregnancyApproach.AvoidPregnancy =>
                    PregnancyApproach.AvoidPregnancy.GetIcon(),
                SyntheticPregnancyApproach.TryForBaby =>
                    PregnancyApproach.TryForBaby.GetIcon(),
                SyntheticPregnancyApproach.TryForBabyMale =>
                    PregnancyApproach.TryForBaby.GetIcon(),
                SyntheticPregnancyApproach.TryForBabyFemale =>
                    PregnancyApproach.TryForBaby.GetIcon(),
                _ => PregnancyApproach.AvoidPregnancy.GetIcon(),
            };
        }
    }
}
