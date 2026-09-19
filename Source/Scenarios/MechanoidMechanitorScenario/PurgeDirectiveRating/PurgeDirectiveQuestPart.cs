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
    ///  OfferPending →（玩家接取）→ OperationActive →（明确守军击败信号）→ 成功
    ///                                          →（超时）→ 失败（按目标类型扣评级，不扣肃清额度）
    ///                                          →（目标失去敌对 / 接管主脑 / 失效）→ 无处罚结束
    ///  OfferPending →（玩家拒绝 / 抉择期过期）→ 无处罚结束
    ///
    /// 成功结算顺序：确保基础200点(按稳定ID去重) → 发放任务额外奖励(按目标类型) → 登记待结束结果 → 信 → 结束。
    /// 完成信号幂等：无论守军清除通知 / Tick 重试多少次，奖励与 Success 都只结算一次。
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
        private bool completionConfirmed;
        private bool endPending;
        private QuestEndOutcome pendingEndOutcome = QuestEndOutcome.Unknown;
        private bool completionLetterHandled;
        private bool ending;

        public bool IsOperationActive => stage == Stage.OperationActive;

        public bool IsOfferPending => stage == Stage.OfferPending;

        public bool IsEnded => stage == Stage.Ended;

        /// <summary>在正常运行期恢复旧存档未启用的任务，不重置接受时间或行动期限。</summary>
        internal void EnsureTicking()
        {
            if (quest == null || quest.Historical
                || (quest.State != QuestState.Ongoing && !endPending && stage != Stage.Ended))
            {
                return;
            }

            // 旧版本可能已写入 Ended，却在发信或 End 前抛错；保留唯一性并补完结束。
            if (stage == Stage.Ended && !endPending)
            {
                endPending = true;
                pendingEndOutcome = completionConfirmed && baseRewardHandled && extraRewardHandled
                    ? QuestEndOutcome.Success
                    : penaltyHandled ? QuestEndOutcome.Fail : QuestEndOutcome.Unknown;
                completionLetterHandled = true;
            }

            if (State == QuestPartState.NeverEnabled && (IsOperationActive || endPending))
            {
                Enable(default(SignalArgs));
            }
        }

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

        public void InitializeOfferExpiry(int absoluteTick)
        {
            offerExpireTick = absoluteTick;
        }

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
            if (endPending)
            {
                TryFinishEnding();
                return;
            }

            if (stage != Stage.OperationActive)
            {
                return;
            }

            // 接管主脑优先于一切完成重试：现存评级任务必须立即无处罚结束。
            if (GameComponent_CerebrexTakeoverState.IsActive)
            {
                EndWithoutPenalty();
                return;
            }

            // 明确完成信号已到达但奖励提交暂时失败时，持续重试，不把目标后续销毁误判为无效。
            if (completionConfirmed)
            {
                Success();
                return;
            }

            // 任务约定的目标一旦换主或不再具备敌对资格，无处罚失效；不把新所有者
            // 自动接入旧任务，也不因旧派系快照仍敌对而继续计时。
            if (!HasOriginalHostileOwner()
                || !PurgeDirectiveQuestTargetUtility.IsHostileTargetFaction(targetWorldObject?.Faction))
            {
                EndWithoutPenalty();
                return;
            }

            // 只有守军击败补丁发送的明确完成信号才算成功；任意 Destroy/失去登记均无处罚失效。
            if (targetWorldObject == null || targetWorldObject.Destroyed || !targetWorldObject.Spawned)
            {
                EndWithoutPenalty();
                return;
            }

            // 完成期限超时：按目标类型扣评级，不扣肃清额度。
            if (operationExpireTick > 0 && Find.TickManager.TicksGame >= operationExpireTick)
            {
                Fail();
            }
        }

        /// <summary>由守军清除/完成信号调用（如工作站 AllEnemiesDefeated）：若正在针对该目标操作则记为成功。</summary>
        public void NotifyTargetDefeated(WorldObject? obj)
        {
            if (stage == Stage.OperationActive
                && obj != null
                && obj == targetWorldObject
                && HasOriginalHostileOwner()
                && PurgeDirectiveRatingUtility.IsRatingSystemActive())
            {
                completionConfirmed = true;
                Success();
            }
        }

        private bool HasOriginalHostileOwner()
        {
            Faction? player = Faction.OfPlayerSilentFail;
            // 完成通知可能在最后一座据点销毁、派系标记 defeated 后才到达；这里不以
            // Spawned/defeated 拒绝真实完成，只核对原归属与仍存在的敌对关系记录。
            return player != null
                && targetFaction != null
                && targetWorldObject?.Faction == targetFaction
                && targetFaction.RelationWith(player, allowNull: true)?.kind == FactionRelationKind.Hostile;
        }

        private void Success()
        {
            if (stage == Stage.Ended || endPending || !completionConfirmed)
            {
                return;
            }

            int baseReward =
                PurgeDirectiveRatingConfigDefOf.MAP_PurgeDirectiveRatingConfig?.questBaseRewardPoints ?? 200;

            // 先确认历史去重状态；只有奖励已存在或本次实际提交成功，才标记为已处理。
            if (!baseRewardHandled)
            {
                if (string.IsNullOrEmpty(targetStableId))
                {
                    targetStableId = PurgeDirectiveQuestTargetUtility.TryGetStableId(targetWorldObject);
                }

                if (string.IsNullOrEmpty(targetStableId))
                {
                    // 连去重身份也已丢失时无法安全补奖；明确结束，避免永久占用任务槽。
                    Log.Error("[MAP-机械族机械师] 已完成肃清任务丢失目标及稳定 ID，无法安全补发基础奖励，已无处罚结束。");
                    EndWithoutPenalty();
                    return;
                }

                MechanoidMechanitorPurgeDirectiveRuntimeState? runtime =
                    PurgeDirectiveRatingUtility.Runtime;
                if (!string.IsNullOrEmpty(targetStableId)
                    && runtime != null
                    && runtime.HasAwardedBaseReward(targetStableId!))
                {
                    baseRewardHandled = true;
                }
                else if (PurgeDirectiveRatingUtility.TryGrantWorldTargetBaseRewardForId(
                        targetStableId,
                        baseReward))
                {
                    baseRewardHandled = true;
                }
            }

            if (!extraRewardHandled)
            {
                int extra = PurgeDirectiveRatingUtility.GetQuestExtraReward(targetType);
                extraRewardHandled = extra <= 0
                    || PurgeDirectiveRatingUtility.TryAddPurgeDirectiveRewardPoints(extra);
            }

            // 奖励没有真正写入时保持操作阶段并由 Tick 重试，禁止虚假标记成功。
            if (!baseRewardHandled || !extraRewardHandled)
            {
                return;
            }

            EndQuest(QuestEndOutcome.Success);
        }

        private void Fail()
        {
            if (stage == Stage.Ended || endPending)
            {
                return;
            }

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
            if (stage == Stage.Ended || endPending)
            {
                return;
            }

            EndQuest(QuestEndOutcome.Unknown);
        }

        private void EndQuest(QuestEndOutcome outcome)
        {
            endPending = true;
            pendingEndOutcome = outcome;
            TryFinishEnding();
        }

        private void TryFinishEnding()
        {
            if (!endPending || ending)
            {
                return;
            }

            ending = true;
            try
            {
                if (!cooldownHandled)
                {
                    PurgeDirectiveQuestScheduler.NotifyQuestEnded();
                    cooldownHandled = true;
                }

                if (pendingEndOutcome == QuestEndOutcome.Success && !completionLetterHandled)
                {
                    // 通知失败不能阻塞已提交的奖励与任务结束，也不能在重试时重复发送。
                    completionLetterHandled = true;
                    try
                    {
                        PurgeDirectiveRatingLetterUtility.SendQuestCompleteLetter(
                            targetWorldObject,
                            targetType,
                            (PurgeDirectiveRatingConfigDefOf.MAP_PurgeDirectiveRatingConfig?.questBaseRewardPoints ?? 200)
                                + PurgeDirectiveRatingUtility.GetQuestExtraReward(targetType));
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[MAP-机械族机械师] 肃清任务完成通知失败，继续结束任务：" + ex);
                    }
                }

                if (quest != null && !quest.Historical)
                {
                    quest.End(pendingEndOutcome);
                }

                if (quest == null || quest.Historical)
                {
                    stage = Stage.Ended;
                    endPending = false;
                }
            }
            finally
            {
                ending = false;
            }
        }

        public override void Notify_PreCleanup()
        {
            if (stage == Stage.OperationActive
                && !endPending
                && !completionConfirmed
                && !GameComponent_CerebrexTakeoverState.IsActive
                && !penaltyHandled)
            {
                // 玩家接取后从任务界面放弃/外部结束，也属于失败；待接受邀请被拒绝或过期不处罚。
                PurgeDirectiveRatingUtility.ApplyQuestFailurePenalty(targetType);
                penaltyHandled = true;
            }

            stage = Stage.Ended;
            if (!cooldownHandled)
            {
                PurgeDirectiveQuestScheduler.NotifyQuestEnded();
                cooldownHandled = true;
            }

            base.Notify_PreCleanup();
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
            Scribe_Values.Look(ref completionConfirmed, "pdqCompletionConfirmed");
            Scribe_Values.Look(ref endPending, "pdqEndPending", false);
            Scribe_Values.Look(ref pendingEndOutcome, "pdqPendingEndOutcome", QuestEndOutcome.Unknown);
            Scribe_Values.Look(ref completionLetterHandled, "pdqCompletionLetterHandled", false);
        }
    }
}
