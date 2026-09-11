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
    /// 调度状态（下一次检查节流、下一次尝试的绝对 tick）保存在主脑运行时状态中，随存档持久化；
    /// 绝对 tick 不得每 2500 tick 重新随机。通过 Harmony 挂接到主脑 GameComponent 的
    /// Tick / ExposeData 触发，避免改动既有调用链。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class PurgeDirectiveQuestScheduler
    {
        private const int CheckIntervalTicks = 2500;

        // 主脑接管清理是否已完成（仅运行时）：首次检测到接管时清理一次，之后只廉价返回。
        private static bool takeoverCleanupCompleted;

        private static readonly QuestScriptDef? questScriptDef =
            PurgeDirectiveQuestConfigDefOf.MAP_PurgeDirectiveQuestConfig != null
                ? DefDatabase<QuestScriptDef>.GetNamedSilentFail(
                    PurgeDirectiveQuestConfigDefOf.MAP_PurgeDirectiveQuestConfig.questScriptDef)
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

        private static PurgeDirectiveRatingConfigDef RatingConfig =>
            PurgeDirectiveRatingConfigDefOf.MAP_PurgeDirectiveRatingConfig;

        public static void PostTick(GameComponent_MechanoidMechanitorStoryState __instance)
        {
            Tick();
        }

        public static void PostExpose()
        {
            // 仅重置「约 2500 tick 的检查节流」；绝对下一次尝试 tick 由运行时状态持久化，不得漂移。
            if (Scribe.mode != LoadSaveMode.PostLoadInit)
            {
                return;
            }

            // 读档后至少重新校验一次主脑接管状态。
            takeoverCleanupCompleted = false;

            GameComponent_MechanoidMechanitorStoryState? story =
                CurrentGameComponentCache<GameComponent_MechanoidMechanitorStoryState>.Get();
            MechanoidMechanitorPurgeDirectiveRuntimeState? rs = story?.PurgeDirectiveRuntimeState;
            if (rs != null)
            {
                rs.SetNextPurgeQuestCheckTick(Find.TickManager.TicksGame + CheckIntervalTicks);
            }
        }

        public static void Tick()
        {
            GameComponent_MechanoidMechanitorStoryState? story =
                CurrentGameComponentCache<GameComponent_MechanoidMechanitorStoryState>.Get();
            MechanoidMechanitorPurgeDirectiveRuntimeState? rs =
                story?.PurgeDirectiveRuntimeState;
            if (rs == null)
            {
                return;
            }

            // 接管主脑：安全结束所有活动评级任务（无处罚），不再生成新任务。
            // 首次检测执行一次清理，之后每 Tick 只廉价返回。
            if (GameComponent_CerebrexTakeoverState.IsActive)
            {
                if (!takeoverCleanupCompleted)
                {
                    EndAllActivePurgeQuestsWithoutPenalty();
                    takeoverCleanupCompleted = true;
                }
                return;
            }
            takeoverCleanupCompleted = false;

            int now = Find.TickManager.TicksGame;

            // 正常流程：未到检查时间立即返回，不执行评级/惩罚/任务条件查询。
            if (rs.NextPurgeQuestCheckTick > 0 && now < rs.NextPurgeQuestCheckTick)
            {
                return;
            }

            // 到期后再检查评级系统、最终惩罚与任务生成条件。
            if (!PurgeDirectiveRatingUtility.IsRatingSystemActive()
                || PurgeDirectiveRatingUtility.IsFinalPenaltyTriggered())
            {
                return;
            }

            rs.SetNextPurgeQuestCheckTick(now + CheckIntervalTicks);

            PurgeDirectiveRatingConfigDef cfg = RatingConfig;
            if (cfg == null || questScriptDef == null)
            {
                return;
            }

            // 绝对下一次尝试时间未到：不生成（不重新随机）。
            if (now < rs.PurgeQuestCooldownEndTick)
            {
                return;
            }

            // 已有活动任务（含待接受邀请）则跳过，保证 maxActive 限制。
            if (!CanGenerateQuest(story!))
            {
                return;
            }

            WorldObject? target = PurgeDirectiveQuestTargetUtility.SelectTarget(
                PurgeDirectiveRatingUtility.CurrentRatingLevel);
            if (target == null)
            {
                // 到期但无合法目标：1 天后重试（不生成）。
                rs.SchedulePurgeQuestAttemptAfter(cfg.questNoTargetRetryDays);
                return;
            }

            GenerateQuest(target, cfg);
        }

        /// <summary>接管成功入口：立即无处罚清理活动评级任务，并阻止后续任务生成。</summary>
        public static void NotifyCerebrexTakenOver()
        {
            EndAllActivePurgeQuestsWithoutPenalty();
            takeoverCleanupCompleted = true;
        }

        /// <summary>是否满足「可生成任务」的全部前置：游戏存在、叙事者允许暴力任务、机械巢可联络、无活动任务。</summary>
        private static bool CanGenerateQuest(GameComponent_MechanoidMechanitorStoryState story)
        {
            if (Current.Game == null) return false;
            if (!Find.Storyteller.difficulty.allowViolentQuests) return false;
            if (!MechanoidMechanitorMechHiveCommunicationUtility.TryGetContactableMechHive(out _)) return false;
            if (AnyActivePurgeQuest()) return false;
            return true;
        }

        /// <summary>
        /// 任意任务结束（成功/失败/拒绝/超时/无效）后调用：安排 7~10 天后再尝试。
        /// 无处罚结束同样进入此冷却。
        /// </summary>
        public static void NotifyQuestEnded()
        {
            GameComponent_MechanoidMechanitorStoryState? story =
                Current.Game?.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            MechanoidMechanitorPurgeDirectiveRuntimeState? rs = story?.PurgeDirectiveRuntimeState;
            if (rs == null) return;

            PurgeDirectiveRatingConfigDef cfg = RatingConfig;
            if (cfg == null) return;

            rs.SchedulePurgeQuestAttemptRange(cfg.questRetryDelayDaysMin, cfg.questRetryDelayDaysMax);
        }

        /// <summary>DEV：立即将下一次检查时间设为当前，并绕过冷却强制尝试调度。</summary>
        public static void ForceDueNow()
        {
            GameComponent_MechanoidMechanitorStoryState? story =
                Current.Game?.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            MechanoidMechanitorPurgeDirectiveRuntimeState? rs = story?.PurgeDirectiveRuntimeState;
            if (rs != null)
            {
                rs.SchedulePurgeQuestAttemptAfter(0);
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
                rs.SchedulePurgeQuestAttemptAfter(0);
            }
        }

        /// <summary>活动任务计数：本MOD肃清任务中尚未真正结束的（含待接受邀请与进行中）都计入。</summary>
        private static bool AnyActivePurgeQuest()
        {
            if (questScriptDef == null)
            {
                return false;
            }

            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.root != questScriptDef
                    || (quest.State != QuestState.Ongoing
                        && quest.State != QuestState.NotYetAccepted))
                {
                    continue;
                }

                foreach (QuestPart part in quest.PartsListForReading)
                {
                    if (part is PurgeDirectiveQuestPart purgePart
                        && (purgePart.IsOfferPending || purgePart.IsOperationActive))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>接管主脑发生时：无处罚结束所有尚未真正结束的活动评级任务（含待接受邀请）。</summary>
        private static void EndAllActivePurgeQuestsWithoutPenalty()
        {
            if (questScriptDef == null)
            {
                return;
            }

            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.root != questScriptDef
                    || (quest.State != QuestState.Ongoing
                        && quest.State != QuestState.NotYetAccepted))
                {
                    continue;
                }

                foreach (QuestPart part in quest.PartsListForReading)
                {
                    if (part is PurgeDirectiveQuestPart purgePart
                        && (purgePart.IsOfferPending || purgePart.IsOperationActive))
                    {
                        purgePart.EndWithoutPenalty();
                    }
                }
            }
        }

        private static void GenerateQuest(WorldObject target, PurgeDirectiveRatingConfigDef cfg)
        {
            if (cfg == null || questScriptDef == null)
            {
                return;
            }

            Faction? targetFaction = target.Faction;
            if (!MechanoidMechanitorMechHiveCommunicationUtility.TryGetContactableMechHive(
                    out Faction proposerFaction))
            {
                return;
            }

            string? stableId = PurgeDirectiveQuestTargetUtility.TryGetStableId(target);
            PurgeDirectiveTargetType type = PurgeDirectiveQuestTargetUtility.ClassifyTargetType(target);
            int extraReward = PurgeDirectiveRatingUtility.GetQuestExtraReward(type);
            int offerTimeout = cfg.questOfferTimeoutDays * 60000;
            int operationTimeout = cfg.questOperationTimeoutDays * 60000;

            Slate slate = new Slate();
            slate.Set("targetWorldObject", target);
            slate.Set("targetFaction", targetFaction);
            slate.Set("proposerFaction", proposerFaction);
            slate.Set("purgeQuestConfigDefName", cfg.defName);
            slate.Set("rewardValue", extraReward);
            slate.Set("offerTimeoutTicks", offerTimeout);
            slate.Set("operationTimeoutTicks", operationTimeout);

            Quest quest = QuestUtility.GenerateQuestAndMakeAvailable(questScriptDef, slate);
            if (quest != null)
            {
                if (stableId != null)
                {
                    // 仅在任务实际生成成功后登记历史目标，避免生成异常永久浪费目标。
                    PurgeDirectiveRatingUtility.Runtime?.MarkQuestTargetUsed(stableId);
                }

                quest.name = "MAP_PurgeDirectiveRating.Quest.Title".Translate(target.LabelCap);
            }
        }
    }
}
