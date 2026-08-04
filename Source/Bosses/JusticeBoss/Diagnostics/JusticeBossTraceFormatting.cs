using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    internal static class JusticeBossTraceFormatting
    {
        internal static string Sanitize(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "null";
            }

            string result = value!;
            if (result.IndexOf('\r') >= 0)
            {
                result = result.Replace('\r', ' ');
            }

            if (result.IndexOf('\n') >= 0)
            {
                result = result.Replace('\n', ' ');
            }

            if (result.IndexOf('\t') >= 0)
            {
                result = result.Replace('\t', ' ');
            }

            return result;
        }

        internal static string DescribeCell(IntVec3 cell)
        {
            return cell.x + ":" + cell.y + ":" + cell.z;
        }

        internal static string DescribeTarget(LocalTargetInfo target)
        {
            try
            {
                if (!target.IsValid)
                {
                    return "invalid";
                }

                Thing? thing = target.Thing;
                if (thing != null)
                {
                    return "thing:" + thing.thingIDNumber
                        + ":" + Sanitize(thing.def?.defName);
                }

                return "cell:" + DescribeCell(target.Cell);
            }
            catch (Exception exception)
            {
                return "describeException:"
                    + Sanitize(exception.GetType().FullName);
            }
        }

        internal static string DescribePawnState(Pawn? pawn)
        {
            if (pawn == null)
            {
                return "pawnState=null";
            }

            try
            {
                Pawn_JobTracker? jobs = pawn.jobs;
                StringBuilder builder = new StringBuilder(256);
                builder.Append("kind=").Append(Sanitize(pawn.kindDef?.defName));
                builder.Append(" race=").Append(Sanitize(pawn.def?.defName));
                builder.Append(" spawned=").Append(pawn.Spawned);
                builder.Append(" map=").Append(pawn.Map?.uniqueID ?? -1);
                builder.Append(" position=").Append(DescribeCell(pawn.Position));
                builder.Append(" dead=").Append(pawn.Dead);
                builder.Append(" downed=").Append(pawn.Downed);
                builder.Append(" job=").Append(
                    Sanitize(jobs?.curJob?.def?.defName));
                builder.Append(" jobDriver=").Append(
                    Sanitize(jobs?.curDriver?.GetType().FullName));
                builder.Append(" duty=").Append(
                    Sanitize(pawn.mindState?.duty?.def?.defName));
                return builder.ToString();
            }
            catch (Exception exception)
            {
                return "describeException="
                    + Sanitize(exception.GetType().FullName);
            }
        }

        internal static string DescribeJobTrackerState(Pawn_JobTracker? jobs)
        {
            if (jobs == null)
            {
                return "jobTracker=null";
            }

            try
            {
                Job? job = jobs.curJob;
                StringBuilder builder = new StringBuilder(256);
                builder.Append("job=").Append(Sanitize(job?.def?.defName));
                builder.Append(" jobLoadId=").Append(job?.loadID ?? -1);
                builder.Append(" jobStartTick=").Append(job?.startTick ?? -1);
                builder.Append(" jobDriver=").Append(
                    Sanitize(jobs.curDriver?.GetType().FullName));
                builder.Append(" queued=").Append(jobs.jobQueue?.Count ?? -1);
                builder.Append(" determiningNextJob=").Append(jobs.DeterminingNextJob);
                builder.Append(" startingNewJob=").Append(jobs.startingNewJob);
                builder.Append(" posture=").Append(jobs.posture);
                return builder.ToString();
            }
            catch (Exception exception)
            {
                return "describeException="
                    + Sanitize(exception.GetType().FullName);
            }
        }

        internal static string DescribeJobDriverState(
            JobDriver? driver,
            List<Toil>? toils)
        {
            if (driver == null)
            {
                return "jobDriver=null";
            }

            try
            {
                Job? job = driver.job;
                StringBuilder builder = new StringBuilder(320);
                builder.Append("driverType=").Append(
                    Sanitize(driver.GetType().FullName));
                builder.Append(" job=").Append(Sanitize(job?.def?.defName));
                builder.Append(" jobLoadId=").Append(job?.loadID ?? -1);
                builder.Append(" curToilIndex=").Append(driver.CurToilIndex);
                builder.Append(" toilCount=").Append(toils?.Count ?? -1);
                builder.Append(" ticksLeftThisToil=").Append(driver.ticksLeftThisToil);
                builder.Append(" debugTicksSpentThisToil=").Append(
                    driver.debugTicksSpentThisToil);
                builder.Append(" ended=").Append(driver.ended);
                builder.Append(" asleep=").Append(driver.asleep);
                if (job != null)
                {
                    builder.Append(" targetA=").Append(DescribeTarget(job.targetA));
                    builder.Append(" targetB=").Append(DescribeTarget(job.targetB));
                    builder.Append(" targetC=").Append(DescribeTarget(job.targetC));
                }
                else
                {
                    builder.Append(" targetA=null targetB=null targetC=null");
                }

                return builder.ToString();
            }
            catch (Exception exception)
            {
                return "describeException="
                    + Sanitize(exception.GetType().FullName);
            }
        }

        internal static string DescribePatherState(
            Pawn_PathFollower? pather,
            IntVec3 lastCell)
        {
            if (pather == null)
            {
                return "pather=null";
            }

            try
            {
                PawnPath? path = pather.curPath;
                StringBuilder builder = new StringBuilder(320);
                builder.Append("moving=").Append(pather.Moving);
                builder.Append(" destination=").Append(
                    DescribeTarget(pather.Destination));
                builder.Append(" nextCell=").Append(DescribeCell(pather.nextCell));
                builder.Append(" lastCell=").Append(DescribeCell(lastCell));
                builder.Append(" nextCellCostLeft=").Append(
                    pather.nextCellCostLeft.ToString("0.###"));
                builder.Append(" nextCellCostTotal=").Append(
                    pather.nextCellCostTotal.ToString("0.###"));
                builder.Append(" lastMovedTick=").Append(pather.LastMovedTick);
                builder.Append(" curPathJobIsStale=").Append(pather.curPathJobIsStale);
                builder.Append(" hasPath=").Append(path != null);
                builder.Append(" pathFound=").Append(path?.Found ?? false);
                builder.Append(" nodesLeftCount=").Append(
                    path != null && path.Found ? path.NodesLeftCount : -1);
                builder.Append(" lastPathedTargetPosition=").Append(
                    DescribeCell(pather.lastPathedTargetPosition));
                return builder.ToString();
            }
            catch (Exception exception)
            {
                return "describeException="
                    + Sanitize(exception.GetType().FullName);
            }
        }

        internal static string DescribeException(Exception? exception)
        {
            if (exception == null)
            {
                return "exception=null";
            }

            try
            {
                return "exception=" + Sanitize(exception.GetType().FullName)
                    + " message=" + Sanitize(exception.Message);
            }
            catch
            {
                return "exception=unreadable";
            }
        }

        internal static string FormatElapsed(long startedTimestamp)
        {
            if (startedTimestamp <= 0L)
            {
                return "-1";
            }

            double milliseconds =
                (Stopwatch.GetTimestamp() - startedTimestamp)
                * 1000d
                / Stopwatch.Frequency;
            return milliseconds.ToString("0.###");
        }

        internal static string DescribeKindCounts(Dictionary<string, int>? counts)
        {
            if (counts == null || counts.Count == 0)
            {
                return "none";
            }

            List<string> names = new List<string>(counts.Count);
            foreach (KeyValuePair<string, int> pair in counts)
            {
                names.Add(pair.Key);
            }

            names.Sort(StringComparer.Ordinal);

            StringBuilder builder = new StringBuilder(names.Count * 16);
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(Sanitize(names[i]));
                builder.Append(':');
                builder.Append(counts[names[i]]);
            }

            return builder.ToString();
        }
    }
}
