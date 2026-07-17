using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 仿生配偶指定/替换流程。入口检查 SyntheticSpouseInteraction。
    /// </summary>
    public static class SyntheticSpouseUtility
    {
        private const string AssignSpouseButtonKey =
            "MAP_MechanoidMechanitor.SyntheticSpouse.AssignSpouseButton";
        private const string AssignSpouseButtonDescKey =
            "MAP_MechanoidMechanitor.SyntheticSpouse.AssignSpouseButtonDesc";
        private const string NoCandidateKey =
            "MAP_MechanoidMechanitor.SyntheticSpouse.AssignSpouseNoCandidate";
        private const string FailedKey =
            "MAP_MechanoidMechanitor.SyntheticSpouse.AssignSpouseFailed";
        private const string MarriageLetterLabelKey =
            "MAP_MechanoidMechanitor.SyntheticSpouse.MarriageLetterLabel";
        private const string MarriageLetterTextKey =
            "MAP_MechanoidMechanitor.SyntheticSpouse.MarriageLetterText";

        private const float MinSpouseCandidateAgeYears = 16f;

        private static readonly List<Pawn> SpouseCandidateTmp = new List<Pawn>();
        private static readonly List<Pawn> OldSpouseTmp = new List<Pawn>();

        public static bool CanShowAssignSpouseButton(Pawn? pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (!MechanoidMechanitorCapabilityUtility.HasCapability(
                pawn, MechanoidMechanitorCapability.SyntheticSpouseInteraction))
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

        public static bool AssignSpouseUnchecked(Pawn? optedInPawn, Pawn? target)
        {
            if (optedInPawn == null || target == null || optedInPawn == target)
            {
                return false;
            }

            if (!MechanoidMechanitorCapabilityUtility.HasCapability(
                optedInPawn, MechanoidMechanitorCapability.SyntheticSpouseInteraction))
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

        public static bool TryReplaceSpouseWithEvents(Pawn? lover, Pawn? target)
        {
            if (lover == null || target == null)
            {
                return false;
            }

            if (!CanShowAssignSpouseButton(lover) || lover.relations == null || target.relations == null)
            {
                return false;
            }

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
            DisableLovinAndResetAfterAssignment(lover);
            SocialCardUtility.ClearCaches();
            return true;
        }

        public static void DrawAssignSpouseButton(Rect buttonRect, Pawn pawn)
        {
            Color previousColor = GUI.color;
            bool previousEnabled = GUI.enabled;
            try
            {
                GUI.color = Color.white;
                GUI.enabled = true;
                if (Widgets.ButtonText(
                        buttonRect,
                        AssignSpouseButtonKey.Translate(),
                        drawBackground: true,
                        doMouseoverSound: true,
                        active: true))
                {
                    TryOpenAssignSpouseMenu(pawn);
                }
            }
            finally
            {
                GUI.color = previousColor;
                GUI.enabled = previousEnabled;
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
            TryGainDivorcedMoodOnly(oldSpouse, lover);
            TaleRecorder.RecordTale(TaleDefOf.Breakup, lover, oldSpouse);
            SendBreakupLetter(lover, oldSpouse);
        }

        private static void TryGainDivorcedMoodOnly(Pawn oldSpouse, Pawn lover)
        {
            MemoryThoughtHandler? memories = oldSpouse.needs?.mood?.thoughts?.memories;
            if (memories == null)
            {
                return;
            }

            ThoughtDef? divorcedMood = ThoughtDefOf.DivorcedMe?.thoughtToMake;
            if (divorcedMood == null)
            {
                return;
            }

            memories.TryGainMemory(divorcedMood, lover);
        }

        private static void RemovePairMarriageMemories(Pawn lover, Pawn oldSpouse)
        {
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

        private static void DisableLovinAndResetAfterAssignment(Pawn lover)
        {
            SyntheticCompanionStateUtility.DisableLovinWithSpouse(lover);
            SyntheticCompanionStateUtility.ResetPregnancyApproachToAvoid(lover);
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
    }
}
