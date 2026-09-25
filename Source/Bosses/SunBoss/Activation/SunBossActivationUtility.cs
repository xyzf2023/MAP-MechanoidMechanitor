using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    internal static class SunBossActivationUtility
    {
        internal const int BatchSize = 4;
        private static JobDef RallyJob => DefDatabase<JobDef>.GetNamed("MAP_SunBossRally");
        // 原版没有公开释放入口，复用 Trigger，保留出仓、空仓外观、眩晕与 Lord 分配。
        private static readonly MethodInfo TriggerGestator =
            AccessTools.Method(typeof(CompMechGestatorTank), "Trigger", new[] { typeof(Map) });

        public static void ActivateFacility(Map map)
        {
            foreach (Thing door in map.listerThings.ThingsOfDef(ThingDefOf.AncientBlastDoor).ToList())
            {
                CompHackable? hackable = door.TryGetComp<CompHackable>();
                if (hackable != null && !hackable.IsHacked) hackable.HackNow();
            }
            // HackNow 不负责清理门的旧不可达缓存。
            map.reachability.ClearCache();
        }

        public static List<Pawn> PrepareRallyBatch(Map map, CellRect arena, List<Pawn> summoned)
        {
            List<Pawn> selected = new List<Pawn>(BatchSize);
            // 唤醒会同步改变整个 Lord 的状态；每次操作后重新检查现役候选。
            List<Pawn> mechs = map.mapPawns.AllPawnsSpawned.Where(p => !summoned.Contains(p)).ToList();
            HashSet<Pawn> attemptedWakeups = new HashSet<Pawn>();
            while (selected.Count < BatchSize)
            {
                Pawn? active = FindNearest(mechs, arena, p => !selected.Contains(p)
                    && CanRally(p, map, arena) && !IsDormant(p));
                if (active != null)
                {
                    selected.Add(active);
                    continue;
                }
                Pawn? sleeper = FindNearest(mechs, arena, p => !selected.Contains(p)
                    && !attemptedWakeups.Contains(p) && CanRally(p, map, arena, allowStunned: true)
                    && IsDormant(p));
                if (sleeper == null) break;
                attemptedWakeups.Add(sleeper);
                sleeper.TryGetComp<CompCanBeDormant>()?.WakeUp();
                Lord? lord = sleeper.GetLord();
                if (lord?.CurLordToil is LordToil_Sleep) lord.Notify_DormancyWakeup();
                if (CanRally(sleeper, map, arena, allowStunned: true) && !IsDormant(sleeper))
                    selected.Add(sleeper);
            }

            if (selected.Count >= BatchSize) return selected;
            List<ThingWithComps> tanks = map.listerThings.AllThings.OfType<ThingWithComps>()
                .Where(t => t.TryGetComp<CompMechGestatorTank>() is CompMechGestatorTank tank
                    && tank.State != CompMechGestatorTank.TankState.Empty).ToList();
            HashSet<Pawn> knownPawns = new HashSet<Pawn>(map.mapPawns.AllPawnsSpawned);
            while (selected.Count < BatchSize && tanks.Count > 0)
            {
                ThingWithComps? thing = FindNearest(tanks, arena, t => t.Spawned && t.Map == map
                    && t.TryGetComp<CompMechGestatorTank>() is CompMechGestatorTank tank
                    && tank.State != CompMechGestatorTank.TankState.Empty
                    && t.OccupiedRect().ExpandedBy(1).EdgeCells.Any(c => c.InBounds(map) && c.Standable(map)));
                if (thing == null) break;
                tanks.Remove(thing);
                TriggerGestator.Invoke(thing.TryGetComp<CompMechGestatorTank>(), new object[] { map });
                // 原版 Trigger 不返回 Pawn；按生成前后的引用差集识别实际出仓者。
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (!knownPawns.Add(pawn)) continue;
                    if (selected.Count < BatchSize && CanRally(pawn, map, arena, allowStunned: true)
                        && !IsDormant(pawn)) selected.Add(pawn);
                }
            }
            return selected;
        }

        private static bool IsDormant(Pawn pawn) => pawn.TryGetComp<CompCanBeDormant>()?.Awake == false
            || pawn.GetLord()?.CurLordToil is LordToil_Sleep;

        private static bool CanRally(Pawn pawn, Map map, CellRect arena, bool allowStunned = false)
        {
            return pawn != null && pawn.Spawned && pawn.Map == map && !pawn.Dead && !pawn.Downed
                && pawn.RaceProps.IsMechanoid && pawn.GetComp<CompSunBossState>() == null
                && pawn.Faction == Faction.OfMechanoids && pawn.HostileTo(Faction.OfPlayer)
                && pawn.jobs != null && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving)
                && !pawn.IsSelfShutdown() && !pawn.IsDeactivated()
                && (allowStunned || pawn.stances?.stunner?.Stunned != true)
                && !arena.Contains(pawn.Position) && pawn.CurJobDef != RallyJob && !pawn.IsFighting();
        }

        // 每次只保留最近候选；同距用蓄水池抽样，不排序、不做全场寻路。
        private static T? FindNearest<T>(IEnumerable<T> things, CellRect arena, Func<T, bool> eligible)
            where T : Thing
        {
            T? nearest = null;
            int bestDistance = int.MaxValue;
            int ties = 0;
            foreach (T thing in things)
            {
                if (!eligible(thing)) continue;
                int dx = Math.Max(arena.minX - thing.Position.x, Math.Max(0, thing.Position.x - arena.maxX));
                int dz = Math.Max(arena.minZ - thing.Position.z, Math.Max(0, thing.Position.z - arena.maxZ));
                int distance = dx * dx + dz * dz;
                if (distance < bestDistance)
                {
                    nearest = thing;
                    bestDistance = distance;
                    ties = 1;
                }
                else if (distance == bestDistance && Rand.Range(0, ++ties) == 0)
                    nearest = thing;
            }
            return nearest;
        }

        public static void RallyMechanoids(Map map, CellRect arena, List<Pawn> selected)
        {
            CellRect inner = arena.ContractedBy(3);
            List<IntVec3> destinations = arena.Cells.Where(c => !inner.Contains(c)
                && c.InBounds(map) && c.Standable(map)).ToList();
            List<Pawn> bosses = map.mapPawns.AllPawnsSpawned
                .Where(p => !p.Dead && p.GetComp<CompSunBossState>() != null).ToList();
            HashSet<IntVec3> assigned = new HashSet<IntVec3>();
            foreach (Pawn mech in selected)
            {
                if (!CanRally(mech, map, arena) || IsDormant(mech)) continue;

                Job? combat = SunBossRallyCombat.TryGetJob(mech);
                if (combat != null)
                {
                    mech.jobs.StartJob(combat, JobCondition.InterruptForced);
                    continue;
                }
                // 就近选择大厅边缘，避免从对面穿过中心热场；不强制覆盖玩家机械体。
                IntVec3 target = destinations.Where(c => !assigned.Contains(c) &&
                    mech.CanReach(c, PathEndMode.OnCell, Danger.Deadly))
                    .OrderBy(c => c.DistanceToSquared(mech.Position))
                    .Take(32).Where(c => HasSafeRallyPath(mech, c, bosses))
                    .DefaultIfEmpty(IntVec3.Invalid).First();
                if (!target.IsValid) continue;
                assigned.Add(target);
                Job job = JobMaker.MakeJob(RallyJob, target);
                job.locomotionUrgency = LocomotionUrgency.Jog;
                mech.jobs.StartJob(job, JobCondition.InterruptForced);
            }
        }

        private static bool HasSafeRallyPath(Pawn mech, IntVec3 target, List<Pawn> bosses)
        {
            using (PawnPath path = mech.Map.pathFinder.FindPathNow(mech.Position, target,
                TraverseParms.For(mech, Danger.Deadly), peMode: PathEndMode.OnCell))
            {
                if (!path.Found) return false;
                foreach (Pawn boss in bosses)
                {
                    // 为最强阶段的 7 格热场留一格余量；已在热场中的单位允许向外撤离。
                    float radius = SunBossStage.For(0).HeatRadius + 1f;
                    float previousDistance = mech.Position.DistanceToSquared(boss.Position);
                    for (int i = path.NodesReversed.Count - 1; i >= 0; i--)
                    {
                        float distance = path.NodesReversed[i].DistanceToSquared(boss.Position);
                        if (distance <= radius * radius && distance < previousDistance) return false;
                        previousDistance = distance;
                    }
                }
                return true;
            }
        }
    }
}
