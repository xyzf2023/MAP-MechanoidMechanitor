using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械巢节点世界管理组件。负责：下一次生成尝试计时、每日额外袭击概率检查。
    /// 仅进行轻量的 tick 比较；实际寻路/统计只在计划 tick 与每日检查时触发。
    /// 生成计时与每日检查时间随存档保存与读取。
    /// </summary>
    public class MechHiveNodeManager : WorldComponent
    {
        private int nextGenerationAttemptTick = -1;

        private int nextDailyCheckTick = -1;

        public MechHiveNodeManager(World world)
            : base(world)
        {
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();

            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
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
                nextDailyCheckTick = now + MechanoidMechanitorMechHiveNodeFrequencyExtensions.TicksPerDay;
            }

            if (now >= nextGenerationAttemptTick)
            {
                if (CurrentFrequency().IsEnabled())
                {
                    MechHiveNodeGenerationUtility.TryRunGenerationAttempt();
                }

                // 无论成功或失败，都重新随机下一段完整间隔，不逐 tick/逐日反复重试。
                nextGenerationAttemptTick = now + ScheduleIntervalTicks();
            }

            if (now >= nextDailyCheckTick)
            {
                nextDailyCheckTick = now + MechanoidMechanitorMechHiveNodeFrequencyExtensions.TicksPerDay;
                MechHiveNodeRaidUtility.RunDailyExtraRaidCheck();
            }
        }

        private static MechanoidMechanitorMechHiveNodeFrequency CurrentFrequency()
        {
            MechanoidMechanitorStoryConfiguration? config =
                GameComponent_MechanoidMechanitorStoryState.CurrentConfiguration;
            return config?.mechHiveNodeFrequency ?? MechanoidMechanitorMechHiveNodeFrequency.Off;
        }

        private static int ScheduleIntervalTicks()
        {
            MechanoidMechanitorMechHiveNodeFrequency freq = CurrentFrequency();
            if (freq.TryGetIntervalDays(out int minDays, out int maxDays))
            {
                return Rand.RangeInclusive(minDays, maxDays)
                    * MechanoidMechanitorMechHiveNodeFrequencyExtensions.TicksPerDay;
            }

            // 关闭时按天廉价重查（不生成），以便配置存在但为关闭时不进行任何生成。
            return MechanoidMechanitorMechHiveNodeFrequencyExtensions.TicksPerDay;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextGenerationAttemptTick, "MAP_mechHiveNode_nextGenerationAttemptTick", -1);
            Scribe_Values.Look(ref nextDailyCheckTick, "MAP_mechHiveNode_nextDailyCheckTick", -1);
        }
    }
}
