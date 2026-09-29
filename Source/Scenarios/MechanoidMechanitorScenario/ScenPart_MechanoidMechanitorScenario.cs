using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class ScenPart_MechanoidMechanitorScenario : ScenPart
    {
        private const int TooltipId = 684271;

        public override void DoEditInterface(Listing_ScenEdit listing)
        {
            Rect rightRect = listing.GetScenPartRect(this, RowHeight);

            Rect fullRowRect = new Rect(
                rightRect.x - rightRect.width,
                rightRect.y,
                rightRect.width * 2f,
                rightRect.height);

            TooltipHandler.TipRegion(
                fullRowRect,
                new TipSignal(
                    "MAP_MechanoidMechanitor.Scenario.Editor.Tooltip".Translate(),
                    TooltipId));
        }

        public override void PostIdeoChosen()
        {
            base.PostIdeoChosen();
            MechanoidMechanitorScenarioUtility.ClearOrdinaryStartingPawnData();
            GameComponent_MechanoidMechanitorScenarioState.EnableForCurrentGame();
        }

        public override string Summary(Scenario scen)
        {
            // Def 描述不能以前导空白开头，仅在显示摘要时添加分隔换行。
            return def.description.NullOrEmpty() ? string.Empty : "\n" + def.description;
        }
    }
}
