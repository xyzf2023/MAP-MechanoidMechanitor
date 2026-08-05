using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Verse;

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

        internal static double ElapsedMilliseconds(long startedTimestamp)
        {
            if (startedTimestamp <= 0L)
            {
                return -1d;
            }

            return (Stopwatch.GetTimestamp() - startedTimestamp)
                * 1000d
                / Stopwatch.Frequency;
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
