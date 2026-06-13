using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 机械师/机械体管理表格「行动模式」列：正义本体 Self Work Mode 显示与切换。
    //
    // 原版 PawnColumnWorker_WorkMode.DoCell 依赖 pawn.GetMechControlGroup()；
    // 正义无外部 overseer/控制组，原版该列会为空。本 Prefix 仅对有
    // CompJusticeSelfWorkMode 的 pawn 绘制本体模式，其余 pawn 走原版逻辑。
    // UI 入口是表格列，不是额外 Gizmo。不影响隐者、普通机械体、
    // 正义控制组内的机械体。
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
