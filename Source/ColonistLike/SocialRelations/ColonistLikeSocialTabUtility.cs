using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 殖民者式社交面板基础能力：显式关系判定和绕过 IsFlesh 的关系枚举。
    /// 所有入口仅检查 ColonistLikeSocialTab 能力。
    /// </summary>
    public static class ColonistLikeSocialTabUtility
    {
        private static readonly List<Pawn> EmptySocialInfoPawns = new List<Pawn>();

        public static List<Pawn> EmptyPawnsForSocialInfo => EmptySocialInfoPawns;

        public static bool HasSocialTab(Pawn? pawn) =>
            MechanoidMechanitorCapabilityUtility.HasCapability(
                pawn, MechanoidMechanitorCapability.ColonistLikeSocialTab);

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
