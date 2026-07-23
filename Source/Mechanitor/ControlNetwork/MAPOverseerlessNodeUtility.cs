using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MAPOverseerlessNodeUtility
    {
        public static bool IsOverseerlessNodeSubject(Pawn? pawn)
        {
            return pawn != null
                && ModsConfig.BiotechActive
                && MAPMechanitorNodeUtility.IsMechanitorNodeController(pawn)
                && !MAPMechanitorNodeUtility.RequiresExternalOverseer(pawn);
        }

        public static bool ShouldClearOwnExternalOverseer(Pawn? pawn)
        {
            return IsOverseerlessNodeSubject(pawn);
        }

        public static void ClearExternalOverseerIfNode(Pawn pawn)
        {
            if (!ShouldClearOwnExternalOverseer(pawn))
            {
                return;
            }

            // 不可用 pawn.GetOverseer()：无需外部监管者的节点经查询补丁后恒为 null。
            // 先复制候选外部监管者，再逐项处理，避免遍历 DirectRelations 时修改同一集合。
            List<Pawn> externalOverseers = new List<Pawn>();
            MAPOverseerRelationDirectionUtility.CollectActualOverseers(pawn, externalOverseers);
            if (externalOverseers.Count == 0)
            {
                return;
            }

            for (int i = 0; i < externalOverseers.Count; i++)
            {
                ClearOneExternalOverseerDirection(pawn, externalOverseers[i]);
            }
        }

        private static void ClearOneExternalOverseerDirection(Pawn node, Pawn externalOverseer)
        {
            if (externalOverseer == null || externalOverseer.Destroyed)
            {
                return;
            }

            // 异常互控：双方控制组都包含对方时，整条 Overseer 关系仍承载 node→external 的合法控制。
            // 只从 external 控制组移除 node，保留关系与 node 对 external 的控制组登记。
            bool nodeControlsExternal =
                node.mechanitor?.GetControlGroup(externalOverseer) != null;

            if (nodeControlsExternal)
            {
                if (externalOverseer.mechanitor == null)
                {
                    return;
                }

                externalOverseer.mechanitor.UnassignPawnFromAnyControlGroup(node);
                externalOverseer.mechanitor.Notify_BandwidthChanged();
                node.mechanitor?.Notify_BandwidthChanged();
                return;
            }

            // 非互控：安全移除双向 Overseer 关系（OnRelationRemoved 会解除 external 对 node 的控制组登记）。
            if (externalOverseer.relations == null)
            {
                return;
            }

            externalOverseer.relations.TryRemoveDirectRelation(
                PawnRelationDefOf.Overseer,
                node);
            externalOverseer.mechanitor?.Notify_BandwidthChanged();
            node.mechanitor?.Notify_BandwidthChanged();
        }
    }
}
