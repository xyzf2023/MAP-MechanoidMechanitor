using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 肃清评级任务根节点。只负责按 Slate 构造 Quest 与唯一的 QuestPart 状态机，
    /// 不引用共生盟约的任何类型与逻辑。完成只接受据点/站点的明确守军击败信号，
    /// 不把任意 WorldObject.Destroy 当作玩家完成。
    /// </summary>
    public class QuestNode_Root_PurgeDirectiveQuest : QuestNode
    {
        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            WorldObject target = slate.Get<WorldObject>("targetWorldObject");
            Faction targetFaction = slate.Get<Faction>("targetFaction");
            Faction proposerFaction = slate.Get<Faction>("proposerFaction");
            string configName = slate.Get<string>("purgeQuestConfigDefName");
            int rewardValue = slate.Get<int>("rewardValue", 0);
            int operationTimeout = slate.Get<int>("operationTimeoutTicks", 0);
            // 星级与奖励均在生成时确定，读档和目标建设阶段变化不重新计算。
            QuestGen.quest.challengeRating = slate.Get<int>("purgeChallengeRating", 1);

            PurgeDirectiveTargetType targetType =
                PurgeDirectiveQuestTargetUtility.ClassifyTargetType(target);

            PurgeDirectiveQuestPart part = new PurgeDirectiveQuestPart
            {
                inSignalEnable = QuestGen.quest.InitiateSignal,
                targetWorldObject = target,
                targetFaction = targetFaction,
                proposerFaction = proposerFaction,
                purgeQuestConfigDefName = configName,
                rewardValue = rewardValue,
                operationTimeoutTicks = operationTimeout,
                targetType = targetType,
                targetStableId = PurgeDirectiveQuestTargetUtility.TryGetStableId(target),
                questTag = "Quest" + QuestGen.quest.id + "."
            };

            part.InitializeAutomaticDirective(Find.TickManager.TicksGame + operationTimeout);
            QuestGen.quest.AddPart(part);

            string title =
                "MAP_PurgeDirectiveRating.Quest.Title".Translate(target?.LabelCap ?? "?");
            string description = "MAP_PurgeDirectiveRating.Quest.Description".Translate(
                targetFaction?.Name ?? "?", target?.LabelCap ?? "?",
                PurgeDirectiveQuestTargetUtility.FormatOperationDuration(operationTimeout));
            QuestGen.quest.name = title;
            slate.Set("resolvedQuestName", title);
            QuestGen.quest.description = description;
            slate.Set("resolvedQuestDescription", description);

            // 单项奖励只用于任务面板展示；额度与评级仍由状态机幂等结算。
            QuestPart_Choice rewards = new QuestPart_Choice();
            QuestPart_Choice.Choice choice = new QuestPart_Choice.Choice();
            choice.rewards.Add(new Reward_PurgeDirectiveCredits());
            rewards.choices.Add(choice);
            QuestGen.quest.AddPart(rewards);
        }

        protected override bool TestRunInt(Slate slate)
        {
            // 仅当调度器已写入全部必要运行期数据时，本次 Quest 生成才视为有效。
            return slate.Exists("targetWorldObject")
                && slate.Exists("targetFaction")
                && slate.Exists("proposerFaction")
                && slate.Exists("purgeQuestConfigDefName")
                && slate.Exists("rewardValue")
                && slate.Exists("purgeChallengeRating")
                && slate.Exists("operationTimeoutTicks");
        }
    }
}
