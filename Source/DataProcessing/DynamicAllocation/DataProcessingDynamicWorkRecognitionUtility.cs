using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public enum DataProcessingWorkRecognitionSource
    {
        NoPawn,
        Drafted,
        NoJob,
        ExplicitWork,
        ExplicitNonWork,
        StandardWorkGiver,
        VanillaSpecialJob,
        UnknownJob
    }

    public struct DataProcessingWorkRecognitionResult
    {
        public bool countsAsWork;
        public DataProcessingWorkRecognitionSource source;
        public JobDef? jobDef;
        public WorkGiverDef? workGiverDef;

        public DataProcessingWorkRecognitionResult(
            bool countsAsWork,
            DataProcessingWorkRecognitionSource source,
            JobDef? jobDef,
            WorkGiverDef? workGiverDef)
        {
            this.countsAsWork = countsAsWork;
            this.source = source;
            this.jobDef = jobDef;
            this.workGiverDef = workGiverDef;
        }
    }

    /// <summary>
    /// 动态分配工作识别的唯一入口。优先尊重 JobDef 扩展，其次识别标准 WorkGiver，
    /// 再处理少量已确认的原版特殊工作；无法可靠判断时保守地视为非工作。
    /// </summary>
    public static class DataProcessingDynamicWorkRecognitionUtility
    {
        private const string BeatFireJobDefName = "BeatFire";

        public static DataProcessingWorkRecognitionResult Analyze(Pawn? pawn)
        {
            if (pawn == null)
            {
                return new DataProcessingWorkRecognitionResult(
                    false,
                    DataProcessingWorkRecognitionSource.NoPawn,
                    null,
                    null);
            }

            if (pawn.Drafted)
            {
                return new DataProcessingWorkRecognitionResult(
                    false,
                    DataProcessingWorkRecognitionSource.Drafted,
                    pawn.CurJob?.def,
                    pawn.CurJob?.workGiverDef);
            }

            Job? job = pawn.CurJob;
            JobDef? jobDef = job?.def;
            WorkGiverDef? workGiverDef = job?.workGiverDef;
            if (job == null || jobDef == null)
            {
                return new DataProcessingWorkRecognitionResult(
                    false,
                    DataProcessingWorkRecognitionSource.NoJob,
                    jobDef,
                    workGiverDef);
            }

            DataProcessingWorkClassificationExtension? extension =
                jobDef.GetModExtension<DataProcessingWorkClassificationExtension>();
            if (extension != null)
            {
                if (extension.classification == DataProcessingWorkClassification.CountsAsWork)
                {
                    return new DataProcessingWorkRecognitionResult(
                        true,
                        DataProcessingWorkRecognitionSource.ExplicitWork,
                        jobDef,
                        workGiverDef);
                }

                if (extension.classification == DataProcessingWorkClassification.DoesNotCountAsWork)
                {
                    return new DataProcessingWorkRecognitionResult(
                        false,
                        DataProcessingWorkRecognitionSource.ExplicitNonWork,
                        jobDef,
                        workGiverDef);
                }
            }

            if (workGiverDef != null)
            {
                return new DataProcessingWorkRecognitionResult(
                    true,
                    DataProcessingWorkRecognitionSource.StandardWorkGiver,
                    jobDef,
                    workGiverDef);
            }

            if (IsVanillaSpecialWork(jobDef))
            {
                return new DataProcessingWorkRecognitionResult(
                    true,
                    DataProcessingWorkRecognitionSource.VanillaSpecialJob,
                    jobDef,
                    workGiverDef);
            }

            return new DataProcessingWorkRecognitionResult(
                false,
                DataProcessingWorkRecognitionSource.UnknownJob,
                jobDef,
                workGiverDef);
        }

        public static bool IsPawnDoingWork(Pawn? pawn)
        {
            return Analyze(pawn).countsAsWork;
        }

        public static string GetSourceLabel(DataProcessingWorkRecognitionSource source)
        {
            // 仅供开发者模式的工作识别诊断显示。
            switch (source)
            {
                case DataProcessingWorkRecognitionSource.NoPawn: return "目标不存在";
                case DataProcessingWorkRecognitionSource.Drafted: return "目标已征召";
                case DataProcessingWorkRecognitionSource.NoJob: return "当前没有Job";
                case DataProcessingWorkRecognitionSource.ExplicitWork: return "JobDef兼容扩展：明确计为工作";
                case DataProcessingWorkRecognitionSource.ExplicitNonWork: return "JobDef兼容扩展：明确排除";
                case DataProcessingWorkRecognitionSource.StandardWorkGiver: return "标准WorkGiver工作";
                case DataProcessingWorkRecognitionSource.VanillaSpecialJob: return "原版特殊工作白名单";
                case DataProcessingWorkRecognitionSource.UnknownJob: return "无法可靠分类的Job";
                default: return source.ToString();
            }
        }

        private static bool IsVanillaSpecialWork(JobDef jobDef)
        {
            return jobDef.defName == BeatFireJobDefName;
        }
    }
}
