using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(PawnColumnWorker_WorkMode), nameof(PawnColumnWorker_WorkMode.DoCell))]
    public static class Patch_PawnColumnWorker_WorkMode_DoCell_MechanoidMechanitorSelfWorkMode
    {
        [HarmonyPrefix]
        public static bool Prefix(Rect rect, Pawn pawn)
        {
            if (!MechanoidMechanitorSelfWorkModeUtility.TryGetCurrentMode(
                    pawn,
                    out MechWorkModeDef? mode)
                || mode == null)
            {
                return true;
            }

            DrawSelfWorkModeCell(rect, pawn, mode);
            return false;
        }

        private static void DrawSelfWorkModeCell(
            Rect rect,
            Pawn pawn,
            MechWorkModeDef mode)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Rect labelRect = rect;
            labelRect.xMin += 3f;
            Widgets.Label(labelRect, mode.LabelCap);
            Text.Anchor = TextAnchor.UpperLeft;

            if (!Mouse.IsOver(rect))
            {
                return;
            }

            TooltipHandler.TipRegion(rect, "ClickToChangeWorkMode".Translate());
            if (Widgets.ButtonInvisible(rect))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>(3);
                MechanoidMechanitorSelfWorkModeUtility
                    .AddSelfWorkModeFloatMenuOptions(options, pawn);
                Find.WindowStack.Add(new FloatMenu(options));
            }

            Widgets.DrawHighlight(rect);
        }
    }
}
