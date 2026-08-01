using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 社交面板可见性与关系列表补丁。仅检查 ColonistLikeSocialTab。
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

        /// <summary>
        /// 缓存的动态社交页签实例。Inspector 页签不绑定固定 Pawn，内部通过当前选中对象解析，
        /// 因此可静态复用，避免每次 GetInspectTabs 都创建新实例。不加入任何 ThingDef 页签列表。
        /// </summary>
        private static readonly ITab_Pawn_Social DynamicSocialTab =
            new ITab_Pawn_Social();

        [HarmonyPatch(typeof(Thing), nameof(Thing.GetInspectTabs))]
        public static class Patch_Thing_GetInspectTabs
        {
            [HarmonyPostfix]
            public static IEnumerable<InspectTabBase> Postfix(
                IEnumerable<InspectTabBase> __result,
                Thing __instance)
            {
                Pawn? pawn = ResolvePawnForInspectTabs(__instance);
                if (!ColonistLikeSocialTabUtility.HasSocialTab(pawn))
                {
                    return __result;
                }

                // 复制原页签列表，避免修改第三方 ThingDef.inspectorTabsResolved 或共享列表。
                List<InspectTabBase> tabs = new List<InspectTabBase>();
                if (__result != null)
                {
                    foreach (InspectTabBase tab in __result)
                    {
                        if (tab != null)
                        {
                            tabs.Add(tab);
                        }
                    }
                }

                // 已包含社交页签（含派生实现）则不重复添加。
                for (int i = 0; i < tabs.Count; i++)
                {
                    if (tabs[i] is ITab_Pawn_Social)
                    {
                        return tabs;
                    }
                }

                // 优先插入到“装备”页签之前；无装备页签则插到“记录”页签之前；都没有则追加到末尾。
                int insertIndex = -1;
                for (int i = 0; i < tabs.Count; i++)
                {
                    if (tabs[i] is ITab_Pawn_Gear)
                    {
                        insertIndex = i;
                        break;
                    }
                }

                if (insertIndex < 0)
                {
                    for (int i = 0; i < tabs.Count; i++)
                    {
                        if (tabs[i] is ITab_Pawn_Log)
                        {
                            insertIndex = i;
                            break;
                        }
                    }
                }

                if (insertIndex >= 0)
                {
                    tabs.Insert(insertIndex, DynamicSocialTab);
                }
                else
                {
                    tabs.Add(DynamicSocialTab);
                }

                return tabs;
            }
        }

        private static Pawn? ResolvePawnForInspectTabs(Thing? thing)
        {
            if (thing is Pawn pawn)
            {
                return pawn;
            }

            if (thing is Corpse corpse)
            {
                return corpse.InnerPawn;
            }

            return null;
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
