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

            foreach (ThingWithComps thing in map.listerThings.AllThings.OfType<ThingWithComps>().ToList())
            {
                CompMechGestatorTank? tank = thing.TryGetComp<CompMechGestatorTank>();
                if (tank != null && tank.State != CompMechGestatorTank.TankState.Empty)
                    TriggerGestator.Invoke(tank, new object[] { map });
            }

            // 使用快照：唤醒信号会同步切换 Lord 状态，并可能结束其他 Pawn 的当前工作。
            List<Pawn> mechs = map.mapPawns.AllPawnsSpawned
                .Where(p => !p.Dead && p.RaceProps.IsMechanoid).ToList();
            foreach (Pawn mech in mechs)
            {
                mech.TryGetComp<CompCanBeDormant>()?.WakeUp();
                Lord? lord = mech.GetLord();
                if (lord?.CurLordToil is LordToil_Sleep) lord.Notify_DormancyWakeup();
            }
        }

        public static void RallyMechanoids(Map map, CellRect arena)
        {
            List<IntVec3> destinations = arena.Cells.Where(c => c.InBounds(map) && c.Standable(map))
                .OrderBy(c => c.DistanceToSquared(arena.CenterCell)).ToList();
            HashSet<IntVec3> assigned = new HashSet<IntVec3>();
            foreach (Pawn mech in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (!mech.RaceProps.IsMechanoid || mech.Dead || mech.Downed ||
                    mech.jobs == null || !mech.health.capacities.CapableOf(PawnCapacityDefOf.Moving)) continue;

                // 分散到大厅中心附近可达的空地，不让整张地图的机械体挤在同一格。
                IntVec3 target = destinations.Where(c => !assigned.Contains(c) &&
                    mech.CanReach(c, PathEndMode.OnCell, Danger.Deadly))
                    .DefaultIfEmpty(IntVec3.Invalid).First();
                if (!target.IsValid) continue;
                assigned.Add(target);
                Job job = JobMaker.MakeJob(JobDefOf.Goto, target);
                job.locomotionUrgency = LocomotionUrgency.Jog;
                job.playerForced = true;
                mech.jobs.StartJob(job, JobCondition.InterruptForced);
            }
        }
    }
}
