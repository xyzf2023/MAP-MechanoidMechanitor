using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 场景记录、激活与周期援军调度；BOSS 的战斗 AI 不在这里处理。
    public sealed class MapComponent_SunBossArena : MapComponent
    {
        public bool Generated;
        public CellRect FacilityBounds;
        public CellRect ArenaBounds;
        public Building? Core;
        public List<Building> Stabilizers = new List<Building>();
        internal int RemainingStabilizers => Stabilizers.Count(s => s != null && !s.Destroyed && s.Spawned && s.Map == map);
        private Pawn? bossPawn;
        private bool bossDefeated;
        internal bool BossDefeated => bossDefeated || (activated && bossPawn != null && (bossPawn.Dead || bossPawn.Destroyed));

        public const int ActivationDurationTicks = 180;
        private const int RallyDelayTicks = 300;
        private const int RallyIntervalTicks = 1250;
        private const float ActivationRadius = 15f;
        private bool discoveryLetterSent;
        private bool activated;
        private int activationStartedTick = -1;
        private int rallyAtTick = -1;
        private int nextRallyBatchTick = -1;
        private bool rallyBatchSchedulingInitialized;
        private List<Pawn> pendingRallyMechs = new List<Pawn>();
        private Effecter? activationProgress;

        public MapComponent_SunBossArena(Map map) : base(map) { }

        internal static bool SuppressesMechanicalFlight(Pawn? pawn)
        {
            Map? pawnMap = pawn?.Map;
            return pawnMap != null
                && pawnMap.GetComponent<MapComponent_SunBossArena>().Generated;
        }

        public override void MapComponentTick()
        {
            if (!Generated) return;
            int now = Find.TickManager.TicksGame;
            if (activated)
            {
                bossDefeated = BossDefeated;
                if (bossDefeated)
                {
                    pendingRallyMechs.Clear();
                    rallyAtTick = -1;
                    nextRallyBatchTick = -1;
                    return;
                }
                // BOSS 暂时离图时保留批次，返回后继续；不补跑错过的周期。
                if (bossPawn == null || !bossPawn.Spawned || bossPawn.Map != map) return;
                if (rallyAtTick >= 0 && now >= rallyAtTick)
                {
                    List<Pawn> batch = pendingRallyMechs;
                    pendingRallyMechs = new List<Pawn>();
                    rallyAtTick = -1;
                    SunBossActivationUtility.RallyMechanoids(map, ArenaBounds, batch);
                }
                if (nextRallyBatchTick >= 0 && now >= nextRallyBatchTick)
                    PrepareRallyBatch(now);
                return;
            }

            Building? core = Core;
            if (core == null || !core.Spawned || core.Map != map)
            {
                CleanupActivationProgress();
                return;
            }

            if (!discoveryLetterSent && core.OccupiedRect().Any(c => !c.Fogged(map)))
            {
                discoveryLetterSent = true;
                Find.LetterStack.ReceiveLetter("MAP_SunBoss_ReactorFoundLabel".Translate(),
                    "MAP_SunBoss_ReactorFoundText".Translate(), LetterDefOf.NeutralEvent, core);
            }

            if (activationStartedTick < 0)
            {
                if (!discoveryLetterSent || !ColonistNearCore(core)) return;
                activationStartedTick = now;
                Find.LetterStack.ReceiveLetter("MAP_SunBoss_ReactorAwakeningLabel".Translate(),
                    "MAP_SunBoss_ReactorAwakeningText".Translate(), LetterDefOf.ThreatBig, core);
            }

            if (now - activationStartedTick < ActivationDurationTicks)
            {
                TickActivationProgress(core, now);
                return;
            }

            // 先生成 Pawn，再移除建筑；生成失败时保留反应堆。
            Pawn boss = PawnGenerator.GeneratePawn(
                DefDatabase<PawnKindDef>.GetNamed("MAP_Mech_SunBOSS"), Faction.OfMechanoids);
            IntVec3 position = core.Position;
            Rot4 rotation = core.Rotation;
            core.DeSpawn();
            try
            {
                GenSpawn.Spawn(boss, position, map, rotation);
            }
            catch
            {
                if (!boss.Destroyed) boss.Destroy(DestroyMode.Vanish);
                GenSpawn.Spawn(core, position, map, rotation);
                throw;
            }
            activated = true;
            bossPawn = boss;
            core.Destroy(DestroyMode.Vanish);
            Core = null;
            CleanupActivationProgress();
            SunBossActivationUtility.ActivateFacility(map);
            PrepareRallyBatch(now);
        }

        private void PrepareRallyBatch(int now)
        {
            rallyBatchSchedulingInitialized = true;
            nextRallyBatchTick = now + RallyIntervalTicks;
            pendingRallyMechs = SunBossActivationUtility.PrepareRallyBatch(map, ArenaBounds);
            rallyAtTick = pendingRallyMechs.Count > 0 ? now + RallyDelayTicks : -1;
        }

        private bool ColonistNearCore(Building core)
        {
            CellRect occupied = core.OccupiedRect();
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.Dead || pawn.Downed) continue;
                // 从建筑占地边缘计算距离；揭雾之后不额外要求直线视野。
                if (occupied.Any(c => c.DistanceToSquared(pawn.Position) <= ActivationRadius * ActivationRadius))
                    return true;
            }
            return false;
        }

        private void TickActivationProgress(Building core, int now)
        {
            if (activationProgress == null)
                activationProgress = EffecterDefOf.ProgressBar.Spawn();
            activationProgress.EffectTick(core, TargetInfo.Invalid);
            MoteProgressBar? mote = ((SubEffecter_ProgressBar)activationProgress.children[0]).mote;
            if (mote != null)
            {
                mote.progress = (float)(now - activationStartedTick) / ActivationDurationTicks;
                mote.offsetZ = -1.5f;
                mote.alwaysShow = true;
                mote.linearScale.x = 2.4f;
            }
        }

        private void CleanupActivationProgress()
        {
            activationProgress?.Cleanup();
            activationProgress = null;
        }

        public override void MapRemoved()
        {
            CleanupActivationProgress();
            base.MapRemoved();
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            if (!Generated) return;
            // 按实际存活建筑修复旧档引用；被摧毁的稳定器不会被补造。
            Stabilizers = map.listerThings.ThingsOfDef(SunBossDefOf.MAP_Building_ReactorStabilizer)
                .OfType<Building>().Where(b => ArenaBounds.Contains(b.Position)).ToList();
            if (bossPawn == null)
                bossPawn = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => !p.Dead && p.GetComp<CompSunBossState>() != null);
            if (activated && !rallyBatchSchedulingInitialized)
            {
                // 旧档没有批次名单，废弃原有全场集结时间，等待完整周期后接入新调度。
                rallyBatchSchedulingInitialized = true;
                pendingRallyMechs.Clear();
                rallyAtTick = -1;
                nextRallyBatchTick = BossDefeated ? -1 : Find.TickManager.TicksGame + RallyIntervalTicks;
            }
        }

        public override void MapComponentOnGUI()
        {
            if (activated && bossPawn != null) SunBossStatusPanel.Draw(bossPawn, map);
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref Generated, "sunFacilityGenerated");
            Scribe_Values.Look(ref FacilityBounds, "sunFacilityBounds");
            Scribe_Values.Look(ref ArenaBounds, "sunArenaBounds");
            Scribe_References.Look(ref Core, "sunArenaCore");
            Scribe_References.Look(ref bossPawn, "sunArenaBossPawn");
            Scribe_Collections.Look(ref Stabilizers, "sunArenaStabilizers", LookMode.Reference);
            Scribe_Values.Look(ref discoveryLetterSent, "sunDiscoveryLetterSent");
            Scribe_Values.Look(ref activated, "sunBossActivated");
            Scribe_Values.Look(ref bossDefeated, "sunBossDefeated");
            Scribe_Values.Look(ref activationStartedTick, "sunActivationStartedTick", -1);
            Scribe_Values.Look(ref rallyAtTick, "sunRallyAtTick", -1);
            Scribe_Values.Look(ref nextRallyBatchTick, "sunNextRallyBatchTick", -1);
            Scribe_Values.Look(ref rallyBatchSchedulingInitialized, "sunRallyBatchSchedulingInitialized");
            Scribe_Collections.Look(ref pendingRallyMechs, "sunPendingRallyMechs", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (Stabilizers == null) Stabilizers = new List<Building>();
                if (pendingRallyMechs == null) pendingRallyMechs = new List<Pawn>();
                pendingRallyMechs.RemoveAll(p => p == null);
            }
        }
    }
}
