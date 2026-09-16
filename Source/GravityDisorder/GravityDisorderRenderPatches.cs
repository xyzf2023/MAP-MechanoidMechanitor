using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.RenderCache))]
    internal static class GravityDisorderPortraitContextPatch
    {
        public static void Prefix(out GravityDisorderPresentation.RenderContext __state) =>
            __state = GravityDisorderPresentation.EnterRender(null);

        public static Exception Finalizer(Exception __exception,
            GravityDisorderPresentation.RenderContext __state)
        {
            GravityDisorderPresentation.RestoreRender(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), "ParallelGetPreRenderResults")]
    internal static class GravityDisorderPreRenderPatch
    {
        public static void Prefix(Pawn ___pawn, ref bool disableCache,
            ref bool neverAimWeapon, ref Rot4? rotOverride,
            out GravityDisorderPresentation.RenderContext __state)
        {
            __state = GravityDisorderPresentation.EnterRender(___pawn);
            if (GravityDisorderPresentation.RenderingPawn != null)
            {
                // 地图倒地姿态不写入原版用于头像/缩放显示的共用图集。
                disableCache = true;
                neverAimWeapon = true;
                rotOverride = null;
            }
        }

        public static Exception Finalizer(Exception __exception,
            GravityDisorderPresentation.RenderContext __state)
        {
            GravityDisorderPresentation.RestoreRender(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.RenderPawnAt))]
    internal static class GravityDisorderRenderPatch
    {
        [HarmonyPriority(Priority.First + 1)]
        public static void Prefix(Pawn ___pawn,
            out GravityDisorderPresentation.RenderContext __state) =>
            __state = GravityDisorderPresentation.EnterRender(___pawn);

        public static void Postfix(PawnRenderer __instance, Pawn ___pawn) =>
            GravityDisorderPresentation.DrawShadow(__instance, ___pawn);

        public static Exception Finalizer(Exception __exception,
            GravityDisorderPresentation.RenderContext __state)
        {
            GravityDisorderPresentation.RestoreRender(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.GetPosture))]
    internal static class GravityDisorderRenderPosturePatch
    {
        public static void Postfix(Pawn p, ref PawnPosture __result)
        {
            // 仅当前线程正在绘制的那个 Pawn；弹丸、医疗和任务查询保持原版语义。
            if (GravityDisorderPresentation.InRenderContext(p))
                __result = PawnPosture.LayingOnGroundNormal;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.BodyAngle))]
    internal static class GravityDisorderBodyAnglePatch
    {
        [HarmonyPriority(Priority.First + 1)]
        public static bool Prefix(Pawn ___pawn, PawnRenderFlags flags, ref float __result)
        {
            if (!GravityDisorderPresentation.InRenderContext(___pawn)
                || flags.FlagSet(PawnRenderFlags.Portrait) || flags.FlagSet(PawnRenderFlags.Statue))
                return true;
            __result = GravityDisorderPresentation.BodyAngle(___pawn);
            return false;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), "GetBodyPos")]
    internal static class GravityDisorderBodyPositionPatch
    {
        public static bool Prefix(Pawn ___pawn, Vector3 drawLoc, ref bool showBody,
            ref Vector3 __result)
        {
            if (!GravityDisorderPresentation.InRenderContext(___pawn))
                return true;
            // 悬浮者即使在床格，也不能被原版床偏移/躺地绘制层拉回地面或藏起身体。
            __result = drawLoc;
            showBody = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), "GetDrawParms")]
    internal static class GravityDisorderDrawParametersPatch
    {
        public static void Postfix(Pawn ___pawn, ref PawnDrawParms __result)
        {
            if (!GravityDisorderPresentation.InRenderContext(___pawn)
                || __result.flags.FlagSet(PawnRenderFlags.Portrait)
                || __result.flags.FlagSet(PawnRenderFlags.Statue))
                return;
            __result.bed = null;
            __result.crawling = false;
            __result.swimming = false;
            __result.flags &= ~PawnRenderFlags.NoBody;
            __result.flags |= PawnRenderFlags.NeverAimWeapon | PawnRenderFlags.Clothes | PawnRenderFlags.Headgear;
        }
    }

    [HarmonyPatch]
    internal static class GravityDisorderRenderLocomotionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.Crawling));
            yield return AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.Swimming));
        }

        public static void Postfix(Pawn __instance, ref bool __result)
        {
            if (GravityDisorderPresentation.InRenderContext(__instance))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.RenderShadowOnlyAt))]
    internal static class GravityDisorderShadowOnlyPatch
    {
        public static bool Prefix(PawnRenderer __instance, Pawn ___pawn)
        {
            if (!GravityDisorderPresentation.ShouldDraw(___pawn))
                return true;
            GravityDisorderPresentation.DrawShadow(__instance, ___pawn);
            return false;
        }
    }

    [HarmonyPatch(typeof(GenUI), nameof(GenUI.ThingsUnderMouse), new[]
    {
        typeof(Vector3), typeof(float), typeof(TargetingParameters), typeof(ITargetingSource)
    })]
    internal static class GravityDisorderMouseTargetPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Vector3 clickPos, float pawnWideClickRadius,
            TargetingParameters clickParams, ITargetingSource source, ref List<Thing> __result)
        {
            Map? map = Find.CurrentMap;
            if (map == null)
                return;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!GravityDisorderPresentation.ShouldDraw(pawn))
                    continue;
                Vector3 delta = clickPos - pawn.DrawPos;
                float horizontalRadius = Mathf.Max(0.8f, pawnWideClickRadius);
                bool inside = delta.x * delta.x / (horizontalRadius * horizontalRadius)
                    + delta.z * delta.z / (0.65f * 0.65f) <= 1f;
                if (!inside || pawn.IsHiddenFromPlayer() || !clickParams.CanTarget(pawn, source))
                {
                    // 原版还会按地面格子添加候选，必须剔除旧位置的残留选取。
                    __result.Remove(pawn);
                }
                else if (!__result.Contains(pawn))
                    __result.Add(pawn);
            }
        }
    }
}
