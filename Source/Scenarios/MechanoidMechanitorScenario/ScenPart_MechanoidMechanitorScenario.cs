using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class ScenPart_MechanoidMechanitorScenario : ScenPart
    {
        public override void PostIdeoChosen()
        {
            base.PostIdeoChosen();
            MechanoidMechanitorScenarioUtility.ClearOrdinaryStartingPawnData();
            GameComponent_MechanoidMechanitorScenarioState.EnableForCurrentGame();
        }

        public override string Summary(Scenario scen)
        {
            return def.description;
        }
    }
}
