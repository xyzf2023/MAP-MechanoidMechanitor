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
        public int warmupTicks = CompHighEnergyLaserBeam.DefaultWarmupTicks;
        public int cooldownTicks;
        public float damageAmount = 50f;
        public int durationTicks = 300;
        public int recoveryTicks;
        public float trackingSpeed = 0.8f; // 格/秒，运行时换算为每 tick 位移。
        public bool trackPawnDuringWarmup = false; // 蓄力落点直接跟随选中的 Pawn；关闭时保留选中格。
        public bool allowAutoFire;

        public CompProperties_HighEnergyLaserBeam() => compClass = typeof(CompHighEnergyLaserBeam);

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (!(range > 0f) || float.IsInfinity(range))
                yield return "高能激光束射程必须为有限正数。";
            if (warmupTicks < 1) yield return "高能激光束蓄力时间必须为正。";
            if (cooldownTicks < 0) yield return "高能激光束冷却不得为负。";
            if (durationTicks < 1) yield return "高能激光束持续时间必须为正。";
            if (recoveryTicks < 0) yield return "高能激光束恢复时长不得为负。";
            if (!(trackingSpeed >= 0f) || float.IsInfinity(trackingSpeed))
                yield return "高能激光束追踪速度必须为有限非负数。";
            if (!(damageAmount > 0f) || float.IsInfinity(damageAmount))
                yield return "高能激光束伤害必须为有限正数。";
        }
    }

    public sealed class CompHighEnergyLaserBeam : ThingComp
    {
        internal const int DefaultWarmupTicks = 180;
        internal const int DamageInterval = 20;
        // 战车聚焦激光每 6 tick 对建筑造成 50 点伤害；太阳保持同频率、双倍伤害。
        internal const int BuildingDamageInterval = 6;
        internal const float BuildingDamageAmount = 100f;
        internal const int EmitterCheckInterval = 60;
        internal const float AreaSideLength = 3f;
        internal const int SweepMode = 2;
        internal const int BuildingTargetMode = 3;
        private int readyTick;
        private HighEnergyLaserBeamTargetSearcher? autoTargetSearcher;

        public CompProperties_HighEnergyLaserBeam Props => (CompProperties_HighEnergyLaserBeam)props;
        internal int WarmupTicks => Mathf.Max(1, Props.warmupTicks);
        internal bool SupportsFireAtWill => Props.allowAutoFire
            && parent is Pawn actor && actor.Faction == Faction.OfPlayer;
        internal bool AutoFireEnabled => SupportsFireAtWill
            && parent is Pawn actor && actor.drafter?.FireAtWill == true;
        internal bool CanStartAutoFire => parent is Pawn actor && AutoFireEnabled
            && CanOperate(actor, allowUndrafted: true) && HasEmitter(actor)
            && Find.TickManager.TicksGame >= readyTick && actor.stances?.FullBodyBusy == false
            && actor.kindDef.canMeleeAttack && !actor.IsCarryingPawn() && !actor.IsShambler
            && !actor.WorkTagIsDisabled(WorkTags.Violent)
            && (actor.IsPlayerControlled || !actor.IsPsychologicallyInvisible())
            && !IsUsingSunSkill;
        internal bool IsUsingSunSkill => parent is Pawn actor
            && (actor.CurJobDef == HighEnergyLaserBeamDefOf.MAP_HighEnergyLaserBeam
                || actor.CurJobDef == AnnihilationCannonDefOf.MAP_AnnihilationCannon
                || actor.CurJobDef == EnergyPulseDefOf.MAP_EnergyPulse);
        internal Verb? AutoAttackVerb => parent is Pawn actor
            ? GetAutoTargetSearcher(actor).CurrentEffectiveVerb : null;
        internal void ResetCooldown() => readyTick = 0;

        internal bool HasEmitter(Pawn actor)
        {
            if (actor.health?.hediffSet == null) return false;
            foreach (BodyPartRecord part in actor.health.hediffSet.GetNotMissingParts())
                if (part.def == HighEnergyLaserBeamDefOf.MAP_SunCentralLaserEmitter) return true;
            return false;
        }

        // 发射期间的每 tick 检查不包含部件检查；部件由 Job 每 60 tick 单独检查。
        internal bool CanOperate(Pawn actor, bool allowUndrafted = false) => actor.Spawned && actor.Map != null
            && actor.jobs != null && !actor.Dead && !actor.Downed && !actor.InMentalState
            && (actor.Faction != Faction.OfPlayer || actor.Drafted || (allowUndrafted && SupportsFireAtWill))
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
            if (target.HasThing)
            {
                Thing victim = target.Thing;
                if (!(victim is Pawn) && !(victim is Building)) return false;
                if (victim.Destroyed || !victim.Spawned || victim.Map != actor.Map
                    || (victim is Pawn targetPawn && targetPawn.Dead)) return false;
            }
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
            return MakeCastJob(actor, target, actor.Faction == Faction.OfPlayer);
        }

        private static Job MakeCastJob(Pawn actor, LocalTargetInfo target, bool playerForced)
        {
            // A 保留 Pawn/建筑/地块目标，B 独立保存确认目标瞬间的所在格，C 锁定施法者位置。
            Job job = JobMaker.MakeJob(HighEnergyLaserBeamDefOf.MAP_HighEnergyLaserBeam, target, target.Cell, actor.Position);
            job.count = target.Thing is Building ? BuildingTargetMode : target.HasThing ? 1 : 0;
            job.playerForced = playerForced;
            return job;
        }

        internal Job? TryMakeSweepCastJob(IntVec3 start, IntVec3 end)
        {
            if (!(parent is Pawn actor) || actor.Faction != Faction.OfPlayer
                || IsFiring(actor) || DisabledReason(actor) != null
                || !ValidInitialTarget(actor, start) || !ValidInitialTarget(actor, end)) return null;
            // B 继续保存初始落点，横扫模式下 A 保存终点；C 仍锁定施法者位置。
            Job job = JobMaker.MakeJob(HighEnergyLaserBeamDefOf.MAP_HighEnergyLaserBeam, end, start, actor.Position);
            job.count = SweepMode;
            job.playerForced = true;
            return job;
        }

        internal Job? TryMakeAutoFireJob()
        {
            if (!(parent is Pawn actor) || !CanStartAutoFire) return null;
            Thing? target = GetAutoTargetSearcher(actor).FindTarget();
            return target == null ? null : TryMakeAutoFireJob(target);
        }

        // 原版 AI 已选定目标时不重新索敌；自动施放不经过手动命令的征召限制。
        internal Job? TryMakeAutoFireJob(Thing target)
        {
            if (!(parent is Pawn actor) || !CanAutoFireAt(target)) return null;
            // Pawn 沿用追踪模式；建筑保留引用以检查存活，落点仍固定在初始地格。
            // 使用原版 Job 存档字段区分自动施放与玩家手动命令。
            return MakeCastJob(actor, HighEnergyLaserBeamTargetSearcher.CastTarget(target), playerForced: false);
        }

        internal bool CanAutoFireAt(Thing target) => parent is Pawn actor && CanStartAutoFire
            && GetAutoTargetSearcher(actor).IsValidTarget(target);

        private HighEnergyLaserBeamTargetSearcher GetAutoTargetSearcher(Pawn actor) =>
            autoTargetSearcher ??= new HighEnergyLaserBeamTargetSearcher(actor, this);

        internal void NotifyFiringStarted() => readyTick = Find.TickManager.TicksGame + Props.cooldownTicks;

        private static bool IsFiring(Pawn actor) => actor.jobs?.curDriver is JobDriver_HighEnergyLaserBeam driver
            && driver.IsFiring;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!(parent is Pawn actor) || actor.Faction != Faction.OfPlayer) yield break;
            bool firing = IsFiring(actor);
            Command_HighEnergyLaserBeam command = new Command_HighEnergyLaserBeam
            {
                defaultLabel = (firing ? "MAP_HighEnergyLaserBeam.Stop.Label" : "MAP_HighEnergyLaserBeam.Label").Translate(),
                defaultDesc = (firing ? "MAP_HighEnergyLaserBeam.Stop.Description" : "MAP_HighEnergyLaserBeam.Description")
                    .Translate().Resolve(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/MM_HighEnergyLaserBeam"),
                action = firing ? () => StopFiring(actor) : () => BeginTargeting(actor),
                rightClickAction = firing ? () => StopFiring(actor) : () => BeginSweepStartTargeting(actor)
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
                canTargetBuildings = true,
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
                DrawTargetingPreview(actor, target);
            }, target => actor.Map == Find.CurrentMap && ValidInitialTarget(actor, target), actor);
        }

        private void BeginSweepStartTargeting(Pawn actor)
        {
            if (!actor.Spawned || actor.Map == null || actor.Map != Find.CurrentMap) return;
            Map map = actor.Map;
            // 两步均由原版 Targeter 处理左键确认、右键/Esc 取消；未选完不创建 Job。
            Find.Targeter.BeginTargeting(TargetingParameters.ForCell(),
                first => BeginSweepEndTargeting(actor, map, first.Cell),
                target =>
                {
                    if (!IsOnTargetingMap(actor, map)) return;
                    DrawTargetingPreview(actor, target.Cell);
                },
                target => IsOnTargetingMap(actor, map) && ValidInitialTarget(actor, target.Cell), actor);
        }

        private void BeginSweepEndTargeting(Pawn actor, Map map, IntVec3 first)
        {
            if (!IsOnTargetingMap(actor, map) || !ValidInitialTarget(actor, first)) return;
            Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), second =>
            {
                if (!IsOnTargetingMap(actor, map)) return;
                Job? job = TryMakeSweepCastJob(first, second.Cell);
                if (job != null) actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }, target =>
            {
                if (!IsOnTargetingMap(actor, map)) return;
                DrawTargetingPreview(actor, target.Cell);
                GenDraw.DrawTargetHighlight(first);
                if (ValidInitialTarget(actor, first) && ValidInitialTarget(actor, target.Cell))
                    GenDraw.DrawLineBetween(first.ToVector3Shifted(), target.Cell.ToVector3Shifted(),
                        AltitudeLayer.MetaOverlays.AltitudeFor());
            }, target => IsOnTargetingMap(actor, map) && ValidInitialTarget(actor, first)
                && ValidInitialTarget(actor, target.Cell), actor);
        }

        private static bool IsOnTargetingMap(Pawn actor, Map map) => actor.Spawned
            && actor.Map == map && map == Find.CurrentMap;

        private void DrawTargetingPreview(Pawn actor, LocalTargetInfo target)
        {
            GenDraw.DrawRadiusRing(actor.Position, Props.range);
            if (!ValidInitialTarget(actor, target)) return;
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

        private sealed class Command_HighEnergyLaserBeam : Command
        {
            public System.Action action = null!;
            public System.Action rightClickAction = null!;

            public override void ProcessInput(Event ev)
            {
                base.ProcessInput(ev);
                // 没有右键菜单项时，原版 GizmoGridDrawer 会转发右键并在处理后消费事件。
                if (ev.button == 1) rightClickAction();
                else action();
            }
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
