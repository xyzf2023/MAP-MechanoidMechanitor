using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public enum SymbiosisCovenantTrustSource : byte
    {
        Other = 0,
        Goodwill = 1,
        Trade = 2,
        Quest = 3,
        MechHiveNode = 4,
        PublicDeclaration = 5,
        Betrayal = 6
    }

    public sealed class SymbiosisCovenantTrustChange : IExposable
    {
        private int tick;
        private int amount;
        private string reason = string.Empty;

        public int Tick => tick;

        public int Amount => amount;

        public string Reason => reason;

        public SymbiosisCovenantTrustChange()
        {
        }

        public SymbiosisCovenantTrustChange(int tick, int amount, string reason)
        {
            this.tick = tick;
            this.amount = amount;
            this.reason = reason ?? string.Empty;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref tick, "tick", 0);
            Scribe_Values.Look(ref amount, "amount", 0);
            Scribe_Values.Look(ref reason, "reason", string.Empty);
        }
    }

    public sealed class SymbiosisCovenantFactionRecord : IExposable
    {
        private Faction? faction;
        private int trust;
        private int highestReachedMilestone;
        private int highestAppliedMilestone;
        private bool covenantMember;
        private int goodwillWindowStartTick = -1;
        private int goodwillTrustGainedInWindow;
        private int tradeWindowStartTick = -1;
        private int tradeTrustGainedInWindow;
        private int lastBetrayalTick = -1;
        private List<SymbiosisCovenantTrustChange> recentChanges =
            new List<SymbiosisCovenantTrustChange>();

        public Faction? Faction => faction;

        public int Trust => trust;

        public int HighestReachedMilestone => highestReachedMilestone;

        public int HighestAppliedMilestone
        {
            get => highestAppliedMilestone;
            set => highestAppliedMilestone = value;
        }

        public bool CovenantMember
        {
            get => covenantMember;
            set => covenantMember = value;
        }

        public IReadOnlyList<SymbiosisCovenantTrustChange> RecentChanges => recentChanges;

        public SymbiosisCovenantFactionRecord()
        {
        }

        public SymbiosisCovenantFactionRecord(Faction faction, int initialTrust)
        {
            this.faction = faction;
            trust = Math.Max(0, Math.Min(100, initialTrust));
            highestReachedMilestone =
                GameComponent_SymbiosisCovenantState.GetMilestoneForTrust(trust);
            highestAppliedMilestone = highestReachedMilestone;
        }

        public int AdjustTrust(
            int requestedAmount,
            string reason,
            SymbiosisCovenantTrustSource source,
            int now)
        {
            if (requestedAmount == 0)
            {
                return 0;
            }

            int amount = ApplySourceLimit(requestedAmount, source, now);
            if (amount == 0)
            {
                return 0;
            }

            int previousTrust = trust;
            trust = Math.Max(0, Math.Min(100, trust + amount));
            int actualAmount = trust - previousTrust;
            if (actualAmount == 0)
            {
                return 0;
            }

            recentChanges.Add(
                new SymbiosisCovenantTrustChange(now, actualAmount, reason));
            while (recentChanges.Count > 8)
            {
                recentChanges.RemoveAt(0);
            }

            if (actualAmount > 0)
            {
                highestReachedMilestone = Math.Max(
                    highestReachedMilestone,
                    GameComponent_SymbiosisCovenantState.GetMilestoneForTrust(trust));
            }

            return actualAmount;
        }

        public void SetTrustDirect(int value, string reason, int now)
        {
            int clamped = Math.Max(0, Math.Min(100, value));
            int delta = clamped - trust;
            if (delta == 0)
            {
                return;
            }

            trust = clamped;
            recentChanges.Add(new SymbiosisCovenantTrustChange(now, delta, reason));
            while (recentChanges.Count > 8)
            {
                recentChanges.RemoveAt(0);
            }

            if (delta > 0)
            {
                highestReachedMilestone = Math.Max(
                    highestReachedMilestone,
                    GameComponent_SymbiosisCovenantState.GetMilestoneForTrust(trust));
            }
        }

        private int ApplySourceLimit(
            int amount,
            SymbiosisCovenantTrustSource source,
            int now)
        {
            if (amount < 0)
            {
                if (source == SymbiosisCovenantTrustSource.Betrayal)
                {
                    if (lastBetrayalTick >= 0
                        && now - lastBetrayalTick
                            < GameComponent_SymbiosisCovenantState.BetrayalCooldownTicks)
                    {
                        return 0;
                    }

                    lastBetrayalTick = now;
                }

                return amount;
            }

            if (source == SymbiosisCovenantTrustSource.Goodwill)
            {
                ResetWindowIfNeeded(
                    ref goodwillWindowStartTick,
                    ref goodwillTrustGainedInWindow,
                    now);
                int remaining = Math.Max(
                    0,
                    GameComponent_SymbiosisCovenantState.GoodwillTrustPerWindow
                        - goodwillTrustGainedInWindow);
                int accepted = Math.Min(amount, remaining);
                goodwillTrustGainedInWindow += accepted;
                return accepted;
            }

            if (source == SymbiosisCovenantTrustSource.Trade)
            {
                ResetWindowIfNeeded(
                    ref tradeWindowStartTick,
                    ref tradeTrustGainedInWindow,
                    now);
                int remaining = Math.Max(
                    0,
                    GameComponent_SymbiosisCovenantState.TradeTrustPerWindow
                        - tradeTrustGainedInWindow);
                int accepted = Math.Min(amount, remaining);
                tradeTrustGainedInWindow += accepted;
                return accepted;
            }

            return amount;
        }

        private static void ResetWindowIfNeeded(
            ref int windowStartTick,
            ref int gained,
            int now)
        {
            if (windowStartTick < 0
                || now < windowStartTick
                || now - windowStartTick
                    >= GameComponent_SymbiosisCovenantState.SourceWindowTicks)
            {
                windowStartTick = now;
                gained = 0;
            }
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref trust, "trust", 0);
            Scribe_Values.Look(
                ref highestReachedMilestone,
                "highestReachedMilestone",
                0);
            Scribe_Values.Look(
                ref highestAppliedMilestone,
                "highestAppliedMilestone",
                0);
            Scribe_Values.Look(ref covenantMember, "covenantMember", false);
            Scribe_Values.Look(
                ref goodwillWindowStartTick,
                "goodwillWindowStartTick",
                -1);
            Scribe_Values.Look(
                ref goodwillTrustGainedInWindow,
                "goodwillTrustGainedInWindow",
                0);
            Scribe_Values.Look(
                ref tradeWindowStartTick,
                "tradeWindowStartTick",
                -1);
            Scribe_Values.Look(
                ref tradeTrustGainedInWindow,
                "tradeTrustGainedInWindow",
                0);
            Scribe_Values.Look(ref lastBetrayalTick, "lastBetrayalTick", -1);
            Scribe_Collections.Look(
                ref recentChanges,
                "recentChanges",
                LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                recentChanges ??= new List<SymbiosisCovenantTrustChange>();
                trust = Math.Max(0, Math.Min(100, trust));
                highestReachedMilestone = Math.Max(
                    highestReachedMilestone,
                    GetInitialMilestoneFallback());
            }
        }

        private int GetInitialMilestoneFallback()
        {
            return GameComponent_SymbiosisCovenantState.GetMilestoneForTrust(trust);
        }
    }

    public sealed class GameComponent_SymbiosisCovenantState : GameComponent
    {
        public const int SourceWindowTicks = 900000;
        public const int GoodwillTrustPerWindow = 15;
        public const int TradeTrustPerWindow = 5;
        public const int BetrayalCooldownTicks = 600;

        private const int SynchronizeIntervalTicks = 2500;

        private bool initialized;
        private bool contactUnlockedLetterSent;
        private bool publicDeclarationBroadcast;
        private bool mechHiveRetaliationTriggered;
        private int targetMemberCount;
        private List<SymbiosisCovenantFactionRecord> factionRecords =
            new List<SymbiosisCovenantFactionRecord>();

        public bool Initialized => initialized;

        public bool PublicDeclarationBroadcast => publicDeclarationBroadcast;

        public bool MechHiveRetaliationTriggered => mechHiveRetaliationTriggered;

        public int TargetMemberCount => targetMemberCount;

        public int CovenantMemberCount =>
            factionRecords.Count(record => record.CovenantMember);

        public static GameComponent_SymbiosisCovenantState? CurrentComponent
        {
            get
            {
                if (Current.Game == null)
                {
                    return null;
                }

                return Current.Game.GetComponent<GameComponent_SymbiosisCovenantState>();
            }
        }

        public static bool IsActive
        {
            get
            {
                if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
                {
                    return false;
                }

                MechanoidMechanitorStoryConfiguration? configuration =
                    GameComponent_MechanoidMechanitorStoryState.CurrentConfiguration;
                return configuration != null
                    && configuration.symbiosisCovenantEnabled
                    && !configuration.purgeDirectiveEnabled;
            }
        }

        public GameComponent_SymbiosisCovenantState(Game game)
        {
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            TryInitializeOrSynchronize();
            CalibrateMechHiveRetaliation();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (Find.TickManager == null
                || Find.TickManager.TicksGame % SynchronizeIntervalTicks != 0)
            {
                return;
            }

            TryInitializeOrSynchronize();
            RetryPendingMilestones();
            CalibrateMechHiveRetaliation();
        }

        public void SynchronizeNow()
        {
            TryInitializeOrSynchronize();
            RetryPendingMilestones();
        }

        public IReadOnlyList<SymbiosisCovenantFactionRecord> GetRecordsSorted()
        {
            List<SymbiosisCovenantFactionRecord> result =
                new List<SymbiosisCovenantFactionRecord>(factionRecords);
            result.Sort(
                (left, right) =>
                {
                    string leftName = left.Faction?.Name ?? string.Empty;
                    string rightName = right.Faction?.Name ?? string.Empty;
                    int nameComparison = string.Compare(
                        leftName,
                        rightName,
                        StringComparison.OrdinalIgnoreCase);
                    if (nameComparison != 0)
                    {
                        return nameComparison;
                    }

                    return (left.Faction?.loadID ?? -1)
                        .CompareTo(right.Faction?.loadID ?? -1);
                });
            return result;
        }

        public SymbiosisCovenantFactionRecord? GetRecord(Faction? faction)
        {
            if (faction == null)
            {
                return null;
            }

            for (int i = 0; i < factionRecords.Count; i++)
            {
                if (factionRecords[i].Faction == faction)
                {
                    return factionRecords[i];
                }
            }

            return null;
        }

        public static bool TryAdjustTrust(
            Faction? faction,
            int amount,
            string reason,
            SymbiosisCovenantTrustSource source)
        {
            GameComponent_SymbiosisCovenantState? state = CurrentComponent;
            if (!IsActive || state == null)
            {
                return false;
            }

            state.TryInitializeOrSynchronize();
            return state.AdjustTrustInternal(faction, amount, reason, source);
        }

        public static int TryAdjustTrustForAll(
            int amount,
            string reason,
            SymbiosisCovenantTrustSource source)
        {
            GameComponent_SymbiosisCovenantState? state = CurrentComponent;
            if (!IsActive || state == null)
            {
                return 0;
            }

            state.TryInitializeOrSynchronize();
            int changed = 0;
            List<Faction> factions = state.factionRecords
                .Select(record => record.Faction)
                .Where(faction => faction != null)
                .Cast<Faction>()
                .ToList();
            for (int i = 0; i < factions.Count; i++)
            {
                if (state.AdjustTrustInternal(
                        factions[i],
                        amount,
                        reason,
                        source))
                {
                    changed++;
                }
            }

            return changed;
        }

        public bool TryBroadcastPublicDeclaration()
        {
            if (!IsActive || !initialized || publicDeclarationBroadcast)
            {
                return false;
            }

            publicDeclarationBroadcast = true;
            TryAdjustTrustForAll(
                10,
                "MAP_MechanoidMechanitor.Symbiosis.TrustReason.PublicDeclaration"
                    .Translate(),
                SymbiosisCovenantTrustSource.PublicDeclaration);
            Find.LetterStack.ReceiveLetter(
                "MAP_MechanoidMechanitor.Symbiosis.Declaration.Letter.Label".Translate(),
                "MAP_MechanoidMechanitor.Symbiosis.Declaration.Letter.Text".Translate(),
                LetterDefOf.NeutralEvent);
            return true;
        }

        public bool TrySignCovenant(SymbiosisCovenantFactionRecord? record)
        {
            if (!IsActive
                || record?.Faction == null
                || record.Trust < 100
                || record.CovenantMember)
            {
                return false;
            }

            record.CovenantMember = true;
            Find.LetterStack.ReceiveLetter(
                "MAP_MechanoidMechanitor.Symbiosis.Signed.Letter.Label".Translate(
                    record.Faction.Name),
                "MAP_MechanoidMechanitor.Symbiosis.Signed.Letter.Text".Translate(
                    record.Faction.Name,
                    CovenantMemberCount,
                    targetMemberCount),
                LetterDefOf.PositiveEvent,
                null,
                record.Faction);
            return true;
        }

        public bool TryMarkContactUnlockedLetterSent()
        {
            if (!IsActive || contactUnlockedLetterSent)
            {
                return false;
            }

            contactUnlockedLetterSent = true;
            return true;
        }

        public void RegisterSevereBetrayal(Faction? faction, string reason)
        {
            SymbiosisCovenantFactionRecord? record = GetRecord(faction);
            if (record == null)
            {
                return;
            }

            record.SetTrustDirect(0, reason, CurrentTick);
            record.CovenantMember = false;
        }

        private bool AdjustTrustInternal(
            Faction? faction,
            int amount,
            string reason,
            SymbiosisCovenantTrustSource source)
        {
            SymbiosisCovenantFactionRecord? record = GetRecord(faction);
            if (record == null)
            {
                return false;
            }

            int actual = record.AdjustTrust(
                amount,
                reason,
                source,
                CurrentTick);
            if (actual == 0)
            {
                return false;
            }

            if (actual > 0)
            {
                TryApplyReachedMilestone(record);
            }

            return true;
        }

        private void TryInitializeOrSynchronize()
        {
            if (!IsActive
                || !GameComponent_MechanoidMechanitorStoryState
                    .HasAppliedInitialOrdinaryFactionRelations
                || Find.FactionManager == null)
            {
                return;
            }

            RemoveInvalidRecords();
            List<Faction> ordinaryFactions =
                MechanoidMechanitorOrdinaryFactionUtility.GetOrdinaryFactionsSorted();
            for (int i = 0; i < ordinaryFactions.Count; i++)
            {
                Faction faction = ordinaryFactions[i];
                if (GetRecord(faction) == null && IsEligibleFaction(faction))
                {
                    factionRecords.Add(
                        new SymbiosisCovenantFactionRecord(
                            faction,
                            GetInitialTrust(faction)));
                }
            }

            if (!initialized)
            {
                initialized = true;
                targetMemberCount = Math.Min(3, factionRecords.Count);
            }

            if (!mechHiveRetaliationTriggered
                && factionRecords.Any(record => record.Trust >= 75))
            {
                TriggerMechHiveRetaliation();
            }
        }

        private void RemoveInvalidRecords()
        {
            for (int i = factionRecords.Count - 1; i >= 0; i--)
            {
                Faction? faction = factionRecords[i].Faction;
                if (faction == null || !IsEligibleFaction(faction))
                {
                    factionRecords.RemoveAt(i);
                }
            }
        }

        private static bool IsEligibleFaction(Faction faction)
        {
            if (!MechanoidMechanitorOrdinaryFactionUtility.IsOrdinaryFaction(faction))
            {
                return false;
            }

            if (!GameComponent_MechanoidMechanitorStoryState
                    .TryGetEffectiveOrdinaryFactionRelationOptionFor(
                        faction,
                        out MechanoidMechanitorFactionRelationOption option))
            {
                return false;
            }

            return option == MechanoidMechanitorFactionRelationOption.Default
                || option == MechanoidMechanitorFactionRelationOption.Hostile
                || option == MechanoidMechanitorFactionRelationOption.Ally;
        }

        private static int GetInitialTrust(Faction faction)
        {
            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null)
            {
                return 0;
            }

            FactionRelation? relation = player.RelationWith(faction, allowNull: true);
            switch (relation?.kind ?? FactionRelationKind.Hostile)
            {
                case FactionRelationKind.Ally:
                    return 75;
                case FactionRelationKind.Neutral:
                    return 50;
                default:
                    return 0;
            }
        }

        private void RetryPendingMilestones()
        {
            if (!IsActive || !initialized)
            {
                return;
            }

            for (int i = 0; i < factionRecords.Count; i++)
            {
                TryApplyReachedMilestone(factionRecords[i]);
            }
        }

        private void TryApplyReachedMilestone(SymbiosisCovenantFactionRecord record)
        {
            Faction? faction = record.Faction;
            if (faction == null)
            {
                return;
            }

            int[] milestones = { 25, 50, 75, 100 };
            for (int i = 0; i < milestones.Length; i++)
            {
                int milestone = milestones[i];
                if (milestone <= record.HighestAppliedMilestone
                    || milestone > record.HighestReachedMilestone)
                {
                    continue;
                }

                if (!TryApplyMilestoneRelation(faction, milestone))
                {
                    return;
                }

                record.HighestAppliedMilestone = milestone;
                SendMilestoneLetter(faction, milestone);
                if (milestone >= 75)
                {
                    TriggerMechHiveRetaliation();
                }
            }
        }

        private static bool TryApplyMilestoneRelation(Faction faction, int milestone)
        {
            if (!TryGetMilestoneRelation(
                    milestone,
                    out int goodwill,
                    out FactionRelationKind relationKind))
            {
                return false;
            }

            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null)
            {
                return false;
            }

            FactionRelation? currentRelation =
                player.RelationWith(faction, allowNull: true);
            int currentGoodwill = currentRelation?.baseGoodwill ?? -100;
            int targetGoodwill = Math.Max(currentGoodwill, goodwill);
            bool needsGoodwill = currentGoodwill < goodwill;
            bool needsRelationKind =
                currentRelation == null
                || GetRelationRank(currentRelation.kind)
                    < GetRelationRank(relationKind);
            if (!needsGoodwill && !needsRelationKind)
            {
                return true;
            }

            if (needsGoodwill
                && !player.CanChangeGoodwillFor(
                    faction,
                    targetGoodwill - currentGoodwill))
            {
                return false;
            }

            return MechanoidMechanitorOrdinaryFactionRelationApplier
                .ApplyExactPlayerRelation(faction, targetGoodwill, relationKind);
        }

        private static int GetRelationRank(FactionRelationKind relationKind)
        {
            switch (relationKind)
            {
                case FactionRelationKind.Ally:
                    return 2;
                case FactionRelationKind.Neutral:
                    return 1;
                default:
                    return 0;
            }
        }

        private static bool TryGetMilestoneRelation(
            int milestone,
            out int goodwill,
            out FactionRelationKind relationKind)
        {
            if (milestone >= 100)
            {
                goodwill = 100;
                relationKind = FactionRelationKind.Ally;
                return true;
            }

            if (milestone >= 75)
            {
                goodwill = 75;
                relationKind = FactionRelationKind.Ally;
                return true;
            }

            if (milestone >= 50)
            {
                goodwill = 0;
                relationKind = FactionRelationKind.Neutral;
                return true;
            }

            if (milestone >= 25)
            {
                goodwill = -50;
                relationKind = FactionRelationKind.Hostile;
                return true;
            }

            goodwill = 0;
            relationKind = FactionRelationKind.Neutral;
            return false;
        }

        private static void SendMilestoneLetter(Faction faction, int milestone)
        {
            Find.LetterStack.ReceiveLetter(
                "MAP_MechanoidMechanitor.Symbiosis.Milestone.Letter.Label".Translate(
                    faction.Name),
                "MAP_MechanoidMechanitor.Symbiosis.Milestone.Letter.Text".Translate(
                    faction.Name,
                    milestone,
                    GetStageLabel(milestone)),
                milestone >= 75 ? LetterDefOf.PositiveEvent : LetterDefOf.NeutralEvent,
                null,
                faction);
        }

        private void TriggerMechHiveRetaliation()
        {
            if (mechHiveRetaliationTriggered)
            {
                return;
            }

            mechHiveRetaliationTriggered = true;
            CalibrateMechHiveRetaliation();
            Find.LetterStack.ReceiveLetter(
                "MAP_MechanoidMechanitor.Symbiosis.MechHiveRetaliation.Label".Translate(),
                "MAP_MechanoidMechanitor.Symbiosis.MechHiveRetaliation.Text".Translate(),
                LetterDefOf.ThreatBig,
                null,
                MechanoidMechanitorOrdinaryFactionUtility.TryGetMechHive());
        }

        private void CalibrateMechHiveRetaliation()
        {
            if (!IsActive || !mechHiveRetaliationTriggered)
            {
                return;
            }

            Faction? mechHive = MechanoidMechanitorOrdinaryFactionUtility.TryGetMechHive();
            if (mechHive != null)
            {
                MechanoidMechanitorMechHiveRelationApplier.ApplyExactMechHiveRelation(
                    mechHive,
                    FactionRelationKind.Hostile,
                    hostileOnHarmByPlayer: false);
            }
        }

        public static bool IsRetaliatingMechHivePair(Faction? a, Faction? b)
        {
            GameComponent_SymbiosisCovenantState? state = CurrentComponent;
            if (!IsActive
                || state == null
                || !state.mechHiveRetaliationTriggered
                || a == null
                || b == null
                || a == b)
            {
                return false;
            }

            Faction? mechHive = MechanoidMechanitorOrdinaryFactionUtility.TryGetMechHive();
            return mechHive != null
                && ((a.IsPlayer && b == mechHive) || (b.IsPlayer && a == mechHive));
        }

        public static int GetMilestoneForTrust(int trust)
        {
            if (trust >= 100)
            {
                return 100;
            }

            if (trust >= 75)
            {
                return 75;
            }

            if (trust >= 50)
            {
                return 50;
            }

            return trust >= 25 ? 25 : 0;
        }

        public static string GetStageLabel(int trust)
        {
            if (trust >= 100)
            {
                return "MAP_MechanoidMechanitor.Symbiosis.Stage.Member".Translate();
            }

            if (trust >= 75)
            {
                return "MAP_MechanoidMechanitor.Symbiosis.Stage.CommonDefense".Translate();
            }

            if (trust >= 50)
            {
                return "MAP_MechanoidMechanitor.Symbiosis.Stage.Ceasefire".Translate();
            }

            if (trust >= 25)
            {
                return "MAP_MechanoidMechanitor.Symbiosis.Stage.SecretContact".Translate();
            }

            return "MAP_MechanoidMechanitor.Symbiosis.Stage.Rejected".Translate();
        }

        private static int CurrentTick => Find.TickManager?.TicksGame ?? 0;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref initialized, "initialized", false);
            Scribe_Values.Look(
                ref contactUnlockedLetterSent,
                "contactUnlockedLetterSent",
                false);
            Scribe_Values.Look(
                ref publicDeclarationBroadcast,
                "publicDeclarationBroadcast",
                false);
            Scribe_Values.Look(
                ref mechHiveRetaliationTriggered,
                "mechHiveRetaliationTriggered",
                false);
            Scribe_Values.Look(ref targetMemberCount, "targetMemberCount", 0);
            Scribe_Collections.Look(
                ref factionRecords,
                "factionRecords",
                LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                factionRecords ??= new List<SymbiosisCovenantFactionRecord>();
                factionRecords.RemoveAll(record => record == null);
            }
        }
    }
}
