using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class GameComponent_MechanoidMechanitorStoryState : GameComponent
    {
        private MechanoidMechanitorStoryStyleDef? selectedStoryStyle;

        public MechanoidMechanitorStoryStyleDef? SelectedStoryStyle => selectedStoryStyle;

        public static MechanoidMechanitorStoryStyleDef? CurrentStoryStyle
        {
            get
            {
                if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
                {
                    return null;
                }

                return CurrentComponent?.selectedStoryStyle;
            }
        }

        public static bool HasSelectedStoryStyle => CurrentStoryStyle != null;

        private static GameComponent_MechanoidMechanitorStoryState? CurrentComponent
        {
            get
            {
                if (Current.Game == null)
                {
                    return null;
                }

                return Current.Game.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            }
        }

        public GameComponent_MechanoidMechanitorStoryState(Game game)
        {
        }

        public void SetStoryStyleForNewGame(MechanoidMechanitorStoryStyleDef storyStyle)
        {
            selectedStoryStyle = storyStyle;
        }

        public static bool IsStoryStyleActive(MechanoidMechanitorStoryStyleDef? storyStyle)
        {
            if (storyStyle == null)
            {
                return false;
            }

            return CurrentStoryStyle == storyStyle;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(
                ref selectedStoryStyle,
                "selectedMechanoidMechanitorStoryStyle");
        }
    }
}
