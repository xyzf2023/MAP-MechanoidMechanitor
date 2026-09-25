using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_EnergyPulse : CompProperties
    {
        public int warmupTicks = 180;
        public float radius = 10f;
        public int stunTicks = 300;
        public float propagationSpeed = 0.5f; // 格 / tick，与憎恶毒蜂死亡冲击波一致。
        public int cooldownTicks;
        public EffecterDef? releaseEffecter;
        public float effectReferenceRadius = 30.9f; // 原版憎恶毒蜂冲击波对应的半径。
        public string iconPath = "UI/Commands/MM_EnergyPulse";

        public CompProperties_EnergyPulse() => compClass = typeof(CompEnergyPulse);

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (warmupTicks < 1 || stunTicks < 1 || cooldownTicks < 0)
                yield return "能量脉冲蓄力、眩晕时间必须为正，冷却不得为负。";
            if (!(radius > 0f) || float.IsInfinity(radius)
                || !(propagationSpeed > 0f) || float.IsInfinity(propagationSpeed)
                || !(effectReferenceRadius > 0f) || float.IsInfinity(effectReferenceRadius))
                yield return "能量脉冲半径、传播速度和特效参考半径必须为有限正数。";
            if (iconPath.NullOrEmpty())
                yield return "能量脉冲必须配置按钮贴图路径。";
        }
    }

    public sealed class CompEnergyPulse : ThingComp
    {
        private int readyTick;
        public CompProperties_EnergyPulse Props => (CompProperties_EnergyPulse)props;
        // 动画可读取独立 Job 的阶段与进度，无需接管 Pawn 的主武器。
        public bool IsCharging => (parent as Pawn)?.jobs?.curDriver is JobDriver_EnergyPulse driver
            && !driver.Released;
        public float ChargeProgress => (parent as Pawn)?.jobs?.curDriver is JobDriver_EnergyPulse driver
            ? driver.ChargeProgress : 0f;

        internal bool CanOperate(Pawn actor) => actor.Spawned && actor.Map != null
            && actor.jobs != null && !actor.Dead && !actor.Downed && !actor.InMentalState
            // 玩家控制仍要求征召；机械巢等非玩家施放者不使用征召状态。
            && (actor.Faction != Faction.OfPlayer || actor.Drafted)
            && actor.stances?.stunner?.Stunned != true
            && actor.CurJobDef != MAPMechanitor_JobDefOf.MAP_MechanicalFlightEmergencyLanding;

        internal void NotifyReleased()
        {
            CompSunBossState? state = parent.GetComp<CompSunBossState>();
            readyTick = Find.TickManager.TicksGame + (state?.Stage.PulseCooldown ?? Props.cooldownTicks);
            state?.NotifyPulseReleased();
        }

        /// <summary>生成独立脉冲与声光效果；显式接收地图和位置，允许死亡后释放。</summary>
        internal void ReleaseAt(Map map, IntVec3 position)
        {
            if (!(parent is Pawn actor) || map == null || !position.InBounds(map)) return;
            EnergyPulseWave wave = (EnergyPulseWave)ThingMaker.MakeThing(EnergyPulseDefOf.MAP_EnergyPulseWave);
            wave.Initialize(actor, Props);
            GenSpawn.Spawn(wave, position, map);
            EffecterDef? effecterDef = Props.releaseEffecter
                ?? DefDatabase<EffecterDef>.GetNamedSilentFail("BlastMechBandShockwave");
            Effecter? effecter = effecterDef?.Spawn(position, map, Props.radius / Props.effectReferenceRadius);
            effecter?.Cleanup();
            DefDatabase<SoundDef>.GetNamedSilentFail("Explosion_MechBandShockwave")
                ?.PlayOneShot(new TargetInfo(position, map));
        }

        /// <summary>玩家按钮和非玩家控制器共用的施放入口；不在这里决定 AI 的施放时机。</summary>
        public Job? TryMakeCastJob()
        {
            if (!(parent is Pawn actor) || !CanOperate(actor) || IsCharging
                || (parent.GetComp<CompSunBossState>()?.RallyPulsePending != true
                    && Find.TickManager.TicksGame < readyTick)) return null;
            Job job = JobMaker.MakeJob(EnergyPulseDefOf.MAP_EnergyPulse, actor);
            job.playerForced = actor.Faction == Faction.OfPlayer;
            return job;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!(parent is Pawn actor) || actor.Faction != Faction.OfPlayer || !actor.Drafted) yield break;
            Command_Action command = new Command_Action
            {
                defaultLabel = "MAP_EnergyPulse.Label".Translate(),
                defaultDesc = "MAP_EnergyPulse.Description".Translate(
                    (Props.warmupTicks / 60f).ToString("0.##"), Props.radius.ToString("0.##"),
                    (Props.stunTicks / 60f).ToString("0.##")).Resolve(),
                icon = ContentFinder<Texture2D>.Get(Props.iconPath),
                action = () =>
                {
                    Job? job = TryMakeCastJob();
                    if (job != null) actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }
            };
            if (!CanOperate(actor)) command.Disable("MAP_EnergyPulse.Disabled.Unavailable".Translate());
            else if (IsCharging) command.Disable("MAP_EnergyPulse.Disabled.Charging".Translate());
            else if (Find.TickManager.TicksGame < readyTick)
                command.Disable("MAP_EnergyPulse.Disabled.Cooldown".Translate(
                    ((readyTick - Find.TickManager.TicksGame) / 60f).ToString("0.0")));
            yield return command;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref readyTick, "energyPulseReadyTick");
        }
    }

    [DefOf]
    public static class EnergyPulseDefOf
    {
        public static JobDef MAP_EnergyPulse = null!;
        public static ThingDef MAP_EnergyPulseWave = null!;
        static EnergyPulseDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(EnergyPulseDefOf));
    }
}
