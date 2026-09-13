using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体渲染替换。只对存在有效活动合体记录的人类启用；
    /// 人类身体、头部、外观与普通服装不再绘制，改为在人类位置绘制源机械族，
    /// 之后只单独补绘人类主武器一次。
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

            MechFusionRenderUtility.DrawWearerWeaponOnly(
                wearer,
                drawLoc,
                rotation,
                neverAimWeapon);
            return false;
        }
    }

    /// <summary>
    /// 作为合体外观绘制 source Pawn 时，跳过其 Equipment 与 Apparel extras：
    /// 不显示 source 自己的武器，也不触发任何 WornApparel.DrawWornExtras。
    /// 普通机械族与其他 Pawn 渲染完全不受影响。
    /// </summary>
    [HarmonyPatch(
        typeof(PawnRenderUtility),
        nameof(PawnRenderUtility.DrawEquipmentAndApparelExtras))]
    internal static class MechFusionSkipSourceExtrasPatch
    {
        public static bool Prefix(Pawn pawn)
        {
            return !MechFusionRenderUtility.IsRenderingSourcePawn(pawn);
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

            MechFusionRenderUtility.BeginSourceRender(source);
            try
            {
                // 不把 wearer 的 overrideApparelColor / overrideHairColor 应用给 source，
                // source 外观与人类颜色覆盖完全隔离。
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
                    null,
                    null,
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
