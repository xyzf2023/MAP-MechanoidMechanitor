using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>BOSS 专属主思考树。技能完整执行；移动、等待和破障定期让出决策机会。</summary>
    public sealed class JobGiver_SunBossCombat : ThinkNode_JobGiver
    {
        private const int ReconsiderTicks = 30;

        protected override Job? TryGiveJob(Pawn pawn)
        {
            CompSunBossState? state = pawn.GetComp<CompSunBossState>();
            if (state == null || !pawn.Spawned || pawn.Dead || pawn.Destroyed) return null;
            // 受伤触发的工作重选不能中断蓄力或持续激光；真正失效由各 JobDriver 自己结束。
            if (IsCasting(pawn)) return null;
            if (pawn.Downed || pawn.stances?.stunner?.Stunned == true || pawn.InMentalState
                || pawn.stances?.FullBodyBusy == true)
                return Wait();

            CompEnergyPulse? pulse = pawn.GetComp<CompEnergyPulse>();
            if (state.OpeningPulsePending && pulse != null)
                return pulse.TryMakeCastJob() ?? Wait();
            int openingDelay = state.OpeningCannonDelayRemaining;
            if (openingDelay > 0) return Wait(openingDelay < ReconsiderTicks ? openingDelay : ReconsiderTicks);

            List<Thing> targets = Targets(pawn);
            CompAnnihilationCannon? cannon = pawn.GetComp<CompAnnihilationCannon>();
            if (cannon != null)
                foreach (Thing target in targets)
                {
                    Job? cast = cannon.TryMakeCastJob(target.Position);
                    if (cast != null) return cast;
                }

            if (pulse != null && targets.OfType<Pawn>().Any(p => p.RaceProps.IsMechanoid
                && p.stances?.stunner != null && p.Position.DistanceToSquared(pawn.Position) <= pulse.Props.radius * pulse.Props.radius
                && GenSight.LineOfSight(pawn.Position, p.Position, pawn.Map, skipFirstCell: true)))
            {
                Job? cast = pulse.TryMakeCastJob();
                if (cast != null) return cast;
            }

            CompHighEnergyLaserBeam? laser = pawn.GetComp<CompHighEnergyLaserBeam>();
            if (laser != null)
                foreach (Pawn target in targets.OfType<Pawn>())
                {
                    Job? cast = laser.TryMakeCastJob(target);
                    if (cast != null) return cast;
                }

            foreach (Thing target in targets)
            {
                Job? approach = Approach(pawn, target);
                if (approach != null) return approach;
            }
            IntVec3 home = state.ActivationCell;
            if (home.IsValid && home.InBounds(pawn.Map) && home != pawn.Position
                && home.Standable(pawn.Map) && pawn.CanReach(home, PathEndMode.OnCell, Danger.Deadly))
                return Reconsider(JobMaker.MakeJob(JobDefOf.Goto, home));
            return Wait();
        }

        private static bool IsCasting(Pawn pawn) => pawn.CurJobDef == AnnihilationCannonDefOf.MAP_AnnihilationCannon
            || pawn.CurJobDef == EnergyPulseDefOf.MAP_EnergyPulse
            || pawn.CurJobDef == HighEnergyLaserBeamDefOf.MAP_HighEnergyLaserBeam;

        private static List<Thing> Targets(Pawn pawn)
        {
            // 不使用普通可达性过滤，围墙内的 Pawn 必须仍能成为破障追击目标。
            return pawn.Map.attackTargetsCache.GetPotentialTargetsFor(pawn)
                .Select(t => t.Thing)
                .Where(t => t != pawn && t.Spawned && !t.Destroyed && pawn.HostileTo(t)
                    && ((t is Pawn p && !p.Dead && !p.Downed)
                        || (t is Building_Turret turret && !turret.ThreatDisabled(pawn))))
                .OrderBy(t => t.Position.DistanceToSquared(pawn.Position)).ThenBy(t => t.thingIDNumber).ToList();
        }

        private static Job? Approach(Pawn pawn, Thing target)
        {
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
                return pawn.CanReachImmediate(target, PathEndMode.Touch) ? Melee(target) : null;
            if (pawn.CanReachImmediate(target, PathEndMode.Touch)) return Melee(target);
            if (pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly))
            {
                using (PawnPath walk = pawn.Map.pathFinder.FindPathNow(pawn.Position, target,
                    TraverseParms.For(pawn, Danger.Deadly), peMode: PathEndMode.Touch))
                    return walk.Found ? Reconsider(JobMaker.MakeJob(JobDefOf.Goto, walk.LastNode)) : null;
            }

            if (!pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly, canBashDoors: true,
                canBashFences: true, mode: TraverseMode.PassAllDestroyableThings)) return null;
            using (PawnPath path = pawn.Map.pathFinder.FindPathNow(pawn.Position, target,
                TraverseParms.For(pawn, Danger.Deadly, TraverseMode.PassAllDestroyableThings,
                    canBashDoors: true, canBashFences: true), peMode: PathEndMode.Touch))
            {
                if (!path.Found) return null;
                Thing blocker = path.FirstBlockingBuilding(out IntVec3 cellBefore, pawn);
                if (blocker == null) return Reconsider(JobMaker.MakeJob(JobDefOf.Goto, path.LastNode));
                // 稳定器不能靠近战打通，也不能选择该路径后永远敲打无耐久建筑。
                if (blocker.def == SunBossDefOf.MAP_Building_ReactorStabilizer
                    || !blocker.def.useHitPoints || !blocker.def.destroyable) return null;
                if (pawn.CanReachImmediate(blocker, PathEndMode.Touch)) return Melee(blocker);
                if (cellBefore.IsValid && pawn.CanReach(cellBefore, PathEndMode.OnCell, Danger.Deadly))
                    return Reconsider(JobMaker.MakeJob(JobDefOf.Goto, cellBefore));
            }
            return null;
        }

        private static Job Melee(Thing target)
        {
            Job job = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
            job.maxNumMeleeAttacks = 1;
            job.canBashDoors = true;
            job.canBashFences = true;
            return Reconsider(job);
        }

        private static Job Reconsider(Job job)
        {
            job.expiryInterval = ReconsiderTicks;
            job.checkOverrideOnExpire = true;
            return job;
        }
        private static Job Wait() => Wait(ReconsiderTicks);
        private static Job Wait(int ticks)
        {
            Job job = JobMaker.MakeJob(JobDefOf.Wait, ticks);
            job.expiryInterval = ticks;
            job.checkOverrideOnExpire = true;
            return job;
        }
    }
}
