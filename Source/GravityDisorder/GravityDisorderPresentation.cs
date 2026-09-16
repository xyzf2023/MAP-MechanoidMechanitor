using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class GravityDisorderPresentation
    {
        // 原版预绘制可能并行执行，不能用全局 bool 临时伪装所有 Pawn 的姿态。
        [ThreadStatic] internal static Pawn? RenderingPawn;
        [ThreadStatic] private static Pawn? renderingSubject;

        internal readonly struct RenderContext
        {
            internal readonly Pawn? Pawn;
            internal readonly Pawn? Subject;

            internal RenderContext(Pawn? pawn, Pawn? subject)
            {
                Pawn = pawn;
                Subject = subject;
            }
        }

        internal static RenderContext EnterRender(Pawn? pawn)
        {
            var previous = new RenderContext(RenderingPawn, renderingSubject);
            Pawn? subject = ShouldDraw(pawn) ? pawn : null;
            // 合体外观绘制的是未 Spawn 的源机械族，控制状态属于外层的人类主体。
            if (subject == null && pawn != null && renderingSubject != null
                && MechFusionRenderUtility.IsRenderingSourcePawn(pawn))
                subject = renderingSubject;
            renderingSubject = subject;
            RenderingPawn = subject != null ? pawn : null;
            return previous;
        }

        internal static void RestoreRender(RenderContext previous)
        {
            RenderingPawn = previous.Pawn;
            renderingSubject = previous.Subject;
        }

        internal static bool ShouldDraw(Pawn? pawn)
        {
            if (pawn?.Spawned != true || pawn.Map == null
                || !GravityDisorderUtility.IsAffected(pawn))
                return false;
            // 合体的地图外隐藏帧由原系统管理，不能把隐藏 Pawn 拉回地图。
            return !GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                || record?.Purpose != MechanicalFlightPurpose.FusionRelocation;
        }

        internal static bool InRenderContext(Pawn pawn) => ReferenceEquals(RenderingPawn, pawn);

        internal static float Height(Pawn pawn) =>
            MechanicalFlightPresentationUtility.VanillaFlightDrawOffset
            + PawnHoverUtility.ExtraHeight(pawn, PawnHoverUtility.DefaultExtraHeight,
                PawnHoverUtility.DefaultBobAmplitude, PawnHoverUtility.DefaultBobPeriodTicks);

        internal static float BodyAngle(Pawn pawn)
        {
            pawn = renderingSubject ?? pawn;
            float baseAngle = pawn.thingIDNumber % 2 == 0 ? 70f : 290f;
            return PawnHoverUtility.ExtraHeight(pawn, baseAngle, 5f, 200f);
        }

        internal static bool TryApplyDrawOffset(Pawn pawn, ref Vector3 drawPos)
        {
            if (!ShouldDraw(pawn))
                return false;

            float vanillaFactor = pawn.flight?.PositionOffsetFactor ?? 0f;
            drawPos.z -= MechanicalFlightPresentationUtility.VanillaFlightDrawOffset * vanillaFactor;
            if (MechanicalFlightStraightPathPatch.TryGetExactGroundDrawPos(pawn, out Vector3 exact))
            {
                drawPos.x = exact.x;
                drawPos.z = exact.z;
            }

            if (MechanicalFlightGroundAnchorContext.Active)
                return true;
            if (MechanicalFlightGroundAnchorContext.ShadowCompensationActive)
            {
                // 原版飞行阴影随后会再减 PositionOffsetFactor。
                drawPos.z += vanillaFactor;
                return true;
            }

            // 鼠标获取目标也使用这一坐标，不沿用旧飞行的地面选取偏移。
            // 迫降可以搬移地面锚点，但本 Hediff 存在时仍由重力场托举身体。
            drawPos.z += Height(pawn);
            drawPos.y += 0.03658537f * (1f - vanillaFactor);
            return true;
        }

        internal static Vector3 GroundAnchor(Pawn pawn)
        {
            MechanicalFlightGroundAnchorContext.Begin();
            try
            {
                return pawn.DrawPos;
            }
            finally
            {
                MechanicalFlightGroundAnchorContext.End();
            }
        }

        internal static void DrawShadow(PawnRenderer renderer, Pawn pawn)
        {
            if (!ShouldDraw(pawn) || pawn.IsHiddenFromPlayer()
                || InvisibilityUtility.GetAlpha(pawn) <= 0f)
                return;
            Vector3 anchor = GroundAnchor(pawn);
            anchor.y = AltitudeLayer.Filth.AltitudeFor();
            Matrix4x4 matrix = Matrix4x4.TRS(anchor, Quaternion.identity, Vector3.one * 0.75f);
            Graphics.DrawMesh(MeshPool.plane10, matrix, renderer.FlightShadowMaterial, 0);
        }
    }
}
