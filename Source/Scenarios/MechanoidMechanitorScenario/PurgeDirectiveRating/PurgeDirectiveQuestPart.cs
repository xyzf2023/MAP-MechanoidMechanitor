using System;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 肃清评级任务的独立状态机。仅管理「玩家摧毁敌对普通派系据点」这一目标的结算，
    /// 与共生盟约的联合行动状态机完全独立，不读取盟约成员 / 信任度 / 团结度 / 参与派系 /
    /// 援军 / Lord 等任何代码。状态转换：
    ///  OfferPending →（玩家接取）→ OperationActive →（目标被摧毁）→ 成功（基础奖励，按稳定ID去重）
    ///                                          →（超时）→ 失败（-1000，不重复）
    ///                                          →（目标失去敌对 / 接管主脑 / 失效）→ 无处罚结束
    ///  OfferPending →（玩家拒绝 / 抉择期过期）→ 无处罚结束
    /// </summary>
    public class PurgeDirectiveQuestPart : QuestPartActivable
    {
        public WorldObject? targetWorldObject;
        public Faction? targetFaction;
        public Faction? proposerFaction;
        public string? purgeQuestConfigDefName;
        public int rewardValue;
        public int operationTimeoutTicks;
        public string? questTag;

        private enum Stage : byte
        {
            OfferPending,
            OperationActive,
            Ended
        }

        private Stage stage = Stage.OfferPending;
        private int operationExpireTick = -1;

        public bool IsActive => true;

        public bool IsOperationActive => stage == Stage.OperationActive;

        public override void PreQuestAccept()
        {
            if (stage == Stage.OfferPending)
            {
                stage = Stage.OperationActive;
                operationExpireTick = Find.TickManager.TicksGame + operationTimeoutTicks;
            }
        }

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (stage != Stage.OperationActive)
            {
                return;
            }

            // 接管主脑：现存评级任务无处罚结束。
            if (GameComponent_CerebrexTakeoverState.IsActive)
            {
                EndWithoutPenalty();
                return;
            }

            // 目标失去敌对：无处罚结束。
            if (targetFaction != null && !targetFaction.HostileTo(Faction.OfPlayer))
            {
                EndWithoutPenalty();
                return;
            }

            // 目标被摧毁：玩家结算路径 → 成功（发放奖励）。
            if (targetWorldObject != null && targetWorldObject.Destroyed)
            {
                Success();
                return;
            }

            // 完成期限超时：失败（-1000，不重复）。
            if (operationExpireTick > 0 && Find.TickManager.TicksGame >= operationExpireTick)
            {
                Fail();
            }
        }

        /// <summary>由 WorldObject 销毁钩子调用：玩家摧毁目标据点 → 成功。</summary>
        public void NotifyTargetDestroyed()
        {
            if (stage == Stage.OperationActive)
            {
                Success();
            }
        }

        private void Success()
        {
            if (stage == Stage.Ended)
            {
                return;
            }

            stage = Stage.Ended;
            if (targetWorldObject != null)
            {
                // 世界目标基础奖励（200 点）按稳定ID持久化去重，同时增加肃清额度与等量评级。
                PurgeDirectiveRatingUtility.TryGrantWorldTargetBaseReward(
                    targetWorldObject,
                    PurgeDirectiveQuestConfigDefOf.MAP_PurgeDirectiveQuestConfig.baseSuccessRewardPoints);
                // 任务额外奖励（rewardValue）：增加肃清额度与等量评级。
                GameComponent_MechanoidMechanitorStoryState.TryAddPurgeDirectiveRewardPoints(rewardValue);
            }

            EndQuest(QuestEndOutcome.Success);
        }

        private void Fail()
        {
            if (stage == Stage.Ended)
            {
                return;
            }

            stage = Stage.Ended;
            // 任务失败/放弃/超时处罚：按目标类型取处罚值（据点 -600 / 工作站 -150 / 前哨 -300），不扣肃清额度。
            PurgeDirectiveRatingUtility.ApplyPenalty(
                PurgeDirectiveRatingUtility.GetQuestFailurePenalty(targetWorldObject));
            EndQuest(QuestEndOutcome.Fail);
        }

        private void EndWithoutPenalty()
        {
            if (stage == Stage.Ended)
            {
                return;
            }

            stage = Stage.Ended;
            EndQuest(QuestEndOutcome.Unknown);
        }

        private void EndQuest(QuestEndOutcome outcome)
        {
            PurgeDirectiveQuestScheduler.NotifyQuestEnded();
            quest?.End(outcome);
        }

        public override void Notify_PreCleanup()
        {
            // 任务以任意方式结束（拒绝 / 抉择期过期 / 接管 / 失效）而此时仍处于 OfferPending：
            // 视为拒绝，无处罚结束，仅进入冷却。
            if (stage != Stage.Ended)
            {
                stage = Stage.Ended;
                PurgeDirectiveQuestScheduler.NotifyQuestEnded();
            }

            base.Notify_PreCleanup();
        }

        /// <summary>供 WorldObject 销毁钩子调用：若本任务正在针对该目标进行，则记为成功。</summary>
        public static void NotifyWorldObjectDestroyed(WorldObject destroyed)
        {
            if (destroyed == null)
            {
                return;
            }

            QuestScriptDef? questScriptDef = PurgeDirectiveQuestTargetUtility.ResolveQuestScriptDef();
            if (questScriptDef == null)
            {
                return;
            }

            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.root != questScriptDef || quest.State != QuestState.Ongoing)
                {
                    continue;
                }

                foreach (QuestPart part in quest.PartsListForReading)
                {
                    if (part is PurgeDirectiveQuestPart purgePart
                        && purgePart.IsOperationActive
                        && purgePart.targetWorldObject == destroyed)
                    {
                        purgePart.NotifyTargetDestroyed();
                    }
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref targetWorldObject, "pdqTargetWorldObject");
            Scribe_References.Look(ref targetFaction, "pdqTargetFaction");
            Scribe_References.Look(ref proposerFaction, "pdqProposerFaction");
            Scribe_Values.Look(ref purgeQuestConfigDefName, "pdqConfigDefName");
            Scribe_Values.Look(ref rewardValue, "pdqRewardValue");
            Scribe_Values.Look(ref operationTimeoutTicks, "pdqOperationTimeoutTicks");
            Scribe_Values.Look(ref questTag, "pdqQuestTag");
            Scribe_Values.Look(ref stage, "pdqStage");
            Scribe_Values.Look(ref operationExpireTick, "pdqOperationExpireTick");
        }
    }
}
