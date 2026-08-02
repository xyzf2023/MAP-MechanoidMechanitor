using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// Overseer 关系方向解析：以控制组归属为准，不依赖 DirectRelations 顺序。
    /// </summary>
    public static class MAPOverseerRelationDirectionUtility
    {
        /// <summary>
        /// 判断 potentialOverseer 是否是 subject 的实际监管者。
        /// 依据：双方存在 Overseer 关系，且 potentialOverseer 的控制组包含 subject。
        /// </summary>
        public static bool IsActualOverseerOf(Pawn? potentialOverseer, Pawn? subject)
        {
            if (potentialOverseer == null || subject == null || potentialOverseer == subject)
            {
                return false;
            }

            if (!ModsConfig.BiotechActive)
            {
                return false;
            }

            if (!HasOverseerRelation(potentialOverseer, subject))
            {
                return false;
            }

            return ControlsSubject(potentialOverseer, subject);
        }

        /// <summary>
        /// 从 subject 的 Overseer DirectRelations 中寻找实际监管者。
        /// 当前循环已经确认候选者与 subject 存在 Overseer 关系，因此只验证控制组方向，
        /// 不再调用 IsActualOverseerOf 重新扫描双方关系列表。
        /// </summary>
        public static Pawn? FindActualOverseer(Pawn? subject, Predicate<Pawn>? predicate = null)
        {
            if (subject?.relations == null || !ModsConfig.BiotechActive)
            {
                return null;
            }

            List<DirectPawnRelation> relations = subject.relations.DirectRelations;
            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation relation = relations[i];
                if (relation.def != PawnRelationDefOf.Overseer)
                {
                    continue;
                }

                Pawn? candidate = relation.otherPawn;
                if (candidate == null || ReferenceEquals(candidate, subject))
                {
                    continue;
                }

                if (predicate != null && !predicate(candidate))
                {
                    continue;
                }

                if (!ControlsSubject(candidate, subject))
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        /// <summary>
        /// 判断 viewer 是否处于与 other 的 Overseer 关系的控制者一侧。
        /// </summary>
        public static bool IsViewerOnOverseerSide(Pawn? viewer, Pawn? other)
        {
            return IsActualOverseerOf(viewer, other);
        }

        /// <summary>
        /// 收集 subject 的全部实际监管者（控制组包含 subject 的 Overseer 对端）。
        /// 写入前不清空 into；调用方应自行准备空列表。
        /// 当前遍历已确认关系存在，因此只检查控制组方向。
        /// </summary>
        public static void CollectActualOverseers(Pawn? subject, List<Pawn> into)
        {
            if (subject?.relations == null || into == null || !ModsConfig.BiotechActive)
            {
                return;
            }

            List<DirectPawnRelation> relations = subject.relations.DirectRelations;
            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation relation = relations[i];
                if (relation.def != PawnRelationDefOf.Overseer)
                {
                    continue;
                }

                Pawn? candidate = relation.otherPawn;
                if (candidate == null || ReferenceEquals(candidate, subject))
                {
                    continue;
                }

                if (!ControlsSubject(candidate, subject))
                {
                    continue;
                }

                if (!into.Contains(candidate))
                {
                    into.Add(candidate);
                }
            }
        }

        private static bool ControlsSubject(Pawn potentialOverseer, Pawn subject)
        {
            return potentialOverseer.mechanitor?.GetControlGroup(subject) != null;
        }

        private static bool HasOverseerRelation(Pawn a, Pawn b)
        {
            if (a.relations != null
                && a.relations.DirectRelationExists(PawnRelationDefOf.Overseer, b))
            {
                return true;
            }

            return b.relations != null
                && b.relations.DirectRelationExists(PawnRelationDefOf.Overseer, a);
        }
    }
}
