using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_JusticeBossController : CompProperties
    {
        public CompProperties_JusticeBossController()
        {
            compClass = typeof(CompJusticeBossController);
        }
    }

    public class CompJusticeBossController : ThingComp
    {
        public const int InfrastructureDelayTicks = 300;

        public const int FirstWaveDelayTicks = 1200;

        public const int WaveIntervalTicks = 1250;

        public const int TotalWaves = 11;

        public const int RetreatDelayAfterFinalWaveTicks = 2500;

        public const int ActivationStartDelayTicks = 90;

        public const int ActivationMaxWaitTicks = 600;

        private bool initialized;

        private IntVec3 anchorCell = IntVec3.Invalid;

        private int arrivalTick = -1;

        private bool infrastructureDeployed;

        private int waveCount;

        private int nextWaveTick = -1;

        private int retreatTick = -1;

        private bool retreatOrdered;

        private Lord? attackerLord;

        private Lord? guardLord;

        private int bossReplacementCount;

        private int justiceEventId;

        private List<Thing> deployedInfrastructure = new List<Thing>();

        private int activationStartTick = -1;

        private int activationDeadlineTick = -1;

        private bool activationFinished;

        private bool loggedActivationTimeout;

        private bool stopped;

        private Pawn Pawn => (Pawn)parent;

        public int WaveCount => waveCount;

        public int BossReplacementCount => bossReplacementCount;

        public bool InfrastructureDeployed => infrastructureDeployed;

        public bool RetreatOrdered => retreatOrdered;

        public IntVec3 AnchorCell => anchorCell;

        public int JusticeEventId => justiceEventId;

        public int DeployedInfrastructureCount => deployedInfrastructure.Count;

        public int PendingActivationCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < deployedInfrastructure.Count; i++)
                {
                    Thing? thing = deployedInfrastructure[i];
                    if (thing == null || thing.Destroyed)
                    {
                        continue;
                    }

                    if (!thing.Spawned)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad && !initialized && JusticePawnUtility.IsBossJustice(Pawn))
            {
                InitializeOnArrival();
            }
        }

        public void InitializeOnArrival()
        {
            if (initialized || Pawn.Map == null)
            {
                return;
            }

            initialized = true;
            stopped = false;
            justiceEventId = Pawn.thingIDNumber;
            anchorCell = Pawn.Position;
            arrivalTick = Find.TickManager.TicksGame;
            infrastructureDeployed = false;
            waveCount = 0;
            nextWaveTick = arrivalTick + FirstWaveDelayTicks;
            retreatTick = -1;
            retreatOrdered = false;
            attackerLord = null;
            guardLord = null;
            deployedInfrastructure = new List<Thing>();
            activationStartTick = -1;
            activationDeadlineTick = -1;
            activationFinished = false;
            loggedActivationTimeout = false;

            GameComponent_JusticeBossCallTracker.Current?.MarkActive();
            GameComponent_JusticeBossCallTracker.Current?.SetJusticePawn(Pawn);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (stopped || !initialized)
            {
                return;
            }

            if (Pawn.Dead || Pawn.Destroyed)
            {
                stopped = true;
                return;
            }

            if (Pawn.Map == null)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;

            if (!infrastructureDeployed && now >= arrivalTick + InfrastructureDelayTicks)
            {
                TryDeployInfrastructure();
            }

            if (!activationFinished)
            {
                TryActivateDeployedInfrastructure(now);
            }

            if (waveCount < TotalWaves && nextWaveTick > 0 && now >= nextWaveTick)
            {
                TrySpawnWave(forceBossReplace: false);
            }

            if (waveCount >= TotalWaves
                && retreatTick > 0
                && !retreatOrdered
                && now >= retreatTick)
            {
                TryOrderRetreat();
            }
        }

        public void DebugForceDeployInfrastructure()
        {
            if (!initialized)
            {
                InitializeOnArrival();
            }

            infrastructureDeployed = false;
            deployedInfrastructure.Clear();
            activationFinished = false;
            TryDeployInfrastructure();
        }

        public void DebugForceNextWave(bool forceBossReplace)
        {
            if (!initialized)
            {
                InitializeOnArrival();
            }

            if (waveCount >= TotalWaves)
            {
                return;
            }

            TrySpawnWave(forceBossReplace);
        }

        public void DebugForceRetreatWait()
        {
            if (!initialized)
            {
                InitializeOnArrival();
            }

            waveCount = TotalWaves;
            nextWaveTick = -1;
            retreatTick = Find.TickManager.TicksGame + RetreatDelayAfterFinalWaveTicks;
        }

        public void DebugForceRetreatNow()
        {
            if (!initialized)
            {
                InitializeOnArrival();
            }

            waveCount = TotalWaves;
            retreatTick = Find.TickManager.TicksGame;
            retreatOrdered = false;
            TryOrderRetreat();
        }

        private void TryDeployInfrastructure()
        {
            if (infrastructureDeployed || Pawn.Map == null)
            {
                return;
            }

            JusticeBossDeploymentUtility.DeployInfrastructure(
                Pawn,
                anchorCell,
                justiceEventId,
                out deployedInfrastructure,
                out guardLord);
            infrastructureDeployed = true;
            int now = Find.TickManager.TicksGame;
            activationStartTick = now + ActivationStartDelayTicks;
            activationDeadlineTick = activationStartTick + ActivationMaxWaitTicks;
            activationFinished = false;
            loggedActivationTimeout = false;
        }

        private void TryActivateDeployedInfrastructure(int now)
        {
            if (activationStartTick < 0 || now < activationStartTick)
            {
                return;
            }

            bool anyStillPending = false;
            for (int i = deployedInfrastructure.Count - 1; i >= 0; i--)
            {
                Thing? thing = deployedInfrastructure[i];
                if (thing == null || thing.Destroyed)
                {
                    deployedInfrastructure.RemoveAt(i);
                    continue;
                }

                if (thing.Spawned && thing.Map == Pawn.Map)
                {
                    JusticeBossDeploymentUtility.ActivateDeployedThing(thing);
                    continue;
                }

                if (thing.ParentHolder is IThingHolder)
                {
                    anyStillPending = true;
                    continue;
                }

                deployedInfrastructure.RemoveAt(i);
            }

            if (!anyStillPending)
            {
                activationFinished = true;
                return;
            }

            if (now >= activationDeadlineTick)
            {
                if (!loggedActivationTimeout)
                {
                    loggedActivationTimeout = true;
                    Log.Warning(
                        "[MAP JusticeBoss] Timed out waiting for some infrastructure pods to open.");
                }

                activationFinished = true;
            }
        }

        private void TrySpawnWave(bool forceBossReplace)
        {
            if (Pawn.Map == null || waveCount >= TotalWaves)
            {
                return;
            }

            int waveIndex = waveCount + 1;
            List<PawnKindDef> composition = forceBossReplace
                ? JusticeBossSpawnUtility.BuildWaveComposition(waveIndex, applyBossReplace: false)
                : JusticeBossSpawnUtility.BuildWaveComposition(waveIndex, applyBossReplace: true);
            if (forceBossReplace)
            {
                JusticeBossSpawnUtility.TryApplyBossReplacement(composition, force: true);
            }

            if (composition.Count == 0)
            {
                waveCount++;
                ScheduleAfterWave();
                return;
            }

            int replacements = JusticeBossSpawnUtility.LaunchWaveDropPodsNear(
                Pawn,
                anchorCell,
                justiceEventId,
                composition,
                out Lord? assaultLord);
            if (assaultLord != null)
            {
                attackerLord = assaultLord;
            }

            bossReplacementCount += replacements;
            waveCount++;
            ScheduleAfterWave();
        }

        private void ScheduleAfterWave()
        {
            if (waveCount >= TotalWaves)
            {
                nextWaveTick = -1;
                retreatTick = Find.TickManager.TicksGame + RetreatDelayAfterFinalWaveTicks;
            }
            else
            {
                nextWaveTick = Find.TickManager.TicksGame + WaveIntervalTicks;
            }
        }

        private void TryOrderRetreat()
        {
            if (retreatOrdered || Pawn.Dead)
            {
                return;
            }

            Lord? lord = Pawn.GetLord();
            if (lord == null)
            {
                return;
            }

            lord.ReceiveMemo(JusticeBossCallUtility.RetreatMemo);
            retreatOrdered = true;
            GameComponent_JusticeBossCallTracker.Current?.MarkRetreating();
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            stopped = true;
        }

        public override void Notify_Killed(Map prevMap, DamageInfo? dinfo = null)
        {
            base.Notify_Killed(prevMap, dinfo);
            stopped = true;
        }

        public string GetDebugStatus()
        {
            int pendingDrops = 0;
            int landed = 0;
            if (Pawn.Map != null)
            {
                MapComponent_JusticeBossDropTracker dropTracker =
                    MapComponent_JusticeBossDropTracker.For(Pawn.Map);
                pendingDrops = dropTracker.PendingCount;
                landed = dropTracker.LandedAndAssignedCount;
            }

            return $"eventId={justiceEventId} init={initialized} waves={waveCount}/{TotalWaves} "
                + $"infra={infrastructureDeployed} pendingInfra={PendingActivationCount} "
                + $"pendingDrops={pendingDrops} landedAssigned={landed} "
                + $"nextWave={nextWaveTick} retreat={retreatTick} ordered={retreatOrdered} "
                + $"bossRepl={bossReplacementCount} stopped={stopped} anchor={anchorCell}";
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref initialized, "justiceBossInitialized", false);
            Scribe_Values.Look(ref anchorCell, "justiceBossAnchorCell");
            Scribe_Values.Look(ref arrivalTick, "justiceBossArrivalTick", -1);
            Scribe_Values.Look(ref infrastructureDeployed, "justiceBossInfraDeployed", false);
            Scribe_Values.Look(ref waveCount, "justiceBossWaveCount", 0);
            Scribe_Values.Look(ref nextWaveTick, "justiceBossNextWaveTick", -1);
            Scribe_Values.Look(ref retreatTick, "justiceBossRetreatTick", -1);
            Scribe_Values.Look(ref retreatOrdered, "justiceBossRetreatOrdered", false);
            Scribe_References.Look(ref attackerLord, "justiceBossAttackerLord");
            Scribe_References.Look(ref guardLord, "justiceBossGuardLord");
            Scribe_Values.Look(ref bossReplacementCount, "justiceBossReplacementCount", 0);
            Scribe_Values.Look(ref justiceEventId, "justiceBossEventId", 0);
            Scribe_Collections.Look(
                ref deployedInfrastructure,
                "justiceBossDeployedInfrastructure",
                LookMode.Reference);
            Scribe_Values.Look(ref activationStartTick, "justiceBossActivationStartTick", -1);
            Scribe_Values.Look(ref activationDeadlineTick, "justiceBossActivationDeadlineTick", -1);
            Scribe_Values.Look(ref activationFinished, "justiceBossActivationFinished", false);
            Scribe_Values.Look(ref loggedActivationTimeout, "justiceBossLoggedActivationTimeout", false);
            Scribe_Values.Look(ref stopped, "justiceBossStopped", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                deployedInfrastructure ??= new List<Thing>();
                deployedInfrastructure.RemoveAll(t => t == null);
                if (justiceEventId == 0 && initialized && Pawn != null)
                {
                    justiceEventId = Pawn.thingIDNumber;
                }
            }
        }
    }
}