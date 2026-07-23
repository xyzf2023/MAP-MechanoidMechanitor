using System;
using System.Collections.Generic;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class GameComponent_MechanoidMechanitorStoryState : GameComponent
    {
        private static readonly string[] MechanoidOvermindGreekPrefixes =
        {
            "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta",
            "Iota", "Kappa", "Lambda", "Mu", "Nu", "Xi", "Omicron", "Pi",
            "Rho", "Sigma", "Tau", "Upsilon", "Phi", "Chi", "Psi", "Omega",
        };

        private MechanoidMechanitorStoryStyleDef? selectedStoryStyle;

        private MechanoidMechanitorStoryConfiguration? activeConfiguration;

        private bool initialOrdinaryFactionRelationsApplied;

        private bool initialMechHiveRelationApplied;

        private bool hasLockedOrdinaryFactionRelations;

        private bool hasLockedMechHiveRelation;

        private Faction? cachedMechHive;

        private MechanoidMechanitorPurgeDirectiveRuntimeState? purgeDirectiveRuntimeState;

        private string? mechanoidOvermindName;

        private Ideo? lockedPrimaryIdeo;

        private bool lockedPrimaryIdeoCaptured;

        private readonly Dictionary<Faction, MechanoidMechanitorFactionRelationOption>
            customFactionOptionCache =
                new Dictionary<Faction, MechanoidMechanitorFactionRelationOption>();

        private readonly HashSet<Faction> ordinaryFactionCache = new HashSet<Faction>();

        public MechanoidMechanitorStoryStyleDef? SelectedStoryStyle => selectedStoryStyle;

        public Faction? CachedMechHive => cachedMechHive;

        public MechanoidMechanitorPurgeDirectiveRuntimeState? PurgeDirectiveRuntimeState =>
            purgeDirectiveRuntimeState;

        public Ideo? LockedPrimaryIdeo => ResolveLockedPrimaryIdeo();

        public bool LockedPrimaryIdeoCaptured => lockedPrimaryIdeoCaptured;

        public bool InitialOrdinaryFactionRelationsApplied =>
            initialOrdinaryFactionRelationsApplied;

        public bool InitialMechHiveRelationApplied => initialMechHiveRelationApplied;

        public bool PurgeDirectiveEnabled =>
            activeConfiguration != null && activeConfiguration.purgeDirectiveEnabled;

        public bool PurgeDirectiveFinalPenaltyTriggered =>
            purgeDirectiveRuntimeState != null
            && purgeDirectiveRuntimeState.FinalPenaltyTriggered;

        public int PurgeDirectiveRewardPoints =>
            purgeDirectiveRuntimeState?.RewardPoints ?? 0;

        public static int GetPurgeDirectiveRewardPoints()
        {
            return CurrentComponent?.PurgeDirectiveRewardPoints ?? 0;
        }

        public static string GetOrCreateMechanoidOvermindName()
        {
            GameComponent_MechanoidMechanitorStoryState? component = CurrentComponent;
            if (component == null)
            {
                return GenerateMechanoidOvermindName();
            }

            return component.GetOrCreateMechanoidOvermindNameInstance();
        }

        private string GetOrCreateMechanoidOvermindNameInstance()
        {
            if (!string.IsNullOrEmpty(mechanoidOvermindName))
            {
                return mechanoidOvermindName!;
            }

            mechanoidOvermindName = GenerateMechanoidOvermindName();
            return mechanoidOvermindName;
        }

        private static string GenerateMechanoidOvermindName()
        {
            // 独立随机源，不触碰 Verse.Rand 全局序列。
            var rng = new Random();
            string prefix = MechanoidOvermindGreekPrefixes[
                rng.Next(MechanoidOvermindGreekPrefixes.Length)];
            int number = rng.Next(1, 10000);
            return prefix + "-" + number.ToString("D4");
        }

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

        public static bool IsPurgeDirectiveActive
        {
            get
            {
                if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
                {
                    return false;
                }

                GameComponent_MechanoidMechanitorStoryState? component = CurrentComponent;
                return component != null && component.PurgeDirectiveEnabled;
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
            lockedPrimaryIdeo = null;
            lockedPrimaryIdeoCaptured = false;
            purgeDirectiveRuntimeState = new MechanoidMechanitorPurgeDirectiveRuntimeState();
            purgeDirectiveRuntimeState.InitializeForNewGame(
                activeConfiguration.purgeDirectiveEnabled);
            RebuildRuntimeCaches();
        }

        public static bool TryAddPurgeDirectiveRewardPoints(int amount)
        {
            if (amount <= 0
                || !GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return false;
            }

            GameComponent_MechanoidMechanitorStoryState? component = CurrentComponent;
            if (component == null
                || !component.PurgeDirectiveEnabled
                || component.purgeDirectiveRuntimeState == null)
            {
                return false;
            }

            component.purgeDirectiveRuntimeState.AddRewardPoints(amount);
            return true;
        }

        public bool TrySpendPurgeDirectiveCredits(int amount)
        {
            if (amount < 0
                || !PurgeDirectiveEnabled
                || purgeDirectiveRuntimeState == null)
            {
                return false;
            }

            return purgeDirectiveRuntimeState.TrySpendCredits(amount);
        }

        public bool RefundPurgeDirectiveCredits(int amount)
        {
            if (amount < 0 || purgeDirectiveRuntimeState == null)
            {
                return false;
            }

            return purgeDirectiveRuntimeState.RefundCredits(amount);
        }

        public static bool TrySpendPurgeDirectiveCredits(int amount, bool forCurrentSave = true)
        {
            if (!forCurrentSave
                || !GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return false;
            }

            return CurrentComponent?.TrySpendPurgeDirectiveCredits(amount) ?? false;
        }

        public static bool RefundPurgeDirectiveCredits(int amount, bool forCurrentSave = true)
        {
            if (!forCurrentSave
                || !GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return false;
            }

            return CurrentComponent?.RefundPurgeDirectiveCredits(amount) ?? false;
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

        /// <summary>
        /// 玩家主动进攻普通中立/普通盟友的机械巢节点时，通过统一关系接口把机械巢转为敌对。
        /// 永久（锁定）关系不允许此操作，直接返回 false，由调用方在更早阶段拦截。
        /// </summary>
        public bool TryTurnMechHiveHostileFromPlayerAttack()
        {
            if (activeConfiguration == null || cachedMechHive == null)
            {
                return false;
            }

            if (hasLockedMechHiveRelation)
            {
                return false;
            }

            activeConfiguration.mechHiveRelationMode =
                MechanoidMechanitorMechHiveRelationMode.Default;
            bool applied = MechanoidMechanitorMechHiveRelationApplier.ApplyExactMechHiveRelation(
                cachedMechHive,
                FactionRelationKind.Hostile,
                hostileOnHarmByPlayer: false);
            RebuildRuntimeCaches();
            return applied;
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

            if (!MechanoidMechanitorMechHiveRelationPolicy.TryGetEffectiveLockedTarget(
                    component,
                    out relationKind,
                    out _))
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

            ValidateLockedPrimaryIdeoAfterLoad();
            MechanoidMechanitorIdeologyAdaptationUtility.CalibrateAllRegisteredMechanitors();

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

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            MechanoidMechanitorPurgeDirectiveUtility.Tick(this);
            TryCaptureLockedPrimaryIdeoOnce();
            if (Find.TickManager != null
                && Find.TickManager.TicksGame % 2500 == 0)
            {
                MechanoidMechanitorIdeologyAdaptationUtility.CalibrateAllRegisteredMechanitors();
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
            Scribe_Deep.Look(
                ref purgeDirectiveRuntimeState,
                "purgeDirectiveRuntimeState");
            Scribe_Values.Look(ref mechanoidOvermindName, "mechanoidOvermindName");
            Scribe_References.Look(ref lockedPrimaryIdeo, "lockedPrimaryIdeo");
            Scribe_Values.Look(
                ref lockedPrimaryIdeoCaptured,
                "lockedPrimaryIdeoCaptured",
                false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (purgeDirectiveRuntimeState == null)
                {
                    purgeDirectiveRuntimeState =
                        new MechanoidMechanitorPurgeDirectiveRuntimeState();
                }

                purgeDirectiveRuntimeState.CleanupTrackedPawns();

                if (activeConfiguration != null && Find.FactionManager != null)
                {
                    MechanoidMechanitorStoryConfigurationContext context =
                        MechanoidMechanitorStoryConfigurationContext.Create(activeConfiguration);
                    activeConfiguration.Normalize(context);
                    RebuildRuntimeCaches();
                }
            }
        }

        public static MechanoidMechanitorIdeologyAdaptationLevel GetConfiguredIdeologyAdaptationLevel()
        {
            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled
                || Current.Game == null)
            {
                return MechanoidMechanitorIdeologyAdaptationLevel.Disabled;
            }

            GameComponent_MechanoidMechanitorStoryState? component = CurrentComponent;
            if (component?.activeConfiguration == null)
            {
                return MechanoidMechanitorIdeologyAdaptationLevel.Disabled;
            }

            return component.activeConfiguration.ideologyAdaptationLevel;
        }

        public static Ideo? GetLockedPrimaryIdeo()
        {
            return CurrentComponent?.ResolveLockedPrimaryIdeo();
        }

        public static void NotifyPrimaryIdeoSetExternally(Ideo? ideo)
        {
            CurrentComponent?.NotifyPrimaryIdeoSetExternallyInstance(ideo);
        }

        private void NotifyPrimaryIdeoSetExternallyInstance(Ideo? ideo)
        {
            if (ideo == null
                || !ModsConfig.IdeologyActive
                || !GameComponent_MechanoidMechanitorScenarioState.IsEnabled
                || activeConfiguration == null
                || activeConfiguration.ideologyAdaptationLevel
                    < MechanoidMechanitorIdeologyAdaptationLevel.Basic)
            {
                return;
            }

            // 外部显式改写主流文化：同步为新的锁定值，避免下一次自动人数重算恢复开局文化。
            lockedPrimaryIdeo = ideo;
            lockedPrimaryIdeoCaptured = true;
        }

        private void TryCaptureLockedPrimaryIdeoOnce()
        {
            if (lockedPrimaryIdeoCaptured
                || !ModsConfig.IdeologyActive
                || !GameComponent_MechanoidMechanitorScenarioState.IsEnabled
                || activeConfiguration == null
                || Current.ProgramState != ProgramState.Playing)
            {
                return;
            }

            if (activeConfiguration.ideologyAdaptationLevel
                < MechanoidMechanitorIdeologyAdaptationLevel.Basic)
            {
                return;
            }

            Ideo? primary = Faction.OfPlayer?.ideos?.PrimaryIdeo;
            if (primary == null)
            {
                return;
            }

            lockedPrimaryIdeo = primary;
            lockedPrimaryIdeoCaptured = true;
        }

        private void ValidateLockedPrimaryIdeoAfterLoad()
        {
            if (!lockedPrimaryIdeoCaptured)
            {
                return;
            }

            if (ResolveLockedPrimaryIdeo() != null)
            {
                return;
            }

            lockedPrimaryIdeo = null;
            lockedPrimaryIdeoCaptured = false;
        }

        private Ideo? ResolveLockedPrimaryIdeo()
        {
            if (!lockedPrimaryIdeoCaptured || lockedPrimaryIdeo == null)
            {
                return null;
            }

            IdeoManager? ideoManager = Find.IdeoManager;
            if (ideoManager == null)
            {
                return null;
            }

            List<Ideo> ideos = ideoManager.IdeosListForReading;
            for (int i = 0; i < ideos.Count; i++)
            {
                if (ReferenceEquals(ideos[i], lockedPrimaryIdeo))
                {
                    return lockedPrimaryIdeo;
                }
            }

            return null;
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

            if (PurgeDirectiveFinalPenaltyTriggered)
            {
                return true;
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
