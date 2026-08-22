using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 联合军事行动的根 Quest 节点。由 QuestScriptDef 的
    /// &lt;root Class="MAP_MechanoidMechanitor.Scenarios.QuestNode_Root_SymbiosisCovenantJointOperation" /&gt;
    /// 引用，在 QuestUtility.GenerateQuestAndMakeAvailable 期间由 QuestGen 调用。
    /// 职责：从 Slate 读取调度器已准备好的运行期数据，构造唯一的 QuestPart（权威状态机），
    /// 设置任务名称/描述（通过 resolvedQuestName / resolvedQuestDescription 让 QuestGen 解析器保留），
    /// 标记为「尚未接受」，并交由 QuestManager 接管。奖励在 QuestPart 成功时直接发放（vanilla 风格实物奖励生成器）。
    /// </summary>
    public class QuestNode_Root_SymbiosisCovenantJointOperation : QuestNode
    {
        protected override bool TestRunInt(Slate slate)
        {
            // 仅当调度器已写入全部必要运行期数据时，本次 Quest 生成才视为有效。
            return slate.Exists("targetWorldObject")
                && slate.Exists("targetFaction")
                && slate.Exists("proposerFaction")
                && slate.Exists("participants")
                && slate.Exists("jointOperationDef")
                && slate.Exists("actionId")
                && slate.Exists("rewardValue");
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Quest quest = QuestGen.quest;

            WorldObject? target = slate.Get<WorldObject>("targetWorldObject");
            Faction? targetFaction = slate.Get<Faction>("targetFaction");
            Faction? proposer = slate.Get<Faction>("proposerFaction");
            List<Faction>? participants = slate.Get<List<Faction>>("participants");
            SymbiosisCovenantJointOperationDef? def =
                slate.Get<SymbiosisCovenantJointOperationDef>("jointOperationDef");
            string actionId = slate.Get<string>("actionId");
            int rewardValue = slate.Get<int>("rewardValue");

            if (target == null
                || targetFaction == null
                || proposer == null
                || participants == null
                || participants.Count == 0
                || def == null)
            {
                // 运行期数据缺失：直接放弃本次生成，不污染 QuestManager。
                return;
            }

            string questName =
                SymbiosisCovenantJointOperationUtility.BuildQuestName(targetFaction, target);
            string questDescription =
                SymbiosisCovenantJointOperationUtility.BuildOfferDescription(
                    proposer,
                    targetFaction,
                    target,
                    participants);

            // 通过 resolvedQuestName / resolvedQuestDescription 让 QuestGen 的
            // 名称/描述解析器保留我们设置的文本，而不是用默认规则重新生成。
            quest.name = questName;
            QuestGen.slate.Set("resolvedQuestName", questName);
            quest.description = questDescription;
            QuestGen.slate.Set("resolvedQuestDescription", questDescription);

            // 为目标 WorldObject / MapParent 添加本任务专属 targetQuestTag（F1）。
            // 之后玩家进入目标地图会发出 <targetQuestTag>.MapGenerated，
            // 站点清除后会发出 <targetQuestTag>.NoActiveThreats / .AllEnemiesDefeated，
            // 由 QuestPart.ProcessQuestSignal 精确处理。
            string targetQuestTag =
                "MAP_SymbiosisCovenantJointOp_Target_" + quest.id;

            bool targetIsMapParent = target is MapParent;
            MapParent? mapParent = target as MapParent;
            if (mapParent != null)
            {
                QuestUtility.AddQuestTag(mapParent, targetQuestTag);
            }

            // 诊断（只读）：确认任务生成时 targetQuestTag 是否写入目标 MapParent 的 questTags。
            {
                List<string>? attachedTags = mapParent?.questTags;
                bool tagPresentAfterAttach = SymbiosisCovenantJointOperationDiagnostics
                    .QuestTagContains(attachedTags, targetQuestTag);
                SymbiosisCovenantJointOperationDiagnostics.Log(
                    "TargetQuestTagAttached",
                    "actionId=" + (actionId ?? "-")
                    + " | questId=" + quest.id.ToString()
                    + " | target=" + (target?.Label ?? "-")
                    + " | targetLoadId=" + (target?.GetUniqueLoadID() ?? "-")
                    + " | targetType=" + (target?.GetType().Name ?? "-")
                    + " | targetIsMapParent=" + targetIsMapParent
                    + " | mapParentHasMap=" + (mapParent?.HasMap ?? false)
                    + " | targetQuestTag=" + (targetQuestTag ?? "-")
                    + " | questTags=" + SymbiosisCovenantJointOperationDiagnostics.FormatQuestTags(attachedTags)
                    + " | tagPresentAfterAttach=" + tagPresentAfterAttach);
            }

            QuestPart_SymbiosisCovenantJointOperation part =
                new QuestPart_SymbiosisCovenantJointOperation
                {
                    // 接取任务时由 InitiateSignal 启用本状态机，之后才开始自检与部署援军。
                    inSignalEnable = QuestGen.slate.Get<string>("inSignal"),
                    actionId = actionId,
                    targetQuestTag = targetQuestTag,
                    targetWorldObject = target,
                    targetFaction = targetFaction,
                    proposerFaction = proposer,
                    participantFactions = participants,
                    jointOperationDef = def,
                    rewardValue = rewardValue,
                    offerExpireTick = quest.acceptanceExpireTick,
                    stage = QuestPart_SymbiosisCovenantJointOperation
                        .SymbiosisCovenantJointOperationStage.OfferPending
                };
            quest.AddPart(part);

            // 诊断（只读）：确认 Part 创建时的启用信号与初始状态。
            {
                int partIndex = -1;
                try
                {
                    partIndex = part.Index;
                }
                catch
                {
                    partIndex = -1;
                }

                string inSignal = part.inSignalEnable ?? "-";
                string initiateSignal = quest.InitiateSignal ?? "-";
                bool enableMatches = string.Equals(
                    inSignal, initiateSignal, StringComparison.Ordinal);
                SymbiosisCovenantJointOperationDiagnostics.Log(
                    "QuestPartCreated",
                    "actionId=" + (actionId ?? "-")
                    + " | questId=" + quest.id.ToString()
                    + " | partIndex=" + partIndex
                    + " | inSignalEnable=" + inSignal
                    + " | questInitiateSignal=" + initiateSignal
                    + " | enableSignalMatchesInitiateSignal=" + enableMatches
                    + " | state=" + part.State
                    + " | stage=" + part.stage
                    + " | targetQuestTag=" + (targetQuestTag ?? "-"));
            }

            // 标记为「尚未接受」，玩家在任务面板接受后才会真正进入 OperationActive。
            quest.SetNotYetAccepted();
        }
    }
}
