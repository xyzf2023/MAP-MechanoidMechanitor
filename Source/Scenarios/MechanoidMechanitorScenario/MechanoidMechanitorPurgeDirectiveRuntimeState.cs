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

        private int purgeDirectiveRewardPoints;

        private int nextPurgeDirectiveCheckTick;

        private int nextPurgeDirectiveFinalizationRetryTick;

        private List<Pawn> purgeDirectiveTrackedPawns = new List<Pawn>();

        private int purgeDirectiveOrangeWarningsSent;

        private bool purgeDirectiveFinalPenaltyTriggered;

        private bool purgeDirectiveRaidQueued;

        private string? lastFinalizationFailureReason;

        public int RewardPoints => purgeDirectiveRewardPoints;

        public int NextCheckTick => nextPurgeDirectiveCheckTick;

        public int NextFinalizationRetryTick => nextPurgeDirectiveFinalizationRetryTick;

        public List<Pawn> TrackedPawns => purgeDirectiveTrackedPawns;

        public int OrangeWarningsSent => purgeDirectiveOrangeWarningsSent;

        public bool FinalPenaltyTriggered => purgeDirectiveFinalPenaltyTriggered;

        public bool RaidQueued => purgeDirectiveRaidQueued;

        public void InitializeForNewGame(bool purgeDirectiveEnabled)
        {
            purgeDirectiveRewardPoints = 0;
            purgeDirectiveTrackedPawns = new List<Pawn>();
            purgeDirectiveOrangeWarningsSent = 0;
            purgeDirectiveFinalPenaltyTriggered = false;
            purgeDirectiveRaidQueued = false;
            nextPurgeDirectiveFinalizationRetryTick = 0;
            lastFinalizationFailureReason = null;
            nextPurgeDirectiveCheckTick = purgeDirectiveEnabled
                ? Find.TickManager.TicksGame + ProtocolCheckIntervalTicks
                : 0;
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

                CleanupTrackedPawns();
            }
        }
    }
}
