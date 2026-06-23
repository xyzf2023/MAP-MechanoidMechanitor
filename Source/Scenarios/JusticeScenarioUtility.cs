using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class JusticeScenarioUtility
    {
        private const string LegacyJusticePawnKindDefName = "MAP_Mech_Justice";

        public static PawnKindDef? JusticePawnKind =>
            DefDatabase<PawnKindDef>.GetNamedSilentFail(LegacyJusticePawnKindDefName);

        public static ScenPart_JusticeScenario? ActiveScenarioPart =>
            Find.Scenario?.AllParts.OfType<ScenPart_JusticeScenario>().FirstOrDefault();

        public static ScenPart_MechanoidMechanitor? MechanitorStartPart =>
            Find.Scenario?.AllParts.OfType<ScenPart_MechanoidMechanitor>().FirstOrDefault();

        public static bool IsJusticeScenarioActive => ActiveScenarioPart != null;

        public static Pawn? ScenarioProtagonist =>
            GameComponent_MechanoidMechanitorRegistry.CurrentScenarioProtagonist;

        public static Pawn? MechanicalConsciousnessHost =>
            GameComponent_MechanoidMechanitorRegistry.CurrentMechanicalConsciousnessHost;

        public static bool IsJustice(Pawn? pawn) =>
            pawn != null && pawn.kindDef == JusticePawnKind;

        public static bool IsScenarioProtagonist(Pawn? pawn) =>
            GameComponent_MechanoidMechanitorRegistry.IsScenarioProtagonist(pawn);

        public static bool IsMechanicalConsciousnessHost(Pawn? pawn) =>
            GameComponent_MechanoidMechanitorRegistry.IsMechanicalConsciousnessHost(pawn);

        public static bool HasLivingScenarioProtagonist
        {
            get
            {
                if (!IsJusticeScenarioActive)
                {
                    return false;
                }

                Pawn? protagonist = ScenarioProtagonist;
                return protagonist != null
                    && !protagonist.Dead
                    && !protagonist.Destroyed
                    && protagonist.Faction == Faction.OfPlayer;
            }
        }

        public static bool HasLivingJustice => HasLivingScenarioProtagonist;

        public static bool ShouldPreventGameOver =>
            IsJusticeScenarioActive && HasLivingScenarioProtagonist;

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
