using UnityEngine;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 普通派系前哨与机械巢节点的战略事件概率统一入口。
    /// 概率值来自全局 MOD 设置，以 0～100 的整数百分比保存；
    /// 所有存档共享同一组值。
    /// 多节点事件继续按独立概率 1 - (1 - p)^n 合并。
    /// </summary>
    internal static class MechanoidMechanitorStrategicChanceUtility
    {
        public static float GetFactionOutpostRaidChancePerNode()
        {
            int percent =
                MAPMechanitorMod.Settings?.factionOutpostRaidChancePercent
                ?? MAPMechanitorModSettings
                    .DefaultFactionOutpostRaidChancePercent;

            return PercentToChance(percent);
        }

        public static float GetFactionOutpostSupportChancePerNode()
        {
            int percent =
                MAPMechanitorMod.Settings?.factionOutpostSupportChancePercent
                ?? MAPMechanitorModSettings
                    .DefaultFactionOutpostSupportChancePercent;

            return PercentToChance(percent);
        }

        public static float GetMechHiveNodeRaidChancePerNode()
        {
            int percent =
                MAPMechanitorMod.Settings?.mechHiveNodeRaidChancePercent
                ?? MAPMechanitorModSettings
                    .DefaultMechHiveNodeRaidChancePercent;

            return PercentToChance(percent);
        }

        public static float GetMechHiveNodeSupportChancePerNode()
        {
            int percent =
                MAPMechanitorMod.Settings?.mechHiveNodeSupportChancePercent
                ?? MAPMechanitorModSettings
                    .DefaultMechHiveNodeSupportChancePercent;

            return PercentToChance(percent);
        }

        public static float CombineIndependentChance(
            float perNodeChance,
            int nodeCount)
        {
            if (nodeCount <= 0)
            {
                return 0f;
            }

            float p = Mathf.Clamp01(perNodeChance);

            if (p <= 0f)
            {
                return 0f;
            }

            if (p >= 1f)
            {
                return 1f;
            }

            return 1f - Mathf.Pow(1f - p, nodeCount);
        }

        private static float PercentToChance(int percent)
        {
            return Mathf.Clamp(percent, 0, 100) / 100f;
        }
    }
}
