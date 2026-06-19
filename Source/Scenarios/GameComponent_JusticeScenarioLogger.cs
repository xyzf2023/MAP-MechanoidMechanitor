using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class GameComponent_JusticeScenarioLogger : GameComponent
    {
        public GameComponent_JusticeScenarioLogger(Game game)
        {
        }

        public override void LoadedGame()
        {
            base.LoadedGame();

            Log.Message(JusticeScenarioUtility.IsJusticeScenarioActive
                ? "[MechanoidMechanitor]专属剧本已启用。"
                : "[MechanoidMechanitor]专属剧本未启用。");
        }
    }
}
