using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class GameComponent_MechanoidMechanitorScenarioLogger : GameComponent
    {
        public GameComponent_MechanoidMechanitorScenarioLogger(Game game)
        {
        }

        public override void LoadedGame()
        {
            base.LoadedGame();

            if (MAPMechanitorMod.Settings?.enableStartupDetailedLogging != true)
            {
                return;
            }

            Log.Message(MechanoidMechanitorScenarioUtility.IsScenarioActive
                ? "[MAP-机械族机械师] 机械族机械师剧本已启用。"
                : "[MAP-机械族机械师] 机械族机械师剧本未启用。");
        }
    }
}
