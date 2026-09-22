using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>隔离视觉异常；此入口本身不初始化材质，不承担伤害或生命周期计时。</summary>
    internal static class AnnihilationHitEffect
    {
        internal static void Draw(Vector3 origin, int seed, AnnihilationSettings settings,
            float ageTicks, ref bool visualFailed)
        {
            if (visualFailed || ageTicks >= settings.VisualDurationTicks) return;
            try
            {
                AnnihilationHitVisuals.Draw(origin, seed, settings, ageTicks);
            }
            catch (Exception ex)
            {
                visualFailed = true;
                Log.ErrorOnce("[MAP] 湮灭炮命中动画绘制失败，结算继续执行：" + ex, 1908263101);
            }
        }

        internal static void StartBlackout(ThingWithComps source, int durationTicks)
        {
            try
            {
                CompAffectsSky sky = source.GetComp<CompAffectsSky>();
                if (sky == null)
                    throw new InvalidOperationException("湮灭炮命中对象缺少天空效果组件。");

                // 动画最短为两 tick；短动画对半分配过渡，避免零时长与超出动画生命周期。
                int duration = Mathf.Max(2, durationTicks);
                int transition = Mathf.Min(8, duration / 2);
                sky.StartFadeInHoldFadeOut(transition, duration - transition * 2, transition);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[MAP] 湮灭炮地图压暗失败，结算继续执行：" + ex, 1908263102);
            }
        }
    }

    /// <summary>实际命中与预览共用原版天空组件，保留原有无阳光效果的颜色与强度。</summary>
    public sealed class CompProperties_AnnihilationSky : CompProperties_AffectsSky
    {
        public CompProperties_AnnihilationSky()
        {
            glow = 0f;
            skyColors = GameCondition_NoSunlight.EclipseSkyColors;
            lightsourceShineSize = 1f;
            lightsourceShineIntensity = 0f;
            lerpDarken = true;
        }
    }
}
