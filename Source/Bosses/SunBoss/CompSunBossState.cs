using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_SunBossState : CompProperties
    {
        public float maxStructure = 8000f;
        public float coolingFailureDamagePerSecond = 6f;
        public float unstableDamagePerSecond = 48f;
        public float criticalDamagePerSecond = 240f;
        public CompProperties_SunBossState() => compClass = typeof(CompSunBossState);

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (!(maxStructure > 0f) || float.IsInfinity(maxStructure)
                || !(coolingFailureDamagePerSecond > 0f) || float.IsInfinity(coolingFailureDamagePerSecond)
                || !(unstableDamagePerSecond > 0f) || float.IsInfinity(unstableDamagePerSecond)
                || !(criticalDamagePerSecond > unstableDamagePerSecond) || float.IsInfinity(criticalDamagePerSecond))
                yield return "太阳结构值、自损速率必须为有限正数，临界失稳必须强于失稳。";
        }
    }

    /// <summary>不可变阶段配置；数组下标为剩余稳定器数量，不累计阶段效果。</summary>
    internal sealed class SunBossStage
    {
        public readonly string LabelKey;
        public readonly float DamageFactor, HeatRadius;
        public readonly int CannonCooldown, PulseCooldown;
        private SunBossStage(string label, float damage, int cannonSeconds, int pulseSeconds, float radius)
        {
            LabelKey = label;
            DamageFactor = damage;
            CannonCooldown = cannonSeconds * 60;
            PulseCooldown = pulseSeconds * 60;
            HeatRadius = radius;
        }

        private static readonly SunBossStage[] Stages =
        {
            new SunBossStage("MAP_SunBoss_StructuralCollapse", 4f, 20, 40, 7f),
            new SunBossStage("MAP_SunBoss_CoreInstability", 2f, 20, 40, 5f),
            new SunBossStage("MAP_SunBoss_SteadyStateCritical", 1f, 20, 40, 3.5f),
            new SunBossStage("MAP_SunBoss_FeedbackDetuning", 0.75f, 20, 35, 3.5f),
            new SunBossStage("MAP_SunBoss_LoadFluctuation", 0.5f, 20, 30, 3.5f),
            new SunBossStage("MAP_SunBoss_SteadyStateDisturbed", 0.3f, 15, 30, 3.5f),
            new SunBossStage("MAP_SunBoss_FullSteadyState", 0f, 10, 25, 3.5f)
        };
        public static SunBossStage For(int count) => Stages[Mathf.Clamp(count, 0, 6)];
    }

    /// <summary>只挂在 BOSS Def 上；结构值独立于伤口，状态查询不初始化或修改健康状态。</summary>
    public sealed class CompSunBossState : ThingComp
    {
        internal const int BurnInterval = 20;
        private const int OpeningCannonDelayTicks = 10 * 60;
        private bool initialized;
        private bool deathLetterSent;
        private bool deathEffectsReleased;
        private bool openingPulsePending = true;
        private int openingCannonReadyTick;
        internal bool OpeningPulsePending => openingPulsePending;
        private bool rallyPulsePending;
        internal bool RallyPulsePending => rallyPulsePending;
        internal void RequestRallyPulse() => rallyPulsePending = true;
        internal int OpeningCannonDelayRemaining =>
            Mathf.Max(0, openingCannonReadyTick - Find.TickManager.TicksGame);
        internal void NotifyPulseReleased()
        {
            if (openingPulsePending)
                openingCannonReadyTick = Find.TickManager.TicksGame + OpeningCannonDelayTicks;
            openingPulsePending = false;
            rallyPulsePending = false;
        }
        private float structure;
        private bool coolingFailed;
        private int burnTicks;
        private int lastStabilizers = 6;
        private IntVec3 activationCell = IntVec3.Invalid;
        // 瞬时表现不存档；重新生成到地图时清除，避免离图后补播。
        private readonly SunBossShieldImpactVisuals shieldImpact = new SunBossShieldImpactVisuals();
        public CompProperties_SunBossState Props => (CompProperties_SunBossState)props;
        internal Pawn Boss => (Pawn)parent;
        public float Structure => initialized ? structure : Props.maxStructure;
        public float MaxStructure => Props.maxStructure;
        internal IntVec3 ActivationCell => activationCell;
        internal bool CoolingFailed => coolingFailed;
        internal int Stabilizers => Boss.Spawned
            ? Boss.Map.GetComponent<MapComponent_SunBossArena>().RemainingStabilizers
            : lastStabilizers;
        internal SunBossStage Stage => SunBossStage.For(Stabilizers);
        internal bool ProtectCore => SunBossDamageContext.Find(Boss)?.ProtectCore ?? (Structure > 0f);

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            shieldImpact.Reset();
            EnsureInitialized();
            if (!activationCell.IsValid)
            {
                MapComponent_SunBossArena arena = Boss.Map.GetComponent<MapComponent_SunBossArena>();
                activationCell = arena.Generated ? arena.ArenaBounds.CenterCell : Boss.Position;
            }
            DetectCoolingFailure();
            // 读档时等地图与健康状态完成恢复，首次正常 tick 再补齐标识。
            if (!respawningAfterLoad) SyncStageHediff();
        }

        private void SyncStageHediff()
        {
            if (Boss.health?.hediffSet == null) return;
            HediffDef def = SunBossDefOf.MAP_SunBossStage;
            Hediff? marker = null;
            foreach (Hediff hediff in Boss.health.hediffSet.hediffs.ToList())
            {
                if (hediff.def != def) continue;
                if (marker == null) marker = hediff;
                else Boss.health.RemoveHediff(hediff);
            }
            if (marker == null) marker = Boss.health.AddHediff(def);
            marker.Severity = Mathf.Clamp(Stabilizers, 0, 6) + 1;
        }

        private void EnsureInitialized()
        {
            if (initialized) return;
            structure = Props.maxStructure;
            initialized = true;
        }

        internal void NotifyShieldImpact(DamageInfo damage, float preventedDamage)
            => shieldImpact.Notify(Boss, damage, preventedDamage);

        public override void PostDraw()
        {
            base.PostDraw();
            if (Stage.DamageFactor < 1f) shieldImpact.Draw(Boss);
        }

        internal void ConsumeStructure(float amount)
        {
            EnsureInitialized();
            structure = Mathf.Max(0f, structure - amount);
        }

        internal static bool IsCore(BodyPartRecord part) => part.IsCorePart
            || part.def == SunBossDefOf.MAP_SunCoreProcessor
            || part.def == SunBossDefOf.MAP_SunReactor
            || part.def == SunBossDefOf.MAP_SunFluidFilter;

        internal bool Protects(BodyPartRecord part)
        {
            SunBossDamageContext? context = SunBossDamageContext.Find(Boss);
            int count = context?.Stabilizers ?? Stabilizers;
            return (ProtectCore && IsCore(part))
                || (count >= 1 && part.def == AnnihilationCannonDefOf.MAP_SunAnnihilationCannon)
                || (count >= 4 && part.def == HighEnergyLaserBeamDefOf.MAP_SunCentralLaserEmitter);
        }

        internal void DetectCoolingFailure()
        {
            if (coolingFailed || Boss.health?.hediffSet == null) return;
            BodyPartRecord? cooling = Boss.RaceProps.body.AllParts
                .FirstOrDefault(p => p.def == SunBossDefOf.MAP_SunCoolingSystem);
            // 只检查母部件；子部件缺失不触发，触发后永不撤销。
            if (cooling != null && Boss.health.hediffSet.PartIsMissing(cooling)) coolingFailed = true;
        }

        public override void CompTick()
        {
            if (!Boss.Spawned || Boss.Dead || Boss.Destroyed || Boss.Suspended) return;
            EnsureInitialized();
            int count = Stabilizers;
            if (SunBossStage.For(count).HeatRadius > SunBossStage.For(lastStabilizers).HeatRadius)
                Messages.Message("MAP_SunBoss_HeatExpanded".Translate(Stage.HeatRadius), Boss,
                    MessageTypeDefOf.ThreatBig);
            if (count != lastStabilizers || Boss.IsHashIntervalTick(30)) SyncStageHediff();
            lastStabilizers = count;
            DetectCoolingFailure();
            float perSecond = (coolingFailed ? Props.coolingFailureDamagePerSecond : 0f)
                + (count == 0 ? Props.criticalDamagePerSecond : count == 1 ? Props.unstableDamagePerSecond : 0f);
            if (perSecond <= 0f) { burnTicks = 0; return; }
            if (++burnTicks < BurnInterval) return;
            burnTicks = 0;
            BodyPartRecord? part = Structure <= 0f ? Boss.RaceProps.body.corePart
                : SunBossDamageContext.RandomPart(Boss, _ => true);
            if (part != null)
                SunBossDamageContext.ApplyInternalBurn(this, perSecond * BurnInterval / 60f, part);
        }

        public override void Notify_Killed(Map prevMap, DamageInfo? dinfo = null)
        {
            base.Notify_Killed(prevMap, dinfo);
            if (!Boss.Dead) return;
            if (!deathEffectsReleased)
            {
                // 先标记防重；离图死亡不补播，复活再击杀也不重复释放。
                deathEffectsReleased = true;
                CompEnergyPulse? pulse = Boss.GetComp<CompEnergyPulse>();
                // 死亡通知到达时 Pawn 已离图，优先使用尸体持有位置。
                IntVec3 position = Boss.PositionHeld;
                if (!position.IsValid) position = Boss.Position;
                if (prevMap != null && position.InBounds(prevMap) && pulse != null)
                {
                    // 沿用原版 DamageWorker 爆炸中心扬尘的数量、分布和尺寸比例。
                    for (int i = 0; i < 4; i++)
                        FleckMaker.ThrowSmoke(position.ToVector3Shifted()
                            + Gen.RandomHorizontalVector(pulse.Props.radius * 0.7f),
                            prevMap, pulse.Props.radius * 0.6f);
                    pulse.ReleaseAt(prevMap, position);
                }
            }
            if (deathLetterSent) return;
            GameComponent_SunBossNotifications? notifications =
                CurrentGameComponentCache<GameComponent_SunBossNotifications>.Get();
            if (notifications == null) return;
            // 每个 BOSS 实例只发送一次，复活再击杀和读档均不重复发信。
            deathLetterSent = true;
            notifications.NotifyDefeated(Boss);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref initialized, "sunStructureInitialized");
            Scribe_Values.Look(ref deathLetterSent, "sunDeathLetterSent");
            // 旧档中已发送陨落信件的实例视为已处理，避免复活后补播。
            Scribe_Values.Look(ref deathEffectsReleased, "sunDeathEffectsReleased", deathLetterSent);
            // 旧存档中的既有 BOSS 不补播开场；新生成的实例默认等待第一次实际释放。
            Scribe_Values.Look(ref openingPulsePending, "sunOpeningPulsePending", false);
            Scribe_Values.Look(ref rallyPulsePending, "sunRallyPulsePending");
            // 旧存档缺少该字段时不额外插入开场等待。
            Scribe_Values.Look(ref openingCannonReadyTick, "sunOpeningCannonReadyTick");
            Scribe_Values.Look(ref structure, "sunStructure");
            Scribe_Values.Look(ref coolingFailed, "sunCoolingFailed");
            Scribe_Values.Look(ref burnTicks, "sunBurnTicks");
            Scribe_Values.Look(ref lastStabilizers, "sunLastStabilizers", 6);
            Scribe_Values.Look(ref activationCell, "sunActivationCell", IntVec3.Invalid);
        }
    }

    [DefOf]
    public static class SunBossDefOf
    {
        public static BodyPartDef MAP_SunCoreProcessor = null!;
        public static HediffDef MAP_SunBossStage = null!;
        public static BodyPartDef MAP_SunReactor = null!;
        public static BodyPartDef MAP_SunFluidFilter = null!;
        public static BodyPartDef MAP_SunCoolingSystem = null!;
        public static ThingDef MAP_Building_ReactorStabilizer = null!;
        static SunBossDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(SunBossDefOf));
    }
}
