using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 只计算表现高度；不赋予飞行资格、移动能力或近战免疫。
    internal static class PawnHoverUtility
    {
        internal const float DefaultExtraHeight = 2.5f;
        internal const float DefaultBobAmplitude = 0.15f;
        internal const float DefaultBobPeriodTicks = 100f;

        internal static float ExtraHeight(Pawn pawn, float height, float amplitude, float periodTicks)
        {
            float phase = (Find.TickManager.TicksGame + pawn.thingIDNumber % 100)
                / Mathf.Max(1f, periodTicks) * Mathf.PI * 2f;
            return height + Mathf.Sin(phase) * amplitude;
        }
    }
}
