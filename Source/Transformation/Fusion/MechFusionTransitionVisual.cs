using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 由动画 Job 独占的运行期视觉句柄。目标弱引用索引仅用于地图朝向查询，无存档字段，
    /// 取消时只清理自己的 Mote，不接触合体会话或机械体的真实形态。
    /// </summary>
    internal sealed class MechFusionTransitionVisual
    {
        internal const int DurationTicks = 48;

        private static readonly ConditionalWeakTable<Pawn, HashSet<MechFusionTransitionVisual>>
            MergeTargetVisuals = new ConditionalWeakTable<Pawn, HashSet<MechFusionTransitionVisual>>();

        private readonly Game game;
        private readonly Pawn actor;
        private readonly Job owner;
        private readonly Map map;
        private readonly IntVec3 position;
        private readonly int startTick;
        private readonly Mote mote;
        private readonly Pawn? mergeTarget;
        private bool ended;

        private MechFusionTransitionVisual(Pawn actor, Job owner, Mote mote, Pawn anchor)
        {
            game = Current.Game;
            this.actor = actor;
            this.owner = owner;
            map = actor.Map;
            position = actor.Position;
            startTick = Find.TickManager.TicksGame;
            this.mote = mote;
            if (!ReferenceEquals(anchor, actor))
            {
                mergeTarget = anchor;
                MergeTargetVisuals.GetValue(anchor, _ => new HashSet<MechFusionTransitionVisual>())
                    .Add(this);
            }
        }

        internal static bool ShouldDrawMergeTargetSouth(Pawn pawn)
        {
            if (!pawn.Spawned || !MergeTargetVisuals.TryGetValue(pawn, out var visuals))
            {
                return false;
            }

            foreach (MechFusionTransitionVisual visual in visuals)
            {
                if (visual.IsVisible && pawn.Map == visual.map)
                {
                    return true;
                }
            }

            return false;
        }

        internal bool IsVisible => !ended
            && ReferenceEquals(Current.Game, game)
            && ReferenceEquals(actor.CurJob, owner)
            && actor.Spawned && !actor.Dead && !actor.Downed
            && !MechanicalFlightUtility.IsAirborne(actor)
            && actor.Map == map && actor.Position == position
            && mote.Spawned && !mote.Destroyed && mote.Map == map
            && Find.TickManager.TicksGame >= startTick
            && Find.TickManager.TicksGame < startTick + DurationTicks
            && Find.UIRoot?.HideMotes != true;

        internal static MechFusionTransitionVisual? TryCreate(
            Pawn actor, Job owner, ThingDef? moteDef, Pawn? visualTarget = null)
        {
            Pawn anchor = visualTarget ?? actor;
            if (Current.Game == null || actor.Spawned != true || actor.Map == null
                || !anchor.Spawned || anchor.Map != actor.Map
                || moteDef == null || Find.UIRoot?.HideMotes == true
                || MechanicalFlightUtility.IsAirborne(actor))
            {
                return null;
            }

            try
            {
                // 屏外也生成，避免镜头移入时缺失替身；数量饱和仍可返回 null。
                // 合体虚影与目标重叠；隐藏和清理仍属于 actor，不能随绘制位置改绑。
                // 实际高度由 Mote Def 的 BuildingOnTop 层决定，低于人类 Pawn 层。
                Mote? mote = MoteMaker.MakeStaticMote(
                    anchor.DrawPos, actor.Map, moteDef, 1f, makeOffscreen: true);
                return mote == null ? null : new MechFusionTransitionVisual(actor, owner, mote, anchor);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[MAP-机械族机械师] 展开过渡创建失败，回退到折跃表现：" + ex,
                    2147045620);
                return null;
            }
        }

        internal void End()
        {
            if (ended)
            {
                return;
            }

            ended = true;
            if (mergeTarget != null && MergeTargetVisuals.TryGetValue(mergeTarget, out var visuals))
            {
                visuals.Remove(this);
                if (visuals.Count == 0)
                {
                    MergeTargetVisuals.Remove(mergeTarget);
                }
            }

            try
            {
                if (!mote.Destroyed)
                {
                    mote.Destroy(DestroyMode.Vanish);
                }
            }
            catch (Exception ex)
            {
                // 表现清理失败不能阻断强制解除；Mote 自身仍有有限寿命。
                Log.ErrorOnce("[MAP-机械族机械师] 展开过渡 Mote 清理失败：" + ex,
                    2147045621);
            }

            MechFusionRenderUtility.RefreshTransitionGraphics(actor);
        }
    }
}
