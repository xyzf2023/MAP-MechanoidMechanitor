using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 展开合体期间，人类地图贴图固定南向。覆盖预绘制参数，兼顾近景与远景缓存，
    /// 不改人类真实 Rotation，不干扰其工作、移动或头像朝向。
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderer), "GetDrawParms")]
    internal static class MechFusionTransitionFacingPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(Pawn ___pawn, PawnRenderFlags flags, ref Rot4 bodyFacing)
        {
            if (!flags.FlagSet(PawnRenderFlags.Portrait)
                && !flags.FlagSet(PawnRenderFlags.Statue)
                // 地图参数已选择南向缓存；生成图集时保留各方向，避免污染其他朝向。
                && !flags.FlagSet(PawnRenderFlags.Cache)
                && !MechFusionRenderUtility.IsRenderingSource
                && MechFusionTransitionVisual.ShouldDrawMergeTargetSouth(___pawn))
            {
                bodyFacing = Rot4.South;
            }
        }
    }

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
            if (MechFusionVisualUtility.IsMergeSourceHidden(wearer))
            {
                return false;
            }

            if (MechFusionVisualUtility.IsReleasing(wearer))
            {
                // 只改变地图表现；会话仍然 Active，肖像继续显示正式合体外观。
                return true;
            }

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
    /// 合体源机械族实时绘制时按“附着在目标殖民者位置的站立外观”处理：
    /// 原版 GetBodyPos 对非站立 Pawn 会访问 ParentHolder.ParentHolder，
    /// 而合体源已存入 WorldPawns，ParentHolder 可能为空。仅在合体渲染上下文
    /// 命中时直接使用目标殖民者传入的 drawLoc 并显示完整身体；其余 Pawn 原样放行。
    /// Priority.First 确保先于 SyntheticLovinRenderPatches 的同名补丁执行。
    /// 目标方法只在补丁初始化时按精确签名解析并缓存，不逐帧反射。
    /// </summary>
    [HarmonyPatch]
    internal static class MechFusionSourceBodyPosPatch
    {
        private const int ErrorKeyTargetMethodNotFound = 2147045619;

        private static MethodBase? cachedTargetMethod;

        private static bool Prepare()
        {
            if (TargetMethod() != null)
            {
                return true;
            }

            Log.ErrorOnce(
                "[MAP-机械族机械师] 未找到 PawnRenderer.GetBodyPos"
                + "(Vector3, PawnPosture, out bool)，合体源站立外观补丁未应用。",
                ErrorKeyTargetMethodNotFound);
            return false;
        }

        private static MethodBase? TargetMethod()
        {
            if (cachedTargetMethod != null)
            {
                return cachedTargetMethod;
            }

            cachedTargetMethod = AccessTools.Method(
                typeof(PawnRenderer),
                "GetBodyPos",
                new[]
                {
                    typeof(Vector3),
                    typeof(PawnPosture),
                    typeof(bool).MakeByRefType()
                });
            return cachedTargetMethod;
        }

        [HarmonyPriority(Priority.First)]
        [HarmonyPrefix]
        public static bool Prefix(
            Pawn ___pawn,
            Vector3 drawLoc,
            ref bool showBody,
            ref Vector3 __result)
        {
            if (!MechFusionRenderUtility.IsRenderingSourcePawn(___pawn))
            {
                return true;
            }

            showBody = true;
            __result = drawLoc;
            return false;
        }
    }

    /// <summary>
    /// 合体源机械族实时绘制时身体角度固定为站立角度 0f：
    /// 原版 BodyAngle 的非站立分支同样访问 ParentHolder.ParentHolder，
    /// 会在 GetBodyPos 之后再次空引用。仅在合体渲染上下文命中时接管，
    /// 其余 Pawn 原样放行。Priority.First 确保先于 SyntheticLovinRenderPatches
    /// 的同名补丁执行。朝向仍由 TryRenderSourceAt 传入的目标殖民者 rotOverride 决定。
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.BodyAngle))]
    internal static class MechFusionSourceBodyAnglePatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyPrefix]
        public static bool Prefix(Pawn ___pawn, ref float __result)
        {
            if (!MechFusionRenderUtility.IsRenderingSourcePawn(___pawn))
            {
                return true;
            }

            __result = 0f;
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
            Pawn pawn = PawnField(__instance);
            return !MechFusionVisualUtility.IsMergeSourceHidden(pawn)
                && (MechFusionVisualUtility.IsReleasing(pawn)
                    || !MechFusionRenderUtility.IsActiveFusionWearer(pawn));
        }
    }

    [HarmonyPatch(
        typeof(SilhouetteUtility),
        nameof(SilhouetteUtility.DrawSilhouetteJob))]
    internal static class MechFusionSilhouettePatch
    {
        public static bool Prefix(Thing thing)
        {
            return thing is not Pawn pawn
                || (!MechFusionVisualUtility.IsMergeSourceHidden(pawn)
                    && (MechFusionVisualUtility.IsReleasing(pawn)
                        || !MechFusionRenderUtility.TryDrawSourceSilhouette(pawn)));
        }
    }

    // 必须早于 DynamicDrawManager 读取轮廓图形；仅跳过最终轮廓绘制还可能访问空图形。
    [HarmonyPatch(typeof(SilhouetteUtility), nameof(SilhouetteUtility.ShouldDrawSilhouette))]
    internal static class MechFusionTransitionSilhouetteVisibilityPatch
    {
        public static bool Prefix(Thing thing, ref bool __result)
        {
            if (thing is not Pawn pawn || !MechFusionVisualUtility.IsMergeSourceHidden(pawn))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), "DrawShadowInternal")]
    internal static class MechFusionTransitionShadowPatch
    {
        public static bool Prefix(Pawn ___pawn)
        {
            return !MechFusionVisualUtility.IsMergeSourceHidden(___pawn);
        }
    }

    [HarmonyPatch(typeof(ApparelGraphicRecordGetter),
        nameof(ApparelGraphicRecordGetter.TryGetGraphicApparel))]
    internal static class MechFusionTransitionApparelPatch
    {
        public static bool Prefix(Apparel apparel, ref ApparelGraphicRecord rec, ref bool __result)
        {
            if (apparel?.TryGetComp<CompMechFusionShell>() == null
                || !MechFusionVisualUtility.IsReleasing(apparel.Wearer))
            {
                return true;
            }

            rec = default;
            __result = false;
            return false;
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
            // 此入口也用于远景地图图集；展开时只放行地图缓存，头像仍替换为机械体。
            if ((!portrait && MechFusionVisualUtility.IsReleasing(pawn))
                || MechFusionRenderUtility.IsRenderingSource
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
