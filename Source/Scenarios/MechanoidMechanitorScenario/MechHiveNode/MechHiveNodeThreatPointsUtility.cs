#nullable enable
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械巢节点「守军点数」的统一计算工具。
    /// 职责单一：只计算节点创建时应保存的守军点数快照，不生成 Pawn、不生成建筑、不创建地图、
    /// 不创建 WorldObject，也不修改任何全局设置或叙事者点数。
    /// 新节点在创建时调用本类计算出完成态 / 建设态点数并保存为节点自己的永久快照；
    /// 之后该节点的守军预算不再随玩家财富、殖民者数量、叙事者或 MOD 设置变化。
    /// 与普通派系前哨不同：机械巢节点不接受 factionOutpostGarrisonThreatScalePercent 影响，
    /// 也不得在玩家进入地图或读档时重新计算。
    /// </summary>
    public static class MechHiveNodeThreatPointsUtility
    {
        /// <summary>
        /// 旧存档回退值：建设态固定 2000、完成态固定 10000。
        /// 仅用于无可读快照的旧节点，不得作为新节点的常规生成值。
        /// </summary>
        public const int LegacyBuildingGarrisonThreatPoints = 2000;

        public const int LegacyCompletedGarrisonThreatPoints = 10000;

        /// <summary>
        /// 计算「新节点创建时应保存的完成态守军点数」。
        /// 正常路径：StorytellerUtility.DefaultThreatPointsNow(referenceMap)，
        /// referenceMap 为实际用于挑选该节点 tile 的来源玩家殖民地地图。
        /// 为 null / 已销毁时安全回退 LegacyCompletedGarrisonThreatPoints，避免调用原版方法时崩溃。
        /// 结果至少为 1，并对 NaN / Infinity / 负数做兜底。
        /// 不读取 Find.AnyPlayerHomeMap，也不把原版点数写回全局设置。
        /// </summary>
        public static int CalculateCompletedGarrisonPoints(Map? referenceMap)
        {
            float points;
            if (referenceMap == null || referenceMap.Disposed)
            {
                Log.Warning(
                    "[MAP] 机械巢节点创建时无法获得来源玩家殖民地地图，守军点数将回退旧默认完成态值。");
                points = LegacyCompletedGarrisonThreatPoints;
            }
            else
            {
                points = StorytellerUtility.DefaultThreatPointsNow(referenceMap);
            }

            if (float.IsNaN(points) || float.IsInfinity(points) || points < 0f)
            {
                points = LegacyCompletedGarrisonThreatPoints;
            }

            if (points < 1f)
            {
                return 1;
            }

            return Mathf.RoundToInt(points);
        }

        /// <summary>
        /// 由完成态点数推导建设态点数，保持旧的 0.2 比例（2000 : 10000）。
        /// completedPoints 非法或不大于 0 时回退旧建设态值；结果至少为 1。
        /// </summary>
        public static int CalculateBuildingGarrisonPoints(int completedPoints)
        {
            if (completedPoints <= 0)
            {
                return LegacyBuildingGarrisonThreatPoints;
            }

            float building = completedPoints * 0.2f;
            if (float.IsNaN(building) || float.IsInfinity(building) || building < 1f)
            {
                return 1;
            }

            return Mathf.RoundToInt(building);
        }
    }
}
