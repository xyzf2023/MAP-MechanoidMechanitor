using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class GameComponent_JusticeScenarioState : GameComponent
    {
        private bool justiceOnlyColonyEnabled;

        public bool JusticeOnlyColonyEnabled => justiceOnlyColonyEnabled;

        public static bool IsEnabled
        {
            get
            {
                if (Current.Game == null)
                {
                    return false;
                }

                GameComponent_JusticeScenarioState? component =
                    Current.Game.GetComponent<GameComponent_JusticeScenarioState>();
                if (component == null)
                {
                    return false;
                }

                return component.justiceOnlyColonyEnabled;
            }
        }

        public GameComponent_JusticeScenarioState(Game game)
        {
        }

        public static void EnableForCurrentGame()
        {
            if (Current.Game == null)
            {
                return;
            }

            GameComponent_JusticeScenarioState? component =
                Current.Game.GetComponent<GameComponent_JusticeScenarioState>();
            if (component == null)
            {
                Log.Error(
                    "[MechanoidMechanitor] Cannot enable Justice-only colony state: " +
                    "GameComponent_JusticeScenarioState is missing.");
                return;
            }

            component.justiceOnlyColonyEnabled = true;
        }

        public static void SyncFromScenarioMarker()
        {
            if (Current.Game == null)
            {
                return;
            }

            GameComponent_JusticeScenarioState? component =
                Current.Game.GetComponent<GameComponent_JusticeScenarioState>();
            if (component == null)
            {
                return;
            }

            component.justiceOnlyColonyEnabled = JusticeScenarioUtility.IsJusticeScenarioActive;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref justiceOnlyColonyEnabled,
                "justiceOnlyColonyEnabled",
                false);
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            SyncFromScenarioMarker();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            SyncFromScenarioMarker();
        }
    }
}
