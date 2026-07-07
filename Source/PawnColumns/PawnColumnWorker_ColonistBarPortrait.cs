using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class PawnColumnWorker_ColonistBarPortrait : PawnColumnWorker
    {
        private const float CheckboxSize = 24f;
        private const int ColumnWidth = 74;

        public override bool VisibleCurrently =>
            JusticeScenarioFreeColonistUtility.IsPortraitDisplayFeatureAvailable;

        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            if (!JusticeScenarioFreeColonistUtility.CanUsePortraitDisplayToggle(pawn))
            {
                return;
            }

            bool enabled = GameComponent_JusticeScenarioState.IsPortraitDisplayEnabled(pawn);
            bool previousEnabled = enabled;

            rect.xMin += (rect.width - CheckboxSize) / 2f;
            rect.yMin += (rect.height - CheckboxSize) / 2f;
            Widgets.Checkbox(rect.position, ref enabled, CheckboxSize, disabled: false, def.paintable);

            if (enabled != previousEnabled)
            {
                GameComponent_JusticeScenarioState.SetPortraitDisplayEnabled(pawn, enabled);
            }
        }

        public override int GetMinWidth(PawnTable table)
        {
            return ColumnWidth;
        }

        public override int GetMaxWidth(PawnTable table)
        {
            return ColumnWidth;
        }

        public override int GetOptimalWidth(PawnTable table)
        {
            return ColumnWidth;
        }

        public override int GetMinCellHeight(Pawn pawn)
        {
            return Mathf.Max(base.GetMinCellHeight(pawn), (int)CheckboxSize);
        }
    }
}
