using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public enum MechanoidOvermindThingCategory : byte
    {
        Weapon,
        Apparel,
        Food,
        Medicine,
        Material,
        Building,
        Special,
        Other
    }

    public sealed class MechanoidOvermindMechCatalogEntry
    {
        public PawnKindDef Kind { get; }

        public ThingDef Race { get; }

        public MechWeightClassDef? WeightClass { get; }

        public int WeightSortOrder { get; }

        public float BandwidthCost { get; }

        public float MarketValue { get; }

        public int PurgePrice { get; }

        public MechanoidOvermindMechCatalogEntry(
            PawnKindDef kind,
            ThingDef race,
            MechWeightClassDef? weightClass,
            int weightSortOrder,
            float bandwidthCost,
            float marketValue,
            int purgePrice)
        {
            Kind = kind;
            Race = race;
            WeightClass = weightClass;
            WeightSortOrder = weightSortOrder;
            BandwidthCost = bandwidthCost;
            MarketValue = marketValue;
            PurgePrice = purgePrice;
        }
    }

    public sealed class MechanoidOvermindThingCatalogEntry
    {
        public ThingDef Def { get; }

        public MechanoidOvermindThingCategory Category { get; }

        public bool MadeFromStuff { get; }

        public bool HasQuality { get; }

        public float DefaultReferenceMarketValue { get; }

        public MechanoidOvermindThingCatalogEntry(
            ThingDef def,
            MechanoidOvermindThingCategory category,
            bool madeFromStuff,
            bool hasQuality,
            float defaultReferenceMarketValue)
        {
            Def = def;
            Category = category;
            MadeFromStuff = madeFromStuff;
            HasQuality = hasQuality;
            DefaultReferenceMarketValue = defaultReferenceMarketValue;
        }
    }

    public static class MechanoidOvermindCatalogService
    {
        private static List<MechanoidOvermindMechCatalogEntry>? mechCatalog;

        private static List<MechanoidOvermindThingCatalogEntry>? thingCatalog;
        private static Dictionary<ThingDef, MechanoidOvermindThingCatalogEntry>? thingLookup;

        private static HashSet<ThingDef>? thingBlacklistCache;

        private static HashSet<PawnKindDef>? mechPawnKindBlacklistCache;

        private static readonly Dictionary<ThingDef, List<ThingDef>> stuffCandidatesCache =
            new Dictionary<ThingDef, List<ThingDef>>();

        public static IReadOnlyList<MechanoidOvermindMechCatalogEntry> GetMechCatalog()
        {
            EnsureMechCatalog();
            return mechCatalog!;
        }

        public static IReadOnlyList<MechanoidOvermindThingCatalogEntry> GetThingCatalog()
        {
            EnsureThingCatalog();
            return thingCatalog!;
        }

        /// <summary>
        /// 根据 ThingDef 查找其物资目录条目（类别 + 默认参考单位市场价值）。
        /// 用于肃清评级物资分级，避免退回 ThingDef.BaseMarketValue 的粗略五档分类。
        /// 查询失败或目录未收录时返回 false。
        /// </summary>
        public static bool TryFindThingCatalogEntry(
            ThingDef def,
            out MechanoidOvermindThingCatalogEntry entry)
        {
            entry = null!;
            if (def == null)
            {
                return false;
            }

            EnsureThingCatalog();
            if (thingCatalog == null)
            {
                return false;
            }

            if (thingLookup == null)
            {
                thingLookup = new Dictionary<ThingDef, MechanoidOvermindThingCatalogEntry>();
                for (int i = 0; i < thingCatalog.Count; i++)
                {
                    MechanoidOvermindThingCatalogEntry e = thingCatalog[i];
                    if (e.Def != null && !thingLookup.ContainsKey(e.Def))
                    {
                        thingLookup[e.Def] = e;
                    }
                }
            }

            return thingLookup.TryGetValue(def, out entry);
        }

        public static IReadOnlyList<ThingDef> GetStuffCandidates(ThingDef def)
        {
            if (def == null || !def.MadeFromStuff)
            {
                return Array.Empty<ThingDef>();
            }

            if (stuffCandidatesCache.TryGetValue(def, out List<ThingDef> cached))
            {
                return cached;
            }

            List<ThingDef> list = new List<ThingDef>();
            foreach (ThingDef stuff in GenStuff.AllowedStuffsFor(def))
            {
                if (stuff?.stuffProps != null)
                {
                    list.Add(stuff);
                }
            }

            list.Sort(CompareDefsByLabel);
            stuffCandidatesCache[def] = list;
            return list;
        }

        public static MechanoidOvermindThingCategory ClassifyThing(ThingDef def)
        {
            if (def == null)
            {
                return MechanoidOvermindThingCategory.Other;
            }

            if (def.IsWeapon)
            {
                return MechanoidOvermindThingCategory.Weapon;
            }

            if (def.IsApparel)
            {
                return MechanoidOvermindThingCategory.Apparel;
            }

            if (def.IsMedicine)
            {
                return MechanoidOvermindThingCategory.Medicine;
            }

            if (def.IsIngestible)
            {
                return MechanoidOvermindThingCategory.Food;
            }

            if (def.IsStuff)
            {
                return MechanoidOvermindThingCategory.Material;
            }

            if (def.category == ThingCategory.Building && def.Minifiable)
            {
                return MechanoidOvermindThingCategory.Building;
            }

            if (LooksSpecialTradeItem(def))
            {
                return MechanoidOvermindThingCategory.Special;
            }

            return MechanoidOvermindThingCategory.Other;
        }

        public static int GetWeightSortOrder(MechWeightClassDef? weightClass)
        {
            if (weightClass == null)
            {
                return 0;
            }

            if (weightClass == MechWeightClassDefOf.Light)
            {
                return 0;
            }

            if (weightClass == MechWeightClassDefOf.Medium)
            {
                return 1;
            }

            if (weightClass == MechWeightClassDefOf.Heavy)
            {
                return 2;
            }

            if (weightClass == MechWeightClassDefOf.UltraHeavy)
            {
                return 3;
            }

            return 4;
        }

        public static HashSet<ThingDef> GetMergedBlacklist()
        {
            EnsureBlacklist();
            return thingBlacklistCache!;
        }

        public static bool IsMechPawnKindBlacklisted(PawnKindDef? kind)
        {
            if (kind == null)
            {
                return false;
            }

            EnsureBlacklist();
            return mechPawnKindBlacklistCache!.Contains(kind);
        }

        public static void ClearCaches()
        {
            PurgeDirectiveGoodsRatingOverrideUtility.ClearCache();
            mechCatalog = null;
            thingCatalog = null;
            thingLookup = null;
            thingBlacklistCache = null;
            mechPawnKindBlacklistCache = null;
            stuffCandidatesCache.Clear();
            MechanoidOvermindPricingService.ClearThingMarketValueCache();
        }

        private static void EnsureMechCatalog()
        {
            if (mechCatalog != null)
            {
                return;
            }

            EnsureBlacklist();
            List<MechanoidOvermindMechCatalogEntry> list =
                new List<MechanoidOvermindMechCatalogEntry>();
            List<PawnKindDef> allKinds = DefDatabase<PawnKindDef>.AllDefsListForReading;
            for (int i = 0; i < allKinds.Count; i++)
            {
                PawnKindDef kind = allKinds[i];
                if (!IsCatalogMech(kind))
                {
                    continue;
                }

                if (mechPawnKindBlacklistCache!.Contains(kind))
                {
                    continue;
                }

                if (!MechanoidOvermindPricingService.TryGetMechBandwidthCost(
                        kind,
                        out float bandwidth))
                {
                    continue;
                }

                if (!MechanoidOvermindPricingService.TryGetMechMarketValue(
                        kind,
                        out float marketValue))
                {
                    continue;
                }

                if (!MechanoidOvermindPricingService.TryGetMechUnitPrice(kind, out int price))
                {
                    continue;
                }

                MechWeightClassDef? weightClass = kind.race.race.mechWeightClass;
                list.Add(
                    new MechanoidOvermindMechCatalogEntry(
                        kind,
                        kind.race,
                        weightClass,
                        GetWeightSortOrder(weightClass),
                        bandwidth,
                        marketValue,
                        price));
            }

            list.Sort(CompareMechEntries);
            mechCatalog = list;
        }

        private static void EnsureThingCatalog()
        {
            if (thingCatalog != null)
            {
                return;
            }

            EnsureBlacklist();
            List<MechanoidOvermindThingCatalogEntry> list =
                new List<MechanoidOvermindThingCatalogEntry>();
            List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < allDefs.Count; i++)
            {
                ThingDef def = allDefs[i];
                if (!IsCatalogThing(def))
                {
                    continue;
                }

                MechanoidOvermindThingSpec defaultSpec =
                    MechanoidOvermindThingSpec.CreateDefault(def);
                if (!MechanoidOvermindPricingService.TryGetThingUnitMarketValue(
                        defaultSpec,
                        out float referenceValue))
                {
                    continue;
                }

                list.Add(
                    new MechanoidOvermindThingCatalogEntry(
                        def,
                        ClassifyThing(def),
                        def.MadeFromStuff,
                        def.HasComp(typeof(CompQuality)),
                        referenceValue));
            }

            list.Sort(CompareThingEntries);
            thingCatalog = list;
        }

        private static void EnsureBlacklist()
        {
            if (thingBlacklistCache != null && mechPawnKindBlacklistCache != null)
            {
                return;
            }

            HashSet<ThingDef> thingSet = new HashSet<ThingDef>();
            HashSet<PawnKindDef> mechSet = new HashSet<PawnKindDef>();
            List<MechanoidMechanitorPurgeTradeBlacklistDef> defs =
                DefDatabase<MechanoidMechanitorPurgeTradeBlacklistDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                MechanoidMechanitorPurgeTradeBlacklistDef? blacklistDef = defs[i];
                if (blacklistDef == null)
                {
                    continue;
                }

                List<ThingDef>? thingDefs = blacklistDef.thingDefs;
                if (thingDefs != null)
                {
                    for (int j = 0; j < thingDefs.Count; j++)
                    {
                        ThingDef? thingDef = thingDefs[j];
                        if (thingDef != null)
                        {
                            thingSet.Add(thingDef);
                        }
                    }
                }

                List<PawnKindDef>? mechPawnKinds = blacklistDef.mechPawnKinds;
                if (mechPawnKinds != null)
                {
                    for (int j = 0; j < mechPawnKinds.Count; j++)
                    {
                        PawnKindDef? mechPawnKind = mechPawnKinds[j];
                        if (mechPawnKind != null)
                        {
                            mechSet.Add(mechPawnKind);
                        }
                    }
                }
            }

            List<PawnKindDef> allKinds = DefDatabase<PawnKindDef>.AllDefsListForReading;
            for (int i = 0; i < allKinds.Count; i++)
            {
                PawnKindDef kind = allKinds[i];
                if (HasFiniteMechPowerCell(kind))
                {
                    mechSet.Add(kind);
                }
            }

            thingBlacklistCache = thingSet;
            mechPawnKindBlacklistCache = mechSet;
        }

        private static bool HasFiniteMechPowerCell(PawnKindDef? kind)
        {
            if (kind?.race?.race == null || !kind.race.race.IsMechanoid)
            {
                return false;
            }

            return kind.race.GetCompProperties<CompProperties_MechPowerCell>() != null;
        }

        private static bool IsCatalogMech(PawnKindDef? kind)
        {
            if (kind?.race?.race == null)
            {
                return false;
            }

            return kind.race.race.IsMechanoid;
        }

        private static bool IsCatalogThing(ThingDef? def)
        {
            if (def == null)
            {
                return false;
            }

            if (thingBlacklistCache != null && thingBlacklistCache.Contains(def))
            {
                return false;
            }

            if (def == ThingDefOf.Silver || def.IsCorpse)
            {
                return false;
            }

            // 使用储存筛选的分类树，包含子分类及第三方 MOD 加入的物品。
            ThingCategoryDef? rawFood = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("FoodRaw");
            if ((rawFood != null && def.IsWithinCategory(rawFood))
                || def.IsWithinCategory(ThingCategoryDefOf.Leathers)
                || def.IsWithinCategory(ThingCategoryDefOf.Wools)
                || def.IsWithinCategory(ThingCategoryDefOf.Drugs))
            {
                return false;
            }

            if (def.category == ThingCategory.Building)
            {
                return false;
            }

            if (def.category == ThingCategory.Pawn
                || def.category == ThingCategory.Mote
                || def.category == ThingCategory.Projectile
                || def.category == ThingCategory.Plant
                || def.category == ThingCategory.Filth
                || def.category == ThingCategory.Gas
                || def.category == ThingCategory.Ethereal
                || def.category == ThingCategory.Attachment
                || def.category == ThingCategory.PsychicEmitter
                || def.category == ThingCategory.None)
            {
                return false;
            }

            if (def.IsBlueprint || def.IsFrame)
            {
                return false;
            }

            if (typeof(Pawn).IsAssignableFrom(def.thingClass)
                || typeof(Corpse).IsAssignableFrom(def.thingClass)
                || typeof(MinifiedThing).IsAssignableFrom(def.thingClass)
                || typeof(Blueprint).IsAssignableFrom(def.thingClass)
                || typeof(Frame).IsAssignableFrom(def.thingClass))
            {
                return false;
            }

            if (!def.tradeability.TraderCanSell())
            {
                return false;
            }

            if (def.destroyOnDrop)
            {
                return false;
            }

            return def.category == ThingCategory.Item && def.EverHaulable;
        }

        private static bool LooksSpecialTradeItem(ThingDef def)
        {
            List<string>? tradeTags = def.tradeTags;
            if (tradeTags == null || tradeTags.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < tradeTags.Count; i++)
            {
                string? tag = tradeTags[i];
                if (tag.NullOrEmpty())
                {
                    continue;
                }

                if (tag.IndexOf("Exotic", StringComparison.OrdinalIgnoreCase) >= 0
                    || tag.IndexOf("Artifact", StringComparison.OrdinalIgnoreCase) >= 0
                    || string.Equals(tag, "PsychicWeapon", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tag, "PsychicApparel", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareMechEntries(
            MechanoidOvermindMechCatalogEntry a,
            MechanoidOvermindMechCatalogEntry b)
        {
            int weightCompare = a.WeightSortOrder.CompareTo(b.WeightSortOrder);
            if (weightCompare != 0)
            {
                return weightCompare;
            }

            int priceCompare = a.PurgePrice.CompareTo(b.PurgePrice);
            if (priceCompare != 0)
            {
                return priceCompare;
            }

            return CompareDefsByLabel(a.Kind, b.Kind);
        }

        private static int CompareThingEntries(
            MechanoidOvermindThingCatalogEntry a,
            MechanoidOvermindThingCatalogEntry b)
        {
            int categoryCompare = ((byte)a.Category).CompareTo((byte)b.Category);
            if (categoryCompare != 0)
            {
                return categoryCompare;
            }

            return CompareDefsByLabel(a.Def, b.Def);
        }

        private static int CompareDefsByLabel(Def a, Def b)
        {
            int labelCompare = string.CompareOrdinal(a.label, b.label);
            if (labelCompare != 0)
            {
                return labelCompare;
            }

            return string.CompareOrdinal(a.defName, b.defName);
        }
    }
}
