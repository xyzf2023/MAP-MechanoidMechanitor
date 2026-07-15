using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 显式社交关系工具：只作用于真正挂载 CompExplicitSocialRelationUser 的 Pawn。
    /// </summary>
    public static class ExplicitSocialRelationUtility
    {
        private const string AssignSpouseButtonKey =
            "MAP_MechanoidMechanitor.ExplicitSocial.AssignSpouseButton";
        private const string AssignSpouseButtonDescKey =
            "MAP_MechanoidMechanitor.ExplicitSocial.AssignSpouseButtonDesc";
        private const string NoCandidateKey =
            "MAP_MechanoidMechanitor.ExplicitSocial.AssignSpouseNoCandidate";
        private const string FailedKey =
            "MAP_MechanoidMechanitor.ExplicitSocial.AssignSpouseFailed";
        private const string MarriageLetterLabelKey =
            "MAP_MechanoidMechanitor.ExplicitSocial.MarriageLetterLabel";
        private const string MarriageLetterTextKey =
            "MAP_MechanoidMechanitor.ExplicitSocial.MarriageLetterText";

        private const float MinSpouseCandidateAgeYears = 16f;

        private static readonly List<Pawn> EmptySocialInfoPawns = new List<Pawn>();
        private static readonly List<Pawn> SpouseCandidateTmp = new List<Pawn>();
        private static readonly List<Pawn> OldSpouseTmp = new List<Pawn>();

        /// <summary>
        /// 社交面板「见过的人」列表的空结果；调用方只遍历、不修改。
        /// </summary>
        public static List<Pawn> EmptyPawnsForSocialInfo => EmptySocialInfoPawns;

        public static bool IsOptedIn(Pawn? pawn) =>
            pawn?.GetComp<CompExplicitSocialRelationUser>() != null;

        /// <summary>
        /// 是否应在社交面板底部为授权 Pawn 预留「指定配偶……」按钮区域。
        /// </summary>
        public static bool CanShowAssignSpouseButton(Pawn? pawn)
        {
            if (!IsOptedIn(pawn) || pawn == null)
            {
                return false;
            }

            if (pawn.Dead || !pawn.Spawned)
            {
                return false;
            }

            if (pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }

            return pawn.relations != null;
        }

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
        /// 写入 Spouse 关系。不做婚姻合法性检查；不发送事件、不触发生成记忆。
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
        /// 更换恋人为唯一当前配偶：先解除旧婚姻并发送离婚反馈，再建立新婚并发送结婚反馈。
        /// 不调用原版 DoDivorce / Married / InteractionWorker_Breakup.Interacted。
        /// </summary>
        public static bool TryReplaceSpouseWithEvents(Pawn? lover, Pawn? target)
        {
            if (lover == null || target == null)
            {
                return false;
            }

            if (!IsOptedIn(lover) || lover.relations == null || target.relations == null)
            {
                return false;
            }

            if (!CanShowAssignSpouseButton(lover))
            {
                return false;
            }

            // 菜单打开后状态可能变化：必须先确认新目标仍可指定，再解除旧婚姻。
            if (!IsValidSpouseCandidate(lover, target))
            {
                return false;
            }

            CollectCurrentSpousesExcluding(lover, target, OldSpouseTmp);
            for (int i = 0; i < OldSpouseTmp.Count; i++)
            {
                DivorceLoverFromOldSpouse(lover, OldSpouseTmp[i]);
            }

            if (lover.relations.DirectRelationExists(PawnRelationDefOf.ExSpouse, target))
            {
                lover.relations.TryRemoveDirectRelation(PawnRelationDefOf.ExSpouse, target);
            }

            if (!AssignSpouseUnchecked(lover, target))
            {
                return false;
            }

            ApplyNewlyMarriedFeedback(lover, target);
            TaleRecorder.RecordTale(TaleDefOf.Marriage, lover, target);
            SendMarriageLetter(lover, target);
            SocialCardUtility.ClearCaches();
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

        /// <summary>
        /// 在原版「恋爱……」按钮区域内绘制「指定配偶……」。
        /// </summary>
        public static void DrawAssignSpouseButton(Rect buttonRect, Pawn pawn)
        {
            if (Widgets.ButtonText(buttonRect, AssignSpouseButtonKey.Translate()))
            {
                TryOpenAssignSpouseMenu(pawn);
            }

            TooltipHandler.TipRegion(buttonRect, AssignSpouseButtonDescKey.Translate());
        }

        private static void TryOpenAssignSpouseMenu(Pawn lover)
        {
            List<Pawn> candidates = CollectSpouseCandidates(lover);
            if (candidates.Count == 0)
            {
                Messages.Message(
                    NoCandidateKey.Translate(),
                    lover,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn candidate = candidates[i];
                Pawn localCandidate = candidate;
                options.Add(new FloatMenuOption(
                    localCandidate.LabelCap,
                    delegate
                    {
                        TryAssignSpouseFromMenu(lover, localCandidate);
                    }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void TryAssignSpouseFromMenu(Pawn lover, Pawn target)
        {
            if (TryReplaceSpouseWithEvents(lover, target))
            {
                return;
            }

            Messages.Message(
                FailedKey.Translate(),
                lover,
                MessageTypeDefOf.RejectInput,
                historical: false);
        }

        /// <summary>
        /// 点击时收集当前地图上的成年自由殖民者；不按性别/取向/意识形态过滤。
        /// </summary>
        private static List<Pawn> CollectSpouseCandidates(Pawn lover)
        {
            SpouseCandidateTmp.Clear();

            if (lover?.Map == null || lover.relations == null)
            {
                return SpouseCandidateTmp;
            }

            List<Pawn> freeColonists = lover.Map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < freeColonists.Count; i++)
            {
                Pawn candidate = freeColonists[i];
                if (!IsValidSpouseCandidate(lover, candidate))
                {
                    continue;
                }

                SpouseCandidateTmp.Add(candidate);
            }

            SpouseCandidateTmp.Sort((a, b) => string.CompareOrdinal(a.LabelShort, b.LabelShort));
            return SpouseCandidateTmp;
        }

        private static bool IsValidSpouseCandidate(Pawn lover, Pawn candidate)
        {
            if (candidate == null || candidate == lover)
            {
                return false;
            }

            if (!candidate.Spawned || candidate.Dead)
            {
                return false;
            }

            if (candidate.Faction != Faction.OfPlayer)
            {
                return false;
            }

            if (lover.relations == null || candidate.relations == null)
            {
                return false;
            }

            if (candidate.ageTracker == null
                || candidate.ageTracker.AgeBiologicalYearsFloat < MinSpouseCandidateAgeYears)
            {
                return false;
            }

            if (lover.relations.DirectRelationExists(PawnRelationDefOf.Spouse, candidate))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 直接遍历 DirectRelations 复制旧配偶列表；禁止使用带 IsFlesh 门槛的 GetSpouses 等扩展。
        /// </summary>
        private static void CollectCurrentSpousesExcluding(Pawn lover, Pawn exclude, List<Pawn> into)
        {
            into.Clear();
            if (lover.relations == null)
            {
                return;
            }

            List<DirectPawnRelation> relations = lover.relations.DirectRelations;
            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation relation = relations[i];
                if (relation.def != PawnRelationDefOf.Spouse)
                {
                    continue;
                }

                Pawn? other = relation.otherPawn;
                if (other == null || other == exclude)
                {
                    continue;
                }

                into.Add(other);
            }
        }

        /// <summary>
        /// 恋人与单个旧配偶的受控离婚：仅关系、定向记忆、信封与故事，不含改姓/婚床/意识形态。
        /// </summary>
        private static void DivorceLoverFromOldSpouse(Pawn lover, Pawn oldSpouse)
        {
            if (lover.relations == null || oldSpouse.relations == null)
            {
                return;
            }

            if (!lover.relations.DirectRelationExists(PawnRelationDefOf.Spouse, oldSpouse))
            {
                return;
            }

            lover.relations.RemoveDirectRelation(PawnRelationDefOf.Spouse, oldSpouse);

            if (!lover.relations.DirectRelationExists(PawnRelationDefOf.ExSpouse, oldSpouse))
            {
                lover.relations.AddDirectRelation(PawnRelationDefOf.ExSpouse, oldSpouse);
            }

            RemovePairMarriageMemories(lover, oldSpouse);

            if (oldSpouse.needs?.mood != null)
            {
                oldSpouse.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.DivorcedMe, lover);
            }

            TaleRecorder.RecordTale(TaleDefOf.Breakup, lover, oldSpouse);
            SendBreakupLetter(lover, oldSpouse);
        }

        private static void RemovePairMarriageMemories(Pawn lover, Pawn oldSpouse)
        {
            // 必须按 otherPawn 定向删除，禁止 RemoveMemoriesOfDef(GotMarried)，以免误删与他人的新婚记忆。
            MemoryThoughtHandler? loverMemories = lover.needs?.mood?.thoughts?.memories;
            if (loverMemories != null)
            {
                loverMemories.RemoveMemoriesOfDefWhereOtherPawnIs(ThoughtDefOf.GotMarried, oldSpouse);
                loverMemories.RemoveMemoriesOfDefWhereOtherPawnIs(ThoughtDefOf.HoneymoonPhase, oldSpouse);
            }

            MemoryThoughtHandler? oldSpouseMemories = oldSpouse.needs?.mood?.thoughts?.memories;
            if (oldSpouseMemories != null)
            {
                oldSpouseMemories.RemoveMemoriesOfDefWhereOtherPawnIs(ThoughtDefOf.GotMarried, lover);
                oldSpouseMemories.RemoveMemoriesOfDefWhereOtherPawnIs(ThoughtDefOf.HoneymoonPhase, lover);
            }
        }

        private static void ApplyNewlyMarriedFeedback(Pawn lover, Pawn target)
        {
            ApplyNewlyMarriedSide(lover, target);
            ApplyNewlyMarriedSide(target, lover);
        }

        private static void ApplyNewlyMarriedSide(Pawn pawn, Pawn otherPawn)
        {
            MemoryThoughtHandler? memories = pawn.needs?.mood?.thoughts?.memories;
            if (memories == null)
            {
                return;
            }

            memories.RemoveMemoriesOfDefWhereOtherPawnIs(ThoughtDefOf.DivorcedMe, otherPawn);
            memories.TryGainMemory(ThoughtDefOf.GotMarried, otherPawn);
            memories.TryGainMemory(ThoughtDefOf.HoneymoonPhase, otherPawn);
        }

        private static void SendBreakupLetter(Pawn lover, Pawn oldSpouse)
        {
            TaggedString label = "LetterLabelBreakup".Translate();
            TaggedString text = "LetterNoLongerLovers".Translate(
                lover.LabelShort,
                oldSpouse.LabelShort,
                lover.Named("PAWN1"),
                oldSpouse.Named("PAWN2"));
            Find.LetterStack.ReceiveLetter(
                label,
                text,
                LetterDefOf.NegativeEvent,
                new LookTargets(lover, oldSpouse));
        }

        private static void SendMarriageLetter(Pawn lover, Pawn target)
        {
            Find.LetterStack.ReceiveLetter(
                MarriageLetterLabelKey.Translate(),
                MarriageLetterTextKey.Translate(lover.Named("LOVER"), target.Named("TARGET")),
                LetterDefOf.PositiveEvent,
                new LookTargets(lover, target));
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
