using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>沿用原版 Mote 的 Maintain/Alpha/销毁时序，绘制蓝白蓄力与收缩环。</summary>
    public sealed class Mote_AnnihilationWarmup : Mote
    {
        private Pawn? caster;
        private Vector3 emitterPosition;
        private IntVec3 target;
        private float progress;
        private float chargeSeconds;
        private int part;

        internal void UpdateCharge(Pawn pawn, IntVec3 cell, float chargeProgress, float elapsedSeconds, int visualPart)
        {
            caster = pawn;
            emitterPosition = SunDrawUtility.BreathingLightPosition(pawn.DrawPos);
            target = cell;
            progress = chargeProgress;
            chargeSeconds = elapsedSeconds;
            part = visualPart;
            exactPosition = part == 2 ? target.ToVector3Shifted() : emitterPosition;
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (paused || Find.UIRoot?.HideMotes == true || !Spawned) return;
            // 瞄准束与聚能每次绘制都跟随呼吸灯，包含悬浮与受击晃动；
            // 施法者死亡/离图后保留最后有效位置，目标环仍固定在落点。
            if (part != 2 && caster?.Spawned == true && !caster.Dead && caster.Map == Map)
            {
                emitterPosition = SunDrawUtility.BreathingLightPosition(caster.DrawPos);
                exactPosition = emitterPosition;
            }
            AnnihilationCannonVisuals.DrawWarmupPart(emitterPosition, target, progress, chargeSeconds, part, Mathf.Clamp01(Alpha));
        }
    }
}
