using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>发射时保存参数快照，发射者死亡、离图或修改 Def 不改变在途攻击。</summary>
    public sealed class AnnihilationSettings : IExposable
    {
        public float innerRadius;
        public float outerRadius;
        public float effectRadius;
        public int damage;
        public float armorPenetration;
        public int delayTicks;
        public int expandTicks;
        public int holdTicks;
        public int contractTicks;

        // 计时不依赖图形资源，材质初始化失败也不能阻止落点完成结算和销毁。
        public int VisualDurationTicks => Mathf.Max(1, expandTicks)
            + Mathf.Max(0, holdTicks) + Mathf.Max(1, contractTicks);

        internal float VisualProgress(float ageTicks)
        {
            int expand = Mathf.Max(1, expandTicks);
            int hold = Mathf.Max(0, holdTicks);
            int contract = Mathf.Max(1, contractTicks);
            if (ageTicks < expand)
                return 0.13f * Mathf.Clamp01(ageTicks / expand);
            if (hold > 0 && ageTicks < expand + hold)
                return Mathf.Lerp(0.13f, 0.62f, (ageTicks - expand) / hold);
            return Mathf.Lerp(0.62f, 1f, Mathf.Clamp01((ageTicks - expand - hold) / contract));
        }

        public AnnihilationSettings() { }
        public AnnihilationSettings(CompProperties_AnnihilationCannon props)
        {
            innerRadius = props.innerRadius;
            outerRadius = props.outerRadius;
            effectRadius = props.skipEffectRadius;
            damage = props.explosionDamage;
            armorPenetration = props.armorPenetration;
            delayTicks = Mathf.Max(1, props.phaseDelayTicks);
            expandTicks = Mathf.Max(1, props.effectExpandTicks);
            holdTicks = Mathf.Max(0, props.effectHoldTicks);
            contractTicks = Mathf.Max(1, props.effectContractTicks);
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref innerRadius, "innerRadius", 4.9f);
            Scribe_Values.Look(ref outerRadius, "outerRadius", 9.8f);
            Scribe_Values.Look(ref effectRadius, "effectRadius", 10f);
            Scribe_Values.Look(ref damage, "damage", 1000);
            Scribe_Values.Look(ref armorPenetration, "armorPenetration", 15f);
            Scribe_Values.Look(ref delayTicks, "delayTicks", 3);
            Scribe_Values.Look(ref expandTicks, "expandTicks", 15);
            Scribe_Values.Look(ref holdTicks, "holdTicks", 60);
            Scribe_Values.Look(ref contractTicks, "contractTicks", 20);
        }
    }

    /// <summary>只绘制飞行动画的 Ethereal Thing；不继承 Projectile，不运行碰撞或拦截逻辑。</summary>
    public sealed class AnnihilationShot : Thing
    {
        private Pawn? launcher;
        private Vector3 origin;
        private IntVec3 destination;
        private int flightTicks;
        private int elapsedTicks;
        private bool impacted;
        private AnnihilationSettings settings = null!;

        public void Initialize(Pawn actor, IntVec3 target, CompProperties_AnnihilationCannon props)
        {
            launcher = actor;
            destination = target;
            Vector3 direction = (target.ToVector3Shifted() - actor.DrawPos).Yto0().normalized;
            origin = actor.DrawPos + direction * 1.07f;
            flightTicks = Mathf.Max(1, Mathf.CeilToInt(
                (target.ToVector3Shifted() - origin).Yto0().magnitude * 60f / props.projectileSpeed));
            settings = new AnnihilationSettings(props);
        }

        public override Vector3 DrawPos => Vector3.Lerp(origin, destination.ToVector3Shifted(),
            Mathf.Clamp01((float)elapsedTicks / Mathf.Max(1, flightTicks))).WithY(def.altitudeLayer.AltitudeFor());

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (impacted) return;
            Vector3 direction = (destination.ToVector3Shifted() - origin).Yto0();
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(DrawPos,
                Quaternion.LookRotation(direction.sqrMagnitude > 0.001f ? direction : Vector3.forward),
                new Vector3(def.graphicData.drawSize.x, 1f, def.graphicData.drawSize.y)), Graphic.MatSingle, 0);
        }

        protected override void Tick()
        {
            if (!Spawned || impacted) return;
            elapsedTicks++;
            if (elapsedTicks < flightTicks)
            {
                IntVec3 current = DrawPos.ToIntVec3();
                if (current.InBounds(Map)) Position = current;
                return;
            }
            impacted = true;
            Map map = Map;
            // 先移除动画，再启动落点结算。控制实体独立于发射者的 Job 生命周期。
            Destroy(DestroyMode.Vanish);
            if (!destination.InBounds(map) || settings == null) return;
            AnnihilationImpact impact = (AnnihilationImpact)ThingMaker.MakeThing(AnnihilationCannonDefOf.MAP_AnnihilationImpact);
            impact.Initialize(launcher, settings);
            GenSpawn.Spawn(impact, destination, map);
            impact.Begin();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref launcher, "launcher");
            Scribe_Values.Look(ref origin, "origin");
            Scribe_Values.Look(ref destination, "destination");
            Scribe_Values.Look(ref flightTicks, "flightTicks", 1);
            Scribe_Values.Look(ref elapsedTicks, "elapsedTicks");
            Scribe_Values.Look(ref impacted, "impacted");
            Scribe_Deep.Look(ref settings, "settings");
        }
    }
}
