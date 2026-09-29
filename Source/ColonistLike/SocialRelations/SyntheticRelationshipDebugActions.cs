using System.Text;
using LudeonTK;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class SyntheticRelationshipDebugActions
    {
        [DebugAction("MAP-机械族机械师", "诊断仿生伴侣关系流程",
            false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Diagnose(Pawn pawn)
        {
            var text = new StringBuilder();
            text.AppendLine("=== 仿生伴侣关系流程（只读）===");
            text.AppendLine("机械体：" + pawn.LabelShortCap);
            text.AppendLine("持久授权：" + SyntheticCompanionRelationshipUtility.HasModule(pawn));
            text.AppendLine("追求入口：" + (SyntheticRelationshipFeedback.DiagnosticReason(SyntheticCompanionRelationshipUtility.PursuitReason(pawn)) ?? "可选择目标"));
            text.AppendLine("当前工作：" + pawn.CurJobDef?.defName);
            foreach (Pawn partner in SyntheticCompanionRelationshipUtility.GetPartners(pawn))
            {
                text.AppendLine("\n伴侣：" + partner.LabelShortCap + "，死亡=" + partner.Dead + "，在场=" + partner.Spawned);
                text.AppendLine("自动解除保护：" + SyntheticCompanionRelationshipUtility.IsProtectedPair(pawn, partner));
                foreach (DirectPawnRelation relation in pawn.relations.DirectRelations)
                {
                    if (relation.otherPawn != partner || !SyntheticCompanionRelationshipUtility.IsLoveRelation(relation.def)) continue;
                    float days = (Find.TickManager.TicksGame - relation.startTicks) / 60000f;
                    text.AppendLine("关系：" + relation.def.label + "，持续天数=" + days.ToString("F2"));
                    if (relation.def == PawnRelationDefOf.Fiance)
                    {
                        text.AppendLine("婚礼订婚时长门槛（>10天）：" + (days > 10f));
                        if (pawn.Spawned && partner.Spawned && pawn.Map == partner.Map)
                        {
                            text.AppendLine("玩家家园地图：" + pawn.Map.IsPlayerHome);
                            text.AppendLine("婚礼环境：" + MarriageCeremonyUtility.AcceptableGameConditionsToStartCeremony(pawn.Map));
                            text.AppendLine("机械体婚礼就绪：" + MarriageCeremonyUtility.FianceReadyToStartCeremony(pawn, partner));
                            text.AppendLine("伴侣婚礼就绪：" + MarriageCeremonyUtility.FianceReadyToStartCeremony(partner, pawn));
                        }
                    }
                }
                text.AppendLine("自然求婚：" + (SyntheticRelationshipFeedback.DiagnosticReason(SyntheticCompanionRelationshipUtility.ProposalReason(pawn, partner)) ?? "当前可互动，等待随机机会"));
                text.AppendLine("求婚权重：" + SyntheticCompanionRelationshipUtility.ProposalWeight(pawn, partner));
                text.AppendLine("机械体发起闲聊：" + SyntheticCompanionSocialUtility.CanTalk(pawn, partner, InteractionDefOf.Chitchat));
                text.AppendLine("人类发起闲聊：" + SyntheticCompanionSocialUtility.CanTalk(partner, pawn, InteractionDefOf.Chitchat));
                text.AppendLine("机械体发起深入交谈：" + SyntheticCompanionSocialUtility.CanTalk(pawn, partner, InteractionDefOf.DeepTalk));
                text.AppendLine("人类发起深入交谈：" + SyntheticCompanionSocialUtility.CanTalk(partner, pawn, InteractionDefOf.DeepTalk));
                text.AppendLine("亲热资格：" + (SyntheticRelationshipFeedback.DiagnosticReason(SyntheticCompanionRelationshipUtility.IntimacyReason(pawn, partner)) ?? "关系、开关、戒律与意愿通过；床铺及身体状态见亲热诊断"));
                if (ModsConfig.IdeologyActive)
                    text.AppendLine("双方肉欲戒律允许共床：" + BedUtility.WillingToShareBed(pawn, partner));
            }
            Log.Message("[MAP-机械族机械师] " + text);
            Messages.Message("仿生伴侣关系诊断已写入日志。", MessageTypeDefOf.NeutralEvent, historical: false);
        }
    }
}
