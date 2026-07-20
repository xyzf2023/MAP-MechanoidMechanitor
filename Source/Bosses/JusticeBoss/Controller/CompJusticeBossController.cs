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

        private int activationSweepTick = -1;

        private bool stopped;

        private Pawn Pawn => (Pawn)parent;

        public int WaveCount => waveCount;

        public int BossReplacementCount => bossReplacementCount;

        public bool InfrastructureDeployed => infrastructureDeployed;

        public bool RetreatOrdered => retreatOrdered;

        public IntVec3 AnchorCell => anchorCell;

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
            anchorCell = Pawn.Position;
            arrivalTick = Find.TickManager.TicksGame;
            infrastructureDeployed = false;
            waveCount = 0;
            nextWaveTick = arrivalTick + FirstWaveDelayTicks;
            retreatTick = -1;
            retreatOrdered = false;
            attackerLord = null;
            guardLord = null;
            activationSweepTick = -1;

            GameComponent_JusticeBossCallTracker.Current?.MarkActive();
            GameComponent_JusticeBossCallTracker.Current?.SetJusticePawn(Pawn);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (stopped || !initialized || Pawn.Dead || Pawn.Destroyed)
            {
                if (Pawn.Dead || Pawn.Destroyed)
                {
                    stopped = true;
                }

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

            if (activationSweepTick > 0 && now >= activationSweepTick)
            {
                JusticeBossDeploymentUtility.ActivateFactionBuildingsNear(
                    Pawn.Map,
                    Pawn.Faction,
                    anchorCell,
                    34f);
                activationSweepTick = -1;
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

            JusticeBossDeploymentUtility.DeployInfrastructure(Pawn, anchorCell, out guardLord);
            infrastructureDeployed = true;
            activationSweepTick = Find.TickManager.TicksGame + 90;
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

            // 若强制替换失败生成，回退普通池单位。
            List<PawnKindDef> safe = new List<PawnKindDef>(composition.Count);
            for (int i = 0; i < composition.Count; i++)
            {
                PawnKindDef kind = composition[i];
                safe.Add(kind);
            }

            JusticeBossSpawnUtility.SpawnWaveNear(
                Pawn,
                anchorCell,
                safe,
                ref attackerLord,
                out int replacements);
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
            return $"init={initialized} waves={waveCount}/{TotalWaves} infra={infrastructureDeployed} "
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
            Scribe_Values.Look(ref activationSweepTick, "justiceBossActivationSweepTick", -1);
            Scribe_Values.Look(ref stopped, "justiceBossStopped", false);
        }
    }
}