#nullable enable
using RimWorld;
using UnityEngine;
using Verse;
using MAP_MechanoidMechanitor;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 普通派系前哨「守军点数」的统一计算工具。
    /// 职责单一：只计算与缓存前哨的守军预算，不生成 Pawn、不创建 WorldObject、不修改现有前哨，
    /// 也不处理联合行动的援军规模或奖励。
    /// 新前哨在创建时调用本类计算出完成态/建设态点数，并保存为快照；
    /// 之后该前哨的守军预算不再随玩家财富、殖民者数量、叙事者或 MOD 设置变化。
    /// </summary>
    public static class FactionOutpostThreatPointsUtility
    {
        public const int DefaultScalePercent = 100;
        public const int MinScalePercent = 10;
        public const int MaxScalePercent = 500;
        public const int ScaleStepPercent = 10;

        /// <summary>
        /// 旧存档回退值：建设态固定 2000、完成态固定 10000。
        /// 仅用于无可读快照的旧前哨，不得作为新前哨的常规生成值。
        /// </summary>
        public const int LegacyBuildingGarrisonThreatPoints = 2000;
        public const int LegacyCompletedGarrisonThreatPoints = 10000;

        /// <summary>
        /// 计算「新前哨创建时应保存的完成态守军点数」。
        /// 正常路径：StorytellerUtility.DefaultThreatPointsNow(referenceMap) × 设置倍率。
        /// referenceMap 为实际用于挑选该前哨 tile 的来源玩家殖民地地图；
        /// 为 null / 已销毁时，安全回退到 LegacyCompletedGarrisonThreatPoints，避免调用原版方法时崩溃。
        /// 结果至少 1，且对 NaN / Infinity / 负数做兜底。
        /// 不在此读取 Find.AnyPlayerHomeMap，也不把原版点数写回全局设置。
        /// </summary>
        public static int CalculateCompletedGarrisonPoints(Map? referenceMap)
        {
            float basePoints;
            if (referenceMap == null || referenceMap.Disposed)
            {
                Log.Warning(
                    "[MAP] 前哨创建时无法获得来源玩家殖民地地图，守军点数将回退旧默认完成态值。");
                basePoints = LegacyCompletedGarrisonThreatPoints;
            }
            else
            {
                basePoints = StorytellerUtility.DefaultThreatPointsNow(referenceMap);
            }

            if (float.IsNaN(basePoints)
                || float.IsInfinity(basePoints)
                || basePoints < 0f)
            {
                basePoints = LegacyCompletedGarrisonThreatPoints;
            }

            int scale = ClampScalePercent(
                MAPMechanitorMod.Settings?.factionOutpostGarrisonThreatScalePercent
                ?? DefaultScalePercent);

            float scaled = basePoints * (scale / 100f);
            if (float.IsNaN(scaled)
                || float.IsInfinity(scaled)
                || scaled < 1f)
            {
                return 1;
            }

            return Mathf.RoundToInt(scaled);
        }

        /// <summary>
        /// 由完成态点数推导建设态点数，保持旧的 0.2 比例（2000 : 10000）。
        /// 结果至少 1。
        /// </summary>
        public static int CalculateBuildingGarrisonPoints(int completedPoints)
        {
            if (completedPoints <= 0)
            {
                return LegacyBuildingGarrisonThreatPoints;
            }

            float building = completedPoints * 0.2f;
            if (float.IsNaN(building)
                || float.IsInfinity(building)
                || building < 1f)
            {
                return 1;
            }

            return Mathf.RoundToInt(building);
        }

        /// <summary>
        /// 将设置倍率钳制到合法范围 [MinScalePercent, MaxScalePercent]，并向下对齐到 ScaleStepPercent 步长。
        /// 非法（NaN / Infinity / 负数 / 0）一律回退到 DefaultScalePercent。
        /// </summary>
        public static int ClampScalePercent(int value)
        {
            if (value < MinScalePercent)
            {
                return DefaultScalePercent;
            }

            int clamped = Mathf.Clamp(value, MinScalePercent, MaxScalePercent);
            int stepped = Mathf.RoundToInt((float)clamped / ScaleStepPercent) * ScaleStepPercent;
            return Mathf.Clamp(stepped, MinScalePercent, MaxScalePercent);
        }
    }
}
