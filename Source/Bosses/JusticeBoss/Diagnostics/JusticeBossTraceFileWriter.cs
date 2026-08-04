using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal enum JusticeBossTraceWriteMode
    {
        Buffered,
        Critical,
        Emergency,
    }

    internal static class JusticeBossTraceFileWriter
    {
        internal const long MaxFileBytes = 20L * 1024L * 1024L;
        internal const long EmergencyReserveBytes = 64L * 1024L;
        internal const int BufferedFlushLineCount = 128;

        private const string TraceFileName = "JusticeBossCriticalTrace.log";
        private const string PreviousTraceFileName =
            "JusticeBossCriticalTrace.previous.log";
        private const int TraceIoErrorKey = 196840731;

        private static readonly object SyncRoot = new object();
        private static readonly UTF8Encoding FileEncoding = new UTF8Encoding(false);

        private static FileStream? traceStream;
        private static StreamWriter? traceWriter;
        private static long sequence;
        private static long writtenBytes;
        private static int bufferedLines;
        private static volatile bool normalLimitReached;
        private static bool ioFailureReported;
        private static bool reportingIoFailure;
        private static volatile bool isOpen;

        internal static bool IsOpen => isOpen;
        internal static bool NormalLimitReached => normalLimitReached;
        internal static bool CanWriteNormal => isOpen && !normalLimitReached;

        internal static string TraceFilePath =>
            Path.Combine(GenFilePaths.SaveDataFolderPath, TraceFileName);

        internal static string PreviousTraceFilePath =>
            Path.Combine(GenFilePaths.SaveDataFolderPath, PreviousTraceFileName);

        internal static bool OpenSession()
        {
            lock (SyncRoot)
            {
                CloseWriterUnsafe();
                sequence = 0L;
                writtenBytes = 0L;
                bufferedLines = 0;
                normalLimitReached = false;
                ioFailureReported = false;

                string currentPath;
                try
                {
                    currentPath = TraceFilePath;
                    RotatePreviousUnsafe(currentPath);
                    traceStream = new FileStream(
                        currentPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.ReadWrite);
                    traceWriter = new StreamWriter(traceStream, FileEncoding)
                    {
                        AutoFlush = false,
                    };
                    isOpen = true;
                }
                catch (Exception exception)
                {
                    CloseWriterUnsafe();
                    ReportIoFailure(exception);
                    return false;
                }

                WriteUnsafe(
                    "SESSION_BEGIN",
                    "process=" + GetProcessId()
                        + " file=" + JusticeBossTraceFormatting.Sanitize(currentPath)
                        + " maxFileBytes=" + MaxFileBytes
                        + " emergencyReserveBytes=" + EmergencyReserveBytes,
                    JusticeBossTraceWriteMode.Critical);
                return isOpen;
            }
        }

        internal static void CloseSession(string reason)
        {
            lock (SyncRoot)
            {
                if (traceWriter != null)
                {
                    WriteUnsafe(
                        "SESSION_END",
                        "reason=" + JusticeBossTraceFormatting.Sanitize(reason)
                            + " normalLimitReached=" + normalLimitReached,
                        JusticeBossTraceWriteMode.Emergency);
                }

                CloseWriterUnsafe();
            }
        }

        internal static void Write(
            string stage,
            string? detail,
            JusticeBossTraceWriteMode mode)
        {
            if (!isOpen)
            {
                return;
            }

            lock (SyncRoot)
            {
                WriteUnsafe(stage, detail, mode);
            }
        }

        internal static void FlushBuffered()
        {
            if (!isOpen)
            {
                return;
            }

            lock (SyncRoot)
            {
                if (bufferedLines > 0)
                {
                    FlushUnsafe();
                }
            }
        }

        private static void RotatePreviousUnsafe(string currentPath)
        {
            string previousPath = PreviousTraceFilePath;
            if (File.Exists(previousPath))
            {
                File.Delete(previousPath);
            }

            if (File.Exists(currentPath))
            {
                File.Move(currentPath, previousPath);
            }
        }

        private static void WriteUnsafe(
            string stage,
            string? detail,
            JusticeBossTraceWriteMode mode)
        {
            if (traceWriter == null || traceStream == null)
            {
                return;
            }

            bool emergency = mode == JusticeBossTraceWriteMode.Emergency;
            if (normalLimitReached && !emergency)
            {
                return;
            }

            string line;
            int lineBytes;
            try
            {
                line = BuildLineUnsafe(stage, detail);
                lineBytes = FileEncoding.GetByteCount(line)
                    + FileEncoding.GetByteCount(Environment.NewLine);
            }
            catch (Exception exception)
            {
                CloseWriterUnsafe();
                ReportIoFailure(exception);
                return;
            }

            long budget = emergency
                ? MaxFileBytes
                : MaxFileBytes - EmergencyReserveBytes;
            if (writtenBytes + lineBytes > budget)
            {
                if (!emergency)
                {
                    EnterNormalLimitUnsafe();
                }

                return;
            }

            try
            {
                traceWriter.WriteLine(line);
                writtenBytes += lineBytes;
                bufferedLines++;
                if (mode != JusticeBossTraceWriteMode.Buffered
                    || bufferedLines >= BufferedFlushLineCount)
                {
                    FlushUnsafe();
                }
            }
            catch (Exception exception)
            {
                CloseWriterUnsafe();
                ReportIoFailure(exception);
            }
        }

        private static string BuildLineUnsafe(string stage, string? detail)
        {
            StringBuilder builder = new StringBuilder(320);
            builder.Append("seq=").Append(++sequence);
            builder.Append(" utc=").Append(DateTime.UtcNow.ToString("O"));
            builder.Append(" tick=").Append(
                Current.Game?.tickManager?.TicksGame ?? -1);
            builder.Append(" thread=").Append(
                Thread.CurrentThread.ManagedThreadId);
            builder.Append(" stage=").Append(
                JusticeBossTraceFormatting.Sanitize(stage));
            if (!string.IsNullOrWhiteSpace(detail))
            {
                builder.Append(' ').Append(
                    JusticeBossTraceFormatting.Sanitize(detail));
            }

            return builder.ToString();
        }

        private static void EnterNormalLimitUnsafe()
        {
            if (normalLimitReached)
            {
                return;
            }

            normalLimitReached = true;
            WriteUnsafe(
                "TRACE_LIMIT_REACHED",
                "writtenBytes=" + writtenBytes
                    + " maxFileBytes=" + MaxFileBytes
                    + " emergencyReserveBytes=" + EmergencyReserveBytes,
                JusticeBossTraceWriteMode.Emergency);
        }

        private static void FlushUnsafe()
        {
            if (traceWriter == null || traceStream == null)
            {
                bufferedLines = 0;
                return;
            }

            try
            {
                traceWriter.Flush();
                traceStream.Flush();
            }
            catch (Exception exception)
            {
                CloseWriterUnsafe();
                ReportIoFailure(exception);
            }
            finally
            {
                bufferedLines = 0;
            }
        }

        private static void CloseWriterUnsafe()
        {
            isOpen = false;
            try
            {
                traceWriter?.Dispose();
            }
            catch
            {
            }

            try
            {
                traceStream?.Dispose();
            }
            catch
            {
            }

            traceWriter = null;
            traceStream = null;
            bufferedLines = 0;
        }

        private static void ReportIoFailure(Exception exception)
        {
            if (reportingIoFailure || ioFailureReported)
            {
                return;
            }

            reportingIoFailure = true;
            try
            {
                ioFailureReported = true;
                Log.ErrorOnce(
                    "[MAP JusticeBoss Trace] 独立轨迹日志已禁用，文件操作失败："
                        + exception,
                    TraceIoErrorKey);
            }
            catch
            {
            }
            finally
            {
                reportingIoFailure = false;
            }
        }

        private static int GetProcessId()
        {
            try
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    return process.Id;
                }
            }
            catch
            {
                return -1;
            }
        }
    }
}
