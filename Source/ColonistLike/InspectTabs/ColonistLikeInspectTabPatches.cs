using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 统一判断类殖民者 Pawn 应当具备哪些 Inspector 页签。
    /// 这里只检查既有身份、能力与 Tracker，不在 UI 查询期间初始化或修改 Pawn 状态。
    /// </summary>
    internal static class ColonistLikeInspectTabUtility
    {
        public static Pawn? ResolvePawn(Thing? thing)
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

        public static bool HasCharacterTabAuthorization(Pawn? pawn)
        {
            return pawn != null
                && pawn.Faction == Faction.OfPlayer
                && MechanoidMechanitorCapabilityUtility.HasCapability(
                    pawn, MechanoidMechanitorCapability.CharacterTab);
        }

        public static bool HasCharacterTabData(Pawn? pawn)
        {
            return pawn != null
                && pawn.Name != null
                && pawn.health != null
                && pawn.ageTracker != null
                && pawn.story?.traits != null
                && pawn.skills != null
                && pawn.abilities != null;
        }

        public static bool ShouldShowCharacterTab(Pawn? pawn)
        {
            return HasCharacterTabAuthorization(pawn)
                && HasCharacterTabData(pawn);
        }

        public static bool ShouldAddSocialTab(Pawn? pawn)
        {
            return ColonistLikeSocialTabUtility.HasSocialTab(pawn);
        }

        public static bool ShouldAddGearTab(Pawn? pawn)
        {
            if (pawn == null
                || pawn.inventory == null
                || pawn.equipment == null
                || pawn.apparel == null)
            {
                return false;
            }

            bool allowsHumanWeapons =
                pawn.Faction == Faction.OfPlayer
                && MechanoidMechanitorRoleUtility.AllowsHumanWeapons(pawn);

            bool allowsHumanApparel =
                HumanApparelUtility.TryGetApparelComp(
                    pawn,
                    out CompHumanApparelUser? apparelComp)
                && apparelComp!.AllowRemoveApparel;

            return allowsHumanWeapons || allowsHumanApparel;
        }

        public static bool ShouldAddFormingCaravanTab(Pawn? pawn)
        {
            return pawn != null
                && (MechanoidMechanitorCapabilityUtility.HasCapability(
                        pawn,
                        MechanoidMechanitorCapability.TravelLeadCaravan)
                    || pawn.IsFormingCaravan());
        }

        public static bool ShouldAddGeneTabs(Pawn? pawn)
        {
            if (!ModsConfig.BiotechActive || pawn?.genes == null)
            {
                return false;
            }

            return HasCharacterTabAuthorization(pawn)
                || MechanoidMechanitorCapabilityUtility.HasCapability(
                    pawn,
                    MechanoidMechanitorCapability.SyntheticPregnancy);
        }
    }

    /// <summary>
    /// 为显式获得类殖民者能力、但其 ThingDef 删除了原版页签的 Pawn 动态补齐页签。
    /// 始终复制 GetInspectTabs 的结果，不修改 ThingDef.inspectorTabsResolved 或第三方共享列表。
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.GetInspectTabs))]
    public static class Patch_Thing_GetInspectTabs_ColonistLikeCompletion
    {
        [HarmonyPostfix]
        public static IEnumerable<InspectTabBase> Postfix(
            IEnumerable<InspectTabBase> __result,
            Thing __instance)
        {
            Pawn? pawn = ColonistLikeInspectTabUtility.ResolvePawn(__instance);
            if (pawn == null)
            {
                return __result;
            }

            bool addCharacter = ColonistLikeInspectTabUtility.ShouldShowCharacterTab(pawn);
            bool addSocial = ColonistLikeInspectTabUtility.ShouldAddSocialTab(pawn);
            bool addGear = ColonistLikeInspectTabUtility.ShouldAddGearTab(pawn);
            // 原版远行队页签只通过 SelPawn 取目标，不支持 Corpse.InnerPawn。
            bool addFormingCaravan = __instance is Pawn
                && ColonistLikeInspectTabUtility.ShouldAddFormingCaravanTab(pawn);
            bool addGenes = ColonistLikeInspectTabUtility.ShouldAddGeneTabs(pawn);

            if (!addCharacter
                && !addSocial
                && !addGear
                && !addFormingCaravan
                && !addGenes)
            {
                return __result;
            }

            List<InspectTabBase> tabs = CopyTabs(__result);

            if (addCharacter && !ContainsTab<ITab_Pawn_Character>(tabs))
            {
                InsertBeforeFirst<ITab_Pawn_Health>(
                    tabs,
                    GetSharedTab(typeof(ITab_Pawn_Character)),
                    fallbackIndex: 0);
            }

            if (addFormingCaravan && !ContainsTab<ITab_Pawn_FormingCaravan>(tabs))
            {
                int insertIndex = FindFirstIndex<ITab_Pawn_Social>(tabs);
                if (insertIndex < 0)
                {
                    insertIndex = FindFirstIndex<ITab_Pawn_Gear>(tabs);
                }
                if (insertIndex < 0)
                {
                    insertIndex = FindFirstIndex<ITab_Pawn_Log>(tabs);
                }

                InsertAtOrAppend(
                    tabs,
                    GetSharedTab(typeof(ITab_Pawn_FormingCaravan)),
                    insertIndex);
            }

            if (addSocial && !ContainsTab<ITab_Pawn_Social>(tabs))
            {
                int insertIndex = FindFirstIndex<ITab_Pawn_Gear>(tabs);
                if (insertIndex < 0)
                {
                    insertIndex = FindFirstIndex<ITab_Pawn_Log>(tabs);
                }

                InsertAtOrAppend(
                    tabs,
                    GetSharedTab(typeof(ITab_Pawn_Social)),
                    insertIndex);
            }

            if (addGear && !ContainsTab<ITab_Pawn_Gear>(tabs))
            {
                InsertBeforeFirst<ITab_Pawn_Log>(
                    tabs,
                    GetSharedTab(typeof(ITab_Pawn_Gear)),
                    fallbackIndex: tabs.Count);
            }

            if (addGenes)
            {
                // ITab_GenesPregnancy 继承 ITab_Genes，必须按具体运行时类型分别去重。
                if (!ContainsExactTab(tabs, typeof(ITab_Genes)))
                {
                    tabs.Add(GetSharedTab(typeof(ITab_Genes)));
                }

                if (!ContainsExactTab(tabs, typeof(ITab_GenesPregnancy)))
                {
                    tabs.Add(GetSharedTab(typeof(ITab_GenesPregnancy)));
                }
            }

            return tabs;
        }

        private static List<InspectTabBase> CopyTabs(
            IEnumerable<InspectTabBase> source)
        {
            List<InspectTabBase> result = new List<InspectTabBase>();
            if (source == null)
            {
                return result;
            }

            foreach (InspectTabBase tab in source)
            {
                if (tab != null)
                {
                    result.Add(tab);
                }
            }

            return result;
        }

        private static InspectTabBase GetSharedTab(Type tabType)
        {
            return InspectTabManager.GetSharedInstance(tabType);
        }

        private static bool ContainsTab<TTab>(List<InspectTabBase> tabs)
            where TTab : InspectTabBase
        {
            return FindFirstIndex<TTab>(tabs) >= 0;
        }

        private static bool ContainsExactTab(
            List<InspectTabBase> tabs,
            Type tabType)
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                if (tabs[i].GetType() == tabType)
                {
                    return true;
                }
            }

            return false;
        }

        private static int FindFirstIndex<TTab>(List<InspectTabBase> tabs)
            where TTab : InspectTabBase
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                if (tabs[i] is TTab)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void InsertBeforeFirst<TTab>(
            List<InspectTabBase> tabs,
            InspectTabBase tab,
            int fallbackIndex)
            where TTab : InspectTabBase
        {
            int insertIndex = FindFirstIndex<TTab>(tabs);
            if (insertIndex < 0)
            {
                insertIndex = fallbackIndex;
            }

            InsertAtOrAppend(tabs, tab, insertIndex);
        }

        private static void InsertAtOrAppend(
            List<InspectTabBase> tabs,
            InspectTabBase tab,
            int insertIndex)
        {
            if (insertIndex >= 0 && insertIndex < tabs.Count)
            {
                tabs.Insert(insertIndex, tab);
            }
            else
            {
                tabs.Add(tab);
            }
        }
    }
}
