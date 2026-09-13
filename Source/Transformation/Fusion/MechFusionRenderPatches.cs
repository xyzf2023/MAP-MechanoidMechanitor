using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体渲染替换。只对存在有效活动合体记录的人类启用；
    /// 人类身体、头部、外观与普通服装不再绘制，改为在人类位置绘制源机械族。
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.RenderPawnAt))]
    internal static class MechFusionRenderPawnPatch
    {
        private static readonly AccessTools.FieldRef<PawnRenderer, Pawn> PawnField =
            AccessTools.FieldRefAccess<PawnRenderer, Pawn>("pawn");

        public static bool Prefix(
            PawnRenderer __instance,
            Vector3 drawLoc,
            Rot4? rotOverride,
            bool neverAimWeapon)
        {
            Pawn wearer = PawnField(__instance);
            if (!MechFusionRenderUtility.TryGetFusionSource(wearer, out _))
            {
                return true;
            }

            Rot4 rotation = rotOverride ?? wearer.Rotation;
            if (!MechFusionRenderUtility.TryRenderSourceAt(
                    wearer,
                    drawLoc,
                    rotation,
                    neverAimWeapon))
            {
                return true;
            }

            MechFusionRenderUtility.DrawWearerEquipment(
                wearer,
                drawLoc,
                rotation,
                neverAimWeapon);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(PawnRenderer),
        nameof(PawnRenderer.RenderShadowOnlyAt))]
    internal static class MechFusionShadowOnlyPatch
    {
        private static readonly AccessTools.FieldRef<PawnRenderer, Pawn> PawnField =
            AccessTools.FieldRefAccess<PawnRenderer, Pawn>("pawn");

        public static bool Prefix(PawnRenderer __instance)
        {
            return !MechFusionRenderUtility.IsActiveFusionWearer(
                PawnField(__instance));
        }
    }

    [HarmonyPatch(
        typeof(SilhouetteUtility),
        nameof(SilhouetteUtility.DrawSilhouetteJob))]
    internal static class MechFusionSilhouettePatch
    {
        public static bool Prefix(Thing thing, Matrix4x4 trs)
        {
            return thing is not Pawn pawn
                || !MechFusionRenderUtility.TryDrawSourceSilhouette(pawn, trs);
        }
    }

    [HarmonyPatch(
        typeof(PawnCacheRenderer),
        nameof(PawnCacheRenderer.RenderPawn))]
    internal static class MechFusionPortraitPatch
    {
        public static bool Prefix(
            PawnCacheRenderer __instance,
            Pawn pawn,
            RenderTexture renderTexture,
            Vector3 cameraOffset,
            float cameraZoom,
            float angle,
            Rot4 rotation,
            bool renderHead,
            bool renderHeadgear,
            bool renderClothes,
            bool portrait,
            Vector3 positionOffset,
            IReadOnlyDictionary<Apparel, Color> overrideApparelColor,
            Color? overrideHairColor,
            bool stylingStation)
        {
            if (MechFusionRenderUtility.IsRenderingSource
                || !MechFusionRenderUtility.TryGetFusionSource(
                    pawn,
                    out Pawn? source)
                || source == null)
            {
                return true;
            }

            MechFusionRenderUtility.BeginSourceRender();
            try
            {
                __instance.RenderPawn(
                    source,
                    renderTexture,
                    cameraOffset,
                    cameraZoom,
                    angle,
                    rotation,
                    renderHead,
                    renderHeadgear,
                    renderClothes,
                    portrait,
                    positionOffset,
                    overrideApparelColor,
                    overrideHairColor,
                    stylingStation);
            }
            finally
            {
                MechFusionRenderUtility.EndSourceRender();
            }

            return false;
        }
    }
}
