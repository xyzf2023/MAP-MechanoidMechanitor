using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 显式社交关系：开放社交面板、限制列表来源、修复 GetRelations 的 IsFlesh 门槛。
    /// </summary>
    public static class ExplicitSocialRelationPatches
    {
        private const string LogPrefix = "[MAP-机械族机械师] ExplicitSocialRelationPatches：";

        private const int ErrorKeyShouldShowTargetNotFound = 879345501;
        private const int ErrorKeyShowAllRelationsFieldNotFound = 879345502;

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
            private static FieldInfo? cachedShowAllRelationsField;
            private static bool loggedShowAllRelationsMissing;

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

                if (TryGetShowAllRelations())
                {
                    __result = true;
                    return false;
                }

                if ((pawn.RaceProps.Animal && pawn.Dead && pawn.Corpse == null)
                    || pawn.Name == null
                    || pawn.Name.Numerical)
                {
                    __result = false;
                    return false;
                }

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

            private static bool TryGetShowAllRelations()
            {
                if (cachedShowAllRelationsField == null)
                {
                    cachedShowAllRelationsField = AccessTools.Field(
                        typeof(SocialCardUtility),
                        "showAllRelations");
                }

                if (cachedShowAllRelationsField == null)
                {
                    if (!loggedShowAllRelationsMissing)
                    {
                        loggedShowAllRelationsMissing = true;
                        Log.ErrorOnce(
                            $"{LogPrefix}未找到 SocialCardUtility.showAllRelations，DEV AllRelations 开关将对此路径无效。",
                            ErrorKeyShowAllRelationsFieldNotFound);
                    }

                    return false;
                }

                return (bool)cachedShowAllRelationsField.GetValue(null);
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
    }
}
