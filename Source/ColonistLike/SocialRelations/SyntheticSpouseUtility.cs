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

        public static bool AssignSpouseUnchecked(Pawn? syntheticCompanion, Pawn? target)
        {
            if (syntheticCompanion == null || target == null || syntheticCompanion == target)
            {
                return false;
            }

            if (!MechanoidMechanitorCapabilityUtility.HasCapability(
                syntheticCompanion, MechanoidMechanitorCapability.SyntheticSpouseInteraction))
            {
                return false;
            }

            if (syntheticCompanion.relations == null || target.relations == null)
            {
                return false;
            }

            if (syntheticCompanion.relations.DirectRelationExists(PawnRelationDefOf.Spouse, target))
            {
                return false;
            }

            syntheticCompanion.relations.AddDirectRelation(PawnRelationDefOf.Spouse, target);
            return true;
        }

        public static bool TryReplaceSpouseWithEvents(Pawn? syntheticCompanion, Pawn? target)
        {
            if (syntheticCompanion == null || target == null)
            {
                return false;
            }

            if (!CanShowAssignSpouseButton(syntheticCompanion)
                || syntheticCompanion.relations == null
                || target.relations == null)
            {
                return false;
            }

            if (!IsValidSpouseCandidate(syntheticCompanion, target))
            {
                return false;
            }

            CollectCurrentSpousesExcluding(syntheticCompanion, target, OldSpouseTmp);
            for (int i = 0; i < OldSpouseTmp.Count; i++)
            {
                DivorceSyntheticCompanionFromOldSpouse(syntheticCompanion, OldSpouseTmp[i]);
            }

            if (syntheticCompanion.relations.DirectRelationExists(PawnRelationDefOf.ExSpouse, target))
            {
                syntheticCompanion.relations.TryRemoveDirectRelation(PawnRelationDefOf.ExSpouse, target);
            }

            if (!AssignSpouseUnchecked(syntheticCompanion, target))
            {
                return false;
            }

            ApplyNewlyMarriedFeedback(syntheticCompanion, target);
            TaleRecorder.RecordTale(TaleDefOf.Marriage, syntheticCompanion, target);
            SendMarriageLetter(syntheticCompanion, target);
            DisableLovinAndResetAfterAssignment(syntheticCompanion);
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

        private static void TryOpenAssignSpouseMenu(Pawn syntheticCompanion)
        {
            List<Pawn> candidates = CollectSpouseCandidates(syntheticCompanion);
            if (candidates.Count == 0)
            {
                Messages.Message(
                    NoCandidateKey.Translate(),
                    syntheticCompanion,
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
                        TryAssignSpouseFromMenu(syntheticCompanion, localCandidate);
                    }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void TryAssignSpouseFromMenu(Pawn syntheticCompanion, Pawn target)
        {
            if (TryReplaceSpouseWithEvents(syntheticCompanion, target))
            {
                return;
            }

            Messages.Message(
                FailedKey.Translate(),
                syntheticCompanion,
                MessageTypeDefOf.RejectInput,
                historical: false);
        }

        private static List<Pawn> CollectSpouseCandidates(Pawn syntheticCompanion)
        {
            SpouseCandidateTmp.Clear();

            if (syntheticCompanion?.Map == null || syntheticCompanion.relations == null)
            {
                return SpouseCandidateTmp;
            }

            List<Pawn> freeColonists = syntheticCompanion.Map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < freeColonists.Count; i++)
            {
                Pawn candidate = freeColonists[i];
                if (!IsValidSpouseCandidate(syntheticCompanion, candidate))
                {
                    continue;
                }

                SpouseCandidateTmp.Add(candidate);
            }

            SpouseCandidateTmp.Sort((a, b) => string.CompareOrdinal(a.LabelShort, b.LabelShort));
            return SpouseCandidateTmp;
        }

        private static bool IsValidSpouseCandidate(Pawn syntheticCompanion, Pawn candidate)
        {
            if (candidate == null || candidate == syntheticCompanion)
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

            if (syntheticCompanion.relations == null || candidate.relations == null)
            {
                return false;
            }

            if (candidate.ageTracker == null
                || candidate.ageTracker.AgeBiologicalYearsFloat < MinSpouseCandidateAgeYears)
            {
                return false;
            }

            if (syntheticCompanion.relations.DirectRelationExists(PawnRelationDefOf.Spouse, candidate))
            {
                return false;
            }

            return true;
        }

        private static void CollectCurrentSpousesExcluding(
            Pawn syntheticCompanion,
            Pawn exclude,
            List<Pawn> into)
        {
            into.Clear();
            if (syntheticCompanion.relations == null)
            {
                return;
            }

            List<DirectPawnRelation> relations = syntheticCompanion.relations.DirectRelations;
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

        private static void DivorceSyntheticCompanionFromOldSpouse(
            Pawn syntheticCompanion,
            Pawn oldSpouse)
        {
            if (syntheticCompanion.relations == null || oldSpouse.relations == null)
            {
                return;
            }

            if (!syntheticCompanion.relations.DirectRelationExists(PawnRelationDefOf.Spouse, oldSpouse))
            {
                return;
            }

            syntheticCompanion.relations.RemoveDirectRelation(PawnRelationDefOf.Spouse, oldSpouse);

            if (!syntheticCompanion.relations.DirectRelationExists(PawnRelationDefOf.ExSpouse, oldSpouse))
            {
                syntheticCompanion.relations.AddDirectRelation(PawnRelationDefOf.ExSpouse, oldSpouse);
            }

            RemovePairMarriageMemories(syntheticCompanion, oldSpouse);
            TryGainDivorcedMoodOnly(oldSpouse, syntheticCompanion);
            TaleRecorder.RecordTale(TaleDefOf.Breakup, syntheticCompanion, oldSpouse);
            SendBreakupLetter(syntheticCompanion, oldSpouse);
        }

        private static void TryGainDivorcedMoodOnly(Pawn oldSpouse, Pawn syntheticCompanion)
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

            memories.TryGainMemory(divorcedMood, syntheticCompanion);
        }

        private static void RemovePairMarriageMemories(Pawn syntheticCompanion, Pawn oldSpouse)
        {
            MemoryThoughtHandler? companionMemories =
                syntheticCompanion.needs?.mood?.thoughts?.memories;
            if (companionMemories != null)
            {
                companionMemories.RemoveMemoriesOfDefWhereOtherPawnIs(
                    ThoughtDefOf.GotMarried, oldSpouse);
                companionMemories.RemoveMemoriesOfDefWhereOtherPawnIs(
                    ThoughtDefOf.HoneymoonPhase, oldSpouse);
            }

            MemoryThoughtHandler? oldSpouseMemories = oldSpouse.needs?.mood?.thoughts?.memories;
            if (oldSpouseMemories != null)
            {
                oldSpouseMemories.RemoveMemoriesOfDefWhereOtherPawnIs(
                    ThoughtDefOf.GotMarried, syntheticCompanion);
                oldSpouseMemories.RemoveMemoriesOfDefWhereOtherPawnIs(
                    ThoughtDefOf.HoneymoonPhase, syntheticCompanion);
            }
        }

        private static void ApplyNewlyMarriedFeedback(Pawn syntheticCompanion, Pawn target)
        {
            ApplyNewlyMarriedSide(syntheticCompanion, target);
            ApplyNewlyMarriedSide(target, syntheticCompanion);
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

        private static void DisableLovinAndResetAfterAssignment(Pawn syntheticCompanion)
        {
            SyntheticCompanionStateUtility.DisableLovinWithSpouse(syntheticCompanion);
            SyntheticCompanionStateUtility.ResetPregnancyApproachToAvoid(syntheticCompanion);
        }

        private static void SendBreakupLetter(Pawn syntheticCompanion, Pawn oldSpouse)
        {
            TaggedString label = "LetterLabelBreakup".Translate();
            TaggedString text = "LetterNoLongerLovers".Translate(
                syntheticCompanion.LabelShort,
                oldSpouse.LabelShort,
                syntheticCompanion.Named("PAWN1"),
                oldSpouse.Named("PAWN2"));
            Find.LetterStack.ReceiveLetter(
                label,
                text,
                LetterDefOf.NegativeEvent,
                new LookTargets(syntheticCompanion, oldSpouse));
        }

        private static void SendMarriageLetter(Pawn syntheticCompanion, Pawn target)
        {
            Find.LetterStack.ReceiveLetter(
                MarriageLetterLabelKey.Translate(),
                MarriageLetterTextKey.Translate(
                    syntheticCompanion.Named("COMPANION"),
                    target.Named("TARGET")),
                LetterDefOf.PositiveEvent,
                new LookTargets(syntheticCompanion, target));
        }
    }
}
