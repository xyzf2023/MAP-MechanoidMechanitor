using RimWorld;
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

        /// <summary>
        /// 开发者控制台入口：复用自然生成到期时的同一套流程与合法性检查，并重置下一次生成计时。
        /// 不临时修改设置或强制开启节点生成。
        /// </summary>
        public void DevTryNaturalGenerationAttempt()
        {
            if (Current.Game == null || Find.World == null || Find.TickManager == null)
            {
                return;
            }

            MechanoidMechanitorMechHiveNodeFrequency frequency = CurrentFrequency();
            if (!frequency.IsEnabled())
            {
                Messages.Message(
                    "当前未允许机械巢节点自然生成。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
            }
            else
            {
                // 与计时到期相同：完整合法性检查；无机械巢派系/无殖民地时安全失败。
                MechHiveNodeGenerationUtility.TryRunGenerationAttempt();
            }

            // 无论开关状态、成功或失败，均重新计算并重置下一次生成计时。
            int now = Find.TickManager.TicksGame;
            nextGenerationAttemptTick = now + ScheduleIntervalTicks();
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
