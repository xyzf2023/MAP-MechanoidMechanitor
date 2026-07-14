using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class GameComponent_MechanoidMechanitorStoryState : GameComponent
    {
        private MechanoidMechanitorStoryStyleDef? selectedStoryStyle;

        public MechanoidMechanitorStoryStyleDef? SelectedStoryStyle => selectedStoryStyle;

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
            if (storyStyle == null
                || !GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return false;
            }

            GameComponent_MechanoidMechanitorStoryState? component = CurrentComponent;
            return component != null && component.selectedStoryStyle == storyStyle;
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
