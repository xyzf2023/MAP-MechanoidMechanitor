using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 肃清评级任务调度器。独立状态机：只负责「是否生成任务」，不参与共生盟约逻辑，
    /// 也不读取盟约成员 / 信任度 / 团结度 / 参与派系 / 援军 / Lord 等任何代码。
    ///
    /// 调度状态（下一次检查时间、冷却到期时间）保存在主脑运行时状态中，随存档持久化。
    /// 通过 Harmony 挂接到主脑 GameComponent 的 Tick / ExposeData 触发，避免改动既有调用链。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class PurgeDirectiveQuestScheduler
    {
        private const int CheckIntervalTicks = 2500;

        private static readonly QuestScriptDef? questScriptDef =
            PurgeDirectiveQuestConfigDefOf.MAP_PurgeDirectiveQuestConfig != null
                ? DefDatabase<QuestScriptDef>.GetNamedSilentFail(
                    PurgeDirectiveQuestConfigDefOf.MAP_PurgeDirectiveQuestConfig.questScriptDefName)
                : null;

        static PurgeDirectiveQuestScheduler()
        {
            Harmony harmony = new Harmony("MAP.PurgeDirectiveRating.QuestScheduler");
            harmony.Patch(
                AccessTools.Method(typeof(GameComponent_MechanoidMechanitorStoryState), "GameComponentTick"),
                postfix: new HarmonyMethod(typeof(PurgeDirectiveQuestScheduler), nameof(PostTick)));
            harmony.Patch(
                AccessTools.Method(typeof(GameComponent_MechanoidMechanitorStoryState), "ExposeData"),
                postfix: new HarmonyMethod(typeof(PurgeDirectiveQuestScheduler), nameof(PostExpose)));
        }

        private static PurgeDirectiveQuestConfigDef Config =>
            PurgeDirectiveQuestConfigDefOf.MAP_PurgeDirectiveQuestConfig;

        public static void PostTick(GameComponent_MechanoidMechanitorStoryState __instance)
        {
            Tick();
        }

        public static void PostExpose()
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit)
            {
                return;
            }

            GameComponent_MechanoidMechanitorStoryState? story =
                Current.Game?.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            MechanoidMechanitorPurgeDirectiveRuntimeState? rs = story?.PurgeDirectiveRuntimeState;
            if (rs != null)
            {
                rs.SetNextPurgeQuestCheckTick(Find.TickManager.TicksGame + CheckIntervalTicks);
            }
        }

        public static void Tick()
        {
            GameComponent_MechanoidMechanitorStoryState? story =
                Current.Game?.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            if (story == null)
            {
                return;
            }

            // 接管主脑或肃清额度未启用：停止评级任务调度。
            if (!PurgeDirectiveRatingUtility.IsRatingSystemActive())
            {
                return;
            }

            MechanoidMechanitorPurgeDirectiveRuntimeState? rs = story.PurgeDirectiveRuntimeState;
            if (rs == null)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            if (rs.NextPurgeQuestCheckTick > 0 && now < rs.NextPurgeQuestCheckTick)
            {
                return;
            }

            rs.SetNextPurgeQuestCheckTick(now + CheckIntervalTicks);

            if (Config == null)
            {
                return;
            }

            // 冷却期内不生成。
            if (now < rs.PurgeQuestCooldownEndTick)
            {
                return;
            }

            // 评级不足不生成。
            if (PurgeDirectiveRatingUtility.CurrentRatingLevel < Config.minRatingLevel)
            {
                return;
            }

            // 已有进行中/待抉择任务则跳过（保证 maxActive 限制）。
            if (AnyActivePurgeQuest())
            {
                return;
            }

            WorldObject? target = PurgeDirectiveQuestTargetUtility.TryGetValidTarget();
            if (target == null)
            {
                return;
            }

            GenerateQuest(target, story);
        }

        /// <summary>
        /// 任务结束（成功/失败/拒绝/超时/目标失效/接管）后调用，进入冷却期。
        /// </summary>
        public static void NotifyQuestEnded()
        {
            if (Config == null)
            {
                return;
            }

            GameComponent_MechanoidMechanitorStoryState? story =
                Current.Game?.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            MechanoidMechanitorPurgeDirectiveRuntimeState? rs = story?.PurgeDirectiveRuntimeState;
            if (rs == null)
            {
                return;
            }

            int cooldownTicks = Config.idleCooldownDays * 60000;
            rs.SetPurgeQuestCooldownEndTick(Find.TickManager.TicksGame + cooldownTicks);
        }

        /// <summary>DEV：立即将下一次检查时间设为当前，绕过间隔。</summary>
        public static void ForceDueNow()
        {
            GameComponent_MechanoidMechanitorStoryState? story =
                Current.Game?.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            MechanoidMechanitorPurgeDirectiveRuntimeState? rs = story?.PurgeDirectiveRuntimeState;
            if (rs != null)
            {
                rs.SetNextPurgeQuestCheckTick(Find.TickManager.TicksGame);
                Tick();
            }
        }

        /// <summary>DEV：清空冷却，使任务可立即再次调度。</summary>
        public static void ClearCooldown()
        {
            GameComponent_MechanoidMechanitorStoryState? story =
                Current.Game?.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            MechanoidMechanitorPurgeDirectiveRuntimeState? rs = story?.PurgeDirectiveRuntimeState;
            if (rs != null)
            {
                rs.SetPurgeQuestCooldownEndTick(0);
            }
        }

        private static bool AnyActivePurgeQuest()
        {
            if (questScriptDef == null)
            {
                return false;
            }

            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.root == questScriptDef && quest.State == QuestState.Ongoing)
                {
                    return true;
                }
            }

            return false;
        }

        private static void GenerateQuest(WorldObject target, GameComponent_MechanoidMechanitorStoryState story)
        {
            if (Config == null || questScriptDef == null)
            {
                return;
            }

            Faction? targetFaction = target.Faction;

            int rewardValue = Rand.RangeInclusive(Config.minRewardValue, Config.maxRewardValue);
            int offerTimeout = Config.offerTimeoutDays * 60000;
            int operationTimeout = Config.operationTimeoutDays * 60000;

            Slate slate = new Slate();
            slate.Set("targetWorldObject", target);
            slate.Set("targetFaction", targetFaction);
            slate.Set("proposerFaction", Faction.OfPlayer);
            slate.Set("purgeQuestConfigDefName", Config.defName);
            slate.Set("rewardValue", rewardValue);
            slate.Set("offerTimeoutTicks", offerTimeout);
            slate.Set("operationTimeoutTicks", operationTimeout);

            Quest quest = QuestUtility.GenerateQuestAndMakeAvailable(questScriptDef, slate);
            if (quest != null)
            {
                quest.name = "MAP_PurgeDirectiveRating.Quest.Title".Translate(target.LabelCap);
            }
        }
    }
}
