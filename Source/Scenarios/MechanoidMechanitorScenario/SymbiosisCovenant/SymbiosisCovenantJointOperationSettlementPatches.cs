using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 联合军事行动对“据点（Settlement）被正确摧毁”的窄范围判定（F2）。
    ///
    /// 设计要点：
    /// - 仅 Patch SettlementDefeatUtility.CheckDefeated 的 Postfix。
    /// - 不 Patch WorldObject.Destroy() 等过于宽泛的入口。
    /// - 只在原版 CheckDefeated 正常返回后检查。
    /// - 只查当前正在进行的联合军事行动 QuestPart。
    /// - 仅当 operation.targetWorldObject == 本次传入的 factionBase（引用相等）时才通知。
    /// - 不限制是否已接取：未接取（OfferPending）但据点确实被摧毁也允许成功结算；
    ///   是否允许成功由 QuestPart 内部统一判定（OfferPending+真摧毁 / 已接取且战斗就绪 /
    ///   已接取未就绪则拒绝），本 Patch 不复制状态机。
    /// - 绝不因其他派系据点被摧毁、任何普通任务、任何其他 MOD 世界对象而误结算本任务。
    /// </summary>
    [HarmonyPatch(typeof(SettlementDefeatUtility), nameof(SettlementDefeatUtility.CheckDefeated))]
    public static class SymbiosisCovenantJointOperationSettlementPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Settlement factionBase)
        {
            if (factionBase == null)
            {
                return;
            }

            // CheckDefeated 只是“执行检查”：原版在确认据点尚未被击败时会直接 return，
            // 不会调用 factionBase.Destroy()。只有当原版已经真正摧毁该 Settlement（
            // factionBase.Destroyed 变为 true）时，才允许联合行动结算。
            // 仅凭 CheckDefeated 被调用就结算会导致击倒部分守军即提前完成任务。
            if (!factionBase.Destroyed)
            {
                return;
            }

            QuestPart_SymbiosisCovenantJointOperation? part =
                SymbiosisCovenantJointOperationUtility.FindActiveOperationPart();
            if (part == null)
            {
                return;
            }

            // 引用相等校验：只认本行动自己的目标据点，避免误判其他据点。
            if (!ReferenceEquals(part.targetWorldObject, factionBase))
            {
                return;
            }

            // 本 Patch 只负责报告“原版已经真正摧毁了这个 Settlement”。
            // 是否允许成功（OfferPending + 真摧毁 / 已接取且战斗就绪 / 已接取未就绪则拒绝）
            // 全部交由 QuestPart 内部统一判定，不在 Patch 中复制状态机。
            part.NotifySettlementDestroyed(factionBase);
        }
    }
}
