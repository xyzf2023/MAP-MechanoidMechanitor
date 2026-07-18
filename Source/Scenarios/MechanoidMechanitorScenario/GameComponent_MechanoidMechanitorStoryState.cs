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

        private bool initialMechHiveRelationApplied;

        private bool hasLockedOrdinaryFactionRelations;

        private bool hasLockedMechHiveRelation;

        private Faction? cachedMechHive;

        private readonly Dictionary<Faction, MechanoidMechanitorFactionRelationOption>
            customFactionOptionCache =
                new Dictionary<Faction, MechanoidMechanitorFactionRelationOption>();

        private readonly HashSet<Faction> ordinaryFactionCache = new HashSet<Faction>();

        public MechanoidMechanitorStoryStyleDef? SelectedStoryStyle => selectedStoryStyle;

        public Faction? CachedMechHive => cachedMechHive;

        public bool InitialOrdinaryFactionRelationsApplied =>
            initialOrdinaryFactionRelationsApplied;

        public bool InitialMechHiveRelationApplied => initialMechHiveRelationApplied;

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

        public static bool HasAppliedInitialMechHiveRelation
        {
            get
            {
                GameComponent_MechanoidMechanitorStoryState? component = CurrentComponent;
                return component != null && component.initialMechHiveRelationApplied;
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

        public static bool HasLockedMechHiveRelation
        {
            get
            {
                GameComponent_MechanoidMechanitorStoryState? component = CurrentComponent;
                return component != null && component.hasLockedMechHiveRelation;
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
            initialMechHiveRelationApplied = false;
            RebuildRuntimeCaches();
        }

        public void MarkInitialOrdinaryFactionRelationsApplied()
        {
            initialOrdinaryFactionRelationsApplied = true;
            RebuildRuntimeCaches();
        }

        public void MarkInitialMechHiveRelationApplied()
        {
            initialMechHiveRelationApplied = true;
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

        public bool IsCurrentMechHive(Faction? faction)
        {
            return faction != null && cachedMechHive != null && faction == cachedMechHive;
        }

        public bool TryGetMechHiveRelationMode(
            out MechanoidMechanitorMechHiveRelationMode mode)
        {
            if (activeConfiguration == null)
            {
                mode = MechanoidMechanitorMechHiveRelationMode.Default;
                return false;
            }

            mode = activeConfiguration.mechHiveRelationMode;
            return true;
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

        public static bool TryGetLockedMechHiveRelation(
            Faction a,
            Faction b,
            out Faction mechHive,
            out FactionRelationKind relationKind)
        {
            mechHive = null!;
            relationKind = FactionRelationKind.Neutral;

            GameComponent_MechanoidMechanitorStoryState? component = CurrentComponent;
            if (component == null
                || !component.initialMechHiveRelationApplied
                || !component.hasLockedMechHiveRelation
                || component.activeConfiguration == null
                || component.cachedMechHive == null
                || a == null
                || b == null
                || a == b)
            {
                return false;
            }

            Faction other;
            if (a.IsPlayer && !b.IsPlayer)
            {
                other = b;
            }
            else if (b.IsPlayer && !a.IsPlayer)
            {
                other = a;
            }
            else
            {
                return false;
            }

            if (other != component.cachedMechHive)
            {
                return false;
            }

            if (!MechanoidMechanitorMechHiveRelationPolicy.TryGetLockedTarget(
                    component.activeConfiguration.mechHiveRelationMode,
                    out relationKind))
            {
                return false;
            }

            mechHive = component.cachedMechHive;
            return true;
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
            if (MechanoidMechanitorOrdinaryFactionUtility.IsOrdinaryFaction(
                    faction,
                    currentFactions,
                    cachedMechHive))
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
            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled
                || activeConfiguration == null)
            {
                return;
            }

            if (initialOrdinaryFactionRelationsApplied)
            {
                MechanoidMechanitorOrdinaryFactionRelationApplier
                    .CalibrateLockedOrdinaryFactionRelations(this);
            }

            if (initialMechHiveRelationApplied)
            {
                MechanoidMechanitorMechHiveRelationApplier
                    .CalibrateLockedMechHiveRelation(this);
            }
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
            Scribe_Values.Look(
                ref initialMechHiveRelationApplied,
                "initialMechHiveRelationApplied",
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
            cachedMechHive = null;
            hasLockedOrdinaryFactionRelations = false;
            hasLockedMechHiveRelation = false;

            cachedMechHive = ResolveCachedMechHive();
            RebuildOrdinaryFactionCache();
            RebuildCustomFactionOptionCache();
            hasLockedOrdinaryFactionRelations = ComputeHasLockedOrdinaryFactionRelations();
            hasLockedMechHiveRelation = ComputeHasLockedMechHiveRelation();
        }

        private static Faction? ResolveCachedMechHive()
        {
            FactionManager? factionManager = Find.FactionManager;
            if (factionManager == null)
            {
                return null;
            }

            return MechanoidMechanitorOrdinaryFactionUtility.TryGetMechHive(
                factionManager,
                factionManager.AllFactionsListForReading);
        }

        private void RebuildOrdinaryFactionCache()
        {
            FactionManager? factionManager = Find.FactionManager;
            if (factionManager == null)
            {
                return;
            }

            List<Faction> currentFactions = factionManager.AllFactionsListForReading;
            for (int i = 0; i < currentFactions.Count; i++)
            {
                Faction faction = currentFactions[i];
                if (MechanoidMechanitorOrdinaryFactionUtility.IsOrdinaryFaction(
                        faction,
                        currentFactions,
                        cachedMechHive))
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

        private bool ComputeHasLockedMechHiveRelation()
        {
            if (cachedMechHive == null || activeConfiguration == null)
            {
                return false;
            }

            return MechanoidMechanitorMechHiveRelationPolicy.IsLockedMode(
                activeConfiguration.mechHiveRelationMode);
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
                if (setting?.faction == null
                    || !ordinaryFactionCache.Contains(setting.faction))
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
