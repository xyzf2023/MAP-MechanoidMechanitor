using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 场景记录、激活与援军数量调度；BOSS 的战斗 AI 不在这里处理。
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

        public const int ActivationDurationTicks = 360;
        private const int RallyDelayTicks = 300;
        private const int RallyIntervalTicks = 1250;
        private const int RallyCheckIntervalTicks = 30;
        private const float ActivationRadius = 15f;
        private bool discoveryLetterSent;
        private bool activated;
        private int activationStartedTick = -1;
        private int rallyAtTick = -1;
        private int nextRallyBatchTick = -1;
        private bool rallyBatchSchedulingInitialized;
        private List<Pawn> pendingRallyMechs = new List<Pawn>();
        private List<Pawn> summonedMechs = new List<Pawn>();
        private bool summonedMechsInitialized;
        private bool noRallyCandidates;
        private int ActiveSummonedMechCount => summonedMechs.Count(p => p != null && !p.Destroyed
            && !p.Dead && p.Spawned && p.Map == map && !p.Downed
            && p.Faction == Faction.OfMechanoids && p.HostileTo(Faction.OfPlayer));
        internal bool NoRallyCandidates => noRallyCandidates
            && ActiveSummonedMechCount < SunBossActivationUtility.BatchSize;

        public MapComponent_SunBossArena(Map map) : base(map) { }

        internal float ActivationProgressFor(Thing core) => !activated && activationStartedTick >= 0
            && ReferenceEquals(Core, core) && core.Spawned
                ? Mathf.Clamp01((float)(Find.TickManager.TicksGame - activationStartedTick) / ActivationDurationTicks)
                : 0f;

        // 稳定器共用已存档的竞技场时间线。核心转成 Pawn 后仍返回展开状态；
        // 旧档缺少激活起点时直接视为已完成，流光仍随游戏刻推进。
        internal bool TryGetStabilizerActivation(Thing stabilizer, out int elapsedTicks, out bool powered)
        {
            elapsedTicks = -1;
            powered = false;
            if (!(stabilizer is Building building) || building.Destroyed || !building.Spawned
                || building.Map != map || !Stabilizers.Contains(building)) return false;

            int now = Find.TickManager.TicksGame;
            if (activated)
            {
                elapsedTicks = activationStartedTick >= 0
                    ? Mathf.Max(ActivationDurationTicks, now - activationStartedTick)
                    : ActivationDurationTicks + now;
                powered = !BossDefeated;
                return true;
            }
            if (activationStartedTick < 0 || Core == null || Core.Destroyed
                || !Core.Spawned || Core.Map != map) return false;

            elapsedTicks = Mathf.Max(0, now - activationStartedTick);
            powered = true;
            return true;
        }

        internal bool CanStartActivation(Building core) => !activated && activationStartedTick < 0
            && !core.Destroyed && core.Spawned && core.Map == map
            && (ReferenceEquals(Core, core) || (!Generated && Core == null));

        internal bool TryStartDevActivation(Building core)
        {
            if (!DebugSettings.ShowDevGizmos || core.Destroyed || !core.Spawned || core.Map != map)
                return false;
            // 同一建筑正在苏醒时不重置计时；其他反应堆随时可接管调试记录。
            if (ReferenceEquals(Core, core) && !activated && activationStartedTick >= 0) return true;

            // DEV 可反复激活；保留已经生成的 BOSS，仅将地图级调度切换到本次测试。
            Core = core;
            bossPawn = null;
            bossDefeated = false;
            activated = false;
            activationStartedTick = -1;
            pendingRallyMechs.Clear();
            rallyAtTick = -1;
            nextRallyBatchTick = -1;
            rallyBatchSchedulingInitialized = false;
            noRallyCandidates = false;
            if (!Generated)
            {
                // 普通地图不设置 Generated，避免启用设施禁飞和迷雾规则。
                ArenaBounds = CellRect.CenteredOn(core.Position, (int)ActivationRadius).ClipInsideMap(map);
            }
            RefreshStabilizers();
            return TryStartActivation(core);
        }

        private void RefreshStabilizers()
        {
            Stabilizers = map.listerThings.ThingsOfDef(SunBossDefOf.MAP_Building_ReactorStabilizer)
                .OfType<Building>().Where(b => ArenaBounds.Contains(b.Position)).ToList();
        }

        // 自然靠近与 DEV 按钮共用入口；后续苏醒、生成和设施激活仍由地图 Tick 推进。
        internal bool TryStartActivation(Building core)
        {
            if (!CanStartActivation(core)) return false;
            discoveryLetterSent = true;
            activationStartedTick = Find.TickManager.TicksGame;
            Find.LetterStack.ReceiveLetter("MAP_SunBoss_ReactorAwakeningLabel".Translate(),
                "MAP_SunBoss_ReactorAwakeningText".Translate(), LetterDefOf.ThreatBig, core);
            return true;
        }

        internal static bool SuppressesMechanicalFlight(Pawn? pawn)
        {
            Map? pawnMap = pawn?.Map;
            return pawnMap != null
                && pawnMap.GetComponent<MapComponent_SunBossArena>().Generated;
        }

        public override void MapComponentTick()
        {
            if (!Generated && activationStartedTick < 0 && !activated) return;
            int now = Find.TickManager.TicksGame;
            if (activated)
            {
                bossDefeated = BossDefeated;
                if (bossDefeated)
                {
                    pendingRallyMechs.Clear();
                    summonedMechs.Clear();
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
                return;

            if (!discoveryLetterSent && core.OccupiedRect().Any(c => !c.Fogged(map)))
            {
                discoveryLetterSent = true;
                Find.LetterStack.ReceiveLetter("MAP_SunBoss_ReactorFoundLabel".Translate(),
                    "MAP_SunBoss_ReactorFoundText".Translate(), LetterDefOf.NeutralEvent, core);
            }

            if (activationStartedTick < 0)
            {
                if (!discoveryLetterSent || !ColonistNearCore(core)) return;
                if (!TryStartActivation(core)) return;
            }

            int activationElapsed = now - activationStartedTick;
            if (activationElapsed < ActivationDurationTicks)
            {
                // 共用已存档的激活时间，只在升降阶段喷尘；稳定器本身仍不需要 Tick。
                foreach (Building stabilizer in Stabilizers)
                    if (stabilizer != null && stabilizer.Map == map)
                        ReactorStabilizerPresentation.TickDeploymentDust(stabilizer, activationElapsed);
                return;
            }

            // 先生成 Pawn，再移除建筑；生成失败时保留反应堆。
            Pawn boss = PawnGenerator.GeneratePawn(
                DefDatabase<PawnKindDef>.GetNamed("MAP_Mech_SunBOSS"), Faction.OfMechanoids);
            // 只交接表现；实际生成、开场脉冲及援军调度仍走原有流程。
            boss.GetComp<CompSunBossState>()?.BeginAwakeningHandoff(core.thingIDNumber);
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
            summonedMechsInitialized = true;
            bossPawn = boss;
            core.Destroy(DestroyMode.Vanish);
            Core = null;
            SunBossActivationUtility.ActivateFacility(map);
            PrepareRallyBatch(now);
        }

        private void PrepareRallyBatch(int now)
        {
            rallyBatchSchedulingInitialized = true;
            nextRallyBatchTick = now + RallyCheckIntervalTicks;
            // 保留倒地、离图者的引用以防重新选中；只统计本图仍可参战的援军。
            summonedMechs.RemoveAll(p => p == null || p.Destroyed || p.Dead);
            if (rallyAtTick >= 0 || bossPawn?.GetComp<CompSunBossState>()?.RallyPulsePending == true
                || ActiveSummonedMechCount >= SunBossActivationUtility.BatchSize)
            {
                noRallyCandidates = false;
                return;
            }
            pendingRallyMechs = SunBossActivationUtility.PrepareRallyBatch(map, ArenaBounds, summonedMechs);
            noRallyCandidates = pendingRallyMechs.Count == 0;
            if (noRallyCandidates)
                nextRallyBatchTick = now + RallyIntervalTicks;
            else
            {
                summonedMechs.AddRange(pendingRallyMechs);
                bossPawn?.GetComp<CompSunBossState>()?.RequestRallyPulse();
            }
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

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            if (!Generated && activationStartedTick < 0 && !activated) return;
            // 按实际存活建筑修复旧档引用；被摧毁的稳定器不会被补造。
            RefreshStabilizers();
            if (bossPawn == null)
                bossPawn = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => !p.Dead && p.GetComp<CompSunBossState>() != null);
            if (activated && !summonedMechsInitialized)
            {
                // 旧档没有历史召唤名单：将大厅内和正在集结的敌方机械体纳入限流。
                summonedMechs = map.mapPawns.AllPawnsSpawned.Where(p => p != bossPawn
                    && !p.Dead && p.RaceProps.IsMechanoid && p.Faction == Faction.OfMechanoids
                    && (ArenaBounds.Contains(p.Position) || p.CurJobDef?.defName == "MAP_SunBossRally"))
                    .Concat(pendingRallyMechs.Take(SunBossActivationUtility.BatchSize)).Distinct().ToList();
                pendingRallyMechs = pendingRallyMechs.Take(SunBossActivationUtility.BatchSize).ToList();
                summonedMechsInitialized = true;
            }
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
            Scribe_Collections.Look(ref summonedMechs, "sunSummonedMechs", LookMode.Reference);
            Scribe_Values.Look(ref summonedMechsInitialized, "sunSummonedMechsInitialized");
            Scribe_Values.Look(ref noRallyCandidates, "sunNoRallyCandidates");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (Stabilizers == null) Stabilizers = new List<Building>();
                if (pendingRallyMechs == null) pendingRallyMechs = new List<Pawn>();
                if (summonedMechs == null) summonedMechs = new List<Pawn>();
                pendingRallyMechs.RemoveAll(p => p == null);
            }
        }
    }
}
