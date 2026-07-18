using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class GameComponent_MechanoidMechanitorStoryState : GameComponent
    {
        private MechanoidMechanitorStoryStyleDef? selectedStoryStyle;

        private MechanoidMechanitorStoryConfiguration? activeConfiguration;

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

        public static MechanoidMechanitorStoryConfiguration? CurrentConfiguration
        {
            get
            {
                if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
                {
                    return null;
                }

                if (Current.Game == null)
                {
                    return null;
                }

                GameComponent_MechanoidMechanitorStoryState? component = CurrentComponent;
                if (component?.activeConfiguration == null)
                {
                    return null;
                }

                return component.activeConfiguration.CreateCopy();
            }
        }

        public static bool HasActiveConfiguration
        {
            get
            {
                if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
                {
                    return false;
                }

                if (Current.Game == null)
                {
                    return false;
                }

                return CurrentComponent?.activeConfiguration != null;
            }
        }

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

        public void SetStoryStyleForNewGame(
            MechanoidMechanitorStoryStyleDef storyStyle,
            MechanoidMechanitorStoryConfiguration configuration)
        {
            selectedStoryStyle = storyStyle;
            activeConfiguration = configuration;
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
            Scribe_Deep.Look(
                ref activeConfiguration,
                "activeMechanoidMechanitorStoryConfiguration");

            if (Scribe.mode == LoadSaveMode.PostLoadInit && activeConfiguration != null)
            {
                MechanoidMechanitorStoryConfigurationContext context =
                    MechanoidMechanitorStoryConfigurationContext.Create(activeConfiguration);
                activeConfiguration.Normalize(context);
            }
        }
    }
}
