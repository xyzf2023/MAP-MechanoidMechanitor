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
            return HasCharacterTabData(pawn)
                && HasCharacterTabAuthorization(pawn);
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

            return (pawn.Faction == Faction.OfPlayer
                    && MechanoidMechanitorRoleUtility.AllowsHumanWeapons(pawn))
                || (HumanApparelUtility.TryGetApparelComp(
                        pawn,
                        out CompHumanApparelUser? apparelComp)
                    && apparelComp!.AllowRemoveApparel);
        }

        public static bool ShouldAddFormingCaravanTab(Pawn? pawn)
        {
            return pawn != null
                && (MechanoidMechanitorCapabilityUtility.HasCapability(
                        pawn,
                        MechanoidMechanitorCapability.TravelLeadCaravan)
                    || pawn.IsFormingCaravan());
        }

        public static bool ShouldAddGeneTabs(
            Pawn? pawn,
            bool? characterTabAuthorization = null)
        {
            if (!ModsConfig.BiotechActive || pawn?.genes == null)
            {
                return false;
            }

            return (characterTabAuthorization ?? HasCharacterTabAuthorization(pawn))
                || MechanoidMechanitorCapabilityUtility.HasCapability(
                    pawn,
                    MechanoidMechanitorCapability.SyntheticPregnancy);
        }
    }

    /// <summary>
    /// 为显式获得类殖民者能力、但其 ThingDef 删除了原版页签的 Pawn 动态补齐页签。
    /// 可索引列表先检查缺失页签，仅在补齐或清理空条目时复制；未知枚举保留单次物化回退。
    /// 不修改 ThingDef.inspectorTabsResolved 或第三方共享列表。
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.GetInspectTabs))]
    public static class Patch_Thing_GetInspectTabs_ColonistLikeCompletion
    {
        private struct InspectTabPresence
        {
            public bool Character;
            public bool Social;
            public bool Gear;
            public bool FormingCaravan;
            public bool Genes;
            public bool GenesPregnancy;
            public bool HasNullTabs;
        }

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

            // 原版返回已解析的 List；先扫描一次，已有页签不再查询能力和注册表。
            // 未知 IEnumerable 不预先枚举，保留后续单次物化的兼容路径。
            IList<InspectTabBase>? sourceTabs = __result as IList<InspectTabBase>;
            InspectTabPresence existing = sourceTabs != null
                ? ScanTabs(sourceTabs)
                : default;

            bool? characterTabAuthorization = null;
            bool addCharacter = false;
            if (!existing.Character && ColonistLikeInspectTabUtility.HasCharacterTabData(pawn))
            {
                characterTabAuthorization =
                    ColonistLikeInspectTabUtility.HasCharacterTabAuthorization(pawn);
                addCharacter = characterTabAuthorization.Value;
            }

            bool addSocial = !existing.Social
                && ColonistLikeInspectTabUtility.ShouldAddSocialTab(pawn);
            bool addGear = !existing.Gear
                && ColonistLikeInspectTabUtility.ShouldAddGearTab(pawn);
            // 原版远行队页签只通过 SelPawn 取目标，不支持 Corpse.InnerPawn。
            bool addFormingCaravan = !existing.FormingCaravan
                && __instance is Pawn
                && ColonistLikeInspectTabUtility.ShouldAddFormingCaravanTab(pawn);
            bool addGenes = (!existing.Genes || !existing.GenesPregnancy)
                && ColonistLikeInspectTabUtility.ShouldAddGeneTabs(pawn, characterTabAuthorization);

            if (!addCharacter
                && !addSocial
                && !addGear
                && !addFormingCaravan
                && !addGenes
                && !existing.HasNullTabs)
            {
                return __result;
            }

            int additionalCapacity = (addCharacter ? 1 : 0)
                + (addSocial ? 1 : 0)
                + (addGear ? 1 : 0)
                + (addFormingCaravan ? 1 : 0)
                + (addGenes && !existing.Genes ? 1 : 0)
                + (addGenes && !existing.GenesPregnancy ? 1 : 0);
            List<InspectTabBase> tabs = CopyTabs(__result, additionalCapacity);
            if (sourceTabs == null)
            {
                // 第三方延迟枚举只消费一次，再对物化结果去重。
                existing = ScanTabs(tabs);
            }

            if (addCharacter && !existing.Character)
            {
                InsertBeforeFirst<ITab_Pawn_Health>(
                    tabs,
                    GetSharedTab(typeof(ITab_Pawn_Character)),
                    fallbackIndex: 0);
            }

            if (addFormingCaravan && !existing.FormingCaravan)
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

            if (addSocial && !existing.Social)
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

            if (addGear && !existing.Gear)
            {
                InsertBeforeFirst<ITab_Pawn_Log>(
                    tabs,
                    GetSharedTab(typeof(ITab_Pawn_Gear)),
                    fallbackIndex: tabs.Count);
            }

            if (addGenes)
            {
                // ITab_GenesPregnancy 继承 ITab_Genes，必须按具体运行时类型分别去重。
                if (!existing.Genes)
                {
                    tabs.Add(GetSharedTab(typeof(ITab_Genes)));
                }

                if (!existing.GenesPregnancy)
                {
                    tabs.Add(GetSharedTab(typeof(ITab_GenesPregnancy)));
                }
            }

            return tabs;
        }

        private static List<InspectTabBase> CopyTabs(
            IEnumerable<InspectTabBase> source,
            int additionalCapacity)
        {
            int sourceCount = (source as ICollection<InspectTabBase>)?.Count ?? 0;
            List<InspectTabBase> result = new List<InspectTabBase>(sourceCount + additionalCapacity);
            if (source == null)
            {
                return result;
            }

            if (source is IList<InspectTabBase> sourceTabs)
            {
                for (int i = 0; i < sourceTabs.Count; i++)
                {
                    InspectTabBase tab = sourceTabs[i];
                    if (tab != null)
                    {
                        result.Add(tab);
                    }
                }

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

        private static InspectTabPresence ScanTabs(IList<InspectTabBase> tabs)
        {
            InspectTabPresence result = default;
            for (int i = 0; i < tabs.Count; i++)
            {
                InspectTabBase tab = tabs[i];
                if (tab == null)
                {
                    result.HasNullTabs = true;
                }
                else if (tab is ITab_Pawn_Character)
                {
                    result.Character = true;
                }
                else if (tab is ITab_Pawn_Social)
                {
                    result.Social = true;
                }
                else if (tab is ITab_Pawn_Gear)
                {
                    result.Gear = true;
                }
                else if (tab is ITab_Pawn_FormingCaravan)
                {
                    result.FormingCaravan = true;
                }
                else if (tab is ITab_Genes)
                {
                    // 孕育页是基因页的子类，两者分别按具体运行时类型记录。
                    Type tabType = tab.GetType();
                    if (tabType == typeof(ITab_Genes))
                    {
                        result.Genes = true;
                    }
                    else if (tabType == typeof(ITab_GenesPregnancy))
                    {
                        result.GenesPregnancy = true;
                    }
                }
            }

            return result;
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
