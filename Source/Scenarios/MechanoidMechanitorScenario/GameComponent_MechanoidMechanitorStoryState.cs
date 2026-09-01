using System;
using System.Collections.Generic;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public enum MechanoidMechanitorStoryConfigurationOrigin : byte
    {
        None = 0,
        MechanoidMechanitorScenario = 1,
        GeneralScenario = 2
    }

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

        private MechanoidMechanitorStoryConfigurationOrigin storyConfigurationOrigin;

        private bool initialOrdinaryFactionRelationsApplied;

        private bool initialMechHiveRelationApplied;

        private bool hasLockedOrdinaryFactionRelations;

        private bool hasLockedMechHiveRelation;

        private bool initialInsectRelationApplied;

        private bool hasLockedInsectRelation;

        private Faction? cachedInsectFaction;

        private Faction? cachedMechHive;

        private MechanoidMechanitorPurgeDirectiveRuntimeState? purgeDirectiveRuntimeState;

        private string? mechanoidOvermindName;

        private Ideo? lockedPrimaryIdeo;

        private bool lockedPrimaryIdeoCaptured;

        private readonly Dictionary<Faction, MechanoidMechanitorFactionRelationOption>
            customFactionOptionCache =
                new Dictionary<Faction, MechanoidMechanitorFactionRelationOption>();

        private readonly HashSet<Faction> ordinaryFactionCache = new HashSet<Faction>();

        // 运行期通知队列：仅在单次游戏运行期间使用，不通过 Scribe 序列化。
        private readonly List<MechanoidMechanitorPendingFactionRelationNotification>
            pendingFactionRelationNotifications =
                new List<MechanoidMechanitorPendingFactionRelationNotification>();

        public MechanoidMechanitorStoryStyleDef? SelectedStoryStyle => selectedStoryStyle;

        public Faction? CachedMechHive => cachedMechHive;

        public Faction? CachedInsectFaction => cachedInsectFaction;

        public MechanoidMechanitorPurgeDirectiveRuntimeState? PurgeDirectiveRuntimeState =>
            purgeDirectiveRuntimeState;

        public Ideo? LockedPrimaryIdeo => ResolveLockedPrimaryIdeo();

        public bool LockedPrimaryIdeoCaptured => lockedPrimaryIdeoCaptured;

        public bool InitialOrdinaryFactionRelationsApplied =>
            initialOrdinaryFactionRelationsApplied;

        public bool InitialMechHiveRelationApplied => initialMechHiveRelationApplied;

        public bool InitialInsectRelationApplied => initialInsectRelationApplied;

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

        public static MechanoidMechanitorStoryConfigurationOrigin CurrentConfigurationOrigin =>
            CurrentComponent?.storyConfigurationOrigin
            ?? MechanoidMechanitorStoryConfigurationOrigin.None;

        public static bool HasActiveConfiguration
        {
            get
            {
                if (Current.Game == null)
                {
                    return false;
                }

                GameComponent_MechanoidMechanitorStoryState? component = CurrentComponent;
                return component?.activeConfiguration != null
                    && component.storyConfigurationOrigin
                        != MechanoidMechanitorStoryConfigurationOrigin.None;
            }
        }

        public static bool IsStoryConfigurationActive => HasActiveConfiguration;

        public static bool IsGeneralScenarioStoryConfiguration =>
            HasActiveConfiguration
            && CurrentConfigurationOrigin
                == MechanoidMechanitorStoryConfigurationOrigin.GeneralScenario;

        public static bool IsMechanoidMechanitorScenarioStoryConfiguration =>
            HasActiveConfiguration
            && CurrentConfigurationOrigin
                == MechanoidMechanitorStoryConfigurationOrigin.MechanoidMechanitorScenario;

        public static MechanoidMechanitorStoryStyleDef? CurrentStoryStyle
        {
            get
            {
                if (!HasActiveConfiguration)
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
                if (!HasActiveConfiguration)
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

        public static bool HasAppliedInitialInsectRelation
        {
            get
            {
                GameComponent_MechanoidMechanitorStoryState? component = CurrentComponent;
                return component != null && component.initialInsectRelationApplied;
            }
        }

        public static bool HasLockedInsectRelation
        {
            get
            {
                GameComponent_MechanoidMechanitorStoryState? component = CurrentComponent;
                return component != null && component.hasLockedInsectRelation;
            }
        }

        public static bool IsPurgeDirectiveActive
        {
            get
            {
                if (!HasActiveConfiguration)
                {
                    return false;
                }

                GameComponent_MechanoidMechanitorStoryState? component = CurrentComponent;
                return component != null && component.PurgeDirectiveEnabled;
            }
        }

        public static bool ShouldRunPurgeFleshColonistComplianceCheck =>
            IsPurgeDirectiveActive
            && IsMechanoidMechanitorScenarioStoryConfiguration;

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
            storyConfigurationOrigin = MechanoidMechanitorScenarioUtility.IsScenarioActive
                ? MechanoidMechanitorStoryConfigurationOrigin.MechanoidMechanitorScenario
                : MechanoidMechanitorStoryConfigurationOrigin.GeneralScenario;
            selectedStoryStyle = storyStyle;
            activeConfiguration = configuration.CreateCopy();
            initialOrdinaryFactionRelationsApplied = false;
            initialMechHiveRelationApplied = false;
            initialInsectRelationApplied = false;
            pendingFactionRelationNotifications.Clear();
            lockedPrimaryIdeo = null;
            lockedPrimaryIdeoCaptured = false;
            purgeDirectiveRuntimeState = new MechanoidMechanitorPurgeDirectiveRuntimeState();
            purgeDirectiveRuntimeState.InitializeForNewGame(
                activeConfiguration.purgeDirectiveEnabled);
            RebuildRuntimeCaches();
        }

        public static bool TryAddPurgeDirectiveRewardPoints(int amount)
        {
            if (amount <= 0 || !IsPurgeDirectiveActive)
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

            // 所有合法肃清额度正向奖励同步增加等量评级（退款 / 订单取消返款 / DEV 调额度
            // 不经由本入口，因此不会增加评级）。接管主脑后本调用内部自动停止评级增减。
            PurgeDirectiveRatingUtility.TryAddRating(amount);
            return true;
        }

        /// <summary>
        /// 世界目标基础奖励（如摧毁据点 200 点）。按世界目标稳定ID持久化去重，防止重复结算。
        /// 奖励同时增加肃清额度与等量评级（经由统一奖励入口）。
        /// </summary>
        public static bool TryAddPurgeDirectiveWorldTargetBaseReward(
            RimWorld.Planet.WorldObject target,
            int points)
        {
            return PurgeDirectiveRatingUtility.TryGrantWorldTargetBaseReward(target, points);
        }

        public static int GetPurgeDirectiveRatingValue()
        {
            return PurgeDirectiveRatingUtility.CurrentRatingValue();
        }

        /// <summary>
        /// DEV / 内部调额入口：仅增加肃清额度，不增加评级值。
        /// 退款、订单取消返款、DEV 调额度都走这里，避免误触发评级增减。
        /// </summary>
        public static bool AddPurgeDirectiveCreditsDev(int amount)
        {
            if (amount == 0 || !IsPurgeDirectiveActive)
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

            long next = (long)component.purgeDirectiveRuntimeState.RewardPoints + amount;
            if (next < 0L) next = 0L;
            if (next > int.MaxValue) next = int.MaxValue;
            component.purgeDirectiveRuntimeState.AddRewardPoints((int)(next - component.purgeDirectiveRuntimeState.RewardPoints));
            return true;
        }

        public static int GetPurgeDirectiveRatingLevel()
        {
            return PurgeDirectiveRatingUtility.CurrentRatingLevel;
        }

        public static bool IsPurgeDirectiveRatingActive()
        {
            return PurgeDirectiveRatingUtility.IsRatingSystemActive();
        }

        public bool TrySpendPurgeDirectiveCredits(int amount)
        {
            if (amount < 0
                || storyConfigurationOrigin
                    == MechanoidMechanitorStoryConfigurationOrigin.None
                || !PurgeDirectiveEnabled
                || purgeDirectiveRuntimeState == null)
            {
                return false;
            }

            return purgeDirectiveRuntimeState.TrySpendCredits(amount);
        }

        public bool RefundPurgeDirectiveCredits(int amount)
        {
            if (amount < 0
                || storyConfigurationOrigin
                    == MechanoidMechanitorStoryConfigurationOrigin.None
                || !PurgeDirectiveEnabled
                || purgeDirectiveRuntimeState == null)
            {
                return false;
            }

            return purgeDirectiveRuntimeState.RefundCredits(amount);
        }

        public static bool TrySpendPurgeDirectiveCredits(int amount, bool forCurrentSave = true)
        {
            if (!forCurrentSave || !IsPurgeDirectiveActive)
            {
                return false;
            }

            return CurrentComponent?.TrySpendPurgeDirectiveCredits(amount) ?? false;
        }

        public static bool RefundPurgeDirectiveCredits(int amount, bool forCurrentSave = true)
        {
            if (!forCurrentSave || !IsPurgeDirectiveActive)
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

        public void MarkInitialInsectRelationApplied()
        {
            initialInsectRelationApplied = true;
            RebuildRuntimeCaches();
        }

        internal void QueueFactionRelationNotification(
            Faction subject,
            Faction other,
            FactionRelationKind previousKind,
            FactionRelationKind expectedCurrentKind,
            string context)
        {
            if (subject == null
                || other == null
                || subject == other
                || previousKind == expectedCurrentKind)
            {
                return;
            }

            for (int i = 0; i < pendingFactionRelationNotifications.Count; i++)
            {
                MechanoidMechanitorPendingFactionRelationNotification existing =
                    pendingFactionRelationNotifications[i];

                if (ReferenceEquals(existing.Subject, subject)
                    && ReferenceEquals(existing.Other, other)
                    && existing.PreviousKind == previousKind
                    && existing.ExpectedCurrentKind == expectedCurrentKind)
                {
                    return;
                }
            }

            pendingFactionRelationNotifications.Add(
                new MechanoidMechanitorPendingFactionRelationNotification(
                    subject,
                    other,
                    previousKind,
                    expectedCurrentKind,
                    context));
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

        public bool IsCurrentInsectFaction(Faction? faction)
        {
            return faction != null
                && cachedInsectFaction != null
                && faction == cachedInsectFaction;
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

        public bool TryGetInsectRelationMode(
            out MechanoidMechanitorInsectRelationMode mode)
        {
            if (activeConfiguration == null)
            {
                mode = MechanoidMechanitorInsectRelationMode.Default;
                return false;
            }

            mode = activeConfiguration.insectRelationMode;
            return true;
        }

        private bool TryGetPlayerAndCachedInsect(
            Faction a,
            Faction b,
            out Faction player,
            out Faction insectFaction)
        {
            player = null!;
            insectFaction = null!;

            if (a == null || b == null || a == b || cachedInsectFaction == null)
            {
                return false;
            }

            if (a.IsPlayer && b == cachedInsectFaction)
            {
                player = a;
                insectFaction = b;
                return true;
            }

            if (b.IsPlayer && a == cachedInsectFaction)
            {
                player = b;
                insectFaction = a;
                return true;
            }

            return false;
        }

        public static bool TryGetLockedInsectRelation(
            Faction a,
            Faction b,
            out Faction insectFaction,
            out FactionRelationKind relationKind)
        {
            insectFaction = null!;
            relationKind = FactionRelationKind.Neutral;

            if (!HasActiveConfiguration)
            {
                return false;
            }

            GameComponent_MechanoidMechanitorStoryState? component =
                CurrentComponent;

            if (component == null
                || !component.initialInsectRelationApplied
                || !component.hasLockedInsectRelation
                || component.activeConfiguration == null
                || component.cachedInsectFaction == null
                || a == null
                || b == null)
            {
                return false;
            }

            if (!component.TryGetPlayerAndCachedInsect(
                    a,
                    b,
                    out _,
                    out insectFaction))
            {
                return false;
            }

            return MechanoidMechanitorInsectRelationPolicy.TryGetLockedTarget(
                component.activeConfiguration.insectRelationMode,
                out relationKind);
        }

        public static bool ShouldSuppressInsectPermanentHostility(
            FactionDef a,
            FactionDef b)
        {
            if (!HasActiveConfiguration || a == null || b == null)
            {
                return false;
            }

            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null)
            {
                return false;
            }

            bool isPlayerInsectPair =
                (a == FactionDefOf.Insect && b == player.def)
                || (b == FactionDefOf.Insect && a == player.def);

            if (!isPlayerInsectPair)
            {
                return false;
            }

            GameComponent_MechanoidMechanitorStoryState? component =
                CurrentComponent;

            if (component?.activeConfiguration == null
                || component.cachedInsectFaction == null)
            {
                return false;
            }

            MechanoidMechanitorInsectRelationMode mode =
                component.activeConfiguration.insectRelationMode;

            return mode == MechanoidMechanitorInsectRelationMode.PermanentNeutral
                || mode == MechanoidMechanitorInsectRelationMode.Ally;
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
            if (!HasActiveConfiguration)
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
            if (!HasActiveConfiguration)
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

            if (!HasActiveConfiguration)
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

        public static bool TryGetLockedMechHiveRelation(
            Faction a,
            Faction b,
            out Faction mechHive,
            out FactionRelationKind relationKind)
        {
            mechHive = null!;
            relationKind = FactionRelationKind.Neutral;

            if (!HasActiveConfiguration)
            {
                return false;
            }

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

            if (faction.def == FactionDefOf.Insect
                || factionManager.OfInsects == faction)
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
            if (!HasActiveConfiguration)
            {
                return;
            }

            ValidateLockedPrimaryIdeoAfterLoad();
            MechanoidMechanitorIdeologyAdaptationUtility.CalibrateAllRegisteredMechanitors();

            try
            {
                if (!initialMechHiveRelationApplied)
                {
                    MechanoidMechanitorMechHiveRelationApplier
                        .ApplyInitialMechHiveRelation(
                            this,
                            MechanoidMechanitorFactionRelationNotificationMode.Deferred);
                }
                else
                {
                    MechanoidMechanitorMechHiveRelationApplier
                        .CalibrateLockedMechHiveRelation(
                            this,
                            MechanoidMechanitorFactionRelationNotificationMode.Deferred);
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 加载存档时恢复或校准机械巢关系失败。\n"
                    + ex);
            }

            try
            {
                if (!initialOrdinaryFactionRelationsApplied)
                {
                    MechanoidMechanitorOrdinaryFactionRelationApplier
                        .ApplyInitialOrdinaryFactionRelations(
                            this,
                            MechanoidMechanitorFactionRelationNotificationMode.Deferred);
                }
                else
                {
                    MechanoidMechanitorOrdinaryFactionRelationApplier
                        .CalibrateLockedOrdinaryFactionRelations(
                            this,
                            MechanoidMechanitorFactionRelationNotificationMode.Deferred);
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 加载存档时恢复或校准普通派系关系失败。\n"
                    + ex);
            }

            try
            {
                if (!initialInsectRelationApplied)
                {
                    MechanoidMechanitorInsectRelationApplier
                        .ApplyInitialInsectRelation(
                            this,
                            MechanoidMechanitorFactionRelationNotificationMode.Deferred);
                }
                else
                {
                    MechanoidMechanitorInsectRelationApplier
                        .CalibrateLockedInsectRelation(
                            this,
                            MechanoidMechanitorFactionRelationNotificationMode.Deferred);
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 加载存档时恢复或校准虫巢关系失败。\n"
                    + ex);
            }
        }

        private void ProcessPendingFactionRelationNotifications()
        {
            if (pendingFactionRelationNotifications.Count == 0
                || Current.ProgramState != ProgramState.Playing
                || Find.TickManager == null
                || Find.FactionManager == null)
            {
                return;
            }

            List<MechanoidMechanitorPendingFactionRelationNotification> processing =
                new List<MechanoidMechanitorPendingFactionRelationNotification>(
                    pendingFactionRelationNotifications);

            pendingFactionRelationNotifications.Clear();

            for (int i = 0; i < processing.Count; i++)
            {
                MechanoidMechanitorPendingFactionRelationNotification notification =
                    processing[i];

                MechanoidMechanitorFactionRelationNotificationUtility.NotifySafely(
                    notification.Subject,
                    notification.Other,
                    notification.PreviousKind,
                    notification.ExpectedCurrentKind,
                    notification.Context);
            }
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            ProcessPendingFactionRelationNotifications();

            if (ShouldRunPurgeFleshColonistComplianceCheck)
            {
                MechanoidMechanitorPurgeDirectiveUtility.Tick(this);
            }

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
                ref storyConfigurationOrigin,
                "mechanoidMechanitorStoryConfigurationOrigin",
                MechanoidMechanitorStoryConfigurationOrigin.None);
            Scribe_Values.Look(
                ref initialOrdinaryFactionRelationsApplied,
                "initialOrdinaryFactionRelationsApplied",
                false);
            Scribe_Values.Look(
                ref initialMechHiveRelationApplied,
                "initialMechHiveRelationApplied",
                false);
            Scribe_Values.Look(
                ref initialInsectRelationApplied,
                "initialInsectRelationApplied",
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
                // 历史版本只有机械族机械师专用剧本能够生成活动剧情配置，因此旧存档缺少
                // origin 字段时可以无歧义地迁移为专用剧本来源。普通旧存档没有活动配置，
                // 不会因本次兼容改动而自动启用剧情系统。
                if (activeConfiguration == null)
                {
                    storyConfigurationOrigin =
                        MechanoidMechanitorStoryConfigurationOrigin.None;
                }
                else if (storyConfigurationOrigin
                    == MechanoidMechanitorStoryConfigurationOrigin.None)
                {
                    storyConfigurationOrigin =
                        MechanoidMechanitorStoryConfigurationOrigin.MechanoidMechanitorScenario;
                }

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
            if (!HasActiveConfiguration)
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
                || !HasActiveConfiguration
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
                || !HasActiveConfiguration
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
            cachedInsectFaction = null;
            hasLockedOrdinaryFactionRelations = false;
            hasLockedMechHiveRelation = false;
            hasLockedInsectRelation = false;

            cachedMechHive = ResolveCachedMechHive();
            cachedInsectFaction = ResolveCachedInsectFaction();
            RebuildOrdinaryFactionCache();
            RebuildCustomFactionOptionCache();
            hasLockedOrdinaryFactionRelations = ComputeHasLockedOrdinaryFactionRelations();
            hasLockedMechHiveRelation = ComputeHasLockedMechHiveRelation();
            hasLockedInsectRelation = ComputeHasLockedInsectRelation();
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

        private static Faction? ResolveCachedInsectFaction()
        {
            return MechanoidMechanitorInsectFactionUtility.TryGetInsectFaction();
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

        private bool ComputeHasLockedInsectRelation()
        {
            if (cachedInsectFaction == null || activeConfiguration == null)
            {
                return false;
            }

            return MechanoidMechanitorInsectRelationPolicy.IsLockedMode(
                activeConfiguration.insectRelationMode);
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
