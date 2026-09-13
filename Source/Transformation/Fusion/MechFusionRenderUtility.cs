using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体渲染替换工具。Pawn 的逻辑实体始终是目标人类；这里只负责
    /// 在人类位置绘制被收纳的真实源机械族，并只保留人类主武器显示。
    /// source 渲染期间使用 ThreadStatic 上下文标记，抑制 source 自身武器与
    /// 服装附加效果；上下文在 finally 中必定清理。使用重入 guard，
    /// 防止绘制源机械族时再次进入合体替换。
    /// </summary>
    internal static class MechFusionRenderUtility
    {
        [ThreadStatic]
        private static bool renderingSource;

        [ThreadStatic]
        private static Pawn? renderingSourcePawn;

        internal static bool IsRenderingSource => renderingSource;

        internal static bool TryGetFusionSource(
            Pawn? wearer,
            out Pawn? source)
        {
            source = null;
            if (wearer == null || renderingSource)
            {
                return false;
            }

            if (!GameComponent_MechFusionSessionRegistry.TryGetSessionForWearer(
                    wearer,
                    out MechFusionSession? session)
                || session == null
                || !session.IsActive)
            {
                return false;
            }

            source = session.SourcePawn;
            return source != null && !source.Destroyed && !source.Discarded;
        }

        internal static bool IsActiveFusionWearer(Pawn? pawn)
        {
            return TryGetFusionSource(pawn, out _);
        }

        /// <summary>
        /// 当前是否正在把该 Pawn 作为“合体外观的源机械族”绘制。
        /// </summary>
        internal static bool IsRenderingSourcePawn(Pawn? pawn)
        {
            return renderingSource
                && pawn != null
                && ReferenceEquals(renderingSourcePawn, pawn);
        }

        internal static bool TryRenderSourceAt(
            Pawn wearer,
            Vector3 drawLoc,
            Rot4 rotation,
            bool neverAimWeapon)
        {
            if (!TryGetFusionSource(wearer, out Pawn? source)
                || source == null
                || renderingSource)
            {
                return false;
            }

            renderingSource = true;
            renderingSourcePawn = source;
            try
            {
                PawnRenderer? renderer = source.Drawer?.renderer;
                if (renderer == null)
                {
                    return false;
                }

                renderer.EnsureGraphicsInitialized();
                renderer.RenderPawnAt(drawLoc, rotation, neverAimWeapon);
                return true;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 合体源机械族渲染失败：" + ex,
                    source.thingIDNumber ^ 0x51F2A3B);
                return false;
            }
            finally
            {
                renderingSource = false;
                renderingSourcePawn = null;
            }
        }

        /// <summary>
        /// 只额外绘制人类当前主武器，不绘制任何服装的 DrawWornExtras，
        /// 也不绘制固定合体外甲。武器瞄准角度仍以人类真实 DrawPos、姿态和
        /// 目标计算，绘制位置以合体机械族当前 drawLoc 为基础。
        /// </summary>
        internal static void DrawWearerWeaponOnly(
            Pawn wearer,
            Vector3 drawLoc,
            Rot4 facing,
            bool neverAimWeapon)
        {
            if (wearer == null)
            {
                return;
            }

            ThingWithComps? weapon = wearer.equipment?.Primary;
            if (weapon == null)
            {
                return;
            }

            Job? curJob = wearer.CurJob;
            if (curJob != null && curJob.def?.neverShowWeapon == false)
            {
                Stance_Busy? stanceBusy = wearer.stances?.curStance as Stance_Busy;
                float equipmentDrawDistanceFactor = wearer.ageTracker
                    .CurLifeStage.equipmentDrawDistanceFactor;
                float aimAngle = 0f;
                if (!neverAimWeapon
                    && stanceBusy != null
                    && !stanceBusy.neverAimWeapon
                    && stanceBusy.focusTarg.IsValid)
                {
                    Thing? focusThing = stanceBusy.focusTarg.Thing;
                    Vector3 focus = stanceBusy.focusTarg.HasThing
                        && focusThing != null
                        ? focusThing.DrawPos
                        : stanceBusy.focusTarg.Cell.ToVector3Shifted();
                    if ((focus - wearer.DrawPos).MagnitudeHorizontalSquared()
                        > 0.001f)
                    {
                        aimAngle = (focus - wearer.DrawPos).AngleFlat();
                    }

                    Verb currentEffectiveVerb = wearer.CurrentEffectiveVerb;
                    if (currentEffectiveVerb != null
                        && currentEffectiveVerb.AimAngleOverride.HasValue)
                    {
                        aimAngle = currentEffectiveVerb.AimAngleOverride.Value;
                    }

                    Vector3 aimDrawLoc = drawLoc
                        + new Vector3(
                            0f,
                            0f,
                            0.4f + weapon.def.equippedDistanceOffset)
                            .RotatedBy(aimAngle)
                            * equipmentDrawDistanceFactor;
                    PawnRenderUtility.DrawEquipmentAiming(
                        weapon,
                        aimDrawLoc,
                        aimAngle);
                }
                else if (PawnRenderUtility.CarryWeaponOpenly(wearer))
                {
                    Vector3 carriedDrawLoc = drawLoc.WithYOffset(
                        PawnRenderUtility.AltitudeForLayer(
                            facing == Rot4.North ? -10f : 90f));
                    PawnRenderUtility.DrawCarriedWeapon(
                        weapon,
                        carriedDrawLoc,
                        facing,
                        equipmentDrawDistanceFactor);
                }
            }
        }

        internal static bool TryDrawSourceSilhouette(
            Pawn wearer,
            Matrix4x4 trs)
        {
            if (renderingSource)
            {
                // 已经在绘制源机械族：不再替换，避免递归。
                return false;
            }

            if (!TryGetFusionSource(wearer, out Pawn? source) || source == null)
            {
                return false;
            }

            PawnRenderer? renderer = source.Drawer?.renderer;
            if (renderer == null)
            {
                return true;
            }

            renderer.EnsureGraphicsInitialized();
            if (renderer.SilhouetteGraphic == null)
            {
                // 源机械族尚未完成一次绘制：本次吞掉人类剪影，避免叠加。
                return true;
            }

            SilhouetteUtility.DrawSilhouetteJob(source, trs);
            return true;
        }

        internal static void BeginSourceRender(Pawn source)
        {
            renderingSource = true;
            renderingSourcePawn = source;
        }

        internal static void EndSourceRender()
        {
            renderingSource = false;
            renderingSourcePawn = null;
        }
    }
}
