using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 显式社交关系工具：只作用于真正挂载 CompExplicitSocialRelationUser 的 Pawn。
    /// </summary>
    public static class ExplicitSocialRelationUtility
    {
        private static readonly List<Pawn> EmptySocialInfoPawns = new List<Pawn>();

        /// <summary>
        /// 社交面板「见过的人」列表的空结果；调用方只遍历、不修改。
        /// </summary>
        public static List<Pawn> EmptyPawnsForSocialInfo => EmptySocialInfoPawns;

        public static bool IsOptedIn(Pawn? pawn) =>
            pawn?.GetComp<CompExplicitSocialRelationUser>() != null;

        /// <summary>
        /// 双方 DirectRelations 中是否存在对方（任一方向均可；不依赖 everSeenByPlayer / 好感度）。
        /// </summary>
        public static bool HasExplicitDirectRelation(Pawn? a, Pawn? b)
        {
            if (a == null || b == null || a == b)
            {
                return false;
            }

            if (HasOtherPawnInDirectRelations(a, b) || HasOtherPawnInDirectRelations(b, a))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 写入 Spouse 关系。不做婚姻合法性检查；由上游选择系统保证目标已通过规则校验。
        /// 只调用一次 AddDirectRelation（Spouse 为 reflexive，原版会自动写入双方）。
        /// </summary>
        public static bool AssignSpouseUnchecked(Pawn? optedInPawn, Pawn? target)
        {
            if (optedInPawn == null || target == null)
            {
                return false;
            }

            if (optedInPawn == target)
            {
                return false;
            }

            if (!IsOptedIn(optedInPawn))
            {
                return false;
            }

            if (optedInPawn.relations == null || target.relations == null)
            {
                return false;
            }

            if (optedInPawn.relations.DirectRelationExists(PawnRelationDefOf.Spouse, target))
            {
                return false;
            }

            optedInPawn.relations.AddDirectRelation(PawnRelationDefOf.Spouse, target);
            return true;
        }

        /// <summary>
        /// 在已确认存在主动写入的明确关系时，按原版 Worker 逻辑解析关系，且不要求双方 IsFlesh。
        /// 不得再调用 GetRelations，以免递归进 Harmony 补丁。
        /// </summary>
        public static IEnumerable<PawnRelationDef> EnumerateRelationsWithoutFleshRequirement(
            Pawn me,
            Pawn other)
        {
            if (me == null || other == null || me == other)
            {
                yield break;
            }

            if (me.relations == null || other.relations == null)
            {
                yield break;
            }

            if (!HasExplicitDirectRelation(me, other))
            {
                yield break;
            }

            bool anyNonKinFamilyByBloodRelation = false;
            List<PawnRelationDef> defs = DefDatabase<PawnRelationDef>.AllDefsListForReading;
            int count = defs.Count;
            for (int i = 0; i < count; i++)
            {
                PawnRelationDef pawnRelationDef = defs[i];
                if (pawnRelationDef == PawnRelationDefOf.Kin)
                {
                    continue;
                }

                if (!pawnRelationDef.Worker.InRelation(me, other))
                {
                    continue;
                }

                if (pawnRelationDef.familyByBloodRelation)
                {
                    anyNonKinFamilyByBloodRelation = true;
                }

                yield return pawnRelationDef;
            }

            if (!anyNonKinFamilyByBloodRelation
                && PawnRelationDefOf.Kin.Worker.InRelation(me, other))
            {
                yield return PawnRelationDefOf.Kin;
            }
        }

        private static bool HasOtherPawnInDirectRelations(Pawn owner, Pawn other)
        {
            if (owner.relations == null)
            {
                return false;
            }

            List<DirectPawnRelation> relations = owner.relations.DirectRelations;
            for (int i = 0; i < relations.Count; i++)
            {
                if (relations[i].otherPawn == other)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
