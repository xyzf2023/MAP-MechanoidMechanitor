using System.Collections.Generic;
using System.Reflection;
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
            return !MechFusionRenderUtility.IsActiveFusionWearer(
                PawnField(__instance));
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
                || !MechFusionRenderUtility.TryDrawSourceSilhouette(pawn);
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
