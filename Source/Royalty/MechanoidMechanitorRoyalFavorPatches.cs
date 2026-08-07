using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 原版荣誉奖励的 ChoosePawn 信件只会从 FreeColonists 填充候选。
    /// 这里仅在确认该 ChoosePawn 信件对应 QuestPart_GiveRoyalFavor 时，
    /// 按原任务的地图 / 远行队 / 信号上下文追加机械族机械师。
    /// </summary>
    internal static class MechanoidMechanitorRoyalFavorUtility
    {
        private static readonly FieldInfo? ColonistsFromSignalField =
            AccessTools.Field(typeof(QuestPart_Letter), "colonistsFromSignal");

        [ThreadStatic]
        private static RoyalFavorCandidateContext? currentContext;

        internal static RoyalFavorCandidateContext? CurrentContext
        {
            get => currentContext;
            set => currentContext = value;
        }

        internal static bool ShouldPrepareContext(
            QuestPart_Letter part,
            Signal signal)
        {
            return ModsConfig.RoyaltyActive
                && part.letter is ChoiceLetter_ChoosePawn
                && part.quest != null
                && !part.chosenPawnSignal.NullOrEmpty()
                && signal.tag == part.inSignal
                && HasMatchingRoyalFavorPart(
                    part.quest,
                    part.chosenPawnSignal);
        }

        internal static RoyalFavorCandidateContext BuildContext(
            QuestPart_Letter part,
            Signal signal)
        {
            List<Pawn> candidates = new List<Pawn>();

            if (part.useColonistsOnMap != null
                && part.useColonistsOnMap.HasMap)
            {
                AddEligibleCandidates(
                    candidates,
                    part.useColonistsOnMap.Map.mapPawns.AllPawns);
            }

            if (part.useColonistsFromCaravanArg
                && signal.args.TryGetArg(
                    "CARAVAN",
                    out Caravan caravan)
                && caravan != null)
            {
                AddEligibleCandidates(
                    candidates,
                    caravan.PawnsListForReading);
            }

            if (!part.getColonistsFromSignal.NullOrEmpty()
                && ColonistsFromSignalField?.GetValue(part)
                    is List<Pawn> signalPawns)
            {
                AddEligibleCandidates(candidates, signalPawns);
            }

            if (signal.args.TryGetArg("SUBJECT", out var subjectArg))
            {
                AddEligibleCandidatesFromObject(
                    candidates,
                    subjectArg.arg);
            }

            if (signal.args.TryGetArg("SENT", out var sentArg))
            {
                AddEligibleCandidatesFromObject(
                    candidates,
                    sentArg.arg);
            }

            return new RoyalFavorCandidateContext(
                part.quest,
                part.chosenPawnSignal,
                candidates);
        }

        internal static void TryAugmentLetter(Letter letter)
        {
            RoyalFavorCandidateContext? context = CurrentContext;
            if (context == null
                || letter is not ChoiceLetter_ChoosePawn choosePawn
                || !ReferenceEquals(choosePawn.quest, context.Quest)
                || !SignalsMatch(
                    choosePawn.chosenPawnSignal,
                    context.ChosenPawnSignal))
            {
                return;
            }

            for (int i = 0; i < context.Candidates.Count; i++)
            {
                Pawn pawn = context.Candidates[i];
                if (!choosePawn.pawns.Contains(pawn))
                {
                    choosePawn.pawns.Add(pawn);
                }
            }
        }

        private static bool HasMatchingRoyalFavorPart(
            Quest quest,
            string chosenPawnSignal)
        {
            List<QuestPart> parts = quest.PartsListForReading;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] is QuestPart_GiveRoyalFavor royalFavor
                    && SignalsMatch(
                        royalFavor.inSignal,
                        chosenPawnSignal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SignalsMatch(
            string? first,
            string? second)
        {
            if (first.NullOrEmpty() || second.NullOrEmpty())
            {
                return false;
            }

            if (string.Equals(first, second, StringComparison.Ordinal))
            {
                return true;
            }

            return first.EndsWith(
                       "." + second,
                       StringComparison.Ordinal)
                || second.EndsWith(
                    "." + first,
                    StringComparison.Ordinal);
        }

        private static void AddEligibleCandidates(
            List<Pawn> destination,
            IEnumerable<Pawn>? source)
        {
            if (source == null)
            {
                return;
            }

            foreach (Pawn pawn in source)
            {
                AddEligibleCandidate(destination, pawn);
            }
        }

        private static void AddEligibleCandidatesFromObject(
            List<Pawn> destination,
            object? source)
        {
            if (source is Pawn pawn)
            {
                AddEligibleCandidate(destination, pawn);
                return;
            }

            if (source is IEnumerable<Pawn> pawns)
            {
                AddEligibleCandidates(destination, pawns);
                return;
            }

            if (source is IEnumerable<Thing> things)
            {
                foreach (Thing thing in things)
                {
                    if (thing is Pawn thingPawn)
                    {
                        AddEligibleCandidate(
                            destination,
                            thingPawn);
                    }
                }
            }
        }

        private static void AddEligibleCandidate(
            List<Pawn> destination,
            Pawn? pawn)
        {
            if (!MechanoidMechanitorRoyaltyUtility
                    .IsRoyaltyEligibleMechanitor(pawn))
            {
                return;
            }

            MechanoidMechanitorRoyaltyUtility
                .EnsureRoyaltyInfrastructure(pawn);

            if (!destination.Contains(pawn!))
            {
                destination.Add(pawn!);
            }
        }

        internal sealed class RoyalFavorCandidateContext
        {
            internal RoyalFavorCandidateContext(
                Quest quest,
                string chosenPawnSignal,
                List<Pawn> candidates)
            {
                Quest = quest;
                ChosenPawnSignal = chosenPawnSignal;
                Candidates = candidates;
            }

            internal Quest Quest { get; }
            internal string ChosenPawnSignal { get; }
            internal List<Pawn> Candidates { get; }
        }
    }

    [HarmonyPatch(
        typeof(QuestPart_Letter),
        nameof(QuestPart_Letter.Notify_QuestSignalReceived))]
    internal static class Patch_QuestPart_Letter_RoyalFavorMechanitorCandidates
    {
        [HarmonyPrefix]
        internal static void Prefix(
            QuestPart_Letter __instance,
            Signal signal,
            out MechanoidMechanitorRoyalFavorUtility
                .RoyalFavorCandidateContext? __state)
        {
            __state =
                MechanoidMechanitorRoyalFavorUtility.CurrentContext;

            if (!MechanoidMechanitorRoyalFavorUtility
                    .ShouldPrepareContext(__instance, signal))
            {
                return;
            }

            MechanoidMechanitorRoyalFavorUtility.CurrentContext =
                MechanoidMechanitorRoyalFavorUtility.BuildContext(
                    __instance,
                    signal);
        }

        [HarmonyFinalizer]
        internal static Exception? Finalizer(
            Exception? __exception,
            MechanoidMechanitorRoyalFavorUtility
                .RoyalFavorCandidateContext? __state)
        {
            MechanoidMechanitorRoyalFavorUtility.CurrentContext =
                __state;
            return __exception;
        }
    }

    [HarmonyPatch(
        typeof(LetterStack),
        nameof(LetterStack.ReceiveLetter),
        new Type[]
        {
            typeof(Letter),
            typeof(string),
            typeof(int),
            typeof(bool)
        })]
    public static class Patch_LetterStack_ReceiveLetter_RoyalFavorMechanitorCandidates
    {
        [HarmonyPrefix]
        public static void Prefix(Letter let)
        {
            MechanoidMechanitorRoyalFavorUtility
                .TryAugmentLetter(let);
        }
    }
}
