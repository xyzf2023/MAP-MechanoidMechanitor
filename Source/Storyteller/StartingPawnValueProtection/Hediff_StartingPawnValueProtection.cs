using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 隐藏的开局 Pawn 价值保护状态。
    /// Severity 永远只表示“恢复进度”0..1，不直接表示最终价值倍率。
    /// </summary>
    public sealed class Hediff_StartingPawnValueProtection : Hediff
    {
        public override bool ShouldRemove => Severity >= 1f;

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);

            if (delta <= 0 || Severity >= 1f)
            {
                return;
            }

            GameComponent_StartingPawnValueProtectionRuntime? runtime =
                GameComponent_StartingPawnValueProtectionRuntime.Current;
            if (runtime == null || !runtime.ProtectionEnabled || runtime.ProgressPerTick <= 0f)
            {
                return;
            }

            Severity = Mathf.Min(
                1f,
                Severity + delta * runtime.ProgressPerTick);
        }
    }
}
