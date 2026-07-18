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
                    "MAP_MechanoidMechanitor.Scenario.EditorTooltip".Translate(),
                    TooltipId));
        }

        public override void PostIdeoChosen()
        {
            base.PostIdeoChosen();
            MechanoidMechanitorScenarioUtility.ClearOrdinaryStartingPawnData();
            GameComponent_MechanoidMechanitorScenarioState.EnableForCurrentGame();
        }

        public override void PreMapGenerate()
        {
            base.PreMapGenerate();
            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return;
            }

            if (Current.Game == null)
            {
                return;
            }

            GameComponent_MechanoidMechanitorStoryState? storyState =
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            if (storyState == null
                || !GameComponent_MechanoidMechanitorStoryState.HasActiveConfiguration)
            {
                return;
            }

            if (!storyState.InitialOrdinaryFactionRelationsApplied)
            {
                MechanoidMechanitorOrdinaryFactionRelationApplier
                    .ApplyInitialOrdinaryFactionRelations(storyState);
            }

            if (!storyState.InitialMechHiveRelationApplied)
            {
                MechanoidMechanitorMechHiveRelationApplier
                    .ApplyInitialMechHiveRelation(storyState);
            }
        }

        public override string Summary(Scenario scen)
        {
            return def.description;
        }
    }
}
