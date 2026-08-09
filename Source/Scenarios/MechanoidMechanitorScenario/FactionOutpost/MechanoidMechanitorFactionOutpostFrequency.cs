namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 普通派系前哨的自然生成频率。仅控制新前哨生成尝试，不影响既有前哨行为。
    /// </summary>
    public enum MechanoidMechanitorFactionOutpostFrequency : byte
    {
        Off = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    public static class MechanoidMechanitorFactionOutpostFrequencyExtensions
    {
        public const int TicksPerDay = 60000;

        public static bool IsEnabled(this MechanoidMechanitorFactionOutpostFrequency frequency)
        {
            return frequency != MechanoidMechanitorFactionOutpostFrequency.Off;
        }

        /// <summary>
        /// 与机械巢节点保持同档节奏：低 15～20 日，中 5～15 日，高 3～8 日。
        /// </summary>
        public static bool TryGetIntervalDays(
            this MechanoidMechanitorFactionOutpostFrequency frequency,
            out int minDays,
            out int maxDays)
        {
            switch (frequency)
            {
                case MechanoidMechanitorFactionOutpostFrequency.Low:
                    minDays = 15;
                    maxDays = 20;
                    return true;
                case MechanoidMechanitorFactionOutpostFrequency.Medium:
                    minDays = 5;
                    maxDays = 15;
                    return true;
                case MechanoidMechanitorFactionOutpostFrequency.High:
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
