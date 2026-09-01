using System;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 肃清评级任务的独立状态机。仅管理「玩家攻克敌对普通派系目标」的结算，
    /// 与共生盟约的联合行动状态机完全独立，不读取盟约成员 / 信任度 / 团结度 / 参与派系 /
    /// 援军 / Lord 等任何代码。
    ///
    /// 状态转换（QuestPart 是活动任务状态的唯一权威来源，GameComponent 不保存第二份阶段）：
    ///  OfferPending →（玩家接取）→ OperationActive →（真实完成信号/目标被摧毁）→ 成功
    ///                                          →（超时）→ 失败（按目标类型扣评级，不扣肃清额度）
    ///                                          →（目标失去敌对 / 接管主脑 / 失效）→ 无处罚结束
    ///  OfferPending →（玩家拒绝 / 抉择期过期）→ 无处罚结束
    ///
    /// 成功结算顺序：确保基础200点(按稳定ID去重) → 发放任务额外奖励(按目标类型) → 标记状态 → 信 → 结束。
    /// 完成信号幂等：无论 NotifyTargetDestroyed / 守军清除通知 / Tick 触发多少次，Success 只结算一次。
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

        // 目标类型与稳定ID在任务生成时确定并持久化，结算时不再用模糊的「任意 Site」临时猜测。
        public PurgeDirectiveTargetType targetType = PurgeDirectiveTargetType.Invalid;
        public string? targetStableId;

        private enum Stage : byte
        {
            OfferPending,
            OperationActive,
            Ended
        }

        private Stage stage = Stage.OfferPending;
        private int offerExpireTick = -1;
        private int operationExpireTick = -1;

        // 各结算动作只允许发生一次（幂等），读档后不得重复奖励/处罚/冷却。
        private bool baseRewardHandled;
        private bool extraRewardHandled;
        private bool penaltyHandled;
        private bool cooldownHandled;

        public bool IsOperationActive => stage == Stage.OperationActive;

        public bool IsOfferPending => stage == Stage.OfferPending;

        public bool IsEnded => stage == Stage.Ended;

        public int OfferRemainTicks()
        {
            if (offerExpireTick <= 0) return 0;
            return Mathf.Max(0, offerExpireTick - Find.TickManager.TicksGame);
        }

        public int ActionRemainTicks()
        {
            if (operationExpireTick <= 0) return 0;
            return Mathf.Max(0, operationExpireTick - Find.TickManager.TicksGame);
        }

        public override void PreQuestAccept()
        {
            if (stage == Stage.OfferPending)
            {
                stage = Stage.OperationActive;
                offerExpireTick = Find.TickManager.TicksGame
                    + (PurgeDirectiveRatingConfigDefOf.MAP_PurgeDirectiveRatingConfig?.questOfferTimeoutDays ?? 3) * 60000;
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

            // 目标失去敌对（转中立/友好/被消灭等）：无处罚失效，不是成功。
            if (targetFaction != null && !targetFaction.HostileTo(Faction.OfPlayer))
            {
                EndWithoutPenalty();
                return;
            }

            // 目标引用失效（已销毁但未走成功路径、未登记等）：无处罚失效。
            if (targetWorldObject == null || targetWorldObject.Destroyed || !targetWorldObject.Spawned)
            {
                // 工作站守军清除由 WorkSiteDefeatedPatch 显式通知成功；
                // 据点/前哨的真实摧毁信号亦走显式通知或 Destroy 兜底。
                if (targetWorldObject != null && targetWorldObject.Destroyed
                    && PurgeDirectiveRatingUtility.IsRatingSystemActive())
                {
                    Success();
                    return;
                }

                EndWithoutPenalty();
                return;
            }

            // 完成期限超时：按目标类型扣评级，不扣肃清额度。
            if (operationExpireTick > 0 && Find.TickManager.TicksGame >= operationExpireTick)
            {
                Fail();
            }
        }

        /// <summary>由 WorldObject 销毁钩子调用：玩家摧毁目标 → 成功（兜底/分发，需正处于操作阶段）。</summary>
        public void NotifyTargetDestroyed()
        {
            if (stage == Stage.OperationActive && PurgeDirectiveRatingUtility.IsRatingSystemActive())
            {
                Success();
            }
        }

        /// <summary>由守军清除/完成信号调用（如工作站 AllEnemiesDefeated）：若正在针对该目标操作则记为成功。</summary>
        public void NotifyTargetDefeated(WorldObject? obj)
        {
            if (stage == Stage.OperationActive
                && obj != null
                && obj == targetWorldObject
                && PurgeDirectiveRatingUtility.IsRatingSystemActive())
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

            // 1) 确保基础攻克奖励（按世界目标稳定ID去重，同时增加肃清额度与等量评级）。
            if (targetWorldObject != null && !baseRewardHandled)
            {
                PurgeDirectiveRatingUtility.TryGrantWorldTargetBaseReward(
                    targetWorldObject,
                    PurgeDirectiveRatingConfigDefOf.MAP_PurgeDirectiveRatingConfig?.questBaseRewardPoints ?? 200);
                baseRewardHandled = true;
            }

            // 2) 任务额外奖励（按目标类型，同时增加肃清额度与等量评级）。
            if (!extraRewardHandled)
            {
                int extra = PurgeDirectiveRatingUtility.GetQuestExtraReward(targetType);
                if (extra > 0)
                {
                    PurgeDirectiveRatingUtility.TryAddPurgeDirectiveRewardPoints(extra);
                }

                extraRewardHandled = true;
            }

            // 3) 发送完成提示。
            PurgeDirectiveRatingLetterUtility.SendQuestCompleteLetter(
                targetWorldObject,
                targetType,
                (PurgeDirectiveRatingConfigDefOf.MAP_PurgeDirectiveRatingConfig?.questBaseRewardPoints ?? 200)
                + PurgeDirectiveRatingUtility.GetQuestExtraReward(targetType));

            // 4) 结束并安排 7~10 天后再尝试。
            EndQuest(QuestEndOutcome.Success);
        }

        private void Fail()
        {
            if (stage == Stage.Ended)
            {
                return;
            }

            stage = Stage.Ended;
            if (!penaltyHandled)
            {
                // 任务失败/放弃/超时：按目标类型扣评级（不扣肃清额度）。
                PurgeDirectiveRatingUtility.ApplyQuestFailurePenalty(targetType);
                penaltyHandled = true;
            }

            EndQuest(QuestEndOutcome.Fail);
        }

        public void EndWithoutPenalty()
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
            if (!cooldownHandled)
            {
                PurgeDirectiveQuestScheduler.NotifyQuestEnded();
                cooldownHandled = true;
            }

            quest?.End(outcome);
        }

        public override void Notify_PreCleanup()
        {
            // 任务以任意方式结束（拒绝 / 抉择期过期 / 接管 / 失效）而此时仍处于 OfferPending/OperationActive：
            // 视为无处罚结束，仅安排冷却。失败/成功已在对应路径处理，不会重复处罚或奖励。
            if (stage != Stage.Ended)
            {
                stage = Stage.Ended;
                if (!cooldownHandled)
                {
                    PurgeDirectiveQuestScheduler.NotifyQuestEnded();
                    cooldownHandled = true;
                }
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

        /// <summary>由守军清除/完成信号调用（如工作站 AllEnemiesDefeated）：若本任务正在针对该目标则记为成功。</summary>
        public static void NotifyActiveQuestTargetDefeated(WorldObject? obj)
        {
            if (obj == null)
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
                        && purgePart.targetWorldObject == obj)
                    {
                        purgePart.NotifyTargetDefeated(obj);
                    }
                }
            }
        }

        /// <summary>DEV：强制当前操作阶段任务按失败结算（幂等，按目标类型扣评级，不扣肃清额度）。</summary>
        public void ForceFail()
        {
            Fail();
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
            Scribe_Values.Look(ref targetType, "pdqTargetType");
            Scribe_Values.Look(ref targetStableId, "pdqTargetStableId");
            Scribe_Values.Look(ref stage, "pdqStage");
            Scribe_Values.Look(ref offerExpireTick, "pdqOfferExpireTick");
            Scribe_Values.Look(ref operationExpireTick, "pdqOperationExpireTick");
            Scribe_Values.Look(ref baseRewardHandled, "pdqBaseRewardHandled");
            Scribe_Values.Look(ref extraRewardHandled, "pdqExtraRewardHandled");
            Scribe_Values.Look(ref penaltyHandled, "pdqPenaltyHandled");
            Scribe_Values.Look(ref cooldownHandled, "pdqCooldownHandled");
        }
    }
}
