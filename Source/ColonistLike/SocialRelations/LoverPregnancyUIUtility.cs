using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class LoverPregnancyUIUtility
    {
        public static void DrawApproachButton(Rect rect, Pawn lover, Pawn spouse)
        {
            CompExplicitSocialRelationUser? comp =
                lover.GetComp<CompExplicitSocialRelationUser>();
            if (comp == null)
            {
                return;
            }

            LoverPregnancyApproach current = comp.PregnancyApproach;
            GUI.color = Color.white;
            GUI.DrawTexture(rect, GetIcon(current));
            if (Widgets.ButtonInvisible(rect))
            {
                Find.WindowStack.Add(new FloatMenu(BuildOptions(comp)));
            }

            if (Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(
                    rect,
                    "MAP_MechanoidMechanitor.LoverPregnancy.ApproachTitle".Translate()
                    .Colorize(ColoredText.TipSectionTitleColor)
                    + "\n"
                    + GetLabel(current)
                    + "\n\n"
                    + "ClickToChangePregnancyApproach".Translate()
                    .Colorize(ColoredText.SubtleGrayColor));
            }
        }

        private static List<FloatMenuOption> BuildOptions(
            CompExplicitSocialRelationUser comp)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            AddOption(options, comp, LoverPregnancyApproach.AvoidPregnancy);
            AddOption(options, comp, LoverPregnancyApproach.TryForBaby);
            AddOption(options, comp, LoverPregnancyApproach.TryForBabyMale);
            AddOption(options, comp, LoverPregnancyApproach.TryForBabyFemale);
            return options;
        }

        private static void AddOption(
            List<FloatMenuOption> options,
            CompExplicitSocialRelationUser comp,
            LoverPregnancyApproach approach)
        {
            options.Add(
                new FloatMenuOption(
                    GetLabel(approach),
                    () => comp.SetPregnancyApproach(approach),
                    GetIcon(approach),
                    Color.white));
        }

        private static string GetLabel(LoverPregnancyApproach approach)
        {
            // 仅用原版基础名称，不用 GetDescription()（其会附带原版怀孕概率倍率）。
            return approach switch
            {
                LoverPregnancyApproach.AvoidPregnancy =>
                    PregnancyApproach.AvoidPregnancy.GetLabel().CapitalizeFirst(),
                LoverPregnancyApproach.TryForBaby =>
                    PregnancyApproach.TryForBaby.GetLabel().CapitalizeFirst(),
                LoverPregnancyApproach.TryForBabyMale =>
                    "MAP_MechanoidMechanitor.LoverPregnancy.TryForBabyMale".Translate(),
                LoverPregnancyApproach.TryForBabyFemale =>
                    "MAP_MechanoidMechanitor.LoverPregnancy.TryForBabyFemale".Translate(),
                _ => PregnancyApproach.AvoidPregnancy.GetLabel().CapitalizeFirst(),
            };
        }

        private static Texture2D GetIcon(LoverPregnancyApproach approach)
        {
            return approach switch
            {
                LoverPregnancyApproach.AvoidPregnancy =>
                    PregnancyApproach.AvoidPregnancy.GetIcon(),
                LoverPregnancyApproach.TryForBaby =>
                    PregnancyApproach.TryForBaby.GetIcon(),
                LoverPregnancyApproach.TryForBabyMale =>
                    PregnancyApproach.TryForBaby.GetIcon(),
                LoverPregnancyApproach.TryForBabyFemale =>
                    PregnancyApproach.TryForBaby.GetIcon(),
                _ => PregnancyApproach.AvoidPregnancy.GetIcon(),
            };
        }
    }
}
