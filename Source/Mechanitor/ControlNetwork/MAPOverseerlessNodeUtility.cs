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
            // 用控制组方向工具扫描，只移除“本节点作为被控制对象”的外部监管关系。
            Pawn? overseer = MAPOverseerRelationDirectionUtility.FindActualOverseer(pawn);
            if (overseer?.relations == null)
            {
                return;
            }

            overseer.relations.TryRemoveDirectRelation(PawnRelationDefOf.Overseer, pawn);
        }
    }
}
