using System.Text;
using LudeonTK;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 肃清指令 DEV 面板使用的任务测试辅助方法。
    /// 全局快速生成入口与通讯面板共用正式任务流程。
    /// </summary>
    internal static class PurgeDirectiveRatingDebugUtility
    {
        [DebugAction("MAP-机械族机械师", "快速生成肃清任务",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void GenerateQuest()
        {
            bool success = PurgeDirectiveQuestScheduler.TryGenerateDebugQuest(out string message);
            Messages.Message(message, success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput, false);
        }

        internal static bool TryForceActiveQuestSuccess(out string message)
        {
            PurgeDirectiveQuestPart? part = PurgeDirectiveRatingDisplay.ActiveQuestPart();
            if (part == null)
            {
                message = "当前没有活动中的肃清任务。";
                return false;
            }

            if (!part.IsOperationActive)
            {
                message = "当前任务仍处于待接受阶段，无法强制成功。";
                return false;
            }

            part.NotifyTargetDefeated(part.targetWorldObject);
            if (part.quest?.State != QuestState.EndedSuccess)
            {
                message = "未能完成当前肃清任务：目标可能已失效，或奖励结算仍待重试。";
                return false;
            }

            message = "已通过正式结算入口强制完成当前肃清任务。";
            return true;
        }

        internal static bool TryForceActiveQuestFail(out string message)
        {
            PurgeDirectiveQuestPart? part = PurgeDirectiveRatingDisplay.ActiveQuestPart();
            if (part == null)
            {
                message = "当前没有活动中的肃清任务。";
                return false;
            }

            if (!part.IsOperationActive)
            {
                message = "当前任务仍处于待接受阶段，无法强制失败。";
                return false;
            }

            part.ForceFail();
            message = "已通过正式惩罚入口强制判定当前肃清任务失败。";
            return true;
        }

        internal static string BuildTaskStateText()
        {
            StringBuilder sb = new StringBuilder();
            GameComponent_MechanoidMechanitorStoryState? story =
                Current.Game?.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            MechanoidMechanitorPurgeDirectiveRuntimeState? runtime =
                story?.PurgeDirectiveRuntimeState;
            if (runtime == null)
            {
                return "当前没有可用的肃清指令运行时状态。";
            }

            sb.AppendLine(
                $"评级值={runtime.RatingValue} 等级={PurgeDirectiveRatingUtility.CurrentRatingLevel}");
            sb.AppendLine(
                $"下次检查={runtime.NextPurgeQuestCheckTick} 下次尝试={runtime.PurgeQuestCooldownEndTick}");
            sb.AppendLine(
                $"已用目标数={runtime.UsedQuestTargetCount} 已发基础奖励数={runtime.AwardedBaseRewardCount}");
            sb.AppendLine(
                $"最终惩罚={runtime.FinalPenaltyTriggered} 接管主脑={GameComponent_CerebrexTakeoverState.IsActive}");

            QuestScriptDef? def = PurgeDirectiveQuestTargetUtility.ResolveQuestScriptDef();
            if (def == null || Find.QuestManager == null)
            {
                return sb.ToString();
            }

            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.root != def)
                {
                    continue;
                }

                sb.AppendLine($"Quest {quest.id} state={quest.State}");
                foreach (QuestPart questPart in quest.PartsListForReading)
                {
                    if (questPart is PurgeDirectiveQuestPart purgePart)
                    {
                        string stage = purgePart.IsOfferPending
                            ? "OfferPending"
                            : purgePart.IsOperationActive ? "OperationActive" : "Ended";
                        sb.AppendLine(
                            $"  part stage={stage} target={purgePart.targetWorldObject?.LabelCap} "
                            + $"type={purgePart.targetType} stable={purgePart.targetStableId}");
                    }
                }
            }

            return sb.ToString();
        }
    }
}
