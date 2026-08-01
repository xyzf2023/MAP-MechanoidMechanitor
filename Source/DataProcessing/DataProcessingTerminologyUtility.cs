using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    ///     根据目标类型动态返回数据处理/意识相关术语，避免 C# 中硬编码中文。
    ///     仅替换人类机械师实际会看到的位置；机械族机械师文本保持原意。
    /// </summary>
    public static class DataProcessingTerminologyUtility
    {
        /// <summary>机械体“数据处理”，人类“意识”。</summary>
        public static string GetCapacityTerm(Pawn? pawn)
        {
            return ParallelThoughtArrayUtility.GetCapacityTerm(pawn);
        }

        /// <summary>机械体“数据处理分配”，人类“意识分配”。</summary>
        public static string GetAllocationLabel(Pawn? pawn)
        {
            if (pawn != null && pawn.RaceProps.IsMechanoid)
            {
                return "MAP_MechanoidMechanitor.DataProcessingAllocation";
            }

            return "MAP_MechanoidMechanitor.ConsciousnessAllocation";
        }

        /// <summary>根据目标类型返回对应翻译文本（Gizmo/窗口描述）。</summary>
        public static string GetAllocationDescription(Pawn? pawn)
        {
            if (pawn != null && pawn.RaceProps.IsMechanoid)
            {
                return "MAP_MechanoidMechanitor.DataProcessingAllocation.Desc";
            }

            return "MAP_MechanoidMechanitor.ConsciousnessAllocation.Desc";
        }

        /// <summary>仪表盘标题键：机械体“数据处理”，人类“意识”。</summary>
        public static string GetDashboardTitleKey(Pawn? pawn)
        {
            if (pawn != null && pawn.RaceProps.IsMechanoid)
            {
                return "MAP_MechanoidMechanitor.DataProcessing.Dashboard.Title";
            }

            return "MAP_MechanoidMechanitor.Consciousness.Dashboard.Title";
        }

        /// <summary>基础处理能力/意识 行标签键。</summary>
        public static string GetBaseProcessingKey(Pawn? pawn)
        {
            if (pawn != null && pawn.RaceProps.IsMechanoid)
            {
                return "MAP_MechanoidMechanitor.DataProcessing.Dashboard.BaseProcessing";
            }

            return "MAP_MechanoidMechanitor.Consciousness.Dashboard.BaseConsciousness";
        }

        /// <summary>当前 行标签键。</summary>
        public static string GetCurrentKey(Pawn? pawn)
        {
            if (pawn != null && pawn.RaceProps.IsMechanoid)
            {
                return "MAP_MechanoidMechanitor.DataProcessing.Dashboard.Current";
            }

            return "MAP_MechanoidMechanitor.Consciousness.Dashboard.Current";
        }

        /// <summary>阈值 行标签键。</summary>
        public static string GetThresholdKey(Pawn? pawn)
        {
            if (pawn != null && pawn.RaceProps.IsMechanoid)
            {
                return "MAP_MechanoidMechanitor.DataProcessing.Dashboard.Threshold";
            }

            return "MAP_MechanoidMechanitor.Consciousness.Dashboard.Threshold";
        }

        /// <summary>固定占用 行标签键。</summary>
        public static string GetFixedUsageKey(Pawn? pawn)
        {
            if (pawn != null && pawn.RaceProps.IsMechanoid)
            {
                return "MAP_MechanoidMechanitor.DataProcessing.Dashboard.FixedUsage";
            }

            return "MAP_MechanoidMechanitor.Consciousness.Dashboard.FixedUsage";
        }

        /// <summary>动态占用 行标签键。</summary>
        public static string GetDynamicUsageKey(Pawn? pawn)
        {
            if (pawn != null && pawn.RaceProps.IsMechanoid)
            {
                return "MAP_MechanoidMechanitor.DataProcessing.Dashboard.DynamicUsage";
            }

            return "MAP_MechanoidMechanitor.Consciousness.Dashboard.DynamicUsage";
        }

        /// <summary>未满足请求 行标签键。</summary>
        public static string GetUnmetRequestKey(Pawn? pawn)
        {
            if (pawn != null && pawn.RaceProps.IsMechanoid)
            {
                return "MAP_MechanoidMechanitor.DataProcessing.Dashboard.UnmetRequest";
            }

            return "MAP_MechanoidMechanitor.Consciousness.Dashboard.UnmetRequest";
        }

        /// <summary>目标数量 行标签键。</summary>
        public static string GetTargetCountKey(Pawn? pawn)
        {
            if (pawn != null && pawn.RaceProps.IsMechanoid)
            {
                return "MAP_MechanoidMechanitor.DataProcessing.Dashboard.TargetCount";
            }

            return "MAP_MechanoidMechanitor.Consciousness.Dashboard.TargetCount";
        }
    }
}
