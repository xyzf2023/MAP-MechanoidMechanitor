using LudeonTK;
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

        private void AdvanceSchedule(bool bypassUnlockConditions = false)
        {
            // 已生成时永久停止；移除设施或读档都不会重新开放自然生成。
            if (generated || !ModsConfig.OdysseyActive) return;

            int now = Find.TickManager.TicksGame;
            if (!scheduled)
            {
                if (!bypassUnlockConditions)
                {
                    if (now % ConditionCheckInterval != 0 || now < MinimumGameTicks) return;
                    if (DefDatabase<ResearchProjectDef>.GetNamedSilentFail("MAP_QuantumMechtech")?.IsFinished != true
                        || WealthUtility.PlayerWealth < MinimumWealth) return;
                }

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

        [DebugAction("MAP-机械族机械师", "太阳据点：启动自然生成调度（跳过解锁条件）", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void DebugStartNaturalGeneration()
        {
            GameComponent_SunBossSiteScheduler? scheduler = CurrentGameComponentCache<GameComponent_SunBossSiteScheduler>.Get();
            if (scheduler == null || !ModsConfig.OdysseyActive)
            {
                Messages.Message("需要在已启用奥德赛的游戏中使用。", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            if (scheduler.generated)
            {
                Messages.Message("太阳设施已经自然生成过，本局不会再次生成。", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            // 只绕过首次解锁检查；既有排期、失败重试间隔和永久防重标记均沿用正常调度。
            scheduler.AdvanceSchedule(bypassUnlockConditions: true);
            if (scheduler.generated) return;

            Messages.Message($"太阳设施已进入自然生成调度，预定游戏 Tick：{scheduler.scheduledGenerationTick}。既有排期不会重置；到期失败时按原调度重试。",
                MessageTypeDefOf.NeutralEvent, historical: false);
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
