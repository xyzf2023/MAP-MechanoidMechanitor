using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class GameComponent_MechanoidMechanitorScenarioState : GameComponent
    {
        private bool mechanoidMechanitorScenarioEnabled;

        private List<Pawn> portraitDisplayEnabledPawnsList = new List<Pawn>();
        private HashSet<Pawn> portraitDisplayEnabledPawns = new HashSet<Pawn>();

        private bool factionNamingScenarioChecked;
        private bool factionNamingRoutineEnabled;
        private bool factionNamingRoutineFinished;
        private bool waitingForFactionNameCompletion;
        private int nextFactionNamingCheckTick = GenDate.TicksPerDay;

        public bool MechanoidMechanitorScenarioEnabled => mechanoidMechanitorScenarioEnabled;

        public static bool IsEnabled
        {
            get
            {
                if (Current.Game == null)
                {
                    return false;
                }

                GameComponent_MechanoidMechanitorScenarioState? component =
                    Current.Game.GetComponent<GameComponent_MechanoidMechanitorScenarioState>();
                return component?.mechanoidMechanitorScenarioEnabled == true;
            }
        }

        private static GameComponent_MechanoidMechanitorScenarioState? CurrentComponent
        {
            get
            {
                if (Current.Game == null)
                {
                    return null;
                }

                return Current.Game.GetComponent<GameComponent_MechanoidMechanitorScenarioState>();
            }
        }

        public GameComponent_MechanoidMechanitorScenarioState(Game game)
        {
        }

        public static bool IsPortraitDisplayEnabled(Pawn? pawn)
        {
            GameComponent_MechanoidMechanitorScenarioState? component = CurrentComponent;
            if (component == null || pawn == null)
            {
                return false;
            }

            return component.portraitDisplayEnabledPawns.Contains(pawn);
        }

        public static void SetPortraitDisplayEnabled(Pawn? pawn, bool enabled)
        {
            GameComponent_MechanoidMechanitorScenarioState? component = CurrentComponent;
            if (component == null || pawn == null)
            {
                return;
            }

            bool changed;
            if (enabled)
            {
                if (!MechanoidMechanitorScenarioFreeColonistUtility.CanUsePortraitDisplayToggle(pawn))
                {
                    return;
                }

                changed = component.portraitDisplayEnabledPawns.Add(pawn);
                if (changed && !component.portraitDisplayEnabledPawnsList.Contains(pawn))
                {
                    component.portraitDisplayEnabledPawnsList.Add(pawn);
                }
            }
            else
            {
                changed = component.portraitDisplayEnabledPawns.Remove(pawn);
                component.portraitDisplayEnabledPawnsList.Remove(pawn);
            }

            if (changed)
            {
                MechanoidMechanitorScenarioFreeColonistUtility.NotifyColonistDisplaysDirtyIfReady();
            }
        }

        internal static IReadOnlyList<Pawn> PortraitDisplayEnabledPawnsList
        {
            get
            {
                GameComponent_MechanoidMechanitorScenarioState? component = CurrentComponent;
                return component?.portraitDisplayEnabledPawnsList
                    ?? (IReadOnlyList<Pawn>)System.Array.Empty<Pawn>();
            }
        }

        public static void EnableForCurrentGame()
        {
            if (Current.Game == null)
            {
                return;
            }

            GameComponent_MechanoidMechanitorScenarioState? component =
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorScenarioState>();
            if (component == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 无法启用机械族机械师剧本状态：缺少 GameComponent_MechanoidMechanitorScenarioState 组件。");
                return;
            }

            component.mechanoidMechanitorScenarioEnabled = true;
        }

        public static void SyncFromScenarioMarker()
        {
            if (Current.Game == null)
            {
                return;
            }

            GameComponent_MechanoidMechanitorScenarioState? component =
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorScenarioState>();
            if (component != null)
            {
                component.mechanoidMechanitorScenarioEnabled =
                    MechanoidMechanitorScenarioUtility.IsScenarioActive;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref mechanoidMechanitorScenarioEnabled,
                "mechanoidMechanitorScenarioEnabled",
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
            Scribe_Collections.Look(
                ref portraitDisplayEnabledPawnsList,
                "portraitDisplayEnabledPawns",
                LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                portraitDisplayEnabledPawnsList ??= new List<Pawn>();
                RebuildPortraitDisplayCache();
            }
        }

        private void RebuildPortraitDisplayCache()
        {
            portraitDisplayEnabledPawns = new HashSet<Pawn>();
            for (int i = portraitDisplayEnabledPawnsList.Count - 1; i >= 0; i--)
            {
                Pawn? pawn = portraitDisplayEnabledPawnsList[i];
                if (pawn == null || pawn.Destroyed)
                {
                    portraitDisplayEnabledPawnsList.RemoveAt(i);
                    continue;
                }

                portraitDisplayEnabledPawns.Add(pawn);
            }
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
                factionNamingRoutineEnabled = MechanoidMechanitorScenarioUtility.IsScenarioActive;
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
                    && Find.WindowStack.IsOpen<Dialog_NameMechanoidMechanitorFaction>())
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
                if (!GameComponent_MechanoidMechanitorRegistry.IsMechanicalConsciousnessHost(pawn))
                {
                    continue;
                }

                if (!MechanoidMechanitorScenarioFreeColonistUtility.IsEligibleOnMap(
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

            Find.WindowStack.Add(
                new Dialog_NameMechanoidMechanitorFaction());
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
