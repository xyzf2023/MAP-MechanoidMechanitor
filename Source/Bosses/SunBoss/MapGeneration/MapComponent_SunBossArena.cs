using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 场景记录与一次性激活流程；BOSS 的战斗 AI 不在这里处理。
    public sealed class MapComponent_SunBossArena : MapComponent
    {
        public bool Generated;
        public CellRect FacilityBounds;
        public CellRect ArenaBounds;
        public Building? Core;
        public List<Building> Stabilizers = new List<Building>();

        public const int ActivationDurationTicks = 180;
        private const int RallyDelayTicks = 300;
        private const float ActivationRadius = 15f;
        private bool discoveryLetterSent;
        private bool activated;
        private int activationStartedTick = -1;
        private int rallyAtTick = -1;
        private Effecter? activationProgress;

        public MapComponent_SunBossArena(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (!Generated) return;
            int now = Find.TickManager.TicksGame;
            if (activated)
            {
                if (rallyAtTick >= 0 && now >= rallyAtTick)
                {
                    // 一次性下达，不持续覆盖机械体到达后的行为。
                    rallyAtTick = -1;
                    SunBossActivationUtility.RallyMechanoids(map, ArenaBounds);
                }
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
            rallyAtTick = now + RallyDelayTicks;
            core.Destroy(DestroyMode.Vanish);
            Core = null;
            CleanupActivationProgress();
            SunBossActivationUtility.ActivateFacility(map);
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

        public override void ExposeData()
        {
            Scribe_Values.Look(ref Generated, "sunFacilityGenerated");
            Scribe_Values.Look(ref FacilityBounds, "sunFacilityBounds");
            Scribe_Values.Look(ref ArenaBounds, "sunArenaBounds");
            Scribe_References.Look(ref Core, "sunArenaCore");
            Scribe_Collections.Look(ref Stabilizers, "sunArenaStabilizers", LookMode.Reference);
            Scribe_Values.Look(ref discoveryLetterSent, "sunDiscoveryLetterSent");
            Scribe_Values.Look(ref activated, "sunBossActivated");
            Scribe_Values.Look(ref activationStartedTick, "sunActivationStartedTick", -1);
            Scribe_Values.Look(ref rallyAtTick, "sunRallyAtTick", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && Stabilizers == null)
                Stabilizers = new List<Building>();
        }
    }
}
