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
                && slate.Exists("actionId");
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
            //
            // 关键：tag 必须以 "Quest{quest.id}." 开头。原版 Quest.Notify_SignalReceived
            // 对非 global 信号有硬性接收规则——仅接收以 "Quest{id}." 开头的信号，否则在
            // 分发给任何 QuestPart 之前直接 return。原版 MapParent.PostMapGenerate 会在此 tag
            // 后追加 ".MapGenerated" 再发送；若 tag 不以 Quest 前缀开头，则完整信号
            // （如 "MAP_SymbiosisCovenantJointOp_Target_0.MapGenerated"）会被原版丢弃，
            // 导致 QuestPart 永远收不到 MapGenerated，援军流程无法启动。
            // 因此必须把 quest.id 放在前缀而非末尾。
            string targetQuestTag =
                "Quest" + quest.id + ".MAP_SymbiosisCovenantJointOp_Target";

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
                    // 邀请加入列表时已启用计时；接取信号保持兼容，正式部署仍由 stage 门控。
                    inSignalEnable = QuestGen.slate.Get<string>("inSignal"),
                    actionId = actionId,
                    targetQuestTag = targetQuestTag,
                    targetWorldObject = target,
                    targetFaction = targetFaction,
                    proposerFaction = proposer,
                    participantFactions = participants,
                    jointOperationDef = def,
                    offerExpireTick = quest.acceptanceExpireTick,
                    // 新任务创建时直接赋予：让尚未接受（NotYetAccepted / OfferPending）的
                    // Part 也能监听目标完成信号，从而支持“未接取但已彻底摧毁目标”的成功结算。
                    signalListenMode = QuestPart.SignalListenMode.OngoingOrNotYetAccepted,
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
