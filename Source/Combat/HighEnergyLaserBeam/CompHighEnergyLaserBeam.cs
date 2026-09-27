using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_HighEnergyLaserBeam : CompProperties
    {
        public float range = 20f;
        public int cooldownTicks;
        public float damageAmount = 50f;
        public int durationTicks = 300;
        public float trackingSpeed = 0.8f; // 格/秒，运行时换算为每 tick 位移。
        public bool trackPawnDuringWarmup = false; // 蓄力落点直接跟随选中的 Pawn；关闭时保留选中格。

        public CompProperties_HighEnergyLaserBeam() => compClass = typeof(CompHighEnergyLaserBeam);

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (!(range > 0f) || float.IsInfinity(range))
                yield return "高能激光束射程必须为有限正数。";
            if (cooldownTicks < 0) yield return "高能激光束冷却不得为负。";
            if (durationTicks < 1) yield return "高能激光束持续时间必须为正。";
            if (!(trackingSpeed >= 0f) || float.IsInfinity(trackingSpeed))
                yield return "高能激光束追踪速度必须为有限非负数。";
            if (!(damageAmount > 0f) || float.IsInfinity(damageAmount))
                yield return "高能激光束伤害必须为有限正数。";
        }
    }

    public sealed class CompHighEnergyLaserBeam : ThingComp
    {
        internal const int WarmupTicks = 180;
        internal const int DamageInterval = 20;
        // 战车聚焦激光每 6 tick 对建筑造成 50 点伤害；太阳保持同频率、双倍伤害。
        internal const int BuildingDamageInterval = 6;
        internal const float BuildingDamageAmount = 100f;
        internal const int EmitterCheckInterval = 60;
        internal const float AreaSideLength = 3f;
        private int readyTick;

        public CompProperties_HighEnergyLaserBeam Props => (CompProperties_HighEnergyLaserBeam)props;

        internal bool HasEmitter(Pawn actor)
        {
            if (actor.health?.hediffSet == null) return false;
            foreach (BodyPartRecord part in actor.health.hediffSet.GetNotMissingParts())
                if (part.def == HighEnergyLaserBeamDefOf.MAP_SunCentralLaserEmitter) return true;
            return false;
        }

        // 发射期间的每 tick 检查不包含部件检查；部件由 Job 每 60 tick 单独检查。
        internal bool CanOperate(Pawn actor) => actor.Spawned && actor.Map != null
            && actor.jobs != null && !actor.Dead && !actor.Downed && !actor.InMentalState
            && (actor.Faction != Faction.OfPlayer || actor.Drafted)
            && actor.stances?.stunner?.Stunned != true
            && actor.CurJobDef != MAPMechanitor_JobDefOf.MAP_MechanicalFlightEmergencyLanding;

        private string? DisabledReason(Pawn actor)
        {
            if (!HasEmitter(actor)) return "MAP_HighEnergyLaserBeam.Disabled.EmitterMissing".Translate();
            if (actor.Faction == Faction.OfPlayer && !actor.Drafted) return "MAP_HighEnergyLaserBeam.Disabled.NotDrafted".Translate();
            if (!CanOperate(actor)) return "MAP_HighEnergyLaserBeam.Disabled.Unavailable".Translate();
            if (Find.TickManager.TicksGame < readyTick)
                return "MAP_HighEnergyLaserBeam.Disabled.Cooldown".Translate(
                    ((readyTick - Find.TickManager.TicksGame) / 60f).ToString("0.0"));
            return null;
        }

        internal bool ValidInitialTarget(Pawn actor, LocalTargetInfo target)
        {
            if (!actor.Spawned || actor.Map == null || !target.IsValid) return false;
            if (target.HasThing && (!(target.Thing is Pawn victim) || victim.Dead
                || victim.Destroyed || !victim.Spawned || victim.Map != actor.Map)) return false;
            IntVec3 cell = target.Cell;
            // 射程只约束初始选取；不要求视线、可达性或敌对关系。
            return cell.InBounds(actor.Map) && (!cell.Fogged(actor.Map) || actor.GetComp<CompSunBossState>() != null)
                && (cell - actor.Position).LengthHorizontalSquared <= Props.range * Props.range;
        }

        /// <summary>供玩家命令及后续 BOSS 控制器调用，不在这里决定 AI 施放时机。</summary>
        public Job? TryMakeCastJob(LocalTargetInfo target)
        {
            if (!(parent is Pawn actor) || IsFiring(actor) || DisabledReason(actor) != null
                || !ValidInitialTarget(actor, target)) return null;
            // A 保留 Pawn/地块目标，B 独立保存确认目标瞬间的所在格，C 锁定施法者位置。
            Job job = JobMaker.MakeJob(HighEnergyLaserBeamDefOf.MAP_HighEnergyLaserBeam, target, target.Cell, actor.Position);
            job.count = target.HasThing ? 1 : 0;
            job.playerForced = actor.Faction == Faction.OfPlayer;
            return job;
        }

        internal void NotifyFiringStarted() => readyTick = Find.TickManager.TicksGame + Props.cooldownTicks;

        private static bool IsFiring(Pawn actor) => actor.jobs?.curDriver is JobDriver_HighEnergyLaserBeam driver
            && driver.IsFiring;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!(parent is Pawn actor) || actor.Faction != Faction.OfPlayer) yield break;
            bool firing = IsFiring(actor);
            Command_Action command = new Command_Action
            {
                defaultLabel = (firing ? "MAP_HighEnergyLaserBeam.Stop.Label" : "MAP_HighEnergyLaserBeam.Label").Translate(),
                defaultDesc = (firing ? "MAP_HighEnergyLaserBeam.Stop.Description" : "MAP_HighEnergyLaserBeam.Description")
                    .Translate().Resolve(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/MM_HighEnergyLaserBeam"),
                action = firing ? () => StopFiring(actor) : () => BeginTargeting(actor)
            };
            if (!firing)
            {
                string? reason = DisabledReason(actor);
                if (reason != null) command.Disable(reason);
            }
            yield return command;
        }

        private static void StopFiring(Pawn actor)
        {
            if (IsFiring(actor)) actor.jobs?.EndCurrentJob(JobCondition.InterruptForced);
        }

        private void BeginTargeting(Pawn actor)
        {
            TargetingParameters parameters = new TargetingParameters
            {
                canTargetLocations = true,
                canTargetPawns = true,
                canTargetSelf = true,
                canTargetBuildings = false,
                canTargetItems = false,
                mapObjectTargetsMustBeAutoAttackable = false
            };
            Find.Targeter.BeginTargeting(parameters, target =>
            {
                if (actor.Map != Find.CurrentMap) return;
                Job? job = TryMakeCastJob(target);
                if (job != null) actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }, target =>
            {
                if (!actor.Spawned || actor.Map != Find.CurrentMap) return;
                GenDraw.DrawRadiusRing(actor.Position, Props.range);
                if (ValidInitialTarget(actor, target))
                {
                    GenDraw.DrawTargetHighlight(target);
                    List<IntVec3> cells = new List<IntVec3>(9);
                    for (int x = -1; x <= 1; x++)
                        for (int z = -1; z <= 1; z++)
                        {
                            IntVec3 cell = target.Cell + new IntVec3(x, 0, z);
                            if (cell.InBounds(actor.Map)) cells.Add(cell);
                        }
                    GenDraw.DrawFieldEdges(cells);
                }
            }, target => actor.Map == Find.CurrentMap && ValidInitialTarget(actor, target), actor);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref readyTick, "highEnergyLaserBeamReadyTick");
        }
    }

    [DefOf]
    public static class HighEnergyLaserBeamDefOf
    {
        public static JobDef MAP_HighEnergyLaserBeam = null!;
        public static BodyPartDef MAP_SunCentralLaserEmitter = null!;
        public static DamageDef MAP_HighEnergyLaserBeamHeat = null!;
        public static DamageDef MAP_ChariotLaserSiege = null!;
        public static ThingDef Mote_MAP_HighEnergyLaserBeamOuter = null!;
        public static ThingDef Mote_MAP_HighEnergyLaserBeamCore = null!;
        public static ThingDef Mote_MAP_HighEnergyLaserBeamArea = null!;
        static HighEnergyLaserBeamDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(HighEnergyLaserBeamDefOf));
    }
}
