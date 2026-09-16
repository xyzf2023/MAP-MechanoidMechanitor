#nullable enable

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 普通派系前哨的「布局档位」枚举。
    /// 档位由前哨创建时保存的完成态守军预算决定，保存后不再随财富或设置变化。
    /// </summary>
    public enum FactionOutpostLayoutTier : byte
    {
        Baseline = 0,
        Expanded = 1,
        Large = 2,
        Fortress = 3
    }

    /// <summary>
    /// 普通派系前哨的「布局档位」工具。
    /// 只在前哨创建时根据完成态守军预算决定布局档位，
    /// 不计算财富、不生成 Pawn、不创建 WorldObject、不改写前哨保存状态。
    /// </summary>
    public static class FactionOutpostLayoutUtility
    {
        private const int BaselineMaxCompletedPoints = 3000;
        private const int ExpandedMaxCompletedPoints = 8000;
        private const int LargeMaxCompletedPoints = 15000;

        /// <summary>
        /// 由创建前哨时保存的完成态守军预算推导布局档位。
        /// 严格使用阈值：&gt;=15001 Fortress；8001-15000 Large；3001-8000 Expanded；&lt;=3000 或 &lt;=0 Baseline。
        /// </summary>
        public static FactionOutpostLayoutTier GetTierForCompletedPoints(int completedPoints)
        {
            if (completedPoints <= 0)
            {
                return FactionOutpostLayoutTier.Baseline;
            }

            if (completedPoints <= BaselineMaxCompletedPoints)
            {
                return FactionOutpostLayoutTier.Baseline;
            }

            if (completedPoints <= ExpandedMaxCompletedPoints)
            {
                return FactionOutpostLayoutTier.Expanded;
            }

            if (completedPoints <= LargeMaxCompletedPoints)
            {
                return FactionOutpostLayoutTier.Large;
            }

            return FactionOutpostLayoutTier.Fortress;
        }

        /// <summary>
        /// 返回给定 phase / 档位下预期生成的真实建筑群数量（仅用于 DEV 日志与断言参考）。
        /// 建设中任何档位均为 1；建成态 Large / Fortress 为 3，其余为 2。
        /// </summary>
        public static int GetExpectedClusterCount(
            MechanoidMechanitorFactionOutpostPhase phase,
            FactionOutpostLayoutTier tier)
        {
            if (phase != MechanoidMechanitorFactionOutpostPhase.Completed)
            {
                return 1;
            }

            return tier >= FactionOutpostLayoutTier.Large ? 3 : 2;
        }
    }
}
