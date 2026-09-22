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

        internal static void StartBlackout(Map map, int durationTicks)
        {
            try
            {
                GameConditionDef def = AnnihilationCannonDefOf.MAP_AnnihilationBlackout;
                GameCondition? active = map.gameConditionManager.GetActiveCondition(def);
                if (active != null && !active.Expired)
                {
                    active.TicksLeft = Mathf.Max(active.TicksLeft, durationTicks);
                    return;
                }
                map.gameConditionManager.RegisterCondition(
                    GameConditionMaker.MakeCondition(def, durationTicks));
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[MAP] 湮灭炮地图压暗失败，结算继续执行：" + ex, 1908263102);
            }
        }
    }

    /// <summary>随命中动画短暂压暗地图，前后过渡均包含在总时长内。</summary>
    public sealed class GameCondition_AnnihilationBlackout : GameCondition_NoSunlight
    {
        public override int TransitionTicks => 8;
    }
}
