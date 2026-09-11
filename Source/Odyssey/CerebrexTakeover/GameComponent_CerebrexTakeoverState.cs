using System;
using System.Collections.Generic;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;
using Verse.AI;

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

        // 不存档的、当前游戏实例级别的待同步派系列表（接管主脑后的关系同步）。
        private readonly HashSet<Faction> pendingRelationSyncFactions = new HashSet<Faction>();
        private bool playerMechHiveRecalibrationRequested;

        // 主脑关系同步专用运行期工作刷新批次（不持久化、不跨游戏/存档残留）。
        private int relationJobRefreshBatchDepth;
        private readonly HashSet<Pawn_JobTracker> pendingJobRefreshTrackers = new HashSet<Pawn_JobTracker>();
        private bool relationJobRefreshFlushing;

        public bool TakeoverActive => takeoverActive;
        public int ResourceCredits => resourceCredits;
        public int TakeoverCompletedTick => takeoverCompletedTick;
        public Thing? PendingCore => pendingCore;
        public Pawn? PendingPawn => pendingPawn;

        public static GameComponent_CerebrexTakeoverState? Current =>
            CurrentGameComponentCache<GameComponent_CerebrexTakeoverState>.Get();

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
                CerebrexTakeoverRelationUtility.EnsureTakeoverRelations();
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

            CerebrexTakeoverRelationUtility.EnsureTakeoverRelations();

            return true;
        }

        public void ClearPendingTakeover()
        {
            pendingCore = null;
            pendingPawn = null;
        }

        internal void QueueMechHiveRelationSyncFor(Faction other)
        {
            if (!IsActive || other == null)
            {
                return;
            }

            pendingRelationSyncFactions.Add(other);
        }

        internal void RequestPlayerMechHiveRecalibration()
        {
            if (!IsActive)
            {
                return;
            }

            playerMechHiveRecalibrationRequested = true;
        }

        public bool IsRelationJobRefreshBatchActive => relationJobRefreshBatchDepth > 0;

        internal void EnterRelationJobRefreshBatch()
        {
            relationJobRefreshBatchDepth++;
        }

        internal void ExitRelationJobRefreshBatch()
        {
            if (relationJobRefreshBatchDepth <= 0)
            {
                return;
            }

            relationJobRefreshBatchDepth--;
            if (relationJobRefreshBatchDepth == 0)
            {
                FlushPendingJobRefreshes();
            }
        }

        internal void QueueJobRefreshFor(Pawn_JobTracker tracker)
        {
            if (tracker == null)
            {
                return;
            }

            pendingJobRefreshTrackers.Add(tracker);
        }

        private void FlushPendingJobRefreshes()
        {
            if (relationJobRefreshFlushing)
            {
                return;
            }

            // 非游玩阶段（如加载阶段）不宜启动工作：保留待刷新集合，
            // 待回到 Playing 状态后由 GameComponentTick 的安全处理点统一刷新。
            if (Verse.Current.ProgramState != ProgramState.Playing)
            {
                return;
            }

            // 复制并清空当前集合，避免枚举期间集合被修改。
            List<Pawn_JobTracker> snapshot = new List<Pawn_JobTracker>(pendingJobRefreshTrackers);
            pendingJobRefreshTrackers.Clear();

            relationJobRefreshFlushing = true;
            try
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    Pawn_JobTracker tracker = snapshot[i];
                    try
                    {
                        if (!CerebrexRelationJobRefreshBatch.IsTrackerValidForRefresh(tracker))
                        {
                            continue;
                        }

                        tracker.EndCurrentJob(JobCondition.InterruptForced, true, true);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(
                            "[MAP-机械族机械师] 主脑关系同步后刷新单位工作失败。\n" + ex);
                    }
                }
            }
            finally
            {
                relationJobRefreshFlushing = false;
            }
        }

        internal void ProcessPendingRelationSyncQueue()
        {
            if (!IsActive)
            {
                pendingRelationSyncFactions.Clear();
                playerMechHiveRecalibrationRequested = false;
                return;
            }

            if (pendingRelationSyncFactions.Count == 0 && !playerMechHiveRecalibrationRequested)
            {
                return;
            }

            using (CerebrexRelationJobRefreshBatch.Enter())
            {
                // 复制并清空当前队列，防止处理期间新通知破坏枚举。
                HashSet<Faction> batch = new HashSet<Faction>(pendingRelationSyncFactions);
                bool recalibrate = playerMechHiveRecalibrationRequested;
                pendingRelationSyncFactions.Clear();
                playerMechHiveRecalibrationRequested = false;

                if (recalibrate)
                {
                    CerebrexTakeoverRelationUtility.EnsureMutualAllies();
                }

                foreach (Faction other in batch)
                {
                    try
                    {
                        CerebrexTakeoverRelationUtility.AlignMechHiveRelationToPlayerRelation(other);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(
                            "[MAP-机械族机械师] 处理待同步机械巢关系失败："
                            + (other?.GetUniqueLoadID() ?? "null") + "\n" + ex);
                    }
                }
            }
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
            CerebrexTakeoverRelationUtility.EnsureTakeoverRelations();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (!takeoverActive || Find.TickManager == null)
            {
                return;
            }

            ProcessElapsedDays();
            using (CerebrexRelationJobRefreshBatch.Enter())
            {
                ProcessPendingRelationSyncQueue();
                if (Find.TickManager.TicksGame % RelationCalibrationIntervalTicks == 0)
                {
                    CerebrexTakeoverRelationUtility.EnsureTakeoverRelations();
                }
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

            using (CerebrexRelationJobRefreshBatch.Enter())
            {
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

        /// <summary>
        /// 接管主脑后的全量关系同步：先确保玩家—机械巢固定盟友，再把机械巢与其余派系
        /// 的关系精确同步为玩家与这些派系的三档关系。该方法会复用现有防重入标记。
        /// </summary>
        public static void EnsureTakeoverRelations()
        {
            using (CerebrexRelationJobRefreshBatch.Enter())
            {
                EnsureMutualAllies();
                AlignAllMechHiveRelationsToPlayer();
            }
        }

        /// <summary>
        /// 遍历世界中除玩家与机械巢自身外的每个有效派系，把它们与机械巢的关系同步为
        /// 玩家与这些派系的关系类别。新加入世界、尚未被即时捕获的派系由 2500 tick 的
        /// 全量兜底覆盖。
        /// </summary>
        public static void AlignAllMechHiveRelationsToPlayer()
        {
            if (!ModsConfig.OdysseyActive || Current.Game == null || applying)
            {
                return;
            }

            Faction? player = Faction.OfPlayerSilentFail;
            Faction? mechHive = MechHive;
            if (player == null || mechHive == null)
            {
                return;
            }

            if (!GameComponent_CerebrexTakeoverState.IsActive)
            {
                return;
            }

            using (CerebrexRelationJobRefreshBatch.Enter())
            {
                foreach (Faction other in Find.FactionManager.AllFactionsListForReading)
                {
                    if (other == null || other == player || other == mechHive)
                    {
                        continue;
                    }

                    try
                    {
                        AlignMechHiveRelationToPlayerRelation(other);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(
                            "[MAP-机械族机械师] 校准机械巢与派系关系失败："
                            + (other?.GetUniqueLoadID() ?? "null") + "\n" + ex);
                    }
                }
            }
        }

        /// <summary>
        /// 把机械巢与目标派系的关系双向同步为玩家与目标派系当前的 FactionRelationKind。
        /// 只写入 FactionRelationKind，不复制 baseGoodwill，也不改玩家与其余派系本身的关系。
        /// </summary>
        public static void AlignMechHiveRelationToPlayerRelation(Faction other)
        {
            if (!ModsConfig.OdysseyActive || Current.Game == null || !GameComponent_CerebrexTakeoverState.IsActive)
            {
                return;
            }

            Faction? player = Faction.OfPlayerSilentFail;
            Faction? mechHive = MechHive;
            if (player == null || mechHive == null || other == null)
            {
                return;
            }

            if (ReferenceEquals(other, player) || ReferenceEquals(other, mechHive))
            {
                return;
            }

            if (!TryGetPlayerRelationTemplate(other, out FactionRelationKind playerKind))
            {
                return;
            }

            if (applying)
            {
                return;
            }

            using (CerebrexRelationJobRefreshBatch.Enter())
            {
                applying = true;
            try
            {
                EnsureBidirectionalMechHiveRelation(mechHive, other);

                FactionRelation? mhRel = mechHive.RelationWith(other, allowNull: true);
                FactionRelation? oRel = other.RelationWith(mechHive, allowNull: true);
                if (mhRel == null || oRel == null)
                {
                    return;
                }

                FactionRelationKind previousMh = mhRel.kind;
                FactionRelationKind previousOther = oRel.kind;

                mhRel.kind = playerKind;
                oRel.kind = playerKind;

                if (previousMh != playerKind)
                {
                    MechanoidMechanitorFactionRelationNotificationUtility.NotifySafely(
                        mechHive, other, previousMh, playerKind, "奥德赛主脑接管：机械巢侧");
                }

                if (previousOther != playerKind)
                {
                    MechanoidMechanitorFactionRelationNotificationUtility.NotifySafely(
                        other, mechHive, previousOther, playerKind, "奥德赛主脑接管：派系侧");
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 同步机械巢与派系关系失败："
                    + (other?.GetUniqueLoadID() ?? "null") + "\n" + ex);
            }
            finally
            {
                applying = false;
            }
            }
        }

        /// <summary>
        /// 取得玩家与目标派系当前的关系类别；玩家与该派系尚未建立关系记录时返回 false，
        /// 此时不改动机械巢与该派系的关系。
        /// </summary>
        public static bool TryGetPlayerRelationTemplate(Faction other, out FactionRelationKind kind)
        {
            kind = FactionRelationKind.Neutral;
            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null || other == null || ReferenceEquals(other, player))
            {
                return false;
            }

            FactionRelation? relation = player.RelationWith(other, allowNull: true);
            if (relation == null)
            {
                return false;
            }

            kind = relation.kind;
            return true;
        }

        /// <summary>
        /// 安全补齐机械巢与目标派系之间的双向 FactionRelation 记录。部分隐藏派系可能只与
        /// 玩家一方建立了关系，需在此补齐缺失方向后再写入，避免假定记录一定存在。
        /// </summary>
        private static void EnsureBidirectionalMechHiveRelation(Faction mechHive, Faction other)
        {
            FactionRelation? a = mechHive.RelationWith(other, allowNull: true);
            FactionRelation? b = other.RelationWith(mechHive, allowNull: true);
            if (a != null && b != null)
            {
                return;
            }

            if (a == null && b == null)
            {
                mechHive.TryMakeInitialRelationsWith(other);
                return;
            }

            if (a != null)
            {
                other.SetRelation(new FactionRelation
                {
                    other = mechHive,
                    kind = a.kind,
                    baseGoodwill = a.baseGoodwill
                });
            }
            else
            {
                mechHive.SetRelation(new FactionRelation
                {
                    other = other,
                    kind = b!.kind,
                    baseGoodwill = b.baseGoodwill
                });
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
