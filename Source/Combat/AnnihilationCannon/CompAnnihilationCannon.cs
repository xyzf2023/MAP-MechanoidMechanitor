using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_AnnihilationCannon : CompProperties
    {
        public int warmupTicks = 600;
        public float innerRadius = 4.9f;
        public float outerRadius = 9.8f;
        public float skipEffectRadius = 10f;
        public int explosionDamage = 1000;
        public float armorPenetration = 15f;
        public float range = 37.8f;
        public float minRange = 5.9f;
        public float projectileSpeed = 75f;
        public int phaseDelayTicks = 3;
        public int cooldownTicks = 360;
        public int effectExpandTicks = 15;
        public int effectHoldTicks = 60;
        public int effectContractTicks = 20;

        public CompProperties_AnnihilationCannon()
        {
            compClass = typeof(CompAnnihilationCannon);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (warmupTicks < 1 || phaseDelayTicks < 1 || cooldownTicks < 0)
                yield return "湮灭炮蓄力、阶段间隔必须为正，冷却不得为负。";
            if (!(innerRadius > 0f) || innerRadius > GenRadial.MaxRadialPatternRadius
                || !(outerRadius > 0f) || outerRadius > GenRadial.MaxRadialPatternRadius)
                yield return "湮灭炮内外圈半径必须在原版径向格子表支持的范围内。";
            if (!(skipEffectRadius > 0f) || float.IsInfinity(skipEffectRadius)
                || !(projectileSpeed > 0f) || float.IsInfinity(projectileSpeed))
                yield return "湮灭炮特效半径和动画速度必须为有限正数。";
            if (!(range > 0f) || range > GenRadial.MaxRadialPatternRadius
                || !(minRange >= 0f) || minRange > range)
                yield return "湮灭炮射程配置无效。";
            if (explosionDamage < 1 || !(armorPenetration >= 0f)
                || float.IsInfinity(armorPenetration))
                yield return "湮灭炮伤害必须为正，穿甲必须为有限非负数。";
            if (effectExpandTicks < 1 || effectHoldTicks < 0 || effectContractTicks < 1)
                yield return "湮灭炮扩张和收缩时间必须为正，维持时间不得为负。";
        }
    }

    /// <summary>真实组件负责能力资格、XML 参数、冷却与玩家选点；不接管主武器。</summary>
    public sealed class CompAnnihilationCannon : ThingComp
    {
        private int readyTick;
        public CompProperties_AnnihilationCannon Props => (CompProperties_AnnihilationCannon)props;

        private bool HasEmitter(Pawn actor)
        {
            if (actor.health?.hediffSet == null) return false;
            foreach (BodyPartRecord part in actor.health.hediffSet.GetNotMissingParts())
                if (part.def == AnnihilationCannonDefOf.MAP_SunAnnihilationCannon) return true;
            return false;
        }

        internal bool CanOperate(Pawn actor) => actor.Spawned && actor.Map != null
            && actor.jobs != null && !actor.Dead && !actor.Downed && !actor.InMentalState && actor.Drafted
            && actor.Faction == Faction.OfPlayer && actor.stances?.stunner?.Stunned != true
            && actor.CurJobDef != MAPMechanitor_JobDefOf.MAP_MechanicalFlightEmergencyLanding
            && HasEmitter(actor);

        internal bool ValidTarget(Pawn actor, IntVec3 cell)
        {
            if (!actor.Spawned || actor.Map == null || !cell.IsValid
                || !cell.InBounds(actor.Map) || cell.Fogged(actor.Map)) return false;
            float distance = (cell - actor.Position).LengthHorizontalSquared;
            return distance <= Props.range * Props.range
                && distance >= Props.minRange * Props.minRange
                && GenSight.LineOfSight(actor.Position, cell, actor.Map);
        }

        internal void NotifyLaunched() => readyTick = Find.TickManager.TicksGame + Props.cooldownTicks;

        internal int WarmupTicksFor(Pawn actor)
        {
            float factor = actor.GetStatValue(StatDefOf.AimingDelayFactor);
            if (float.IsNaN(factor) || float.IsInfinity(factor)) factor = 1f;
            return Mathf.Max(1, Mathf.RoundToInt(Props.warmupTicks * factor));
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref readyTick, "annihilationReadyTick");
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!(parent is Pawn actor) || actor.Faction != Faction.OfPlayer || !actor.Drafted) yield break;
            Command_Action command = new Command_Action
            {
                defaultLabel = "MAP_Annihilation.Label".Translate(),
                // defaultDesc 是 string；TaggedString 的隐式转换会 StripTags，必须显式保留富文本。
                defaultDesc = "MAP_Annihilation.Description".Translate().Resolve(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/MM_AnnihilationCannon"),
                action = () => BeginTargeting(actor)
            };
            if (!HasEmitter(actor)) command.Disable("MAP_Annihilation.EmitterMissing".Translate());
            else if (!CanOperate(actor)) command.Disable("MAP_Annihilation.Unavailable".Translate());
            else if (Find.TickManager.TicksGame < readyTick)
                command.Disable("MAP_Annihilation.Cooldown".Translate(
                    ((readyTick - Find.TickManager.TicksGame) / 60f).ToString("0.0")));
            yield return command;
        }

        private void BeginTargeting(Pawn actor)
        {
            Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), target =>
            {
                if (!CanOperate(actor) || Find.TickManager.TicksGame < readyTick
                    || !ValidTarget(actor, target.Cell)) return;
                Job job = JobMaker.MakeJob(AnnihilationCannonDefOf.MAP_AnnihilationCannon, target.Cell);
                job.playerForced = true;
                actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }, target =>
            {
                if (!actor.Spawned || actor.Map == null || actor.Map != Find.CurrentMap) return;
                GenDraw.DrawRadiusRing(actor.Position, Props.range);
                if (Props.minRange > 0f) GenDraw.DrawRadiusRing(actor.Position, Props.minRange, Color.gray);
                if (target.IsValid && ValidTarget(actor, target.Cell))
                {
                    GenDraw.DrawTargetHighlight(target);
                    GenDraw.DrawRadiusRing(target.Cell, Props.outerRadius, Color.gray);
                    GenDraw.DrawRadiusRing(target.Cell, Props.innerRadius, Color.white);
                }
            }, target => ValidTarget(actor, target.Cell), actor);
        }

    }

    [DefOf]
    public static class AnnihilationCannonDefOf
    {
        public static JobDef MAP_AnnihilationCannon = null!;
        public static BodyPartDef MAP_SunAnnihilationCannon = null!;
        public static ThingDef MAP_AnnihilationShot = null!;
        public static ThingDef MAP_AnnihilationImpact = null!;
        public static ThingDef MAP_AnnihilationExplosion = null!;
        public static ThingDef Mote_MAP_AnnihilationAim = null!;
        public static ThingDef Mote_MAP_AnnihilationCharge = null!;
        public static ThingDef Mote_MAP_AnnihilationTarget = null!;
        static AnnihilationCannonDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(AnnihilationCannonDefOf));
    }
}
