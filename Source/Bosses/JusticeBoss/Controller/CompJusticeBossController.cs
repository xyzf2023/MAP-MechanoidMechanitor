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

        public const int RetreatDelayAfterFinalWaveTicks = 2500;

        public const int ActivationStartDelayTicks = 90;

        public const int ActivationMaxWaitTicks = 600;

        public const int DropRetryDelayTicks = 250;

        public const int MaxDropRetryAttempts = 20;

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

        private List<PawnKindDef> pendingWaveKinds = new List<PawnKindDef>();

        private int pendingWaveBossReplacementCount;

        private int waveDropRetryCount;

        private bool loggedWaveDropFailure;

        private List<PawnKindDef> pendingGuardKinds = new List<PawnKindDef>();

        private int nextGuardRetryTick = -1;

        private int guardDropRetryCount;

        private bool loggedGuardDropFailure;

        private bool stopped;

        private bool bossDifficultyCaptured;

        private bool difficultyEnableMortarShield =
            JusticeBossDifficultyValues.DefaultEnableMortarShield;

        private bool difficultyEnableBulletShield =
            JusticeBossDifficultyValues.DefaultEnableBulletShield;

        private int difficultyAutoMortarCount =
            JusticeBossDifficultyValues.DefaultAutoMortarCount;

        private int difficultyAutoChargeBlasterCount =
            JusticeBossDifficultyValues.DefaultAutoChargeBlasterCount;

        private int difficultyAutoInfernoCount =
            JusticeBossDifficultyValues.DefaultAutoInfernoCount;

        private int difficultyTotalWaves =
            JusticeBossDifficultyValues.DefaultTotalWaves;

        private int difficultyWaveIntervalTicks =
            JusticeBossDifficultyValues.DefaultWaveIntervalTicks;

        private int difficultyMechsPerWave =
            JusticeBossDifficultyValues.DefaultMechsPerWave;

        private bool difficultyAllowBossReplacement =
            JusticeBossDifficultyValues.DefaultAllowBossReplacement;

        private Pawn Pawn => (Pawn)parent;

        public int WaveCount => waveCount;

        public int BossReplacementCount => bossReplacementCount;

        public bool InfrastructureDeployed => infrastructureDeployed;

        public bool RetreatOrdered => retreatOrdered;

        public IntVec3 AnchorCell => anchorCell;

        public int JusticeEventId => justiceEventId;

        public int DeployedInfrastructureCount => deployedInfrastructure.Count;

        public int PendingWaveDropCount => pendingWaveKinds.Count;

        public int PendingGuardDropCount => pendingGuardKinds.Count;

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

            CaptureDifficultySnapshot();

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
            pendingWaveKinds = new List<PawnKindDef>();
            pendingWaveBossReplacementCount = 0;
            waveDropRetryCount = 0;
            loggedWaveDropFailure = false;
            pendingGuardKinds = new List<PawnKindDef>();
            nextGuardRetryTick = -1;
            guardDropRetryCount = 0;
            loggedGuardDropFailure = false;

            GameComponent_JusticeBossCallTracker.Current?.MarkActive();
            GameComponent_JusticeBossCallTracker.Current?.SetJusticePawn(Pawn);
        }

        private void CaptureDifficultySnapshot()
        {
            MAPMechanitorModSettings? settings =
                MAPMechanitorMod.Settings;

            if (settings == null)
            {
                ApplyDefaultDifficultySnapshot();
                return;
            }

            difficultyEnableMortarShield =
                settings.justiceBossEnableMortarShield;

            difficultyEnableBulletShield =
                settings.justiceBossEnableBulletShield;

            difficultyAutoMortarCount =
                JusticeBossDifficultyValues.ClampTurretCount(
                    settings.justiceBossAutoMortarCount);

            difficultyAutoChargeBlasterCount =
                JusticeBossDifficultyValues.ClampTurretCount(
                    settings.justiceBossAutoChargeBlasterCount);

            difficultyAutoInfernoCount =
                JusticeBossDifficultyValues.ClampTurretCount(
                    settings.justiceBossAutoInfernoCount);

            difficultyTotalWaves =
                JusticeBossDifficultyValues.ClampTotalWaves(
                    settings.justiceBossTotalWaves);

            difficultyWaveIntervalTicks =
                JusticeBossDifficultyValues.ClampWaveIntervalTicks(
                    settings.justiceBossWaveIntervalTicks);

            difficultyMechsPerWave =
                JusticeBossDifficultyValues.ClampMechsPerWave(
                    settings.justiceBossMechsPerWave);

            difficultyAllowBossReplacement =
                settings.justiceBossAllowBossReplacement;

            bossDifficultyCaptured = true;
        }

        private void ApplyDefaultDifficultySnapshot()
        {
            difficultyEnableMortarShield =
                JusticeBossDifficultyValues.DefaultEnableMortarShield;

            difficultyEnableBulletShield =
                JusticeBossDifficultyValues.DefaultEnableBulletShield;

            difficultyAutoMortarCount =
                JusticeBossDifficultyValues.DefaultAutoMortarCount;

            difficultyAutoChargeBlasterCount =
                JusticeBossDifficultyValues.DefaultAutoChargeBlasterCount;

            difficultyAutoInfernoCount =
                JusticeBossDifficultyValues.DefaultAutoInfernoCount;

            difficultyTotalWaves =
                JusticeBossDifficultyValues.DefaultTotalWaves;

            difficultyWaveIntervalTicks =
                JusticeBossDifficultyValues.DefaultWaveIntervalTicks;

            difficultyMechsPerWave =
                JusticeBossDifficultyValues.DefaultMechsPerWave;

            difficultyAllowBossReplacement =
                JusticeBossDifficultyValues.DefaultAllowBossReplacement;

            bossDifficultyCaptured = true;
        }

        private void ClampDifficultySnapshot()
        {
            difficultyAutoMortarCount =
                JusticeBossDifficultyValues.ClampTurretCount(
                    difficultyAutoMortarCount);

            difficultyAutoChargeBlasterCount =
                JusticeBossDifficultyValues.ClampTurretCount(
                    difficultyAutoChargeBlasterCount);

            difficultyAutoInfernoCount =
                JusticeBossDifficultyValues.ClampTurretCount(
                    difficultyAutoInfernoCount);

            difficultyTotalWaves =
                JusticeBossDifficultyValues.ClampTotalWaves(
                    difficultyTotalWaves);

            difficultyWaveIntervalTicks =
                JusticeBossDifficultyValues.ClampWaveIntervalTicks(
                    difficultyWaveIntervalTicks);

            difficultyMechsPerWave =
                JusticeBossDifficultyValues.ClampMechsPerWave(
                    difficultyMechsPerWave);
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

            if (pendingGuardKinds.Count > 0
                && nextGuardRetryTick > 0
                && now >= nextGuardRetryTick)
            {
                TryRetryGuardDrops(now);
            }

            if (waveCount < difficultyTotalWaves
                && nextWaveTick > 0
                && now >= nextWaveTick)
            {
                TrySpawnWave(forceBossReplace: false);
            }

            if (waveCount >= difficultyTotalWaves
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
            pendingGuardKinds.Clear();
            nextGuardRetryTick = -1;
            guardDropRetryCount = 0;
            loggedGuardDropFailure = false;
            TryDeployInfrastructure();
        }

        public void DebugForceNextWave(bool forceBossReplace)
        {
            if (!initialized)
            {
                InitializeOnArrival();
            }

            if (waveCount >= difficultyTotalWaves)
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

            waveCount = difficultyTotalWaves;
            nextWaveTick = -1;
            retreatTick = Find.TickManager.TicksGame + RetreatDelayAfterFinalWaveTicks;
        }

        public void DebugForceRetreatNow()
        {
            if (!initialized)
            {
                InitializeOnArrival();
            }

            waveCount = difficultyTotalWaves;
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
                difficultyAutoMortarCount,
                difficultyAutoChargeBlasterCount,
                difficultyAutoInfernoCount,
                difficultyEnableMortarShield,
                difficultyEnableBulletShield,
                out deployedInfrastructure,
                out pendingGuardKinds,
                out guardLord);
            infrastructureDeployed = true;
            int now = Find.TickManager.TicksGame;
            activationStartTick = now + ActivationStartDelayTicks;
            activationDeadlineTick = activationStartTick + ActivationMaxWaitTicks;
            activationFinished = false;
            loggedActivationTimeout = false;
            nextGuardRetryTick = pendingGuardKinds.Count > 0
                ? now + DropRetryDelayTicks
                : -1;
            guardDropRetryCount = 0;
            loggedGuardDropFailure = false;
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

        private void TryRetryGuardDrops(int now)
        {
            Map? map = Pawn.Map;
            Faction? faction = Pawn.Faction;
            if (map == null || faction == null || pendingGuardKinds.Count == 0)
            {
                nextGuardRetryTick = -1;
                return;
            }

            JusticeBossDropLaunchResult result =
                JusticeBossSpawnUtility.LaunchGuardDropPodsNear(
                    map,
                    faction,
                    anchorCell,
                    justiceEventId,
                    pendingGuardKinds,
                    out Lord? retryGuardLord);
            if (retryGuardLord != null)
            {
                guardLord = retryGuardLord;
            }

            pendingGuardKinds = new List<PawnKindDef>(result.FailedKinds);
            if (result.FatalFailure)
            {
                if (!loggedGuardDropFailure)
                {
                    loggedGuardDropFailure = true;
                    Log.Error(
                        "[MAP JusticeBoss] Guard drop retries stopped because of a fatal launch failure.");
                }

                nextGuardRetryTick = -1;
                return;
            }

            if (pendingGuardKinds.Count == 0)
            {
                nextGuardRetryTick = -1;
                guardDropRetryCount = 0;
                loggedGuardDropFailure = false;
                return;
            }

            guardDropRetryCount++;
            if (guardDropRetryCount >= MaxDropRetryAttempts)
            {
                if (!loggedGuardDropFailure)
                {
                    loggedGuardDropFailure = true;
                    Log.Error(
                        "[MAP JusticeBoss] Guard drop retries exhausted; missing guards were not launched.");
                }

                nextGuardRetryTick = -1;
                return;
            }

            nextGuardRetryTick = now + DropRetryDelayTicks;
        }

        private void TrySpawnWave(bool forceBossReplace)
        {
            if (Pawn.Map == null || waveCount >= difficultyTotalWaves)
            {
                return;
            }

            if (pendingWaveKinds.Count == 0)
            {
                int waveIndex = waveCount + 1;

                bool applyBossReplace =
                    !forceBossReplace
                    && difficultyAllowBossReplacement;

                List<PawnKindDef> composition =
                    JusticeBossSpawnUtility.BuildWaveComposition(
                        waveIndex,
                        difficultyMechsPerWave,
                        applyBossReplace);

                if (forceBossReplace)
                {
                    JusticeBossSpawnUtility.TryApplyBossReplacement(
                        composition,
                        force: true);
                }

                if (composition.Count == 0)
                {
                    Log.ErrorOnce(
                        "[MAP JusticeBoss] Cannot build the next summoned mechanoid wave.",
                        Pawn.thingIDNumber ^ waveIndex ^ 0x681A);
                    nextWaveTick = -1;
                    return;
                }

                pendingWaveKinds = composition;
                pendingWaveBossReplacementCount = 0;
                waveDropRetryCount = 0;
                loggedWaveDropFailure = false;
            }

            JusticeBossDropLaunchResult result =
                JusticeBossSpawnUtility.LaunchWaveDropPodsNear(
                    Pawn,
                    anchorCell,
                    justiceEventId,
                    pendingWaveKinds,
                    out Lord? assaultLord);
            if (assaultLord != null)
            {
                attackerLord = assaultLord;
            }

            pendingWaveBossReplacementCount += result.BossReplacementCount;
            pendingWaveKinds = new List<PawnKindDef>(result.FailedKinds);

            if (result.FatalFailure)
            {
                if (!loggedWaveDropFailure)
                {
                    loggedWaveDropFailure = true;
                    Log.Error(
                        "[MAP JusticeBoss] Wave drop retries stopped because of a fatal launch failure.");
                }

                nextWaveTick = -1;
                return;
            }

            if (pendingWaveKinds.Count == 0)
            {
                bossReplacementCount += pendingWaveBossReplacementCount;
                pendingWaveBossReplacementCount = 0;
                waveDropRetryCount = 0;
                loggedWaveDropFailure = false;
                waveCount++;
                ScheduleAfterWave();
                return;
            }

            waveDropRetryCount++;
            if (waveDropRetryCount >= MaxDropRetryAttempts)
            {
                if (!loggedWaveDropFailure)
                {
                    loggedWaveDropFailure = true;
                    Log.Error(
                        "[MAP JusticeBoss] Wave drop retries exhausted; the wave remains incomplete.");
                }

                nextWaveTick = -1;
                return;
            }

            nextWaveTick = Find.TickManager.TicksGame + DropRetryDelayTicks;
        }

        private void ScheduleAfterWave()
        {
            if (waveCount >= difficultyTotalWaves)
            {
                nextWaveTick = -1;
                retreatTick = Find.TickManager.TicksGame + RetreatDelayAfterFinalWaveTicks;
            }
            else
            {
                nextWaveTick =
                    Find.TickManager.TicksGame
                    + difficultyWaveIntervalTicks;
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

            return $"eventId={justiceEventId} init={initialized} waves={waveCount}/{difficultyTotalWaves} "
                + $"infra={infrastructureDeployed} pendingInfra={PendingActivationCount} "
                + $"pendingWave={pendingWaveKinds.Count} waveRetries={waveDropRetryCount} "
                + $"pendingGuards={pendingGuardKinds.Count} guardRetries={guardDropRetryCount} "
                + $"pendingDrops={pendingDrops} landedAssigned={landed} "
                + $"nextWave={nextWaveTick} retreat={retreatTick} ordered={retreatOrdered} "
                + $"bossRepl={bossReplacementCount} stopped={stopped} anchor={anchorCell}"
                + $" difficultyCaptured={bossDifficultyCaptured}"
                + $" difficulty=["
                + $"mortar={difficultyAutoMortarCount} "
                + $"charge={difficultyAutoChargeBlasterCount} "
                + $"inferno={difficultyAutoInfernoCount} "
                + $"mortarShield={difficultyEnableMortarShield} "
                + $"bulletShield={difficultyEnableBulletShield} "
                + $"interval={difficultyWaveIntervalTicks} "
                + $"mechsPerWave={difficultyMechsPerWave} "
                + $"allowBoss={difficultyAllowBossReplacement}]";
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
            Scribe_Collections.Look(
                ref pendingWaveKinds,
                "justiceBossPendingWaveKinds",
                LookMode.Def);
            Scribe_Values.Look(
                ref pendingWaveBossReplacementCount,
                "justiceBossPendingWaveBossReplacementCount",
                0);
            Scribe_Values.Look(ref waveDropRetryCount, "justiceBossWaveDropRetryCount", 0);
            Scribe_Values.Look(ref loggedWaveDropFailure, "justiceBossLoggedWaveDropFailure", false);
            Scribe_Collections.Look(
                ref pendingGuardKinds,
                "justiceBossPendingGuardKinds",
                LookMode.Def);
            Scribe_Values.Look(ref nextGuardRetryTick, "justiceBossNextGuardRetryTick", -1);
            Scribe_Values.Look(ref guardDropRetryCount, "justiceBossGuardDropRetryCount", 0);
            Scribe_Values.Look(ref loggedGuardDropFailure, "justiceBossLoggedGuardDropFailure", false);
            Scribe_Values.Look(ref stopped, "justiceBossStopped", false);

            Scribe_Values.Look(
                ref bossDifficultyCaptured,
                "justiceBossDifficultyCaptured",
                false);

            Scribe_Values.Look(
                ref difficultyEnableMortarShield,
                "justiceBossDifficultyEnableMortarShield",
                JusticeBossDifficultyValues.DefaultEnableMortarShield);

            Scribe_Values.Look(
                ref difficultyEnableBulletShield,
                "justiceBossDifficultyEnableBulletShield",
                JusticeBossDifficultyValues.DefaultEnableBulletShield);

            Scribe_Values.Look(
                ref difficultyAutoMortarCount,
                "justiceBossDifficultyAutoMortarCount",
                JusticeBossDifficultyValues.DefaultAutoMortarCount);

            Scribe_Values.Look(
                ref difficultyAutoChargeBlasterCount,
                "justiceBossDifficultyAutoChargeBlasterCount",
                JusticeBossDifficultyValues.DefaultAutoChargeBlasterCount);

            Scribe_Values.Look(
                ref difficultyAutoInfernoCount,
                "justiceBossDifficultyAutoInfernoCount",
                JusticeBossDifficultyValues.DefaultAutoInfernoCount);

            Scribe_Values.Look(
                ref difficultyTotalWaves,
                "justiceBossDifficultyTotalWaves",
                JusticeBossDifficultyValues.DefaultTotalWaves);

            Scribe_Values.Look(
                ref difficultyWaveIntervalTicks,
                "justiceBossDifficultyWaveIntervalTicks",
                JusticeBossDifficultyValues.DefaultWaveIntervalTicks);

            Scribe_Values.Look(
                ref difficultyMechsPerWave,
                "justiceBossDifficultyMechsPerWave",
                JusticeBossDifficultyValues.DefaultMechsPerWave);

            Scribe_Values.Look(
                ref difficultyAllowBossReplacement,
                "justiceBossDifficultyAllowBossReplacement",
                JusticeBossDifficultyValues.DefaultAllowBossReplacement);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                deployedInfrastructure ??= new List<Thing>();
                deployedInfrastructure.RemoveAll(t => t == null);
                pendingWaveKinds ??= new List<PawnKindDef>();
                pendingWaveKinds.RemoveAll(k => k == null);
                pendingGuardKinds ??= new List<PawnKindDef>();
                pendingGuardKinds.RemoveAll(k => k == null);
                if (justiceEventId == 0 && initialized && Pawn != null)
                {
                    justiceEventId = Pawn.thingIDNumber;
                }

                if (!bossDifficultyCaptured)
                {
                    ApplyDefaultDifficultySnapshot();
                }
                else
                {
                    ClampDifficultySnapshot();
                }
            }
        }
    }
}
