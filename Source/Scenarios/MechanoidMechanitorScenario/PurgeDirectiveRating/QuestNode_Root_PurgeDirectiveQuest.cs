using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 肃清评级任务根节点。只负责按 Slate 构造 Quest 与唯一的 QuestPart 状态机，
    /// 不引用共生盟约的任何类型与逻辑。目标信号由 WorldObject 销毁钩子驱动，
    /// 因此无需在此铺设 MapGenerated / NoActiveThreats 等信号链。
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
            int offerTimeout = slate.Get<int>("offerTimeoutTicks", 0);
            int operationTimeout = slate.Get<int>("operationTimeoutTicks", 0);

            PurgeDirectiveQuestPart part = new PurgeDirectiveQuestPart
            {
                targetWorldObject = target,
                targetFaction = targetFaction,
                proposerFaction = proposerFaction,
                purgeQuestConfigDefName = configName,
                rewardValue = rewardValue,
                operationTimeoutTicks = operationTimeout
            };

            QuestGen.quest.AddPart(part);

            // 玩家抉择期：由 QuestScriptDef.expireDaysRange 控制，到期未接取则按拒绝（无处罚）结束。
            QuestGen.quest.name =
                "MAP_PurgeDirectiveRating.Quest.Title".Translate(target?.LabelCap ?? "?");
        }

        protected override bool TestRunInt(Slate slate)
        {
            // 仅当调度器已写入全部必要运行期数据时，本次 Quest 生成才视为有效。
            return slate.Exists("targetWorldObject")
                && slate.Exists("targetFaction")
                && slate.Exists("proposerFaction")
                && slate.Exists("purgeQuestConfigDefName")
                && slate.Exists("rewardValue")
                && slate.Exists("offerTimeoutTicks")
                && slate.Exists("operationTimeoutTicks");
        }
    }
}
