using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体渲染替换工具。Pawn 的逻辑实体始终是目标人类；这里只负责
    /// 在人类位置绘制被收纳的真实源机械族，并保留人类武器显示。
    /// 使用重入 guard，防止绘制源机械族时再次进入合体替换。
    /// </summary>
    internal static class MechFusionRenderUtility
    {
        [ThreadStatic]
        private static bool renderingSource;

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
            }
        }

        internal static void DrawWearerEquipment(
            Pawn wearer,
            Vector3 drawLoc,
            Rot4 rotation,
            bool neverAimWeapon)
        {
            if (wearer?.equipment == null || wearer.equipment.Primary == null)
            {
                return;
            }

            Vector3 equipmentPos = drawLoc.WithYOffset(
                PawnRenderUtility.AltitudeForLayer(90f));
            PawnRenderFlags flags = neverAimWeapon
                ? PawnRenderFlags.NeverAimWeapon
                : PawnRenderFlags.None;
            PawnRenderUtility.DrawEquipmentAndApparelExtras(
                wearer,
                equipmentPos,
                rotation,
                flags);
        }

        internal static bool TryDrawSourceSilhouette(
            Pawn wearer,
            Matrix4x4 trs)
        {
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

        internal static void BeginSourceRender()
        {
            renderingSource = true;
        }

        internal static void EndSourceRender()
        {
            renderingSource = false;
        }
    }
}
