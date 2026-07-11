using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class ScenPart_JusticeScenario : ScenPart
    {
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref visible, "visible", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                visible = false;
            }
        }

        public override void DoEditInterface(Listing_ScenEdit listing)
        {
        }

        public override void PostIdeoChosen()
        {
            base.PostIdeoChosen();

            GameInitData? initData = Find.GameInitData;
            if (initData == null)
            {
                Log.Error("[MAP-机械族机械师] GameInitData 为 null，无法初始化专属剧本。");
                return;
            }

            StartingPawnUtility.ClearAllStartingPawns();
            initData.startingPawnCount = 0;
            initData.startingPawnKind = null;
            initData.startingPawnsRequired = null;
            initData.startingXenotypesRequired = null;
            initData.startingMutantsRequired = null;

            GameComponent_JusticeScenarioState.EnableForCurrentGame();
        }

        public override string Summary(Scenario scen)
        {
            return string.Empty;
        }
    }
}
