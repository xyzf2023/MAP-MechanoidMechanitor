using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class GameComponent_MechanoidMechanitorScenarioState : GameComponent
    {
        private const int EnergyManagementLetterDelayTicks = 2500;
        private const int BandwidthAcquisitionLetterCheckIntervalTicks = 60000;
        private const int BandwidthAcquisitionLetterThreshold = 12;

        private bool mechanoidMechanitorScenarioEnabled;

        private List<Pawn> portraitDisplayEnabledPawnsList = new List<Pawn>();
        private HashSet<Pawn> portraitDisplayEnabledPawns = new HashSet<Pawn>();

        // 默认视为已处理，确保本功能加入前创建的旧存档不会在读档后补发开局提示。
        private bool energyManagementLetterResolved = true;
        private int energyManagementLetterTriggerTick = -1;
        private bool bandwidthAcquisitionLetterSent;

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
                ref energyManagementLetterResolved,
                "energyManagementLetterResolved",
                true);
            Scribe_Values.Look(
                ref energyManagementLetterTriggerTick,
                "energyManagementLetterTriggerTick",
                -1);
            Scribe_Values.Look(
                ref bandwidthAcquisitionLetterSent,
                "bandwidthAcquisitionLetterSent",
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
            bandwidthAcquisitionLetterSent = false;

            if (mechanoidMechanitorScenarioEnabled && Find.TickManager != null)
            {
                energyManagementLetterResolved = false;
                energyManagementLetterTriggerTick =
                    Find.TickManager.TicksGame + EnergyManagementLetterDelayTicks;
            }
            else
            {
                energyManagementLetterResolved = true;
                energyManagementLetterTriggerTick = -1;
            }

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

            TryResolveEnergyManagementLetter();
            TrySendBandwidthAcquisitionLetter();

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

                Faction? newMechHivePlayerFaction = Faction.OfPlayerSilentFail;
                if (newMechHivePlayerFaction == null
                    || newMechHivePlayerFaction.def != NewMechHiveFactionDefOf.MAP_NewMechHive)
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
                if (IsNewMechHiveNamingWindowOpen())
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

            if (!IsNamingHomeMapSafe(namingHomeMap))
            {
                RetryFactionNamingLater();
                return;
            }

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

            Settlement? namingSettlement = TryGetEligibleNamingSettlement(namingHomeMap);
            if (namingSettlement != null)
            {
                Find.WindowStack.Add(
                    new Dialog_NameNewMechHiveFactionAndSettlement(namingSettlement));
            }
            else
            {
                Find.WindowStack.Add(
                    new Dialog_NameNewMechHiveFaction());
            }

            waitingForFactionNameCompletion = true;
        }

        private void TryResolveEnergyManagementLetter()
        {
            if (energyManagementLetterResolved
                || energyManagementLetterTriggerTick < 0
                || Find.TickManager == null
                || Find.TickManager.TicksGame < energyManagementLetterTriggerTick)
            {
                return;
            }

            // 到达预约时间后只检查一次。无论是否存在机械族机械师，本局都不再重复检查。
            energyManagementLetterResolved = true;
            energyManagementLetterTriggerTick = -1;

            if (!mechanoidMechanitorScenarioEnabled
                || Find.LetterStack == null
                || GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors.Count == 0)
            {
                return;
            }

            Find.LetterStack.ReceiveLetter(
                "MAP_MechanoidMechanitor.Scenario.EnergyManagementLetter.Label".Translate(),
                "MAP_MechanoidMechanitor.Scenario.EnergyManagementLetter.Text".Translate(),
                LetterDefOf.NeutralEvent);
        }

        private void TrySendBandwidthAcquisitionLetter()
        {
            if (bandwidthAcquisitionLetterSent
                || !mechanoidMechanitorScenarioEnabled
                || Find.TickManager == null
                || Find.TickManager.TicksGame <= 0
                || Find.TickManager.TicksGame % BandwidthAcquisitionLetterCheckIntervalTicks != 0
                || Find.LetterStack == null)
            {
                return;
            }

            Faction? playerFaction = Faction.OfPlayerSilentFail;
            if (playerFaction == null)
            {
                return;
            }

            IReadOnlyList<Pawn> mechanitors =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;
            for (int i = 0; i < mechanitors.Count; i++)
            {
                Pawn pawn = mechanitors[i];
                if (pawn.Faction != playerFaction
                    || pawn.mechanitor is not Pawn_MechanitorTracker tracker
                    || tracker.UsedBandwidth < BandwidthAcquisitionLetterThreshold)
                {
                    continue;
                }

                Find.LetterStack.ReceiveLetter(
                    "MAP_MechanoidMechanitor.Scenario.BandwidthAcquisitionLetter.Label".Translate(),
                    "MAP_MechanoidMechanitor.Scenario.BandwidthAcquisitionLetter.Text".Translate(),
                    LetterDefOf.NeutralEvent);
                bandwidthAcquisitionLetterSent = true;
                return;
            }
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

        private bool IsNewMechHiveNamingWindowOpen()
        {
            if (Find.WindowStack == null)
            {
                return false;
            }

            return Find.WindowStack.IsOpen<Dialog_NameNewMechHiveFaction>()
                || Find.WindowStack.IsOpen<Dialog_NameNewMechHiveFactionAndSettlement>();
        }

        private static bool IsNamingHomeMapSafe(Map homeMap)
        {
            if (homeMap.dangerWatcher != null
                && homeMap.dangerWatcher.DangerRating == StoryDanger.High)
            {
                return false;
            }

            if (homeMap.attackTargetsCache.TargetsHostileToColony.Any(
                    target => GenHostility.IsActiveThreatToPlayer(target)))
            {
                return false;
            }

            return true;
        }

        private Settlement? TryGetEligibleNamingSettlement(Map homeMap)
        {
            if (homeMap == null)
            {
                return null;
            }

            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (settlement == null
                    || settlement.Faction != Faction.OfPlayer
                    || settlement.namedByPlayer
                    || !settlement.HasMap
                    || settlement.Map != homeMap)
                {
                    continue;
                }

                return settlement;
            }

            return null;
        }
    }
}
