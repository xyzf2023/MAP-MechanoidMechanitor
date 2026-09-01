using System.Collections.Generic;
using System.Linq;
using System.Text;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 肃清评级系统 DEV 调试动作。
    /// 规则：DEV 调评级不改肃清额度；DEV 调额度（肃清额度）不改评级。
    /// 强制成功/失败必须走正式幂等结算入口（PurgeDirectiveQuestPart）。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class PurgeDirectiveRatingDebugActions
    {
        private const string Category = "MAP-机械族机械师";

        // 评级 +/-100 / +/-750
        [DebugAction(Category, "肃清评级：+100", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void DevRatingPlus100() => PurgeDirectiveRatingUtility.TryAddRating(100);

        [DebugAction(Category, "肃清评级：-100", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void DevRatingMinus100() => PurgeDirectiveRatingUtility.TryAddRating(-100);

        [DebugAction(Category, "肃清评级：+750", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void DevRatingPlus750() => PurgeDirectiveRatingUtility.TryAddRating(750);

        [DebugAction(Category, "肃清评级：-750", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void DevRatingMinus750() => PurgeDirectiveRatingUtility.TryAddRating(-750);

        // 直接设置到边界值
        [DebugAction(Category, "肃清评级：设为 0", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void DevRatingSet0() => PurgeDirectiveRatingUtility.SetRatingDirect(0);

        [DebugAction(Category, "肃清评级：设为 749", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void DevRatingSet749() => PurgeDirectiveRatingUtility.SetRatingDirect(749);

        [DebugAction(Category, "肃清评级：设为 750", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void DevRatingSet750() => PurgeDirectiveRatingUtility.SetRatingDirect(750);

        [DebugAction(Category, "肃清评级：设为 1499", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void DevRatingSet1499() => PurgeDirectiveRatingUtility.SetRatingDirect(1499);

        [DebugAction(Category, "肃清评级：设为 1500", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void DevRatingSet1500() => PurgeDirectiveRatingUtility.SetRatingDirect(1500);

        [DebugAction(Category, "肃清评级：设为 2249", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void DevRatingSet2249() => PurgeDirectiveRatingUtility.SetRatingDirect(2249);

        [DebugAction(Category, "肃清评级：设为 2250", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void DevRatingSet2250() => PurgeDirectiveRatingUtility.SetRatingDirect(2250);

        [DebugAction(Category, "肃清评级：设为 2999", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void DevRatingSet2999() => PurgeDirectiveRatingUtility.SetRatingDirect(2999);

        [DebugAction(Category, "肃清评级：设为 3000", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void DevRatingSet3000() => PurgeDirectiveRatingUtility.SetRatingDirect(3000);

        [DebugAction(Category, "肃清评级：设为 4000", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void DevRatingSet4000() => PurgeDirectiveRatingUtility.SetRatingDirect(4000);

        // 肃清额度（仅额度，不改评级）
        [DebugAction(Category, "肃清额度：+1000", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void DevCreditsPlus1000() =>
            GameComponent_MechanoidMechanitorStoryState.AddPurgeDirectiveCreditsDev(1000);

        [DebugAction(Category, "肃清额度：-1000", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void DevCreditsMinus1000() =>
            GameComponent_MechanoidMechanitorStoryState.AddPurgeDirectiveCreditsDev(-1000);

        // 任务调度
        [DebugAction(Category, "肃清任务：立即执行一次检查", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void DevForceCheck() => PurgeDirectiveQuestScheduler.ForceDueNow();

        [DebugAction(Category, "肃清任务：清除冷却", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void DevClearCooldown() => PurgeDirectiveQuestScheduler.ClearCooldown();

        [DebugAction(Category, "肃清任务：输出调度/QuestPart状态", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void DevLogTaskState() => LogTaskState();

        [DebugAction(Category, "肃清任务：强制当前活动任务成功(幂等)", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void ForceActiveQuestSuccess()
        {
            PurgeDirectiveQuestPart? part = PurgeDirectiveRatingDisplay.ActiveQuestPart();
            if (part == null)
            {
                Log.Message("[肃清评级] 当前没有活动任务。");
                return;
            }

            if (part.IsOperationActive)
            {
                part.NotifyTargetDefeated(part.targetWorldObject);
            }
            else
            {
                Log.Message("[肃清评级] 当前任务处于待接受阶段，无法强制成功。");
            }
        }

        [DebugAction(Category, "肃清任务：强制当前活动任务失败(幂等)", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void ForceActiveQuestFail()
        {
            PurgeDirectiveQuestPart? part = PurgeDirectiveRatingDisplay.ActiveQuestPart();
            if (part == null)
            {
                Log.Message("[肃清评级] 当前没有活动任务。");
                return;
            }

            // 失败走正式惩罚路径（按目标类型扣评级，不扣肃清额度）。
            if (part.IsOperationActive)
            {
                part.ForceFail();
            }
            else
            {
                Log.Message("[肃清评级] 当前任务处于待接受阶段，无法强制失败。");
            }
        }

        private static void LogTaskState()
        {
            StringBuilder sb = new StringBuilder();
            GameComponent_MechanoidMechanitorStoryState? story =
                Current.Game?.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            MechanoidMechanitorPurgeDirectiveRuntimeState? rs = story?.PurgeDirectiveRuntimeState;
            if (rs == null)
            {
                Log.Message("[肃清评级] 无运行时状态。");
                return;
            }

            sb.AppendLine($"评级值={rs.RatingValue} 等级={PurgeDirectiveRatingUtility.CurrentRatingLevel}");
            sb.AppendLine($"下次检查={rs.NextPurgeQuestCheckTick} 下次尝试={rs.PurgeQuestCooldownEndTick}");
            sb.AppendLine($"已用目标数={rs.UsedQuestTargetCount} 已发基础奖励数={rs.AwardedBaseRewardCount}");
            sb.AppendLine($"最终惩罚={rs.FinalPenaltyTriggered} 接管主脑={GameComponent_CerebrexTakeoverState.IsActive}");

            QuestScriptDef? def = PurgeDirectiveQuestTargetUtility.ResolveQuestScriptDef();
            if (def != null)
            {
                foreach (Quest quest in Find.QuestManager.QuestsListForReading)
                {
                    if (quest.root != def) continue;
                    sb.AppendLine($"Quest {quest.id} state={quest.State}");
                    foreach (QuestPart part in quest.PartsListForReading)
                    {
                        if (part is PurgeDirectiveQuestPart p)
                        {
                            sb.AppendLine($"  part stage={(p.IsOfferPending ? "OfferPending" : p.IsOperationActive ? "OperationActive" : "Ended")} " +
                                $"target={p.targetWorldObject?.LabelCap} type={p.targetType} stable={p.targetStableId}");
                        }
                    }
                }
            }

            Log.Message(sb.ToString());
        }
    }
}
