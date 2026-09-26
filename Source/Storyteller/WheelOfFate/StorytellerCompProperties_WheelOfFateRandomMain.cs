using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 命运之轮的基础随机调度配置。复用原版字段和事件参数生成逻辑，
    /// 但明确允许用 maxThreatBigIntervalDays = 0 关闭大型威胁超时优先选择。
    /// </summary>
    public sealed class StorytellerCompProperties_WheelOfFateRandomMain
        : StorytellerCompProperties_RandomMain
    {
        public StorytellerCompProperties_WheelOfFateRandomMain()
        {
            compClass = typeof(StorytellerComp_WheelOfFateRandomMain);
            mtbDays = 1.35f;
            maxThreatBigIntervalDays = 0f;
            randomPointsFactorRange = new FloatRange(1f, 1f);
        }

        public override IEnumerable<string> ConfigErrors(StorytellerDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }

            if (!IsFinitePositive(mtbDays))
            {
                yield return "命运之轮：mtbDays 必须是大于 0 的有限数值。";
            }

            if (!IsFiniteNonNegative(maxThreatBigIntervalDays))
            {
                yield return "命运之轮：maxThreatBigIntervalDays 必须是非负有限数值；0 表示关闭。";
            }

            if (!IsFinitePositive(spaceMtbDayFactor)
                || !IsFiniteNonNegative(spaceMinSpacingDays))
            {
                yield return "命运之轮：太空事件间隔倍率必须大于 0，最小间隔不得小于 0，且均须为有限数值。";
            }

            if (!IsFinitePositive(randomPointsFactorRange.min)
                || !IsFinitePositive(randomPointsFactorRange.max)
                || randomPointsFactorRange.min > randomPointsFactorRange.max)
            {
                yield return "命运之轮：randomPointsFactorRange 必须是有效的正数范围。";
            }

            if (categoryWeights.NullOrEmpty())
            {
                yield return "命运之轮：categoryWeights 不得为空。";
                yield break;
            }

            var categories = new HashSet<IncidentCategoryDef>();
            bool hasPositiveWeight = false;
            foreach (IncidentCategoryEntry entry in categoryWeights)
            {
                if (entry?.category == null)
                {
                    yield return "命运之轮：类别权重条目缺少有效的事件类别引用。";
                    continue;
                }

                if (!categories.Add(entry.category))
                {
                    yield return $"命运之轮：事件类别 {entry.category.defName} 重复配置。";
                }

                if (!IsFiniteNonNegative(entry.weight))
                {
                    yield return $"命运之轮：{entry.category.defName} 的类别权重必须是非负有限数值。";
                }
                else if (entry.weight > 0f)
                {
                    hasPositiveWeight = true;
                }
            }

            if (!hasPositiveWeight)
            {
                yield return "命运之轮：至少需要一个权重大于 0 的有效事件类别。";
            }
        }

        internal static bool IsFinitePositive(float value)
        {
            return value > 0f && !float.IsInfinity(value);
        }

        internal static bool IsFiniteNonNegative(float value)
        {
            return value >= 0f && !float.IsInfinity(value);
        }
    }
}
