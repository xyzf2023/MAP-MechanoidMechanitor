using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class GameComponent_JusticeScenarioState : GameComponent
    {
        private bool justiceOnlyColonyEnabled;

        private bool factionNamingScenarioChecked;
        private bool factionNamingRoutineEnabled;
        private bool factionNamingRoutineFinished;
        private bool waitingForFactionNameCompletion;
        private int nextFactionNamingCheckTick = GenDate.TicksPerDay;

        public bool JusticeOnlyColonyEnabled => justiceOnlyColonyEnabled;

        public static bool IsEnabled
        {
            get
            {
                if (Current.Game == null)
                {
                    return false;
                }

                GameComponent_JusticeScenarioState? component =
                    Current.Game.GetComponent<GameComponent_JusticeScenarioState>();
                return component?.justiceOnlyColonyEnabled == true;
            }
        }

        public GameComponent_JusticeScenarioState(Game game)
        {
        }

        public static void EnableForCurrentGame()
        {
            if (Current.Game == null)
            {
                return;
            }

            GameComponent_JusticeScenarioState? component =
                Current.Game.GetComponent<GameComponent_JusticeScenarioState>();
            if (component == null)
            {
                Log.Error(
                    "[MechanoidMechanitor] Cannot enable mechanitor-only colony state: " +
                    "GameComponent_JusticeScenarioState is missing.");
                return;
            }

            component.justiceOnlyColonyEnabled = true;
        }

        public static void SyncFromScenarioMarker()
        {
            if (Current.Game == null)
            {
                return;
            }

            GameComponent_JusticeScenarioState? component =
                Current.Game.GetComponent<GameComponent_JusticeScenarioState>();
            if (component != null)
            {
                component.justiceOnlyColonyEnabled =
                    JusticeScenarioUtility.IsJusticeScenarioActive;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref justiceOnlyColonyEnabled,
                "justiceOnlyColonyEnabled",
                false);
            Scribe_Values.Look(
                ref factionNamingScenarioChecked,
                "factionNamingScenarioChecked",
                false);
            Scribe_Values.Look(
                ref factionNamingRoutineEnabled,
                "factionNamingRoutineEnabled",
                false);
            Scribe_Values.Look(
                ref factionNamingRoutineFinished,
                "factionNamingRoutineFinished",
                false);
            Scribe_Values.Look(
                ref waitingForFactionNameCompletion,
                "waitingForFactionNameCompletion",
                false);
            Scribe_Values.Look(
                ref nextFactionNamingCheckTick,
                "nextFactionNamingCheckTick",
                GenDate.TicksPerDay);
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            SyncFromScenarioMarker();
            factionNamingScenarioChecked = false;
            factionNamingRoutineEnabled = false;
            factionNamingRoutineFinished = false;
            waitingForFactionNameCompletion = false;
            nextFactionNamingCheckTick = GenDate.TicksPerDay;
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            SyncFromScenarioMarker();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            if (factionNamingRoutineFinished
                || Current.Game == null
                || Find.TickManager == null
                || Find.TickManager.TicksGame < nextFactionNamingCheckTick)
            {
                return;
            }

            if (!factionNamingScenarioChecked)
            {
                if (Find.TickManager.TicksGame < GenDate.TicksPerDay)
                {
                    return;
                }

                factionNamingScenarioChecked = true;
                factionNamingRoutineEnabled = JusticeScenarioUtility.IsJusticeScenarioActive;
                if (!factionNamingRoutineEnabled)
                {
                    FinishFactionNamingRoutine();
                    return;
                }

                nextFactionNamingCheckTick = Find.TickManager.TicksGame;
            }

            if (!factionNamingRoutineEnabled)
            {
                FinishFactionNamingRoutine();
                return;
            }

            Faction? playerFaction = Faction.OfPlayerSilentFail;
            if (playerFaction == null)
            {
                RetryFactionNamingLater();
                return;
            }

            if (playerFaction.HasName)
            {
                FinishFactionNamingRoutine();
                return;
            }

            if (waitingForFactionNameCompletion)
            {
                if (Find.WindowStack != null
                    && Find.WindowStack.IsOpen<Dialog_NamePlayerFaction>())
                {
                    return;
                }

                waitingForFactionNameCompletion = false;
                RetryFactionNamingLater();
                return;
            }

            if (Current.ProgramState != ProgramState.Playing
                || Find.WindowStack == null
                || Find.GameEnder == null
                || Find.GameEnder.gameEnding
                || LongEventHandler.AnyEventNowOrWaiting
                || Find.CurrentMap == null
                || !Find.CurrentMap.IsPlayerHome
                || Find.AnyPlayerHomeMap == null)
            {
                RetryFactionNamingLater();
                return;
            }

            if (Find.WindowStack.IsOpen<Dialog_GiveName>()
                || Find.WindowStack.NonImmediateDialogWindowOpen
                || Find.TickManager.TicksGame % 1000 == 200)
            {
                RetryFactionNamingLater();
                return;
            }

            Map namingHomeMap = Find.AnyPlayerHomeMap;
            List<Pawn> freeColonistsSpawned = namingHomeMap.mapPawns.FreeColonistsSpawned;
            bool foundEligibleHost = false;

            for (int i = 0; i < freeColonistsSpawned.Count; i++)
            {
                Pawn pawn = freeColonistsSpawned[i];
                if (!JusticeScenarioUtility.IsMechanicalConsciousnessHost(pawn))
                {
                    continue;
                }

                if (!JusticeScenarioFreeColonistUtility.IsEligibleOnMap(
                        pawn,
                        namingHomeMap.mapPawns,
                        requireSpawned: true))
                {
                    continue;
                }

                if (!pawn.Spawned
                    || pawn.Map != namingHomeMap
                    || pawn.Dead
                    || pawn.Faction != Faction.OfPlayer)
                {
                    continue;
                }

                foundEligibleHost = true;
                break;
            }

            if (!foundEligibleHost)
            {
                RetryFactionNamingLater();
                return;
            }

            Find.WindowStack.Add(new Dialog_NamePlayerFaction());
            waitingForFactionNameCompletion = true;
        }

        private void FinishFactionNamingRoutine()
        {
            factionNamingRoutineFinished = true;
            factionNamingRoutineEnabled = false;
            waitingForFactionNameCompletion = false;
            nextFactionNamingCheckTick = -1;
        }

        private void RetryFactionNamingLater()
        {
            if (Find.TickManager != null)
            {
                nextFactionNamingCheckTick =
                    Find.TickManager.TicksGame + GenDate.TicksPerHour;
            }
        }
    }
}
