using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>运行时资格结果，不保存到存档；目标不合法会另外输出去重警告。</summary>
    public enum SyntheticRelationshipFailure
    {
        InvalidMech, AlreadyPartnered, Downed, Drafted, MentalState, CannotAct,
        InvalidTarget, TargetUnder16, Related, TargetCannotAct, Unreachable,
        NotLovers, ProposalUnavailable, InteractionUnavailable,
        NotPartners, IntimacyOff, IdeologyForbids, IntimacyUnwilling
    }

    /// <summary>玩家提示与开发者诊断分别格式化，避免内部原因泄露到界面。</summary>
    public static class SyntheticRelationshipFeedback
    {
        public static string? PursuitMessage(Pawn pawn, Pawn? target = null)
        {
            SyntheticRelationshipFailure? failure = SyntheticCompanionRelationshipUtility.PursuitReason(pawn, target);
            if (failure == null) return null;
            switch (failure.Value)
            {
                case SyntheticRelationshipFailure.Downed:
                    return "CantRomanceInitiateMessageDowned".Translate(pawn).CapitalizeFirst();
                case SyntheticRelationshipFailure.Drafted:
                    return "CantRomanceInitiateMessageDrafted".Translate(pawn).CapitalizeFirst();
                case SyntheticRelationshipFailure.MentalState:
                    return "CantRomanceInitiateMessageMentalState".Translate(pawn).CapitalizeFirst();
                case SyntheticRelationshipFailure.CannotAct:
                case SyntheticRelationshipFailure.InvalidTarget:
                case SyntheticRelationshipFailure.TargetUnder16:
                case SyntheticRelationshipFailure.Related:
                case SyntheticRelationshipFailure.TargetCannotAct:
                case SyntheticRelationshipFailure.Unreachable:
                    return (SyntheticCompanionRelationshipUtility.Key + failure.Value).Translate();
                default:
                    // 旧菜单或关系变动只需通用拒绝提示，不展示授权、Tracker 等内部状态。
                    return "MAP_MechanoidMechanitor.Lover.Spouse.AssignSpouseFailed".Translate();
            }
        }

        /// <summary>仅供只读诊断调用；不自动写日志，不依赖翻译资源。</summary>
        public static string? DiagnosticReason(SyntheticRelationshipFailure? failure)
        {
            if (failure == null) return null;
            switch (failure.Value)
            {
                case SyntheticRelationshipFailure.InvalidMech: return "缺少有效模块授权、玩家阵营资格或关系 Tracker。";
                case SyntheticRelationshipFailure.AlreadyPartnered: return "已有伴侣，死亡或离图的伴侣仍占用名额。";
                case SyntheticRelationshipFailure.Downed: return "机械体已倒地。";
                case SyntheticRelationshipFailure.Drafted: return "机械体已征召。";
                case SyntheticRelationshipFailure.MentalState: return "机械体处于精神状态。";
                case SyntheticRelationshipFailure.CannotAct: return "机械体不满足当前行动条件。";
                case SyntheticRelationshipFailure.InvalidTarget: return "目标不满足同图、自由成年类人殖民者等追求资格。";
                case SyntheticRelationshipFailure.TargetUnder16: return "目标生理年龄未满16岁。";
                case SyntheticRelationshipFailure.Related: return "双方属于禁止追求的近亲关系。";
                case SyntheticRelationshipFailure.TargetCannotAct: return "对方不在场、无法行动，或双方不在同一地图。";
                case SyntheticRelationshipFailure.Unreachable: return "无法安全到达对方，或双方敌对。";
                case SyntheticRelationshipFailure.NotLovers: return "双方不是模块保护的情侣，无需发起求婚。";
                case SyntheticRelationshipFailure.ProposalUnavailable: return "求婚的行动、同图、阵营或安全条件不满足。";
                case SyntheticRelationshipFailure.InteractionUnavailable: return "当前工作、社交模式、距离或互动间隔不允许求婚。";
                case SyntheticRelationshipFailure.NotPartners: return "伴侣关系、模块授权或人机配对资格无效。";
                case SyntheticRelationshipFailure.IntimacyOff: return "尚未开启与伴侣爱爱。";
                case SyntheticRelationshipFailure.IdeologyForbids: return "至少一方肉欲戒律不允许当前关系阶段亲热。";
                case SyntheticRelationshipFailure.IntimacyUnwilling: return "人类伴侣的性取向或无性恋特性不允许亲热。";
                default: return "未知伴侣资格状态：" + failure.Value;
            }
        }
    }
}
