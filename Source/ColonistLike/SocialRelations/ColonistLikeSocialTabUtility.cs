using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 殖民者式社交面板基础能力：带查看者视角的显式关系判定，以及绕过 IsFlesh 的关系枚举。
    /// 所有入口仅检查 ColonistLikeSocialTab 能力。
    /// </summary>
    public static class ColonistLikeSocialTabUtility
    {
        private static readonly List<Pawn> EmptySocialInfoPawns = new List<Pawn>();

        public static List<Pawn> EmptyPawnsForSocialInfo => EmptySocialInfoPawns;

        public static bool HasSocialTab(Pawn? pawn) =>
            MechanoidMechanitorCapabilityUtility.HasCapability(
                pawn, MechanoidMechanitorCapability.ColonistLikeSocialTab);

        /// <summary>
        /// 从 viewer 视角判断是否应在社交面板显示与 other 的行。
        /// 控制者一侧的 Overseer 不计入；若仅剩该隐藏关系则整行不显示。
        /// </summary>
        public static bool ShouldShowExplicitSocialRelation(Pawn? viewer, Pawn? other)
        {
            if (viewer == null || other == null || viewer == other)
            {
                return false;
            }

            if (viewer.relations == null || other.relations == null)
            {
                return false;
            }

            return HasDisplayableDirectRelationOn(viewer, other, viewer, other)
                || HasDisplayableDirectRelationOn(other, viewer, viewer, other);
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

            if (!ShouldShowExplicitSocialRelation(me, other))
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

                if (ShouldHideOverseerForViewer(me, other, pawnRelationDef))
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

        private static bool ShouldHideOverseerForViewer(
            Pawn viewer,
            Pawn other,
            PawnRelationDef def)
        {
            return def == PawnRelationDefOf.Overseer
                && MAPOverseerRelationDirectionUtility.IsViewerOnOverseerSide(viewer, other);
        }

        private static bool HasDisplayableDirectRelationOn(
            Pawn owner,
            Pawn counterpart,
            Pawn viewer,
            Pawn other)
        {
            if (owner.relations == null)
            {
                return false;
            }

            List<DirectPawnRelation> relations = owner.relations.DirectRelations;
            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation relation = relations[i];
                if (relation.otherPawn != counterpart)
                {
                    continue;
                }

                if (ShouldHideOverseerForViewer(viewer, other, relation.def))
                {
                    continue;
                }

                return true;
            }

            return false;
        }
    }
}
