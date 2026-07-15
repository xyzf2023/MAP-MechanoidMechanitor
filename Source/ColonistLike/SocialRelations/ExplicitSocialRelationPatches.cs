using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 显式社交关系：开放社交面板、限制列表来源、修复 GetRelations，并提供指定配偶按钮。
    /// </summary>
    public static class ExplicitSocialRelationPatches
    {
        private const string LogPrefix = "[MAP-机械族机械师] ExplicitSocialRelationPatches：";

        // 与 ImplantPatches 的 8793455xx 段分开，避免 ErrorOnce 键冲突。
        private const int ErrorKeyShouldShowTargetNotFound = 879346601;
        private const int ErrorKeyCanDrawTryRomanceNotFound = 879346602;
        private const int ErrorKeyDrawTryRomanceNotFound = 879346603;

        [HarmonyPatch(typeof(ITab_Pawn_Social), nameof(ITab_Pawn_Social.IsVisible), MethodType.Getter)]
        public static class Patch_ITab_Pawn_Social_IsVisible
        {
            [HarmonyPostfix]
            public static void Postfix(ref bool __result)
            {
                if (__result)
                {
                    return;
                }

                Pawn? pawn = ResolveSelectedPawnForSocialTab();
                if (!ExplicitSocialRelationUtility.IsOptedIn(pawn))
                {
                    return;
                }

                __result = true;
            }

            private static Pawn? ResolveSelectedPawnForSocialTab()
            {
                Thing? selectedThing = Find.Selector?.SingleSelectedThing;
                if (selectedThing is Pawn pawn)
                {
                    return pawn;
                }

                if (selectedThing is Corpse corpse)
                {
                    return corpse.InnerPawn;
                }

                return null;
            }
        }

        [HarmonyPatch(typeof(SocialCardUtility), nameof(SocialCardUtility.PawnsForSocialInfo))]
        public static class Patch_SocialCardUtility_PawnsForSocialInfo
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn pawn, ref List<Pawn> __result)
            {
                if (!ExplicitSocialRelationUtility.IsOptedIn(pawn))
                {
                    return true;
                }

                __result = ExplicitSocialRelationUtility.EmptyPawnsForSocialInfo;
                return false;
            }
        }

        [HarmonyPatch]
        public static class Patch_SocialCardUtility_ShouldShowPawnRelations
        {
            private static MethodInfo? cachedTargetMethod;

            private static bool Prepare()
            {
                if (TargetMethod() != null)
                {
                    return true;
                }

                Log.ErrorOnce(
                    $"{LogPrefix}未找到 SocialCardUtility.ShouldShowPawnRelations，补丁未应用。",
                    ErrorKeyShouldShowTargetNotFound);
                return false;
            }

            private static MethodBase? TargetMethod()
            {
                if (cachedTargetMethod != null)
                {
                    return cachedTargetMethod;
                }

                cachedTargetMethod = AccessTools.Method(
                    typeof(SocialCardUtility),
                    "ShouldShowPawnRelations",
                    new[] { typeof(Pawn), typeof(Pawn) });
                return cachedTargetMethod;
            }

            [HarmonyPrefix]
            public static bool Prefix(Pawn pawn, Pawn selPawnForSocialInfo, ref bool __result)
            {
                if (!ExplicitSocialRelationUtility.IsOptedIn(pawn)
                    && !ExplicitSocialRelationUtility.IsOptedIn(selPawnForSocialInfo))
                {
                    return true;
                }

                // 显式关系路径：忽略 Name.Numerical / null Name 与 DEV showAllRelations。
                if (pawn.relations == null || selPawnForSocialInfo.relations == null)
                {
                    __result = false;
                    return false;
                }

                if (pawn.relations.hidePawnRelations || selPawnForSocialInfo.relations.hidePawnRelations)
                {
                    __result = false;
                    return false;
                }

                __result = ExplicitSocialRelationUtility.HasExplicitDirectRelation(
                    pawn,
                    selPawnForSocialInfo);
                return false;
            }
        }

        [HarmonyPatch(typeof(PawnRelationUtility), nameof(PawnRelationUtility.GetRelations))]
        public static class Patch_PawnRelationUtility_GetRelations
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn me, Pawn other, ref IEnumerable<PawnRelationDef> __result)
            {
                if (!ExplicitSocialRelationUtility.IsOptedIn(me)
                    && !ExplicitSocialRelationUtility.IsOptedIn(other))
                {
                    return true;
                }

                // 迭代器方法必须在此设置 __result 并跳过原版，否则枚举时仍会执行 IsFlesh 门槛。
                __result = ExplicitSocialRelationUtility.EnumerateRelationsWithoutFleshRequirement(
                    me,
                    other);
                return false;
            }
        }

        [HarmonyPatch]
        public static class Patch_SocialCardUtility_CanDrawTryRomance
        {
            private static MethodInfo? cachedTargetMethod;

            private static bool Prepare()
            {
                if (TargetMethod() != null)
                {
                    return true;
                }

                Log.ErrorOnce(
                    $"{LogPrefix}未找到 SocialCardUtility.CanDrawTryRomance，补丁未应用。",
                    ErrorKeyCanDrawTryRomanceNotFound);
                return false;
            }

            private static MethodBase? TargetMethod()
            {
                if (cachedTargetMethod != null)
                {
                    return cachedTargetMethod;
                }

                cachedTargetMethod = AccessTools.Method(
                    typeof(SocialCardUtility),
                    "CanDrawTryRomance",
                    new[] { typeof(Pawn) });
                return cachedTargetMethod;
            }

            [HarmonyPrefix]
            public static bool Prefix(Pawn pawn, ref bool __result)
            {
                if (!ExplicitSocialRelationUtility.IsOptedIn(pawn))
                {
                    return true;
                }

                __result = ExplicitSocialRelationUtility.CanShowAssignSpouseButton(pawn);
                return false;
            }
        }

        [HarmonyPatch]
        public static class Patch_SocialCardUtility_DrawTryRomance
        {
            private static MethodInfo? cachedTargetMethod;

            private static bool Prepare()
            {
                if (TargetMethod() != null)
                {
                    return true;
                }

                Log.ErrorOnce(
                    $"{LogPrefix}未找到 SocialCardUtility.DrawTryRomance，补丁未应用。",
                    ErrorKeyDrawTryRomanceNotFound);
                return false;
            }

            private static MethodBase? TargetMethod()
            {
                if (cachedTargetMethod != null)
                {
                    return cachedTargetMethod;
                }

                cachedTargetMethod = AccessTools.Method(
                    typeof(SocialCardUtility),
                    "DrawTryRomance",
                    new[] { typeof(Rect), typeof(Pawn) });
                return cachedTargetMethod;
            }

            [HarmonyPrefix]
            public static bool Prefix(Rect buttonRect, Pawn pawn)
            {
                if (!ExplicitSocialRelationUtility.IsOptedIn(pawn))
                {
                    return true;
                }

                ExplicitSocialRelationUtility.DrawAssignSpouseButton(buttonRect, pawn);
                return false;
            }
        }
    }
}
