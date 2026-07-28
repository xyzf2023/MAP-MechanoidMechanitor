using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public enum SymbiosisPreDeclarationTrustLock : byte
    {
        None = 0,
        Neutral = 1,
        Hostile = 2
    }

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
        private SymbiosisPreDeclarationTrustLock preDeclarationLock;
        private bool covenantMember;
        private int proposalResolutionTick = -1;
        private int invitationFailureCount;
        private int nextInvitationTick = -1;
        private int covenantExitCount;
        private int goodwillWindowStartTick = -1;
        private int goodwillTrustGainedInWindow;
        private int tradeWindowStartTick = -1;
        private int tradeTrustGainedInWindow;
        private int lastBetrayalTick = -1;
        private List<SymbiosisCovenantTrustChange> recentChanges =
            new List<SymbiosisCovenantTrustChange>();

        public Faction? Faction => faction;

        public int Trust => trust;

        public SymbiosisPreDeclarationTrustLock PreDeclarationLock => preDeclarationLock;

        public bool CovenantMember
        {
            get => covenantMember;
            set => covenantMember = value;
        }

        public int ProposalResolutionTick => proposalResolutionTick;

        public bool ProposalPending => proposalResolutionTick >= 0;

        public int InvitationFailureCount => invitationFailureCount;

        public int NextInvitationTick => nextInvitationTick;

        public int CovenantExitCount => covenantExitCount;

        public bool PermanentlyRefusesInvitation => covenantExitCount >= 2;

        public IReadOnlyList<SymbiosisCovenantTrustChange> RecentChanges => recentChanges;

        public int GoodwillWindowStartTick => goodwillWindowStartTick;

        public int GoodwillTrustGainedInWindow => goodwillTrustGainedInWindow;

        public int TradeWindowStartTick => tradeWindowStartTick;

        public int TradeTrustGainedInWindow => tradeTrustGainedInWindow;

        public int LastBetrayalTick => lastBetrayalTick;

        public SymbiosisCovenantFactionRecord()
        {
        }

        public SymbiosisCovenantFactionRecord(
            Faction faction,
            int initialTrust,
            SymbiosisPreDeclarationTrustLock initialLock)
        {
            this.faction = faction;
            trust = GameComponent_SymbiosisCovenantState.ClampTrust(initialTrust);
            preDeclarationLock = initialLock;
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
            trust = GameComponent_SymbiosisCovenantState.ClampTrust(trust + amount);
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

            return actualAmount;
        }

        public void SetTrustDirect(int value, string reason, int now)
        {
            int clamped = GameComponent_SymbiosisCovenantState.ClampTrust(value);
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
        }

        public void DevSetTrust(int value, string reason, int now)
        {
            int clamped = GameComponent_SymbiosisCovenantState.ClampTrust(value);
            int delta = clamped - trust;
            trust = clamped;
            if (delta != 0)
            {
                recentChanges.Add(
                    new SymbiosisCovenantTrustChange(now, delta, reason));
                while (recentChanges.Count > 8)
                {
                    recentChanges.RemoveAt(0);
                }
            }
        }

        public void DevResetSourceLimits()
        {
            goodwillWindowStartTick = -1;
            goodwillTrustGainedInWindow = 0;
            tradeWindowStartTick = -1;
            tradeTrustGainedInWindow = 0;
            lastBetrayalTick = -1;
        }

        public void SetPreDeclarationLock(SymbiosisPreDeclarationTrustLock value)
        {
            preDeclarationLock = value;
        }

        public void SetProposalResolutionTick(int value)
        {
            proposalResolutionTick = value;
        }

        public void IncrementInvitationFailureCount()
        {
            invitationFailureCount++;
        }

        public void SetNextInvitationTick(int value)
        {
            nextInvitationTick = value;
        }

        public void IncrementCovenantExitCount()
        {
            covenantExitCount++;
        }

        public void ClearProposal()
        {
            proposalResolutionTick = -1;
        }

        public void ClearInvitationCooldown()
        {
            nextInvitationTick = -1;
        }

        public void DevSetInvitationFailureCount(int value)
        {
            invitationFailureCount = Math.Max(0, value);
        }

        public void DevSetCovenantExitCount(int value)
        {
            covenantExitCount = Math.Max(0, value);
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
            Scribe_Values.Look(
                ref trust,
                "trust",
                GameComponent_SymbiosisCovenantState.MinimumTrust);
            Scribe_Values.Look(
                ref preDeclarationLock,
                "preDeclarationLock",
                SymbiosisPreDeclarationTrustLock.None);
            Scribe_Values.Look(ref covenantMember, "covenantMember", false);
            Scribe_Values.Look(
                ref proposalResolutionTick,
                "proposalResolutionTick",
                -1);
            Scribe_Values.Look(
                ref invitationFailureCount,
                "invitationFailureCount",
                0);
            Scribe_Values.Look(ref nextInvitationTick, "nextInvitationTick", -1);
            Scribe_Values.Look(ref covenantExitCount, "covenantExitCount", 0);
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
                trust = GameComponent_SymbiosisCovenantState.ClampTrust(trust);
                if (invitationFailureCount < 0)
                {
                    invitationFailureCount = 0;
                }

                if (covenantExitCount < 0)
                {
                    covenantExitCount = 0;
                }

                if (proposalResolutionTick < 0)
                {
                    proposalResolutionTick = -1;
                }
            }
        }
    }

    public sealed class GameComponent_SymbiosisCovenantState : GameComponent
    {
        public const int MinimumTrust = -100;
        public const int MaximumTrust = 200;
        public const int SourceWindowTicks = 900000;
        public const int GoodwillTrustPerWindow = 15;
        public const int TradeTrustPerWindow = 5;
        public const int BetrayalCooldownTicks = 600;

        private const int SynchronizeIntervalTicks = 2500;
        private const int ProposalMinTicks = 60000;
        private const int ProposalMaxTicks = 180000;
        private const int InvitationCooldownTicks = 1800000;
        private const int UnityUpdateIntervalTicks = 60000;
        private const int UnityMax = 1000;
        private const int CovenantLevelMax = 5;
        private const int CovenantExitUnityPenalty = 100;
        private const int MemberJoinTrust = 0;
        private const int MemberRejoinTrust = -25;

        private bool initialized;
        private bool contactUnlockedLetterSent;
        private bool publicDeclarationBroadcast;
        private float unity;
        private int covenantLevel;
        private int highestCovenantLevel;
        private int lastUnityUpdateTick = -1;
        private List<SymbiosisCovenantFactionRecord> factionRecords =
            new List<SymbiosisCovenantFactionRecord>();

        public bool Initialized => initialized;

        public bool PublicDeclarationBroadcast => publicDeclarationBroadcast;

        public float Unity => unity;

        public int CovenantLevel => covenantLevel;

        public int HighestCovenantLevel => highestCovenantLevel;

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
            RecalculateGoodwillSituations();
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
            CalibrateMechHiveHostility();
        }

        public void SynchronizeNow()
        {
            TryInitializeOrSynchronize();
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

            // 解除所有派系的声明前信任锁定，保留解除瞬间的实际信任值。
            for (int i = 0; i < factionRecords.Count; i++)
            {
                factionRecords[i].SetPreDeclarationLock(
                    SymbiosisPreDeclarationTrustLock.None);
            }

            // 机械巢敌对只由公开脱离声明触发。
            Faction? mechHive = MechanoidMechanitorOrdinaryFactionUtility.TryGetMechHive();
            if (mechHive != null)
            {
                MechanoidMechanitorMechHiveRelationApplier.ApplyExactMechHiveRelation(
                    mechHive,
                    FactionRelationKind.Hostile,
                    hostileOnHarmByPlayer: false);
            }

            Find.LetterStack.ReceiveLetter(
                "MAP_MechanoidMechanitor.Symbiosis.MechHiveRetaliation.Label".Translate(),
                "MAP_MechanoidMechanitor.Symbiosis.MechHiveRetaliation.Text".Translate(),
                LetterDefOf.ThreatBig,
                null,
                mechHive);

            return true;
        }

        public bool TryBeginCovenantProposal(Faction? faction)
        {
            if (!IsActive || !initialized || !publicDeclarationBroadcast)
            {
                return false;
            }

            SymbiosisCovenantFactionRecord? record = GetRecord(faction);
            if (record == null
                || record.CovenantMember
                || record.ProposalPending
                || record.PermanentlyRefusesInvitation
                || record.NextInvitationTick > CurrentTick)
            {
                return false;
            }

            record.SetProposalResolutionTick(
                CurrentTick + Rand.RangeInclusive(ProposalMinTicks, ProposalMaxTicks));
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

            int previousTrust = record.Trust;
            record.SetTrustDirect(MinimumTrust, reason, CurrentTick);
            HandleTrustChangeForRecord(record, previousTrust);
        }

        public void NotifyGoodwillChangedRelationMayHaveShifted(Faction? ordinaryFaction)
        {
            if (!IsActive || publicDeclarationBroadcast)
            {
                return;
            }

            SymbiosisCovenantFactionRecord? record = GetRecord(ordinaryFaction);
            if (record != null)
            {
                SynchronizePreDeclarationLock(record);
            }
        }

        public static bool IsMechHiveHostileLocked(Faction? a, Faction? b)
        {
            GameComponent_SymbiosisCovenantState? state = CurrentComponent;
            if (!IsActive
                || state == null
                || !state.publicDeclarationBroadcast
                || a == null
                || b == null
                || a == b)
            {
                return false;
            }

            Faction? mechHive = MechanoidMechanitorOrdinaryFactionUtility.TryGetMechHive();
            return mechHive != null
                && ((a.IsPlayer && b == mechHive)
                    || (b.IsPlayer && a == mechHive));
        }

        public int GetInvitationSuccessChance(SymbiosisCovenantFactionRecord? record)
        {
            if (record == null)
            {
                return 0;
            }

            int currentGoodwill = Faction.OfPlayerSilentFail != null
                && record.Faction != null
                    ? Faction.OfPlayerSilentFail.GoodwillWith(record.Faction)
                    : 0;
            int baseChance = Math.Max(
                0,
                75 - 25 * record.InvitationFailureCount);
            return Mathf.Clamp(baseChance + currentGoodwill, 0, 100);
        }

        // ===== DEV 方法 =====

        public bool DevAdjustTrust(Faction? faction, int amount, string reason)
        {
            if (!Prefs.DevMode || faction == null || amount == 0)
            {
                return false;
            }

            SymbiosisCovenantFactionRecord? record = GetRecord(faction);
            if (record == null)
            {
                return false;
            }

            // DEV 直接设置可以绕过声明前锁定入口，但下一次周期同步会按锁定规则恢复。
            int previousTrust = record.Trust;
            record.SetTrustDirect(previousTrust + amount, reason, CurrentTick);
            HandleTrustChangeForRecord(record, previousTrust);
            return record.Trust != previousTrust;
        }

        public bool DevSetTrust(Faction? faction, int value, string reason)
        {
            if (!Prefs.DevMode || faction == null)
            {
                return false;
            }

            SymbiosisCovenantFactionRecord? record = GetRecord(faction);
            if (record == null)
            {
                return false;
            }

            int previousTrust = record.Trust;
            record.DevSetTrust(value, reason, CurrentTick);
            HandleTrustChangeForRecord(record, previousTrust);
            return true;
        }

        public bool DevResetSourceLimits(Faction? faction)
        {
            if (!Prefs.DevMode)
            {
                return false;
            }

            SymbiosisCovenantFactionRecord? record = GetRecord(faction);
            if (record == null)
            {
                return false;
            }

            record.DevResetSourceLimits();
            return true;
        }

        public bool DevRecreateRecord(Faction? faction)
        {
            if (!Prefs.DevMode || faction == null || !IsEligibleFaction(faction))
            {
                return false;
            }

            factionRecords.RemoveAll(record => record.Faction == faction);
            GetInitialState(
                faction,
                !publicDeclarationBroadcast,
                out int trust,
                out SymbiosisPreDeclarationTrustLock lockState);
            factionRecords.Add(
                new SymbiosisCovenantFactionRecord(faction, trust, lockState));
            return true;
        }

        public bool DevBroadcastDeclaration()
        {
            if (!Prefs.DevMode)
            {
                return false;
            }

            return TryBroadcastPublicDeclaration();
        }

        public bool DevBeginProposal(Faction? faction)
        {
            if (!Prefs.DevMode)
            {
                return false;
            }

            return TryBeginCovenantProposal(faction);
        }

        public bool DevResolveProposal(
            Faction? faction,
            bool? forceOutcome)
        {
            if (!Prefs.DevMode)
            {
                return false;
            }

            SymbiosisCovenantFactionRecord? record = GetRecord(faction);
            if (record == null || !record.ProposalPending)
            {
                return false;
            }

            ResolveProposal(record, forceOutcome);
            return true;
        }

        public bool DevClearInvitationCooldown(Faction? faction)
        {
            if (!Prefs.DevMode)
            {
                return false;
            }

            SymbiosisCovenantFactionRecord? record = GetRecord(faction);
            if (record == null)
            {
                return false;
            }

            record.ClearInvitationCooldown();
            return true;
        }

        public bool DevSetInvitationFailureCount(Faction? faction, int value)
        {
            if (!Prefs.DevMode)
            {
                return false;
            }

            SymbiosisCovenantFactionRecord? record = GetRecord(faction);
            if (record == null)
            {
                return false;
            }

            record.DevSetInvitationFailureCount(value);
            return true;
        }

        public bool DevSetCovenantExitCount(Faction? faction, int value)
        {
            if (!Prefs.DevMode)
            {
                return false;
            }

            SymbiosisCovenantFactionRecord? record = GetRecord(faction);
            if (record == null)
            {
                return false;
            }

            record.DevSetCovenantExitCount(value);
            return true;
        }

        public bool DevForceJoinCovenant(Faction? faction)
        {
            if (!Prefs.DevMode)
            {
                return false;
            }

            SymbiosisCovenantFactionRecord? record = GetRecord(faction);
            if (record == null || record.CovenantMember)
            {
                return false;
            }

            JoinCovenant(record);
            return true;
        }

        public bool DevForceLeaveCovenant(Faction? faction)
        {
            if (!Prefs.DevMode)
            {
                return false;
            }

            SymbiosisCovenantFactionRecord? record = GetRecord(faction);
            if (record == null || !record.CovenantMember)
            {
                return false;
            }

            LeaveCovenant(
                record,
                "MAP_MechanoidMechanitor.Symbiosis.TrustReason.Dev".Translate());
            return true;
        }

        public void DevChangeUnity(float delta)
        {
            if (!Prefs.DevMode)
            {
                return;
            }

            unity = Mathf.Clamp(unity + delta, 0f, UnityMax);
            lastUnityUpdateTick = CurrentTick;
            RecalculateCovenantLevel();
        }

        public void DevSetUnity(float value)
        {
            if (!Prefs.DevMode)
            {
                return;
            }

            unity = Mathf.Clamp(value, 0f, UnityMax);
            lastUnityUpdateTick = CurrentTick;
            RecalculateCovenantLevel();
        }

        public void DevUpdateUnityDaily()
        {
            if (!Prefs.DevMode)
            {
                return;
            }

            ApplyDailyUnityDelta();
            lastUnityUpdateTick = CurrentTick;
        }

        public void DevRecalculateCovenantLevel()
        {
            if (!Prefs.DevMode)
            {
                return;
            }

            RecalculateCovenantLevel();
        }

        // ===== 内部逻辑 =====

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

            // 声明前锁定期间不接受任何普通信任变化。
            if (!publicDeclarationBroadcast
                && record.PreDeclarationLock != SymbiosisPreDeclarationTrustLock.None)
            {
                return false;
            }

            int previousTrust = record.Trust;
            int actual = record.AdjustTrust(
                amount,
                reason,
                source,
                CurrentTick);
            if (actual == 0)
            {
                return false;
            }

            HandleTrustChangeForRecord(record, previousTrust);
            return true;
        }

        private void HandleTrustChangeForRecord(
            SymbiosisCovenantFactionRecord record,
            int previousTrust)
        {
            if (record.CovenantMember && record.Trust < -50)
            {
                LeaveCovenant(
                    record,
                    "MAP_MechanoidMechanitor.Symbiosis.TrustReason.Betrayal".Translate());
            }

            bool crossedFifty =
                (previousTrust >= 50) != (record.Trust >= 50);
            if (crossedFifty)
            {
                RecalculateGoodwillSituations();
            }
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
                    GetInitialState(
                        faction,
                        !publicDeclarationBroadcast,
                        out int trust,
                        out SymbiosisPreDeclarationTrustLock lockState);
                    factionRecords.Add(
                        new SymbiosisCovenantFactionRecord(faction, trust, lockState));
                }
            }

            if (!initialized)
            {
                initialized = true;
            }

            if (!publicDeclarationBroadcast)
            {
                for (int i = 0; i < factionRecords.Count; i++)
                {
                    SynchronizePreDeclarationLock(factionRecords[i]);
                }
            }
            else
            {
                for (int i = 0; i < factionRecords.Count; i++)
                {
                    factionRecords[i].SetPreDeclarationLock(
                        SymbiosisPreDeclarationTrustLock.None);
                }

                CalibrateMechHiveHostility();
            }

            ResolveDueProposals();

            for (int i = 0; i < factionRecords.Count; i++)
            {
                SymbiosisCovenantFactionRecord record = factionRecords[i];
                if (record.CovenantMember && record.Trust < -50)
                {
                    LeaveCovenant(
                        record,
                        "MAP_MechanoidMechanitor.Symbiosis.TrustReason.Betrayal"
                            .Translate());
                }
            }

            EnsureNeutralAmongMembers();
            UpdateUnityIfDue();

            if (CovenantMemberCount == 0)
            {
                unity = 0f;
                covenantLevel = 0;
            }

            RecalculateCovenantLevel();
        }

        private void ResolveDueProposals()
        {
            for (int i = 0; i < factionRecords.Count; i++)
            {
                SymbiosisCovenantFactionRecord record = factionRecords[i];
                if (record.ProposalPending && record.ProposalResolutionTick <= CurrentTick)
                {
                    ResolveProposal(record, null);
                }
            }
        }

        private void ResolveProposal(
            SymbiosisCovenantFactionRecord record,
            bool? forceOutcome)
        {
            if (!record.ProposalPending)
            {
                return;
            }

            // 先清除结算时间，避免信件或后续代码异常时重复结算。
            record.ClearProposal();

            int currentGoodwill = 0;
            if (Faction.OfPlayerSilentFail != null && record.Faction != null)
            {
                currentGoodwill =
                    Faction.OfPlayerSilentFail.GoodwillWith(record.Faction);
            }

            int baseChance = Math.Max(
                0,
                75 - 25 * record.InvitationFailureCount);
            int successChance = Mathf.Clamp(baseChance + currentGoodwill, 0, 100);

            bool success = forceOutcome.HasValue
                ? forceOutcome.Value
                : Rand.Chance(successChance / 100f);

            if (success)
            {
                JoinCovenant(record);
            }
            else
            {
                FailProposal(record);
            }
        }

        private void JoinCovenant(SymbiosisCovenantFactionRecord record)
        {
            if (record.CovenantMember)
            {
                return;
            }

            record.CovenantMember = true;
            record.SetTrustDirect(
                record.CovenantExitCount > 0 ? MemberRejoinTrust : MemberJoinTrust,
                "MAP_MechanoidMechanitor.Symbiosis.TrustReason.JoinCovenant".Translate(),
                CurrentTick);
            record.ClearProposal();
            record.ClearInvitationCooldown();

            RecalculateGoodwillSituations();
            EnsureNeutralAmongMembers();
            RecalculateCovenantLevel();

            if (record.Faction != null)
            {
                Find.LetterStack.ReceiveLetter(
                    "MAP_MechanoidMechanitor.Symbiosis.Covenant.Join.Letter.Label"
                        .Translate(record.Faction.Name),
                    "MAP_MechanoidMechanitor.Symbiosis.Covenant.Join.Letter.Text"
                        .Translate(record.Faction.Name),
                    LetterDefOf.PositiveEvent,
                    null,
                    record.Faction);
            }
        }

        private void FailProposal(SymbiosisCovenantFactionRecord record)
        {
            record.IncrementInvitationFailureCount();
            record.SetNextInvitationTick(CurrentTick + InvitationCooldownTicks);

            if (record.Faction != null)
            {
                Find.LetterStack.ReceiveLetter(
                    "MAP_MechanoidMechanitor.Symbiosis.Covenant.Refuse.Letter.Label"
                        .Translate(record.Faction.Name),
                    "MAP_MechanoidMechanitor.Symbiosis.Covenant.Refuse.Letter.Text"
                        .Translate(record.Faction.Name),
                    LetterDefOf.NegativeEvent,
                    null,
                    record.Faction);
            }
        }

        private void LeaveCovenant(
            SymbiosisCovenantFactionRecord record,
            string reason)
        {
            if (!record.CovenantMember)
            {
                return;
            }

            record.CovenantMember = false;
            record.IncrementCovenantExitCount();
            record.SetNextInvitationTick(CurrentTick + InvitationCooldownTicks);

            unity = Mathf.Max(0f, unity - CovenantExitUnityPenalty);
            RecalculateGoodwillSituations();

            int memberCount = CovenantMemberCount;
            RecalculateCovenantLevel();

            if (record.Faction != null)
            {
                Find.LetterStack.ReceiveLetter(
                    "MAP_MechanoidMechanitor.Symbiosis.Covenant.Exit.Letter.Label"
                        .Translate(record.Faction.Name),
                    "MAP_MechanoidMechanitor.Symbiosis.Covenant.Exit.Letter.Text"
                        .Translate(record.Faction.Name),
                    LetterDefOf.NegativeEvent,
                    null,
                    record.Faction);
            }

            if (memberCount == 0)
            {
                unity = 0f;
                covenantLevel = 0;
                Find.LetterStack.ReceiveLetter(
                    "MAP_MechanoidMechanitor.Symbiosis.Covenant.Dissolved.Letter.Label"
                        .Translate(),
                    "MAP_MechanoidMechanitor.Symbiosis.Covenant.Dissolved.Letter.Text"
                        .Translate(),
                    LetterDefOf.NegativeEvent);
            }
        }

        private void EnsureNeutralAmongMembers()
        {
            List<Faction> members = factionRecords
                .Where(record => record.CovenantMember && record.Faction != null)
                .Select(record => record.Faction)
                .Cast<Faction>()
                .ToList();
            for (int i = 0; i < members.Count; i++)
            {
                for (int j = i + 1; j < members.Count; j++)
                {
                    EnsureNeutralBetweenCovenantMembers(members[i], members[j]);
                }
            }
        }

        private static bool EnsureNeutralBetweenCovenantMembers(
            Faction first,
            Faction second)
        {
            if (first == null || second == null || first == second)
            {
                return false;
            }

            FactionRelation? firstRelation = first.RelationWith(second, allowNull: true);
            FactionRelation? secondRelation = second.RelationWith(first, allowNull: true);
            if (firstRelation == null || secondRelation == null)
            {
                first.TryMakeInitialRelationsWith(second);
                firstRelation = first.RelationWith(second, allowNull: true);
                secondRelation = second.RelationWith(first, allowNull: true);
                if (firstRelation == null || secondRelation == null)
                {
                    return false;
                }
            }

            FactionRelationKind firstPrevious = firstRelation.kind;
            FactionRelationKind secondPrevious = secondRelation.kind;

            int requiredGoodwill = Math.Max(
                0,
                Math.Max(firstRelation.baseGoodwill, secondRelation.baseGoodwill));
            firstRelation.baseGoodwill = requiredGoodwill;
            secondRelation.baseGoodwill = requiredGoodwill;

            FactionRelationKind firstTarget = firstRelation.kind == FactionRelationKind.Hostile
                ? FactionRelationKind.Neutral
                : firstRelation.kind;
            FactionRelationKind secondTarget = secondRelation.kind == FactionRelationKind.Hostile
                ? FactionRelationKind.Neutral
                : secondRelation.kind;
            firstRelation.kind = firstTarget;
            secondRelation.kind = secondTarget;

            if (firstPrevious != firstTarget)
            {
                first.Notify_RelationKindChanged(
                    second,
                    firstPrevious,
                    canSendLetter: false,
                    reason: null,
                    GlobalTargetInfo.Invalid,
                    out _);
            }

            if (secondPrevious != secondTarget)
            {
                second.Notify_RelationKindChanged(
                    first,
                    secondPrevious,
                    canSendLetter: false,
                    reason: null,
                    GlobalTargetInfo.Invalid,
                    out _);
            }

            return true;
        }

        private void UpdateUnityIfDue()
        {
            if (lastUnityUpdateTick < 0)
            {
                lastUnityUpdateTick = CurrentTick;
                return;
            }

            if (CurrentTick - lastUnityUpdateTick >= UnityUpdateIntervalTicks)
            {
                lastUnityUpdateTick = CurrentTick;
                ApplyDailyUnityDelta();
            }
        }

        private void ApplyDailyUnityDelta()
        {
            float delta = 0f;
            for (int i = 0; i < factionRecords.Count; i++)
            {
                SymbiosisCovenantFactionRecord record = factionRecords[i];
                if (!record.CovenantMember)
                {
                    continue;
                }

                int trust = record.Trust;
                if (trust > 50)
                {
                    delta += (trust - 50) / 10f;
                }
                else if (trust < 0)
                {
                    delta -= (-trust) / 5f;
                }
            }

            unity = Mathf.Clamp(unity + delta, 0f, UnityMax);
            RecalculateCovenantLevel();
        }

        private void RecalculateCovenantLevel()
        {
            int previousLevel = covenantLevel;
            int memberCount = CovenantMemberCount;

            int level = 0;
            if (memberCount > 0)
            {
                level = 1;
                if (unity >= 700) level = 5;
                else if (unity >= 450) level = 4;
                else if (unity >= 250) level = 3;
                else if (unity >= 100) level = 2;
            }

            covenantLevel = level;
            highestCovenantLevel = Math.Max(highestCovenantLevel, level);
            if (previousLevel != level)
            {
                NotifyCovenantLevelChanged(previousLevel, level);
            }
        }

        private void NotifyCovenantLevelChanged(int previousLevel, int newLevel)
        {
            // 本次不实现具体等级奖励，仅保留未来扩展入口。
        }

        private void RecalculateGoodwillSituations()
        {
            if (Current.Game == null || Find.GoodwillSituationManager == null)
            {
                return;
            }

            Find.GoodwillSituationManager.RecalculateAll(
                canSendHostilityChangedLetter: false);
        }

        private void CalibrateMechHiveHostility()
        {
            if (!IsActive || !publicDeclarationBroadcast)
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

        private void SynchronizePreDeclarationLock(
            SymbiosisCovenantFactionRecord record)
        {
            if (record.PreDeclarationLock == SymbiosisPreDeclarationTrustLock.None)
            {
                return;
            }

            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null || record.Faction == null)
            {
                return;
            }

            FactionRelation? relation = player.RelationWith(record.Faction, allowNull: true);
            SymbiosisPreDeclarationTrustLock current = relation?.kind switch
            {
                FactionRelationKind.Ally => SymbiosisPreDeclarationTrustLock.None,
                FactionRelationKind.Neutral => SymbiosisPreDeclarationTrustLock.Neutral,
                _ => SymbiosisPreDeclarationTrustLock.Hostile
            };

            TightenPreDeclarationLock(record, current);
        }

        private void TightenPreDeclarationLock(
            SymbiosisCovenantFactionRecord record,
            SymbiosisPreDeclarationTrustLock targetLock)
        {
            if (LockSeverity(targetLock) <= LockSeverity(record.PreDeclarationLock))
            {
                return;
            }

            SymbiosisPreDeclarationTrustLock strictest =
                LockSeverity(targetLock) == 2
                    ? SymbiosisPreDeclarationTrustLock.Hostile
                    : SymbiosisPreDeclarationTrustLock.Neutral;

            int previousTrust = record.Trust;
            record.SetPreDeclarationLock(strictest);
            record.SetTrustDirect(
                GetLockedTrustValue(strictest),
                "MAP_MechanoidMechanitor.Symbiosis.TrustReason.PreDeclarationLock"
                    .Translate(),
                CurrentTick);
            HandleTrustChangeForRecord(record, previousTrust);
        }

        private static int GetLockedTrustValue(SymbiosisPreDeclarationTrustLock trustLock)
        {
            switch (trustLock)
            {
                case SymbiosisPreDeclarationTrustLock.Hostile:
                    return MinimumTrust;
                case SymbiosisPreDeclarationTrustLock.Neutral:
                    return -25;
                default:
                    return 0;
            }
        }

        private static int LockSeverity(SymbiosisPreDeclarationTrustLock value)
        {
            switch (value)
            {
                case SymbiosisPreDeclarationTrustLock.Hostile:
                    return 2;
                case SymbiosisPreDeclarationTrustLock.Neutral:
                    return 1;
                default:
                    return 0;
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

        private static void GetInitialState(
            Faction faction,
            bool preDeclaration,
            out int trust,
            out SymbiosisPreDeclarationTrustLock lockState)
        {
            Faction? player = Faction.OfPlayerSilentFail;
            FactionRelation? relation = player?.RelationWith(faction, allowNull: true);
            FactionRelationKind kind = relation?.kind ?? FactionRelationKind.Hostile;

            switch (kind)
            {
                case FactionRelationKind.Ally:
                    trust = 50;
                    break;
                case FactionRelationKind.Neutral:
                    trust = preDeclaration ? -25 : 0;
                    break;
                default:
                    trust = MinimumTrust;
                    break;
            }

            if (!preDeclaration)
            {
                lockState = SymbiosisPreDeclarationTrustLock.None;
            }
            else if (kind == FactionRelationKind.Ally)
            {
                lockState = SymbiosisPreDeclarationTrustLock.None;
            }
            else if (kind == FactionRelationKind.Neutral)
            {
                lockState = SymbiosisPreDeclarationTrustLock.Neutral;
            }
            else
            {
                lockState = SymbiosisPreDeclarationTrustLock.Hostile;
            }
        }

        public static string GetStageLabel(int trust)
        {
            if (trust >= 200)
            {
                return "MAP_MechanoidMechanitor.Symbiosis.Stage.ReadyToSign"
                    .Translate();
            }

            if (trust >= 150)
            {
                return "MAP_MechanoidMechanitor.Symbiosis.Stage.StrategicCooperation"
                    .Translate();
            }

            if (trust >= 100)
            {
                return "MAP_MechanoidMechanitor.Symbiosis.Stage.CommonDefense"
                    .Translate();
            }

            if (trust >= 50)
            {
                return "MAP_MechanoidMechanitor.Symbiosis.Stage.Ceasefire".Translate();
            }

            if (trust >= 1)
            {
                return "MAP_MechanoidMechanitor.Symbiosis.Stage.SecretContact"
                    .Translate();
            }

            if (trust >= -50)
            {
                return "MAP_MechanoidMechanitor.Symbiosis.Stage.WaryObservation"
                    .Translate();
            }

            return "MAP_MechanoidMechanitor.Symbiosis.Stage.Rejected".Translate();
        }

        public static int ClampTrust(int trust)
        {
            return Math.Max(MinimumTrust, Math.Min(MaximumTrust, trust));
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
            Scribe_Values.Look(ref unity, "unity", 0f);
            Scribe_Values.Look(ref covenantLevel, "covenantLevel", 0);
            Scribe_Values.Look(ref highestCovenantLevel, "highestCovenantLevel", 0);
            Scribe_Values.Look(ref lastUnityUpdateTick, "lastUnityUpdateTick", -1);
            Scribe_Collections.Look(
                ref factionRecords,
                "factionRecords",
                LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                factionRecords ??= new List<SymbiosisCovenantFactionRecord>();
                factionRecords.RemoveAll(record => record == null);
                unity = Mathf.Clamp(unity, 0f, UnityMax);
                covenantLevel = Mathf.Clamp(covenantLevel, 0, CovenantLevelMax);
                highestCovenantLevel = Mathf.Clamp(
                    highestCovenantLevel,
                    0,
                    CovenantLevelMax);
                if (lastUnityUpdateTick < 0)
                {
                    lastUnityUpdateTick = -1;
                }
            }
        }
    }
}
