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

        private bool retreatCommitted;

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

        // 旧存档兼容标记。difficulty 字段不再代表整场战斗快照，
        // 仅作为最近一次从 ModSettings 刷新的合法值缓存。
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

        private bool difficultyApplyMobileCombatToSummons =
            JusticeBossDifficultyValues.DefaultApplyMobileCombatToSummons;

        // 已经开始的部署/波次必须保持内部一致，不能因设置中途变化让同一批单位前后规则不同。
        private bool infrastructureApplyMobileCombatToSummons =
            JusticeBossDifficultyValues.DefaultApplyMobileCombatToSummons;

        private bool currentWaveApplyMobileCombatToSummons =
            JusticeBossDifficultyValues.DefaultApplyMobileCombatToSummons;

        // 用于识别旧存档是否缺少上述“当前动作”字段。
        private bool actionSettingsCaptured;

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

            RefreshDifficultySettings();

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
            retreatCommitted = false;
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
            infrastructureApplyMobileCombatToSummons =
                difficultyApplyMobileCombatToSummons;
            currentWaveApplyMobileCombatToSummons =
                difficultyApplyMobileCombatToSummons;
            actionSettingsCaptured = true;

            GameComponent_JusticeBossCallTracker.Current?.MarkActive();
            GameComponent_JusticeBossCallTracker.Current?.SetJusticePawn(Pawn);
        }

        // ----------------------------------------------------------------
        // 实时难度设置
        // ----------------------------------------------------------------

        private void RefreshDifficultySettings()
        {
            MAPMechanitorModSettings? settings =
                MAPMechanitorMod.Settings;

            if (settings == null)
            {
                ApplyDefaultDifficultySettings();
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

            difficultyApplyMobileCombatToSummons =
                settings.justiceBossApplyMobileCombatToSummons;

            bossDifficultyCaptured = true;
        }

        private void ApplyDefaultDifficultySettings()
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

            difficultyApplyMobileCombatToSummons =
                JusticeBossDifficultyValues.DefaultApplyMobileCombatToSummons;

            bossDifficultyCaptured = true;
        }

        private void ClampDifficultyCache()
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

        private int GetCurrentTotalWaves()
        {
            MAPMechanitorModSettings? settings = MAPMechanitorMod.Settings;
            return JusticeBossDifficultyValues.ClampTotalWaves(
                settings?.justiceBossTotalWaves
                ?? JusticeBossDifficultyValues.DefaultTotalWaves);
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
                EndBattle();
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

            // 只在当前没有未完成波次时根据实时总波数决定是否进入撤退。
            // 一旦 committed，之后提高总波数也不得重新开启战斗。
            if (!retreatCommitted
                && pendingWaveKinds.Count == 0
                && waveCount >= GetCurrentTotalWaves())
            {
                CommitToRetreat();
            }

            if (!retreatCommitted
                && nextWaveTick > 0
                && now >= nextWaveTick)
            {
                TrySpawnWave(forceBossReplace: false);
            }

            if (retreatCommitted
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

            if (retreatCommitted)
            {
                return;
            }

            if (pendingWaveKinds.Count == 0
                && waveCount >= GetCurrentTotalWaves())
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

            waveCount = GetCurrentTotalWaves();
            CommitToRetreat();
        }

        public void DebugForceRetreatNow()
        {
            if (!initialized)
            {
                InitializeOnArrival();
            }

            waveCount = GetCurrentTotalWaves();
            CommitToRetreat();
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

            RefreshDifficultySettings();
            infrastructureApplyMobileCombatToSummons =
                difficultyApplyMobileCombatToSummons;
            actionSettingsCaptured = true;

            JusticeBossDeploymentUtility.DeployInfrastructure(
                Pawn,
                anchorCell,
                justiceEventId,
                difficultyAutoMortarCount,
                difficultyAutoChargeBlasterCount,
                difficultyAutoInfernoCount,
                difficultyEnableMortarShield,
                difficultyEnableBulletShield,
                infrastructureApplyMobileCombatToSummons,
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
                        "[MAP-机械族机械师] 正义 BOSS： 等待部分基础设施空投舱开启超时。");
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
                    infrastructureApplyMobileCombatToSummons,
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
                        "[MAP-机械族机械师] 正义 BOSS： 守卫空投发生致命发射错误，已停止重试。");
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
                        "[MAP-机械族机械师] 正义 BOSS： 守卫空投重试次数已耗尽，缺失的守卫未能发射。");
                }

                nextGuardRetryTick = -1;
                return;
            }

            nextGuardRetryTick = now + DropRetryDelayTicks;
        }

        private void TrySpawnWave(bool forceBossReplace)
        {
            if (Pawn.Map == null || retreatCommitted)
            {
                return;
            }

            if (pendingWaveKinds.Count == 0)
            {
                RefreshDifficultySettings();
                if (waveCount >= difficultyTotalWaves)
                {
                    CommitToRetreat();
                    return;
                }

                int waveIndex = waveCount + 1;

                bool applyBossReplace =
                    !forceBossReplace
                    && difficultyAllowBossReplacement;

                currentWaveApplyMobileCombatToSummons =
                    difficultyApplyMobileCombatToSummons;
                actionSettingsCaptured = true;

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
                        "[MAP-机械族机械师] 正义 BOSS： 无法构建下一波召唤机械族。",
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
                    currentWaveApplyMobileCombatToSummons,
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
                        "[MAP-机械族机械师] 正义 BOSS： 波次空投发生致命发射错误，已停止重试。");
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
                        "[MAP-机械族机械师] 正义 BOSS： 波次空投重试次数已耗尽，该波次仍不完整。");
                }

                nextWaveTick = -1;
                return;
            }

            nextWaveTick = Find.TickManager.TicksGame + DropRetryDelayTicks;
        }

        private void ScheduleAfterWave()
        {
            RefreshDifficultySettings();
            if (waveCount >= difficultyTotalWaves)
            {
                CommitToRetreat();
            }
            else
            {
                retreatTick = -1;
                nextWaveTick =
                    Find.TickManager.TicksGame
                    + difficultyWaveIntervalTicks;
            }
        }

        private void CommitToRetreat()
        {
            retreatCommitted = true;
            nextWaveTick = -1;
            if (retreatTick <= 0)
            {
                retreatTick =
                    Find.TickManager.TicksGame
                    + RetreatDelayAfterFinalWaveTicks;
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

        public void EndBattle()
        {
            stopped = true;
            activationFinished = true;
            for (int i = 0; i < deployedInfrastructure.Count; i++)
            {
                Thing? thing = deployedInfrastructure[i];
                if (thing == null || thing.Destroyed
                    || (thing.def.defName != "ShieldGeneratorMortar"
                        && thing.def.defName != "ShieldGeneratorBullets"))
                {
                    continue;
                }

                // 复用原版集群战败通知：永久关闭且由护盾自身存档。
                // 不要求已落地，空投舱中的护盾同样需要提前关机；重复通知无副作用。
                thing.Notify_LordDestroyed();
            }
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            EndBattle();
        }

        public override void Notify_Killed(Map prevMap, DamageInfo? dinfo = null)
        {
            base.Notify_Killed(prevMap, dinfo);
            EndBattle();
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

            int liveTotalWaves = GetCurrentTotalWaves();
            return $"eventId={justiceEventId} init={initialized} waves={waveCount}/{liveTotalWaves} "
                + $"infra={infrastructureDeployed} pendingInfra={PendingActivationCount} "
                + $"pendingWave={pendingWaveKinds.Count} waveRetries={waveDropRetryCount} "
                + $"pendingGuards={pendingGuardKinds.Count} guardRetries={guardDropRetryCount} "
                + $"pendingDrops={pendingDrops} landedAssigned={landed} "
                + $"nextWave={nextWaveTick} retreat={retreatTick} committed={retreatCommitted} ordered={retreatOrdered} "
                + $"bossRepl={bossReplacementCount} stopped={stopped} anchor={anchorCell} "
                + $"waveMobile={currentWaveApplyMobileCombatToSummons} "
                + $"infraMobile={infrastructureApplyMobileCombatToSummons}";
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
            Scribe_Values.Look(ref retreatCommitted, "justiceBossRetreatCommitted", false);
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
                ref infrastructureApplyMobileCombatToSummons,
                "justiceBossInfrastructureApplyMobileCombat",
                JusticeBossDifficultyValues.DefaultApplyMobileCombatToSummons);
            Scribe_Values.Look(
                ref currentWaveApplyMobileCombatToSummons,
                "justiceBossCurrentWaveApplyMobileCombat",
                JusticeBossDifficultyValues.DefaultApplyMobileCombatToSummons);
            Scribe_Values.Look(
                ref actionSettingsCaptured,
                "justiceBossActionSettingsCaptured",
                false);

            // 保留旧 difficulty 键用于旧存档读取与回滚兼容；正常战斗会在阶段边界重新读取实时设置。
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

            Scribe_Values.Look(
                ref difficultyApplyMobileCombatToSummons,
                "justiceBossDifficultyApplyMobileCombatToSummons",
                JusticeBossDifficultyValues.DefaultApplyMobileCombatToSummons);

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

                // 先用旧存档中保存的 difficulty 值恢复正在进行的动作，再切换到实时设置。
                if (!bossDifficultyCaptured)
                {
                    ApplyDefaultDifficultySettings();
                }
                else
                {
                    ClampDifficultyCache();
                }

                if (!actionSettingsCaptured)
                {
                    infrastructureApplyMobileCombatToSummons =
                        difficultyApplyMobileCombatToSummons;
                    currentWaveApplyMobileCombatToSummons =
                        difficultyApplyMobileCombatToSummons;
                    actionSettingsCaptured = true;
                }

                // 旧存档没有 retreatCommitted；已有撤退计时或已下达撤退命令时视为不可反悔。
                if (!retreatCommitted && (retreatTick > 0 || retreatOrdered))
                {
                    retreatCommitted = true;
                }

                RefreshDifficultySettings();
            }
        }
    }
}
