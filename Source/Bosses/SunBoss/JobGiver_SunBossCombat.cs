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
            if ((state.OpeningPulsePending || state.RallyPulsePending) && pulse != null)
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

            // 只有召唤尝试确认无候选时，才回退到阶段冷却驱动的脉冲。
            if (pulse != null && pawn.Map.GetComponent<MapComponent_SunBossArena>().NoRallyCandidates)
            {
                Job? cast = pulse.TryMakeCastJob();
                if (cast != null) return cast;
            }

            CompHighEnergyLaserBeam? laser = pawn.GetComp<CompHighEnergyLaserBeam>();
            // 最近的追击目标被封住时先开路，避免可隔墙选取的普通激光一直抢占破障。
            if (laser != null && targets.Count > 0
                && !pawn.CanReach(targets[0], PathEndMode.Touch, Danger.Deadly))
            {
                Job? breach = TryBreach(pawn, targets[0], laser);
                if (breach != null) return breach;
            }
            if (laser != null)
                foreach (Pawn target in targets.OfType<Pawn>())
                {
                    Job? cast = laser.TryMakeCastJob(target);
                    if (cast != null) return cast;
                }

            foreach (Thing target in targets)
            {
                Job? approach = Approach(pawn, target, laser);
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

        private static Job? Approach(Pawn pawn, Thing target, CompHighEnergyLaserBeam? laser)
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

            return TryBreach(pawn, target, laser);
        }

        private static Job? TryBreach(Pawn pawn, Thing target, CompHighEnergyLaserBeam? laser)
        {
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving)) return null;
            if (!pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly, canBashDoors: true,
                canBashFences: true, mode: TraverseMode.PassAllDestroyableThings)) return null;
            using (PawnPath path = pawn.Map.pathFinder.FindPathNow(pawn.Position, target,
                TraverseParms.For(pawn, Danger.Deadly, TraverseMode.PassAllDestroyableThings,
                    canBashDoors: true, canBashFences: true), peMode: PathEndMode.Touch))
            {
                if (!path.Found) return null;
                Thing blocker = path.FirstBlockingBuilding(out IntVec3 cellBefore, pawn);
                if (blocker == null) return Reconsider(JobMaker.MakeJob(JobDefOf.Goto, path.LastNode));
                // 激光和近战共用排除规则：不主动破坏稳定器或攻击无耐久、不可摧毁的障碍。
                if (!blocker.Spawned || blocker.Destroyed || blocker.Map != pawn.Map
                    || blocker.def == SunBossDefOf.MAP_Building_ReactorStabilizer
                    || !blocker.def.useHitPoints || !blocker.def.destroyable) return null;
                // 复用建筑目标模式、射程及冷却检查；技能任务不附加移动任务的短期重选期限。
                Job? cast = laser?.TryMakeCastJob(blocker);
                if (cast != null) return cast;
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
