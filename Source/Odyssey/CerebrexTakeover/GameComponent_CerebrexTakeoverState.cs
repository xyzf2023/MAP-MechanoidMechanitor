using System;
using System.Collections.Generic;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 奥德赛主脑接管的独立存档状态。该状态不依赖机械族机械师剧本，
    /// 并在接管后成为主脑通讯资源额度的唯一权威来源。
    /// </summary>
    public sealed class GameComponent_CerebrexTakeoverState : GameComponent
    {
        public const int InitialCredits = 2000;
        public const int DailyCredits = 2000;
        public const int DailyCreditCap = 20000;
        private const int TicksPerDay = 60000;
        private const int RelationCalibrationIntervalTicks = 2500;

        private bool takeoverActive;
        private int resourceCredits;
        private int lastProcessedGameDay = -1;
        private int takeoverCompletedTick = -1;
        private Thing? pendingCore;
        private Pawn? pendingPawn;

        public bool TakeoverActive => takeoverActive;
        public int ResourceCredits => resourceCredits;
        public int TakeoverCompletedTick => takeoverCompletedTick;
        public Thing? PendingCore => pendingCore;
        public Pawn? PendingPawn => pendingPawn;

        public static GameComponent_CerebrexTakeoverState? Current
        {
            get
            {
                if (Verse.Current.Game == null)
                {
                    return null;
                }

                return Verse.Current.Game.GetComponent<GameComponent_CerebrexTakeoverState>();
            }
        }

        public static bool IsActive =>
            ModsConfig.OdysseyActive && Current?.takeoverActive == true;

        public GameComponent_CerebrexTakeoverState(Game game)
        {
        }

        public bool TryBeginTakeover(Thing core, Pawn pawn)
        {
            if (!ModsConfig.OdysseyActive
                || takeoverActive
                || core == null
                || core.Destroyed
                || pawn == null
                || pawn.Dead
                || pawn.Destroyed)
            {
                return false;
            }

            pendingCore = core;
            pendingPawn = pawn;
            return true;
        }

        public bool IsPendingFor(CompCerebrexCore? core)
        {
            return core?.parent != null
                && pendingCore != null
                && ReferenceEquals(pendingCore, core.parent);
        }

        public bool CompleteTakeover(CompCerebrexCore core, Pawn? pawn)
        {
            if (!ModsConfig.OdysseyActive || core?.parent == null)
            {
                return false;
            }

            if (takeoverActive)
            {
                pendingCore = null;
                pendingPawn = null;
                CerebrexTakeoverRelationUtility.EnsureMutualAllies();
                return true;
            }

            Pawn? resolvedPawn = pawn ?? pendingPawn;
            CerebrexTakeoverPurgeUtility.DisableLegacyPurgeState();

            takeoverActive = true;
            resourceCredits = InitialCredits;
            lastProcessedGameDay = CurrentGameDay();
            takeoverCompletedTick = Find.TickManager?.TicksGame ?? 0;
            pendingCore = null;
            pendingPawn = null;

            bool relationApplied = CerebrexTakeoverRelationUtility.EnsureMutualAllies();
            if (!relationApplied)
            {
                Log.Error(
                    "[MAP-机械族机械师] 主脑接管已写入存档，但机械巢盟友关系暂未成功应用。"
                    + "后续将按低频自动重试。操作者="
                    + (resolvedPawn?.LabelShort ?? "null"));
            }

            return true;
        }

        public void ClearPendingTakeover()
        {
            pendingCore = null;
            pendingPawn = null;
        }

        public bool TryAddCredits(int amount)
        {
            if (!takeoverActive || amount <= 0)
            {
                return false;
            }

            long next = (long)resourceCredits + amount;
            if (next > int.MaxValue)
            {
                return false;
            }

            resourceCredits = (int)next;
            return true;
        }

        public bool TrySpendCredits(int amount)
        {
            if (!takeoverActive || amount < 0 || resourceCredits < amount)
            {
                return false;
            }

            resourceCredits -= amount;
            return true;
        }

        public bool RefundCredits(int amount)
        {
            return TryAddCredits(amount);
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            if (!takeoverActive)
            {
                return;
            }

            pendingCore = null;
            pendingPawn = null;
            ProcessElapsedDays();
            CerebrexTakeoverRelationUtility.EnsureMutualAllies();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (!takeoverActive || Find.TickManager == null)
            {
                return;
            }

            ProcessElapsedDays();
            if (Find.TickManager.TicksGame % RelationCalibrationIntervalTicks == 0)
            {
                CerebrexTakeoverRelationUtility.EnsureMutualAllies();
            }
        }

        private void ProcessElapsedDays()
        {
            int currentDay = CurrentGameDay();
            if (lastProcessedGameDay < 0)
            {
                lastProcessedGameDay = currentDay;
                return;
            }

            if (currentDay <= lastProcessedGameDay)
            {
                return;
            }

            int elapsedDays = currentDay - lastProcessedGameDay;
            lastProcessedGameDay = currentDay;
            if (resourceCredits >= DailyCreditCap)
            {
                return;
            }

            long generated = (long)elapsedDays * DailyCredits;
            resourceCredits = (int)Math.Min(
                DailyCreditCap,
                (long)resourceCredits + generated);
        }

        private static int CurrentGameDay()
        {
            int ticks = Find.TickManager?.TicksGame ?? 0;
            return ticks / TicksPerDay;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref takeoverActive, "MAP_cerebrexTakeover_active", false);
            Scribe_Values.Look(ref resourceCredits, "MAP_cerebrexTakeover_credits", 0);
            Scribe_Values.Look(
                ref lastProcessedGameDay,
                "MAP_cerebrexTakeover_lastProcessedGameDay",
                -1);
            Scribe_Values.Look(
                ref takeoverCompletedTick,
                "MAP_cerebrexTakeover_completedTick",
                -1);
            Scribe_References.Look(ref pendingCore, "MAP_cerebrexTakeover_pendingCore");
            Scribe_References.Look(ref pendingPawn, "MAP_cerebrexTakeover_pendingPawn");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (resourceCredits < 0)
                {
                    resourceCredits = 0;
                }

                if (!takeoverActive)
                {
                    resourceCredits = 0;
                    takeoverCompletedTick = -1;
                }
            }
        }
    }

    internal static class CerebrexTakeoverRelationUtility
    {
        [ThreadStatic]
        private static bool applying;

        public static bool IsApplying => applying;

        public static Faction? MechHive => Faction.OfMechanoids;

        public static bool IsPlayerAndMechHivePair(Faction? a, Faction? b)
        {
            if (a == null || b == null || ReferenceEquals(a, b))
            {
                return false;
            }

            Faction? player = Faction.OfPlayerSilentFail;
            Faction? mechHive = MechHive;
            return player != null
                && mechHive != null
                && ((ReferenceEquals(a, player) && ReferenceEquals(b, mechHive))
                    || (ReferenceEquals(a, mechHive) && ReferenceEquals(b, player)));
        }

        public static bool AreMutualAllies()
        {
            Faction? player = Faction.OfPlayerSilentFail;
            Faction? mechHive = MechHive;
            if (player == null || mechHive == null)
            {
                return false;
            }

            FactionRelation? playerRelation = player.RelationWith(mechHive, allowNull: true);
            FactionRelation? mechRelation = mechHive.RelationWith(player, allowNull: true);
            return playerRelation?.kind == FactionRelationKind.Ally
                && mechRelation?.kind == FactionRelationKind.Ally
                && !mechHive.deactivated;
        }

        public static bool EnsureMutualAllies()
        {
            if (!ModsConfig.OdysseyActive || Current.Game == null || applying)
            {
                return false;
            }

            Faction? player = Faction.OfPlayerSilentFail;
            Faction? mechHive = MechHive;
            if (player == null || mechHive == null)
            {
                return false;
            }

            applying = true;
            try
            {
                FactionRelation? playerRelation = player.RelationWith(mechHive, allowNull: true);
                FactionRelation? mechRelation = mechHive.RelationWith(player, allowNull: true);
                FactionRelationKind previousPlayerKind =
                    playerRelation?.kind ?? FactionRelationKind.Neutral;
                FactionRelationKind previousMechKind =
                    mechRelation?.kind ?? FactionRelationKind.Neutral;

                if (playerRelation == null && mechRelation == null)
                {
                    player.TryMakeInitialRelationsWith(mechHive);
                }
                else if (playerRelation != null && mechRelation == null)
                {
                    player.SetRelation(new FactionRelation
                    {
                        other = mechHive,
                        kind = playerRelation.kind,
                        baseGoodwill = playerRelation.baseGoodwill
                    });
                }
                else if (playerRelation == null && mechRelation != null)
                {
                    mechHive.SetRelation(new FactionRelation
                    {
                        other = player,
                        kind = mechRelation.kind,
                        baseGoodwill = mechRelation.baseGoodwill
                    });
                }

                playerRelation = player.RelationWith(mechHive, allowNull: true);
                mechRelation = mechHive.RelationWith(player, allowNull: true);
                if (playerRelation == null || mechRelation == null)
                {
                    return false;
                }

                playerRelation.kind = FactionRelationKind.Ally;
                playerRelation.baseGoodwill = 100;
                mechRelation.kind = FactionRelationKind.Ally;
                mechRelation.baseGoodwill = 100;
                mechHive.factionHostileOnHarmByPlayer = false;
                mechHive.deactivated = false;

                if (previousPlayerKind != FactionRelationKind.Ally)
                {
                    MechanoidMechanitorFactionRelationNotificationUtility.NotifySafely(
                        player,
                        mechHive,
                        previousPlayerKind,
                        FactionRelationKind.Ally,
                        "奥德赛主脑接管：玩家侧");
                }

                if (previousMechKind != FactionRelationKind.Ally)
                {
                    MechanoidMechanitorFactionRelationNotificationUtility.NotifySafely(
                        mechHive,
                        player,
                        previousMechKind,
                        FactionRelationKind.Ally,
                        "奥德赛主脑接管：机械巢侧");
                }

                return AreMutualAllies();
            }
            catch (Exception ex)
            {
                Log.Error("[MAP-机械族机械师] 校准主脑接管后的机械巢盟友关系失败。\n" + ex);
                return false;
            }
            finally
            {
                applying = false;
            }
        }
    }

    internal static class CerebrexTakeoverPurgeUtility
    {
        private static readonly AccessTools.FieldRef<MechanoidMechanitorPurgeDirectiveRuntimeState, int>
            RewardPoints = AccessTools.FieldRefAccess<MechanoidMechanitorPurgeDirectiveRuntimeState, int>(
                "purgeDirectiveRewardPoints");

        private static readonly AccessTools.FieldRef<MechanoidMechanitorPurgeDirectiveRuntimeState, int>
            NextCheckTick = AccessTools.FieldRefAccess<MechanoidMechanitorPurgeDirectiveRuntimeState, int>(
                "nextPurgeDirectiveCheckTick");

        private static readonly AccessTools.FieldRef<MechanoidMechanitorPurgeDirectiveRuntimeState, int>
            NextRetryTick = AccessTools.FieldRefAccess<MechanoidMechanitorPurgeDirectiveRuntimeState, int>(
                "nextPurgeDirectiveFinalizationRetryTick");

        private static readonly AccessTools.FieldRef<MechanoidMechanitorPurgeDirectiveRuntimeState, List<Pawn>>
            TrackedPawns = AccessTools.FieldRefAccess<MechanoidMechanitorPurgeDirectiveRuntimeState, List<Pawn>>(
                "purgeDirectiveTrackedPawns");

        private static readonly AccessTools.FieldRef<MechanoidMechanitorPurgeDirectiveRuntimeState, int>
            OrangeWarnings = AccessTools.FieldRefAccess<MechanoidMechanitorPurgeDirectiveRuntimeState, int>(
                "purgeDirectiveOrangeWarningsSent");

        private static readonly AccessTools.FieldRef<MechanoidMechanitorPurgeDirectiveRuntimeState, bool>
            FinalPenalty = AccessTools.FieldRefAccess<MechanoidMechanitorPurgeDirectiveRuntimeState, bool>(
                "purgeDirectiveFinalPenaltyTriggered");

        private static readonly AccessTools.FieldRef<MechanoidMechanitorPurgeDirectiveRuntimeState, bool>
            RaidQueued = AccessTools.FieldRefAccess<MechanoidMechanitorPurgeDirectiveRuntimeState, bool>(
                "purgeDirectiveRaidQueued");

        private static readonly AccessTools.FieldRef<MechanoidMechanitorPurgeDirectiveRuntimeState, string>
            LastFailureReason = AccessTools.FieldRefAccess<MechanoidMechanitorPurgeDirectiveRuntimeState, string>(
                "lastFinalizationFailureReason");

        private static readonly AccessTools.FieldRef<IncidentQueue, List<QueuedIncident>>
            QueuedIncidents = AccessTools.FieldRefAccess<IncidentQueue, List<QueuedIncident>>(
                "queuedIncidents");

        public static void DisableLegacyPurgeState()
        {
            if (Current.Game == null)
            {
                return;
            }

            GameComponent_MechanoidMechanitorStoryState? storyState =
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            MechanoidMechanitorPurgeDirectiveRuntimeState? runtime =
                storyState?.PurgeDirectiveRuntimeState;
            if (runtime == null)
            {
                return;
            }

            try
            {
                if (RaidQueued(runtime))
                {
                    RemoveQueuedFinalRaid();
                }

                RewardPoints(runtime) = 0;
                NextCheckTick(runtime) = 0;
                NextRetryTick(runtime) = 0;
                TrackedPawns(runtime)?.Clear();
                OrangeWarnings(runtime) = 0;
                FinalPenalty(runtime) = false;
                RaidQueued(runtime) = false;
                LastFailureReason(runtime) = null!;
            }
            catch (Exception ex)
            {
                Log.Error("[MAP-机械族机械师] 清理旧肃清指令运行状态失败。\n" + ex);
            }
        }

        private static void RemoveQueuedFinalRaid()
        {
            IncidentQueue? queue = Find.Storyteller?.incidentQueue;
            Faction? mechHive = Faction.OfMechanoids;
            if (queue == null || mechHive == null)
            {
                return;
            }

            List<QueuedIncident>? incidents = QueuedIncidents(queue);
            if (incidents == null)
            {
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            for (int i = incidents.Count - 1; i >= 0; i--)
            {
                QueuedIncident? queued = incidents[i];
                FiringIncident? firing = queued?.FiringIncident;
                IncidentParms? parms = firing?.parms;
                if (firing?.def == IncidentDefOf.RaidEnemy
                    && parms?.faction == mechHive
                    && parms.forced
                    && queued!.FireTick >= now
                    && queued.FireTick <= now
                        + MechanoidMechanitorPurgeDirectiveRuntimeState.FinalRaidDelayTicks
                        + MechanoidMechanitorPurgeDirectiveRuntimeState.FinalizationRetryIntervalTicks)
                {
                    incidents.RemoveAt(i);
                    break;
                }
            }
        }
    }

    internal static class CerebrexTakeoverNodeScope
    {
        [ThreadStatic]
        private static int depth;

        public static bool Active => depth > 0;

        public static void Enter()
        {
            depth++;
        }

        public static void Exit()
        {
            if (depth > 0)
            {
                depth--;
            }
        }
    }
}
