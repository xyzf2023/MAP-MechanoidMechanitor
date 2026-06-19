using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class ScenPart_JusticeScenario : ScenPart
    {
        public override void PostIdeoChosen()
        {
            base.PostIdeoChosen();

            GameInitData? initData = Find.GameInitData;
            if (initData == null)
            {
                Log.Error("[MechanoidMechanitor] Cannot initialize Justice scenario because GameInitData is null.");
                return;
            }

            StartingPawnUtility.ClearAllStartingPawns();
            initData.startingPawnCount = 0;
            initData.startingPawnKind = null;
            initData.startingPawnsRequired = null;
            initData.startingXenotypesRequired = null;
            initData.startingMutantsRequired = null;
        }

        public override string Summary(Scenario scen)
        {
            return string.Empty;
        }
    }
}
