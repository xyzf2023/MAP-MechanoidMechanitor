using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 社交面板可见性与关系列表补丁。仅检查 ColonistLikeSocialTab。
    /// 页签实例的动态补齐统一由 ColonistLikeInspectTabPatches 负责。
    /// </summary>
    public static class ColonistLikeSocialTabPatches
    {
        private const string LogPrefix = "[MAP-机械族机械师] ColonistLikeSocialTabPatches：";
        private const int ErrorKeyShouldShowTargetNotFound = 879346601;

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
                if (!ColonistLikeSocialTabUtility.HasSocialTab(pawn))
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
                if (!ColonistLikeSocialTabUtility.HasSocialTab(pawn))
                {
                    return true;
                }

                __result = ColonistLikeSocialTabUtility.EmptyPawnsForSocialInfo;
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
                if (!ColonistLikeSocialTabUtility.HasSocialTab(pawn)
                    && !ColonistLikeSocialTabUtility.HasSocialTab(selPawnForSocialInfo))
                {
                    return true;
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

                // selPawnForSocialInfo = viewer；pawn = other。控制者侧 Overseer 不显示。
                __result = ColonistLikeSocialTabUtility.ShouldShowExplicitSocialRelation(
                    selPawnForSocialInfo,
                    pawn);
                return false;
            }
        }

        [HarmonyPatch(typeof(PawnRelationUtility), nameof(PawnRelationUtility.GetRelations))]
        public static class Patch_PawnRelationUtility_GetRelations
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn me, Pawn other, ref IEnumerable<PawnRelationDef> __result)
            {
                if (!ColonistLikeSocialTabUtility.HasSocialTab(me)
                    && !ColonistLikeSocialTabUtility.HasSocialTab(other))
                {
                    return true;
                }

                __result = ColonistLikeSocialTabUtility.EnumerateRelationsWithoutFleshRequirement(
                    me,
                    other);
                return false;
            }
        }
    }
}
