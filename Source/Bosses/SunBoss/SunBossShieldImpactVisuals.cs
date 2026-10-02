using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MAP_MechanoidMechanitor
{
    /// <summary>太阳阶段减伤的瞬时表现；只复用护盾素材，不参与伤害和能量结算。</summary>
    [StaticConstructorOnStartup]
    internal sealed class SunBossShieldImpactVisuals
    {
        private const int DurationTicks = 18;
        private const int JitterTicks = 8;
        private const int BurstIntervalTicks = 6;
        private int lastImpactTick = -9999;
        private int lastBurstTick = -9999;
        private Vector3 impactDirection;
        private MaterialPropertyBlock? properties;
        private static readonly Material bubbleMaterial =
            MaterialPool.MatFrom("Other/ShieldBubble", ShaderDatabase.Transparent);

        private static float DrawSize(Pawn pawn)
        {
            Vector2 size = pawn.ageTracker?.CurKindLifeStage?.bodyGraphicData?.drawSize
                ?? new Vector2(3f, 3f);
            return Mathf.Max(1.5f, Mathf.Max(size.x, size.y) * 1.15f);
        }

        internal void Reset()
        {
            lastImpactTick = lastBurstTick = -9999;
            impactDirection = Vector3.zero;
        }

        internal void Notify(Pawn pawn, DamageInfo damage, float preventedDamage)
        {
            if (!pawn.Spawned || pawn.Dead || pawn.Destroyed || pawn.Map == null
                || Scribe.mode != LoadSaveMode.Inactive
                || !(preventedDamage > 0f) || float.IsInfinity(preventedDamage)) return;
            int tick = Find.TickManager.TicksGame;
            lastImpactTick = tick;
            // DamageInfo 会把未指定角度随机化，无来源时不采用该随机方向。
            impactDirection = damage.Instigator != null && damage.Angle >= 0f
                && !float.IsNaN(damage.Angle) && !float.IsInfinity(damage.Angle)
                ? Vector3Utility.HorizontalVectorFromAngle(damage.Angle) : Vector3.zero;
            if (tick - lastBurstTick < BurstIntervalTicks) return;
            lastBurstTick = tick;
            Vector3 loc = pawn.Drawer.DrawPos - impactDirection * (DrawSize(pawn) * 0.35f);
            // 纯表现随机数不改变后续护甲、伤口传播等战斗随机结果。
            Rand.PushState();
            try
            {
                SoundDefOf.EnergyShield_AbsorbDamage.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
                float scale = Mathf.Clamp(1.5f + preventedDamage / 20f, 1.5f, 4f);
                FleckMaker.Static(loc, pawn.Map, FleckDefOf.ExplosionFlash, scale);
                for (int i = 0; i < 2; i++)
                    FleckMaker.ThrowDustPuff(loc, pawn.Map, Rand.Range(0.6f, 0.9f));
            }
            finally { Rand.PopState(); }
        }

        internal void Draw(Pawn pawn)
        {
            if (!pawn.Spawned || pawn.Dead || pawn.Destroyed) return;
            int elapsed = Find.TickManager.TicksGame - lastImpactTick;
            if (elapsed < 0 || elapsed >= DurationTicks) return;
            properties ??= new MaterialPropertyBlock();
            float jitter = Mathf.Clamp01(1f - elapsed / (float)JitterTicks) * 0.05f;
            float size = DrawSize(pawn) - jitter;
            Vector3 center = pawn.Drawer.DrawPos + impactDirection * jitter;
            center.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            properties.Clear();
            properties.SetColor(ShaderPropertyIDs.Color,
                new Color(1f, 1f, 1f, 1f - elapsed / (float)DurationTicks));
            Graphics.DrawMesh(MeshPool.plane10,
                Matrix4x4.TRS(center, Quaternion.identity, new Vector3(size, 1f, size)),
                bubbleMaterial, 0, null, 0, properties);
        }
    }
}
