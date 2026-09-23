using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 普通派系前哨世界管理器。生成频率只控制新前哨产生；每日军事效果继续读取已经存在的
    /// 完整前哨及其所属派系当前真实关系。
    /// </summary>
    public sealed class FactionOutpostManager : WorldComponent
    {
        private int nextGenerationAttemptTick = -1;
        private int nextDailyCheckTick = -1;

        public FactionOutpostManager(World world)
            : base(world)
        {
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            if (!GameComponent_MechanoidMechanitorStoryState.IsStoryConfigurationActive)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            if (nextGenerationAttemptTick < 0)
            {
                nextGenerationAttemptTick = now + ScheduleIntervalTicks();
            }

            if (nextDailyCheckTick < 0)
            {
                nextDailyCheckTick = now
                    + MechanoidMechanitorFactionOutpostFrequencyExtensions.TicksPerDay;
            }

            if (now >= nextGenerationAttemptTick)
            {
                if (CurrentFrequency().IsEnabled())
                {
                    FactionOutpostGenerationUtility.TryRunGenerationAttempt();
                }

                nextGenerationAttemptTick = now + ScheduleIntervalTicks();
            }

            if (now >= nextDailyCheckTick)
            {
                nextDailyCheckTick = now
                    + MechanoidMechanitorFactionOutpostFrequencyExtensions.TicksPerDay;
                FactionOutpostRaidUtility.RunDailyExtraRaidCheck();
            }
        }

        /// <summary>
        /// 开发者入口：完全复用自然生成的派系权重、候选资格与选址规则。
        /// 与机械巢节点 DEV 行为一致，生成关闭时不强行绕过设置；无论成功失败都会重置自然生成计时。
        /// </summary>
        public void DevTryNaturalGenerationAttempt()
        {
            if (!CanRunDevGeneration())
            {
                return;
            }

            if (!CurrentFrequency().IsEnabled())
            {
                Messages.Message(
                    "派系前哨生成当前已关闭。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
            }
            else
            {
                FactionOutpostGenerationUtility.TryRunGenerationAttempt();
            }

            ResetGenerationTimer();
        }

        /// <summary>
        /// 开发者入口：优先选择指定实时关系的合法派系，并完全无视三个关系权重；
        /// 若指定关系没有任何合法候选，则回退到普通权重选择。其余数量、距离、地块等规则均不绕过。
        /// </summary>
        public void DevTryGenerationAttemptForRelation(FactionRelationKind preferredRelation)
        {
            if (!CanRunDevGeneration())
            {
                return;
            }

            if (!CurrentFrequency().IsEnabled())
            {
                Messages.Message(
                    "派系前哨生成当前已关闭。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
            }
            else
            {
                FactionOutpostGenerationUtility.TryRunGenerationAttemptForRelation(preferredRelation);
            }

            ResetGenerationTimer();
        }

        private static bool CanRunDevGeneration()
        {
            return Current.Game != null && Find.World != null && Find.TickManager != null;
        }

        private void ResetGenerationTimer()
        {
            nextGenerationAttemptTick = Find.TickManager.TicksGame + ScheduleIntervalTicks();
        }

        private static MechanoidMechanitorFactionOutpostFrequency CurrentFrequency()
        {
            MechanoidMechanitorStoryConfiguration? configuration =
                GameComponent_MechanoidMechanitorStoryState.CurrentConfiguration;
            return configuration?.factionOutpostFrequency
                ?? MechanoidMechanitorFactionOutpostFrequency.Off;
        }

        private static int ScheduleIntervalTicks()
        {
            MechanoidMechanitorFactionOutpostFrequency frequency = CurrentFrequency();
            if (frequency.TryGetIntervalDays(out int minDays, out int maxDays))
            {
                return Rand.RangeInclusive(minDays, maxDays)
                    * MechanoidMechanitorFactionOutpostFrequencyExtensions.TicksPerDay;
            }

            return MechanoidMechanitorFactionOutpostFrequencyExtensions.TicksPerDay;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref nextGenerationAttemptTick,
                "MAP_factionOutpost_nextGenerationAttemptTick",
                -1);
            Scribe_Values.Look(
                ref nextDailyCheckTick,
                "MAP_factionOutpost_nextDailyCheckTick",
                -1);
        }
    }
}
