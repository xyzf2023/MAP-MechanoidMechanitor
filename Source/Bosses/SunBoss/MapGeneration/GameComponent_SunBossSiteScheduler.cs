using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>每局一次的太阳设施自然生成；解锁后只等待已保存的绝对游戏 tick。</summary>
    public sealed class GameComponent_SunBossSiteScheduler : GameComponent
    {
        private const int ConditionCheckInterval = GenDate.TicksPerDay;
        private const int MinimumGameTicks = 60 * GenDate.TicksPerDay;
        private const float MinimumWealth = 300000f;

        private bool scheduled;
        private int scheduledGenerationTick = -1;
        private bool generated;
        private int nextGenerationAttemptTick = -1;

        public GameComponent_SunBossSiteScheduler(Game game) { }

        public override void GameComponentTick()
        {
            AdvanceSchedule();
        }

        private void AdvanceSchedule()
        {
            // 已生成时永久停止；移除设施或读档都不会重新开放自然生成。
            if (generated || !ModsConfig.OdysseyActive) return;

            int now = Find.TickManager.TicksGame;
            if (!scheduled)
            {
                if (now % ConditionCheckInterval != 0 || now < MinimumGameTicks) return;
                if (DefDatabase<ResearchProjectDef>.GetNamedSilentFail("MAP_QuantumMechtech")?.IsFinished != true
                    || WealthUtility.PlayerWealth < MinimumWealth) return;

                // 仅在首次满足条件时随机一次。财富与研究状态之后不再参与调度。
                scheduledGenerationTick = now + Rand.RangeInclusive(15 * GenDate.TicksPerDay, 30 * GenDate.TicksPerDay);
                nextGenerationAttemptTick = scheduledGenerationTick;
                scheduled = true;
            }

            if (now < scheduledGenerationTick || now < nextGenerationAttemptTick) return;

            // 无可用地块等临时失败每天重试，但不改动最初的生成时间，也不重新抽签。
            nextGenerationAttemptTick = now + ConditionCheckInterval;
            Site? site = SunBossSiteUtility.TryCreateNaturalSite();
            if (site == null) return;

            // 创建入口已将设施加入世界。先永久置位，再发送信件，防止通知回调重复生成。
            generated = true;
            Find.LetterStack.ReceiveLetter(
                "MAP_SunBoss_FacilityFoundLabel".Translate(),
                "MAP_SunBoss_FacilityFoundText".Translate(),
                LetterDefOf.PositiveEvent, site);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref scheduled, "sunBossSiteScheduled", false);
            Scribe_Values.Look(ref scheduledGenerationTick, "sunBossSiteScheduledGenerationTick", -1);
            Scribe_Values.Look(ref generated, "sunBossSiteGenerated", false);
            Scribe_Values.Look(ref nextGenerationAttemptTick, "sunBossSiteNextGenerationAttemptTick", -1);
        }
    }
}
