using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 自定义风格中的“是否生成机械巢节点”配置卡片。仅在存在机械巢时显示。
    /// </summary>
    public sealed class MechanoidMechanitorStoryComponentWorker_MechHiveNode
        : MechanoidMechanitorStoryComponentWorker
    {
        private static readonly MechanoidMechanitorMechHiveNodeFrequency[] Frequencies =
        {
            MechanoidMechanitorMechHiveNodeFrequency.Off,
            MechanoidMechanitorMechHiveNodeFrequency.Low,
            MechanoidMechanitorMechHiveNodeFrequency.Medium,
            MechanoidMechanitorMechHiveNodeFrequency.High
        };

        public override bool ShouldShow(MechanoidMechanitorStoryConfigurationContext context)
        {
            return context.HasMechHive;
        }

        public override string? GetSummaryValue(
            MechanoidMechanitorStoryConfiguration configuration)
        {
            return MechanoidMechanitorStoryConfigurationLabels.LabelFor(
                configuration.mechHiveNodeFrequency);
        }

        public override float GetHeight(
            MechanoidMechanitorStoryConfigurationContext context,
            float width)
        {
            return MeasureCardHeight(context, width);
        }

        public override void Draw(
            Rect rect,
            MechanoidMechanitorStoryConfigurationContext context)
        {
            MechanoidMechanitorStoryConfiguration configuration = context.Configuration;
            DrawCardHeaderAndDropdown(
                rect,
                context,
                MechanoidMechanitorStoryConfigurationLabels.LabelFor(
                    configuration.mechHiveNodeFrequency),
                enabled: true,
                () => OpenDropdownMenu(
                    Frequencies,
                    MechanoidMechanitorStoryConfigurationLabels.LabelFor,
                    frequency =>
                    {
                        if (configuration.mechHiveNodeFrequency == frequency)
                        {
                            return;
                        }

                        configuration.mechHiveNodeFrequency = frequency;
                        NormalizeAfterChange(context);
                    }));
        }
    }
}
