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
