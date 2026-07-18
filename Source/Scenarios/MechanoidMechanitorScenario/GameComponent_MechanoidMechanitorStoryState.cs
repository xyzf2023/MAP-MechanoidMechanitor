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

        private bool hasLockedOrdinaryFactionRelations;

        private readonly Dictionary<Faction, MechanoidMechanitorFactionRelationOption>
            customFactionOptionCache =
                new Dictionary<Faction, MechanoidMechanitorFactionRelationOption>();

        private readonly HashSet<Faction> ordinaryFactionCache = new HashSet<Faction>();

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

        public static bool HasLockedOrdinaryFactionRelations
        {
            get
            {
                GameComponent_MechanoidMechanitorStoryState? component = CurrentComponent;
                return component != null && component.hasLockedOrdinaryFactionRelations;
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
            RebuildRuntimeCaches();
        }

        public void MarkInitialOrdinaryFactionRelationsApplied()
        {
            initialOrdinaryFactionRelationsApplied = true;
            RebuildRuntimeCaches();
        }

        public static bool IsStoryStyleActive(MechanoidMechanitorStoryStyleDef? storyStyle)
        {
            if (storyStyle == null)
            {
                return false;
            }

            return CurrentStoryStyle == storyStyle;
        }

        public bool IsCachedOrdinaryFaction(Faction? faction)
        {
            return faction != null && ordinaryFactionCache.Contains(faction);
        }

        public bool TryGetPlayerAndCachedOrdinary(
            Faction a,
            Faction b,
            out Faction player,
            out Faction ordinary)
        {
            player = null!;
            ordinary = null!;
            if (a == null || b == null || a == b)
            {
                return false;
            }

            if (a.IsPlayer && !b.IsPlayer)
            {
                player = a;
                ordinary = b;
                return ordinaryFactionCache.Contains(ordinary);
            }

            if (b.IsPlayer && !a.IsPlayer)
            {
                player = b;
                ordinary = a;
                return ordinaryFactionCache.Contains(ordinary);
            }

            return false;
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

            if (!component.TryGetPlayerAndCachedOrdinary(a, b, out _, out Faction ordinary))
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
            return TryGetLockedOrdinaryFactionRelation(
                a,
                b,
                out _,
                out goodwill,
                out relationKind);
        }

        public static bool TryGetLockedOrdinaryFactionRelation(
            Faction a,
            Faction b,
            out Faction ordinary,
            out int goodwill,
            out FactionRelationKind relationKind)
        {
            ordinary = null!;
            goodwill = 0;
            relationKind = FactionRelationKind.Neutral;

            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return false;
            }

            GameComponent_MechanoidMechanitorStoryState? component = CurrentComponent;
            if (component == null
                || !component.initialOrdinaryFactionRelationsApplied
                || !component.hasLockedOrdinaryFactionRelations
                || component.activeConfiguration == null)
            {
                return false;
            }

            if (!component.TryGetPlayerAndCachedOrdinary(a, b, out _, out ordinary))
            {
                return false;
            }

            if (!component.TryResolveEffectiveOrdinaryFactionRelationOption(
                    ordinary,
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
                || ordinaryFaction == null
                || !ordinaryFactionCache.Contains(ordinaryFaction))
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

        public void NotifyFactionAdded(Faction faction)
        {
            if (faction == null)
            {
                return;
            }

            FactionManager? factionManager = Find.FactionManager;
            if (factionManager == null)
            {
                return;
            }

            if (faction.def == FactionDefOf.Mechanoid
                || factionManager.OfMechanoids == faction)
            {
                RebuildRuntimeCaches();
                return;
            }

            List<Faction> currentFactions = factionManager.AllFactionsListForReading;
            Faction? mechHive = MechanoidMechanitorOrdinaryFactionUtility.TryGetMechHive(
                factionManager,
                currentFactions);
            if (MechanoidMechanitorOrdinaryFactionUtility.IsOrdinaryFaction(
                    faction,
                    currentFactions,
                    mechHive))
            {
                ordinaryFactionCache.Add(faction);
            }
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            if (Find.World == null || Find.FactionManager == null)
            {
                return;
            }

            RebuildRuntimeCaches();
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
                    RebuildRuntimeCaches();
                }
            }
        }

        public void RebuildRuntimeCaches()
        {
            customFactionOptionCache.Clear();
            ordinaryFactionCache.Clear();
            hasLockedOrdinaryFactionRelations = false;

            RebuildOrdinaryFactionCache();
            RebuildCustomFactionOptionCache();
            hasLockedOrdinaryFactionRelations = ComputeHasLockedOrdinaryFactionRelations();
        }

        private void RebuildOrdinaryFactionCache()
        {
            FactionManager? factionManager = Find.FactionManager;
            if (factionManager == null)
            {
                return;
            }

            List<Faction> currentFactions = factionManager.AllFactionsListForReading;
            Faction? mechHive = MechanoidMechanitorOrdinaryFactionUtility.TryGetMechHive(
                factionManager,
                currentFactions);
            for (int i = 0; i < currentFactions.Count; i++)
            {
                Faction faction = currentFactions[i];
                if (MechanoidMechanitorOrdinaryFactionUtility.IsOrdinaryFaction(
                        faction,
                        currentFactions,
                        mechHive))
                {
                    ordinaryFactionCache.Add(faction);
                }
            }
        }

        private void RebuildCustomFactionOptionCache()
        {
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

        private bool ComputeHasLockedOrdinaryFactionRelations()
        {
            if (activeConfiguration == null)
            {
                return false;
            }

            switch (activeConfiguration.ordinaryFactionRelationsMode)
            {
                case MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentHostile:
                case MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentNeutral:
                case MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentAlly:
                    return true;

                case MechanoidMechanitorOrdinaryFactionRelationsMode.Custom:
                    return CustomSettingsContainLockedOption();

                default:
                    return false;
            }
        }

        private bool CustomSettingsContainLockedOption()
        {
            List<MechanoidMechanitorFactionRelationSetting> settings =
                activeConfiguration!.ordinaryFactionRelationSettings;
            if (settings == null)
            {
                return false;
            }

            for (int i = 0; i < settings.Count; i++)
            {
                MechanoidMechanitorFactionRelationSetting? setting = settings[i];
                if (setting == null)
                {
                    continue;
                }

                if (MechanoidMechanitorOrdinaryFactionRelationPolicy.IsLockedOption(
                        setting.relationOption))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
