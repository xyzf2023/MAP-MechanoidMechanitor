using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorPurgeDirectiveRuntimeState : IExposable
    {
        public const int ProtocolCheckIntervalTicks = 15000;

        public const int FinalRaidDelayTicks = 2500;

        public const int FinalizationRetryIntervalTicks = 250;

        public const int HumanlikeDeathRewardPoints = 15;

        public const int OtherFactionBaseDestroyedRewardPoints = 200;

        private int purgeDirectiveRewardPoints;

        private int nextPurgeDirectiveCheckTick;

        private int nextPurgeDirectiveFinalizationRetryTick;

        private List<Pawn> purgeDirectiveTrackedPawns = new List<Pawn>();

        private int purgeDirectiveOrangeWarningsSent;

        private bool purgeDirectiveFinalPenaltyTriggered;

        private bool purgeDirectiveRaidQueued;

        private bool contactOvermindUnlockedLetterSent;

        private string? lastFinalizationFailureReason;

        // ===== 肃清指令节点评级（与肃清额度机制并存、互不替代） =====
        // 评级值与肃清额度分别存储；额度增减不自动改变评级，仅在统一奖励入口同步增加等量评级。
        private int purgeDirectiveRatingValue;          // 0 ~ 4000 钳制
        private bool purgeDirectiveRatingInitialized;   // 旧存档迁移标记
        // 世界目标基础奖励（如摧毁据点 200 点）按稳定ID持久化去重，防止重复结算。
        private List<string> purgeDirectiveAwardedBaseRewardIds = new List<string>();
        // 肃清评级任务调度状态：下一次每日检查时间 / 冷却到期时间，随主脑存档。
        private int nextPurgeQuestCheckTick = -1;
        private int purgeQuestCooldownEndTick;

        public int RewardPoints => purgeDirectiveRewardPoints;

        public int PurgeDirectiveRatingValue => purgeDirectiveRatingValue;

        public bool PurgeDirectiveRatingInitialized => purgeDirectiveRatingInitialized;

        public int NextPurgeQuestCheckTick => nextPurgeQuestCheckTick;

        public int PurgeQuestCooldownEndTick => purgeQuestCooldownEndTick;

        public int NextCheckTick => nextPurgeDirectiveCheckTick;

        public int NextFinalizationRetryTick => nextPurgeDirectiveFinalizationRetryTick;

        public List<Pawn> TrackedPawns => purgeDirectiveTrackedPawns;

        public int OrangeWarningsSent => purgeDirectiveOrangeWarningsSent;

        public bool FinalPenaltyTriggered => purgeDirectiveFinalPenaltyTriggered;

        public bool RaidQueued => purgeDirectiveRaidQueued;

        public bool ContactOvermindUnlockedLetterSent => contactOvermindUnlockedLetterSent;

        // ===== 评级访问器（钳制由统一工具负责，这里仅提供持久字段读写） =====

        public int RatingValue
        {
            get => purgeDirectiveRatingValue;
            set => purgeDirectiveRatingValue = value;
        }

        public void SetRatingValue(int value)
        {
            purgeDirectiveRatingValue = value;
        }

        public bool HasAwardedBaseReward(string stableId)
        {
            return stableId != null && purgeDirectiveAwardedBaseRewardIds.Contains(stableId);
        }

        public void MarkBaseRewardAwarded(string stableId)
        {
            if (stableId != null && !purgeDirectiveAwardedBaseRewardIds.Contains(stableId))
            {
                purgeDirectiveAwardedBaseRewardIds.Add(stableId);
            }
        }

        public void SetNextPurgeQuestCheckTick(int tick)
        {
            nextPurgeQuestCheckTick = tick;
        }

        public void SetPurgeQuestCooldownEndTick(int tick)
        {
            purgeQuestCooldownEndTick = tick;
        }

        public void InitializeForNewGame(bool purgeDirectiveEnabled)
        {
            purgeDirectiveRewardPoints = 0;
            purgeDirectiveTrackedPawns = new List<Pawn>();
            purgeDirectiveOrangeWarningsSent = 0;
            purgeDirectiveFinalPenaltyTriggered = false;
            purgeDirectiveRaidQueued = false;
            contactOvermindUnlockedLetterSent = false;
            nextPurgeDirectiveFinalizationRetryTick = 0;
            lastFinalizationFailureReason = null;
            nextPurgeDirectiveCheckTick = purgeDirectiveEnabled
                ? Find.TickManager.TicksGame + ProtocolCheckIntervalTicks
                : 0;

            // 新游戏：评级清零并标记已初始化，不依赖旧存档迁移路径。
            purgeDirectiveRatingValue = 0;
            purgeDirectiveRatingInitialized = true;
            purgeDirectiveAwardedBaseRewardIds = new List<string>();
            nextPurgeQuestCheckTick = -1;
            purgeQuestCooldownEndTick = 0;
        }

        public void MarkContactOvermindUnlockedLetterSent()
        {
            contactOvermindUnlockedLetterSent = true;
        }

        public bool TryNoteNewFinalizationFailureReason(string reason)
        {
            if (lastFinalizationFailureReason == reason)
            {
                return false;
            }

            lastFinalizationFailureReason = reason;
            return true;
        }

        public void ClearFinalizationFailureReason()
        {
            lastFinalizationFailureReason = null;
        }

        public void AddRewardPoints(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            purgeDirectiveRewardPoints += amount;
        }

        public bool TrySpendCredits(int amount)
        {
            if (amount < 0 || purgeDirectiveRewardPoints < amount)
            {
                return false;
            }

            purgeDirectiveRewardPoints -= amount;
            return true;
        }

        public bool RefundCredits(int amount)
        {
            if (amount < 0)
            {
                return false;
            }

            long next = (long)purgeDirectiveRewardPoints + amount;
            if (next > int.MaxValue)
            {
                return false;
            }

            purgeDirectiveRewardPoints = (int)next;
            return true;
        }

        public void SetTrackedPawns(List<Pawn> pawns)
        {
            purgeDirectiveTrackedPawns.Clear();
            if (pawns == null)
            {
                return;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn? pawn = pawns[i];
                if (pawn == null || purgeDirectiveTrackedPawns.Contains(pawn))
                {
                    continue;
                }

                purgeDirectiveTrackedPawns.Add(pawn);
            }
        }

        public void ClearTrackedPawns()
        {
            purgeDirectiveTrackedPawns.Clear();
        }

        public void RetainAliveTrackedPawns(List<Pawn> aliveTrackedPawns)
        {
            SetTrackedPawns(aliveTrackedPawns);
        }

        public void SetOrangeWarningsSent(int count)
        {
            if (count < 0)
            {
                count = 0;
            }

            if (count > 2)
            {
                count = 2;
            }

            purgeDirectiveOrangeWarningsSent = count;
        }

        public void MarkFinalPenaltyTriggered()
        {
            purgeDirectiveFinalPenaltyTriggered = true;
        }

        public void MarkRaidQueued()
        {
            purgeDirectiveRaidQueued = true;
        }

        public void ScheduleNextCheckFromNow()
        {
            nextPurgeDirectiveCheckTick =
                Find.TickManager.TicksGame + ProtocolCheckIntervalTicks;
        }

        public void ScheduleFinalizationRetryFromNow()
        {
            nextPurgeDirectiveFinalizationRetryTick =
                Find.TickManager.TicksGame + FinalizationRetryIntervalTicks;
        }

        public void ClearFinalizationRetryTick()
        {
            nextPurgeDirectiveFinalizationRetryTick = 0;
        }

        public void CleanupTrackedPawns()
        {
            if (purgeDirectiveTrackedPawns == null)
            {
                purgeDirectiveTrackedPawns = new List<Pawn>();
                return;
            }

            HashSet<Pawn> seen = new HashSet<Pawn>();
            for (int i = purgeDirectiveTrackedPawns.Count - 1; i >= 0; i--)
            {
                Pawn? pawn = purgeDirectiveTrackedPawns[i];
                if (pawn == null || !seen.Add(pawn))
                {
                    purgeDirectiveTrackedPawns.RemoveAt(i);
                }
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(
                ref purgeDirectiveRewardPoints,
                "purgeDirectiveRewardPoints",
                0);
            Scribe_Values.Look(
                ref nextPurgeDirectiveCheckTick,
                "nextPurgeDirectiveCheckTick",
                0);
            Scribe_Values.Look(
                ref nextPurgeDirectiveFinalizationRetryTick,
                "nextPurgeDirectiveFinalizationRetryTick",
                0);
            Scribe_Collections.Look(
                ref purgeDirectiveTrackedPawns,
                "purgeDirectiveTrackedPawns",
                LookMode.Reference);
            Scribe_Values.Look(
                ref purgeDirectiveOrangeWarningsSent,
                "purgeDirectiveOrangeWarningsSent",
                0);
            Scribe_Values.Look(
                ref purgeDirectiveFinalPenaltyTriggered,
                "purgeDirectiveFinalPenaltyTriggered",
                false);
            Scribe_Values.Look(
                ref purgeDirectiveRaidQueued,
                "purgeDirectiveRaidQueued",
                false);
            Scribe_Values.Look(
                ref contactOvermindUnlockedLetterSent,
                "contactOvermindUnlockedLetterSent",
                false);

            // ===== 肃清指令节点评级持久化字段 =====
            Scribe_Values.Look(
                ref purgeDirectiveRatingValue,
                "purgeDirectiveRatingValue",
                0);
            Scribe_Values.Look(
                ref purgeDirectiveRatingInitialized,
                "purgeDirectiveRatingInitialized",
                false);
            Scribe_Collections.Look(
                ref purgeDirectiveAwardedBaseRewardIds,
                "purgeDirectiveAwardedBaseRewardIds",
                LookMode.Value);
            Scribe_Values.Look(
                ref nextPurgeQuestCheckTick,
                "nextPurgeQuestCheckTick",
                -1);
            Scribe_Values.Look(
                ref purgeQuestCooldownEndTick,
                "purgeQuestCooldownEndTick",
                0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (purgeDirectiveTrackedPawns == null)
                {
                    purgeDirectiveTrackedPawns = new List<Pawn>();
                }

                if (purgeDirectiveOrangeWarningsSent < 0)
                {
                    purgeDirectiveOrangeWarningsSent = 0;
                }
                else if (purgeDirectiveOrangeWarningsSent > 2)
                {
                    purgeDirectiveOrangeWarningsSent = 2;
                }

                // 评级字段迁移与校正。
                if (purgeDirectiveAwardedBaseRewardIds == null)
                {
                    purgeDirectiveAwardedBaseRewardIds = new List<string>();
                }

                if (nextPurgeQuestCheckTick < -1)
                {
                    nextPurgeQuestCheckTick = -1;
                }

                if (purgeQuestCooldownEndTick < 0)
                {
                    purgeQuestCooldownEndTick = 0;
                }

                int maxRating = PurgeDirectiveRatingConfigDefOf.MAP_PurgeDirectiveRatingConfig
                    ?.maxRatingValue ?? 4000;
                if (purgeDirectiveRatingValue < 0)
                {
                    purgeDirectiveRatingValue = 0;
                }
                else if (purgeDirectiveRatingValue > maxRating)
                {
                    purgeDirectiveRatingValue = maxRating;
                }

                // 旧存档兼容：评级系统新接入时从 0 开始（不把现有肃清额度折算为评级），
                // 仅注入默认值并标记已初始化，避免存档损坏或 NaN。
                if (!purgeDirectiveRatingInitialized)
                {
                    purgeDirectiveRatingValue = 0;
                    purgeDirectiveRatingInitialized = true;
                }

                CleanupTrackedPawns();
            }
        }
    }
}
