namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// “是否生成机械巢节点”配置。使用单一枚举同时表达开关与频率。
    /// </summary>
    public enum MechanoidMechanitorMechHiveNodeFrequency : byte
    {
        /// <summary>关闭：不生成机械巢节点。</summary>
        Off = 0,

        /// <summary>频率：低。</summary>
        Low = 1,

        /// <summary>频率：中。</summary>
        Medium = 2,

        /// <summary>频率：高。</summary>
        High = 3
    }

    public static class MechanoidMechanitorMechHiveNodeFrequencyExtensions
    {
        /// <summary>每游戏日固定 60000 tick。</summary>
        public const int TicksPerDay = 60000;

        public static bool IsEnabled(this MechanoidMechanitorMechHiveNodeFrequency frequency)
        {
            return frequency != MechanoidMechanitorMechHiveNodeFrequency.Off;
        }

        /// <summary>
        /// 返回一次生成尝试之间的间隔天数范围（含端点）。关闭时返回 false。
        /// 低：15～20 日；中：5～15 日；高：3～8 日。
        /// </summary>
        public static bool TryGetIntervalDays(
            this MechanoidMechanitorMechHiveNodeFrequency frequency,
            out int minDays,
            out int maxDays)
        {
            switch (frequency)
            {
                case MechanoidMechanitorMechHiveNodeFrequency.Low:
                    minDays = 15;
                    maxDays = 20;
                    return true;

                case MechanoidMechanitorMechHiveNodeFrequency.Medium:
                    minDays = 5;
                    maxDays = 15;
                    return true;

                case MechanoidMechanitorMechHiveNodeFrequency.High:
                    minDays = 3;
                    maxDays = 8;
                    return true;

                default:
                    minDays = 0;
                    maxDays = 0;
                    return false;
            }
        }
    }
}
