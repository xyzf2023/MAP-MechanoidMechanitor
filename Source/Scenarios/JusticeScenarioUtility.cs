using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class JusticeScenarioUtility
    {
        private const string JusticePawnKindDefName = "MAP_Mech_Justice";

        public static ScenPart_JusticeScenario? ActiveScenarioPart =>
            Find.Scenario?.AllParts.OfType<ScenPart_JusticeScenario>().FirstOrDefault();

        public static bool IsJusticeScenarioActive => ActiveScenarioPart != null;

        public static bool HasLivingJustice
        {
            get
            {
                if (!IsJusticeScenarioActive)
                {
                    return false;
                }

                PawnKindDef? justiceKind = DefDatabase<PawnKindDef>.GetNamedSilentFail(JusticePawnKindDefName);
                if (justiceKind == null)
                {
                    return false;
                }

                foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction)
                {
                    if (!pawn.Dead && pawn.kindDef == justiceKind)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public static bool ShouldPreventGameOver =>
            IsJusticeScenarioActive && HasLivingJustice;

        public static bool HasGameEndedLetter
        {
            get
            {
                if (Current.Game == null || Find.LetterStack == null)
                {
                    return false;
                }

                return Find.LetterStack.LettersListForReading.Any(letter =>
                    letter.def == LetterDefOf.GameEnded);
            }
        }

        public static void CancelGameOverState(GameEnder gameEnder)
        {
            gameEnder.gameEnding = false;

            if (Current.Game == null || Find.LetterStack == null)
            {
                return;
            }

            for (int i = Find.LetterStack.LettersListForReading.Count - 1; i >= 0; i--)
            {
                Letter letter = Find.LetterStack.LettersListForReading[i];
                if (letter.def == LetterDefOf.GameEnded)
                {
                    Find.LetterStack.RemoveLetter(letter);
                }
            }
        }
    }
}
