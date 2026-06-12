using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(PawnColumnWorker_WorkMode), nameof(PawnColumnWorker_WorkMode.DoCell))]
    public static class Patch_PawnColumnWorker_WorkMode_DoCell_JusticeSelfWorkMode
    {
        [HarmonyPrefix]
        public static bool Prefix(Rect rect, Pawn pawn)
        {
            CompJusticeSelfWorkMode? comp = CompJusticeSelfWorkMode.GetFor(pawn);
            if (comp == null)
            {
                return true;
            }

            DrawJusticeSelfWorkModeCell(rect, comp);
            return false;
        }

        private static void DrawJusticeSelfWorkModeCell(Rect rect, CompJusticeSelfWorkMode comp)
        {
            MechWorkModeDef mode = comp.CurrentSelfWorkMode;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Rect labelRect = rect;
            labelRect.xMin += 3f;
            Widgets.Label(labelRect, CompJusticeSelfWorkMode.GetDisplayLabel(mode));
            Text.Anchor = TextAnchor.UpperLeft;

            if (!Mouse.IsOver(rect))
            {
                return;
            }

            TooltipHandler.TipRegion(rect, "ClickToChangeWorkMode".Translate());
            if (Widgets.ButtonInvisible(rect))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>(2);
                CompJusticeSelfWorkMode.AddSelfWorkModeFloatMenuOptions(options, comp);
                Find.WindowStack.Add(new FloatMenu(options));
            }

            Widgets.DrawHighlight(rect);
        }
    }
}
