using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>沿用原版 Mote 的 Maintain/Alpha/销毁时序，绘制白金蓄力与收缩环。</summary>
    public sealed class Mote_AnnihilationWarmup : Mote
    {
        private Pawn? caster;
        private Vector3 casterPosition;
        private IntVec3 target;
        private float progress;
        private float chargeSeconds;
        private int part;

        internal void UpdateCharge(Pawn pawn, IntVec3 cell, float chargeProgress, float elapsedSeconds, int visualPart)
        {
            caster = pawn;
            casterPosition = pawn.DrawPos;
            target = cell;
            progress = chargeProgress;
            chargeSeconds = elapsedSeconds;
            part = visualPart;
            exactPosition = part == 2 ? target.ToVector3Shifted() : casterPosition;
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (paused || Find.UIRoot?.HideMotes == true || !Spawned) return;
            // 原版瞄准束淡出时仍跟随有效施法者；死亡/离图后保持最后有效位置。
            if (part == 0 && caster?.Spawned == true && caster.Map == Map)
                casterPosition = caster.DrawPos;
            AnnihilationCannonVisuals.DrawWarmupPart(casterPosition, target, progress, chargeSeconds, part, Mathf.Clamp01(Alpha));
        }
    }
}
