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
    /// - 仅当 operation.targetWorldObject == 本次传入的 factionBase
    ///   且 operation 已接取、尚未成功/失败/无效结束时，才通知该 QuestPart 成功。
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
            if (part == null || !part.IsOperationAccepted)
            {
                return;
            }

            // 引用相等校验：只认本行动自己的目标据点，避免误判其他据点。
            if (!ReferenceEquals(part.targetWorldObject, factionBase))
            {
                return;
            }

            // 仅当行动已进入真正的战斗就绪状态时才允许结算成功：
            // 玩家已进入、目标威胁曾被确认、援军实际生成并已部署。
            if (!part.playerEngaged
                || !part.targetThreatConfirmed
                || !part.reinforcementsGenerated
                || part.stage
                    != QuestPart_SymbiosisCovenantJointOperation
                        .SymbiosisCovenantJointOperationStage.ReinforcementsDeployed)
            {
                return;
            }

            part.NotifySettlementDestroyed(factionBase);
        }
    }
}
