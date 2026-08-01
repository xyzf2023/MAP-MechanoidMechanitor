using Verse;

namespace MAP_MechanoidMechanitor
{
    public enum DataProcessingWorkClassification
    {
        Default,
        CountsAsWork,
        DoesNotCountAsWork
    }

    /// <summary>
    /// 可附加到 JobDef 的兼容扩展，用于明确声明该 Job 是否应触发动态分配的“工作中”状态。
    /// </summary>
    public sealed class DataProcessingWorkClassificationExtension : DefModExtension
    {
        public DataProcessingWorkClassification classification =
            DataProcessingWorkClassification.Default;
    }
}
