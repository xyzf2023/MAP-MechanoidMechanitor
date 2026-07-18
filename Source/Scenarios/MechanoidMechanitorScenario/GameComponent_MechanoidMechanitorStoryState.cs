using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class GameComponent_MechanoidMechanitorStoryState : GameComponent
    {
        private MechanoidMechanitorStoryStyleDef? selectedStoryStyle;

        private MechanoidMechanitorStoryConfiguration? activeConfiguration;

        private bool initialOrdinaryFactionRelationsApplied;

        private readonly Dictionary<Faction, MechanoidMechanitorFactionRelationOption>
            customFactionOptionCache =
                new Dictionary<Faction, MechanoidMechanitorFactionRelationOption>();

        public MechanoidMechanitorStoryStyleDef? SelectedStoryStyle => selectedStoryStyle;

        public bool InitialOrdinaryFactionRelationsApplied =>
            initialOrdinaryFactionRelationsApplied;

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

        public static bool HasAppliedInitialOrdinaryFactionRelations
        {
            get
            {
                GameComponent_MechanoidMechanitorStoryState? component = CurrentComponent;
                return component != null && component.initialOrdinaryFactionRelationsApplied;
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
            activeConfiguration = configuration.CreateCopy();
            initialOrdinaryFactionRelationsApplied = false;
            RebuildCustomFactionOptionCache();
        }

        public void MarkInitialOrdinaryFactionRelationsApplied()
        {
            initialOrdinaryFactionRelationsApplied = true;
            RebuildCustomFactionOptionCache();
        }

        public static bool IsStoryStyleActive(MechanoidMechanitorStoryStyleDef? storyStyle)
        {
            if (storyStyle == null)
            {
                return false;
            }

            return CurrentStoryStyle == storyStyle;
        }

        public static bool TryGetEffectiveOrdinaryFactionRelationOption(
            Faction a,
            Faction b,
            out MechanoidMechanitorFactionRelationOption option)
        {
            option = MechanoidMechanitorFactionRelationOption.Default;
            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return false;
            }

            GameComponent_MechanoidMechanitorStoryState? component = CurrentComponent;
            if (component?.activeConfiguration == null)
            {
                return false;
            }

            if (!MechanoidMechanitorOrdinaryFactionUtility.TryGetPlayerAndOrdinary(
                    a,
                    b,
                    out _,
                    out Faction ordinary))
            {
                return false;
            }

            return component.TryResolveEffectiveOrdinaryFactionRelationOption(
                ordinary,
                out option);
        }

        public static bool TryGetEffectiveOrdinaryFactionRelationOptionFor(
            Faction ordinaryFaction,
            out MechanoidMechanitorFactionRelationOption option)
        {
            option = MechanoidMechanitorFactionRelationOption.Default;
            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return false;
            }

            GameComponent_MechanoidMechanitorStoryState? component = CurrentComponent;
            if (component?.activeConfiguration == null)
            {
                return false;
            }

            return component.TryResolveEffectiveOrdinaryFactionRelationOption(
                ordinaryFaction,
                out option);
        }

        public static bool TryGetLockedOrdinaryFactionRelation(
            Faction a,
            Faction b,
            out int goodwill,
            out FactionRelationKind relationKind)
        {
            goodwill = 0;
            relationKind = FactionRelationKind.Neutral;
            if (!HasAppliedInitialOrdinaryFactionRelations)
            {
                return false;
            }

            if (!TryGetEffectiveOrdinaryFactionRelationOption(
                    a,
                    b,
                    out MechanoidMechanitorFactionRelationOption option))
            {
                return false;
            }

            return MechanoidMechanitorOrdinaryFactionRelationPolicy.TryGetLockedRelationTarget(
                option,
                out goodwill,
                out relationKind);
        }

        public bool TryResolveEffectiveOrdinaryFactionRelationOption(
            Faction ordinaryFaction,
            out MechanoidMechanitorFactionRelationOption option)
        {
            option = MechanoidMechanitorFactionRelationOption.Default;
            if (activeConfiguration == null
                || !MechanoidMechanitorOrdinaryFactionUtility.IsOrdinaryFaction(ordinaryFaction))
            {
                return false;
            }

            MechanoidMechanitorOrdinaryFactionRelationsMode mode =
                activeConfiguration.ordinaryFactionRelationsMode;
            if (mode == MechanoidMechanitorOrdinaryFactionRelationsMode.Custom)
            {
                if (customFactionOptionCache.TryGetValue(ordinaryFaction, out option))
                {
                    return true;
                }

                option = MechanoidMechanitorFactionRelationOption.Default;
                return true;
            }

            option = MechanoidMechanitorOrdinaryFactionRelationPolicy
                .ResolveOptionFromGlobalMode(mode);
            return true;
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            if (Find.World == null || Find.FactionManager == null)
            {
                return;
            }

            RebuildCustomFactionOptionCache();
            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return;
            }

            if (!initialOrdinaryFactionRelationsApplied || activeConfiguration == null)
            {
                return;
            }

            MechanoidMechanitorOrdinaryFactionRelationApplier
                .CalibrateLockedOrdinaryFactionRelations(this);
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
            Scribe_Values.Look(
                ref initialOrdinaryFactionRelationsApplied,
                "initialOrdinaryFactionRelationsApplied",
                false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && activeConfiguration != null)
            {
                if (Find.FactionManager != null)
                {
                    MechanoidMechanitorStoryConfigurationContext context =
                        MechanoidMechanitorStoryConfigurationContext.Create(activeConfiguration);
                    activeConfiguration.Normalize(context);
                    RebuildCustomFactionOptionCache();
                }
            }
        }

        private void RebuildCustomFactionOptionCache()
        {
            customFactionOptionCache.Clear();
            if (activeConfiguration == null
                || activeConfiguration.ordinaryFactionRelationsMode
                    != MechanoidMechanitorOrdinaryFactionRelationsMode.Custom)
            {
                return;
            }

            List<MechanoidMechanitorFactionRelationSetting> settings =
                activeConfiguration.ordinaryFactionRelationSettings;
            if (settings == null)
            {
                return;
            }

            for (int i = 0; i < settings.Count; i++)
            {
                MechanoidMechanitorFactionRelationSetting? setting = settings[i];
                if (setting?.faction == null)
                {
                    continue;
                }

                customFactionOptionCache[setting.faction] = setting.relationOption;
            }
        }
    }
}
