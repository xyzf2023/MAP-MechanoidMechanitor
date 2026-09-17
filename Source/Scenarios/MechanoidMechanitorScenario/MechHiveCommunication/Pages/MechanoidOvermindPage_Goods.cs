using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidOvermindPage_Goods
    {
        private enum SortMode : byte
        {
            Name,
            MarketValueAsc,
            MarketValueDesc
        }

        private const float FilterRowHeight = 28f;

        private const float RowHeight = 40f;

        private const float IconSize = 32f;

        private static float SpecPanelHeight => GameComponent_OvermindEconomy.Enabled ? 192f : 120f;

        private string search = string.Empty;

        private MechanoidOvermindThingCategory? categoryFilter;

        private SortMode sortMode = SortMode.Name;

        private Vector2 listScroll;

        private string? cachedSearch;

        private MechanoidOvermindThingCategory? cachedCategory;

        private SortMode cachedSort = SortMode.Name;

        private bool filterBuilt;

        private int cachedRatingLevel = -1;

        private readonly List<MechanoidOvermindThingCatalogEntry> filtered =
            new List<MechanoidOvermindThingCatalogEntry>();

        private MechanoidOvermindThingCatalogEntry? selected;

        private ThingDef? selectedStuff;

        private QualityCategory selectedQuality = QualityCategory.Normal;

        private int selectedCount;

        private string countEditBuffer = "0";

        private MechanoidOvermindOrder? syncedOrder;

        private int syncedOrderRevision = int.MinValue;

        private MechanoidOvermindThingSpec? pricedSpec;

        private float cachedUnitMarketValue;

        private int cachedEstimatedCredits;

        private int cachedPricedCount = int.MinValue;

        private bool priceDirty = true;

        public void Draw(Rect inRect, MechanoidOvermindOrder order)
        {
            using (MechanoidOvermindUiStyle.Push())
            {
                MechanoidOvermindUiStyle.DrawPanel(inRect);
                Rect inner = inRect.ContractedBy(8f);

                SyncFromOrderRevisionIfNeeded(order);

                float y = inner.y;
                DrawCategoryRow(new Rect(inner.x, y, inner.width, FilterRowHeight));
                y += FilterRowHeight + 4f;

                Rect searchRect = new Rect(inner.x, y, inner.width * 0.62f, FilterRowHeight);
                string nextSearch = Widgets.TextField(searchRect, search);
                if (nextSearch != search)
                {
                    search = nextSearch ?? string.Empty;
                }

                DrawSortButtons(
                    new Rect(
                        searchRect.xMax + 6f,
                        y,
                        inner.width - searchRect.width - 6f,
                        FilterRowHeight));
                y += FilterRowHeight + 6f;

                RebuildFilteredIfNeeded();

                float listHeight = Mathf.Max(
                    80f,
                    inner.yMax - y - SpecPanelHeight - 8f);
                Rect listRect = new Rect(inner.x, y, inner.width, listHeight);
                DrawList(listRect, order);
                y = listRect.yMax + 8f;

                Rect specRect = new Rect(inner.x, y, inner.width, SpecPanelHeight);
                DrawSpecPanel(specRect, order);
            }
        }

        private void SyncFromOrderRevisionIfNeeded(MechanoidOvermindOrder order)
        {
            if (ReferenceEquals(syncedOrder, order)
                && syncedOrderRevision == order.Revision)
            {
                return;
            }

            syncedOrder = order;
            syncedOrderRevision = order.Revision;
            SyncSelectedCountFromOrder(order);
        }

        private void SyncSelectedCountFromOrder(MechanoidOvermindOrder order)
        {
            MechanoidOvermindThingSpec? spec = selected != null
                ? BuildCurrentSpec(selected)
                : null;
            selectedCount = order.GetThingCount(spec);
            countEditBuffer = selectedCount.ToString();
            priceDirty = true;
        }

        private void DrawCategoryRow(Rect rect)
        {
            string[] labels =
            {
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Category.All".Translate(),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Category.Weapon".Translate(),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Category.Apparel".Translate(),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Category.Food".Translate(),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Category.Medicine".Translate(),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Category.Material".Translate(),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Category.Building".Translate(),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Category.Special".Translate(),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Category.Other".Translate()
            };

            MechanoidOvermindThingCategory?[] values =
            {
                null,
                MechanoidOvermindThingCategory.Weapon,
                MechanoidOvermindThingCategory.Apparel,
                MechanoidOvermindThingCategory.Food,
                MechanoidOvermindThingCategory.Medicine,
                MechanoidOvermindThingCategory.Material,
                MechanoidOvermindThingCategory.Building,
                MechanoidOvermindThingCategory.Special,
                MechanoidOvermindThingCategory.Other
            };

            float buttonWidth = rect.width / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                Rect buttonRect = new Rect(
                    rect.x + buttonWidth * i,
                    rect.y,
                    buttonWidth - 2f,
                    rect.height);
                bool selectedCat = categoryFilter == values[i];
                if (MechanoidOvermindUiStyle.DrawActionButton(buttonRect, labels[i])
                    && !selectedCat)
                {
                    categoryFilter = values[i];
                }

                if (selectedCat)
                {
                    Widgets.DrawBoxSolid(
                        new Rect(buttonRect.x, buttonRect.yMax - 2f, buttonRect.width, 2f),
                        MechanoidOvermindUiStyle.AccentBright);
                }
            }
        }

        private void DrawSortButtons(Rect rect)
        {
            float w = rect.width / 3f;
            DrawSortButton(
                new Rect(rect.x, rect.y, w - 2f, rect.height),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Sort.Name".Translate(),
                SortMode.Name);
            DrawSortButton(
                new Rect(rect.x + w, rect.y, w - 2f, rect.height),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Sort.ValueAsc".Translate(),
                SortMode.MarketValueAsc);
            DrawSortButton(
                new Rect(rect.x + w * 2f, rect.y, w - 2f, rect.height),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Sort.Value.Description".Translate(),
                SortMode.MarketValueDesc);
        }

        private void DrawSortButton(Rect rect, string label, SortMode mode)
        {
            bool selected = sortMode == mode;
            if (MechanoidOvermindUiStyle.DrawActionButton(rect, label) && !selected)
            {
                sortMode = mode;
            }

            if (selected)
            {
                Widgets.DrawBoxSolid(
                    new Rect(rect.x, rect.yMax - 2f, rect.width, 2f),
                    MechanoidOvermindUiStyle.AccentBright);
            }
        }

        private void RebuildFilteredIfNeeded()
        {
            int ratingLevel = PurgeDirectiveRatingUtility.CurrentRatingLevel;
            if (filterBuilt
                && cachedSearch == search
                && cachedCategory == categoryFilter
                && cachedSort == sortMode
                && cachedRatingLevel == ratingLevel)
            {
                return;
            }

            cachedSearch = search;
            cachedCategory = categoryFilter;
            cachedSort = sortMode;
            cachedRatingLevel = ratingLevel;
            filterBuilt = true;
            filtered.Clear();

            string needle = search.Trim();
            IReadOnlyList<MechanoidOvermindThingCatalogEntry> catalog =
                MechanoidOvermindCatalogService.GetThingCatalog();
            for (int i = 0; i < catalog.Count; i++)
            {
                MechanoidOvermindThingCatalogEntry entry = catalog[i];
                if (!PurgeDirectiveRatingUtility.IsThingUnlocked(entry.Def))
                {
                    continue;
                }

                if (categoryFilter.HasValue && entry.Category != categoryFilter.Value)
                {
                    continue;
                }

                if (!needle.NullOrEmpty()
                    && !entry.Def.label.ToLowerInvariant().Contains(needle.ToLowerInvariant())
                    && !entry.Def.defName.ToLowerInvariant().Contains(needle.ToLowerInvariant()))
                {
                    continue;
                }

                filtered.Add(entry);
            }

            filtered.Sort(CompareEntries);

            if (selected != null
                && !PurgeDirectiveRatingUtility.IsThingUnlocked(selected.Def))
            {
                selected = null;
                selectedStuff = null;
                selectedCount = 0;
                countEditBuffer = "0";
                pricedSpec = null;
                priceDirty = true;
            }
        }

        private int CompareEntries(
            MechanoidOvermindThingCatalogEntry a,
            MechanoidOvermindThingCatalogEntry b)
        {
            switch (sortMode)
            {
                case SortMode.MarketValueAsc:
                    int asc = a.DefaultReferenceMarketValue.CompareTo(
                        b.DefaultReferenceMarketValue);
                    if (asc != 0)
                    {
                        return asc;
                    }

                    break;
                case SortMode.MarketValueDesc:
                    int desc = b.DefaultReferenceMarketValue.CompareTo(
                        a.DefaultReferenceMarketValue);
                    if (desc != 0)
                    {
                        return desc;
                    }

                    break;
            }

            int label = string.CompareOrdinal(a.Def.label, b.Def.label);
            if (label != 0)
            {
                return label;
            }

            return string.CompareOrdinal(a.Def.defName, b.Def.defName);
        }

        private void DrawList(Rect listRect, MechanoidOvermindOrder order)
        {
            MechanoidOvermindUiStyle.DrawPanel(listRect, alt: true, cornerMarks: false);
            float rowStride = RowHeight + 2f;
            Rect viewRect = new Rect(
                0f,
                0f,
                listRect.width - 16f,
                Mathf.Max(listRect.height, filtered.Count * rowStride));
            Widgets.BeginScrollView(listRect, ref listScroll, viewRect);

            if (filtered.Count > 0)
            {
                int first = Mathf.Max(0, Mathf.FloorToInt(listScroll.y / rowStride) - 1);
                int last = Mathf.Min(
                    filtered.Count - 1,
                    Mathf.CeilToInt((listScroll.y + listRect.height) / rowStride) + 1);
                for (int i = first; i <= last; i++)
                {
                    Rect rowRect = new Rect(4f, i * rowStride, viewRect.width - 8f, RowHeight);
                    DrawRow(rowRect, filtered[i], order);
                }
            }

            Widgets.EndScrollView();
        }

        private void DrawRow(
            Rect rowRect,
            MechanoidOvermindThingCatalogEntry entry,
            MechanoidOvermindOrder order)
        {
            bool isSelected = selected != null && selected.Def == entry.Def;
            Widgets.DrawBoxSolid(
                rowRect,
                isSelected
                    ? MechanoidOvermindUiStyle.NavSelectedFill
                    : MechanoidOvermindUiStyle.Panel);

            Rect iconRect = new Rect(
                rowRect.x + 4f,
                rowRect.y + (rowRect.height - IconSize) / 2f,
                IconSize,
                IconSize);
            Widgets.DefIcon(iconRect, entry.Def);

            float textX = iconRect.xMax + 8f;
            float textRight = rowRect.xMax - 4f;
            float textWidth = Mathf.Max(0f, textRight - textX);

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(textX, rowRect.y + 2f, textWidth, 18f),
                entry.Def.LabelCap);

            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(textX, rowRect.y + 20f, textWidth, 16f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.GoodsRowCategory".Translate(
                    GetCategoryLabel(entry.Category)));

            if (Widgets.ButtonInvisible(rowRect))
            {
                SelectEntry(entry, order);
            }
        }

        private void SelectEntry(
            MechanoidOvermindThingCatalogEntry entry,
            MechanoidOvermindOrder order)
        {
            selected = entry;
            selectedStuff = entry.MadeFromStuff
                ? GenStuff.DefaultStuffFor(entry.Def)
                : null;
            selectedQuality = QualityCategory.Normal;
            SyncSelectedCountFromOrder(order);
            syncedOrderRevision = order.Revision;
            priceDirty = true;
        }

        private void DrawSpecPanel(Rect rect, MechanoidOvermindOrder order)
        {
            MechanoidOvermindUiStyle.DrawPanel(rect, alt: true, cornerMarks: false);
            Rect inner = rect.ContractedBy(8f);
            if (selected == null)
            {
                MechanoidOvermindUiStyle.DrawSecondaryLabel(
                    inner,
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.Goods.SelectHint".Translate());
                return;
            }

            MechanoidOvermindThingCatalogEntry entry = selected;
            float x = inner.x;
            float y = inner.y;

            bool thingUnlocked = PurgeDirectiveRatingUtility.IsThingUnlocked(entry.Def);
            int thingRequiredLevel = PurgeDirectiveRatingUtility.RequiredLevelForThing(entry.Def);

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(x, y, inner.width, 22f),
                entry.Def.LabelCap,
                GameFont.Small);
            y += 26f;

            if (thingUnlocked && entry.MadeFromStuff)
            {
                Rect stuffRect = new Rect(x, y, 220f, 28f);
                string stuffLabel = selectedStuff != null
                    ? selectedStuff.LabelCap
                    : "MAP_MechanoidMechanitor.PurgeDirective.Communication.Stuff.None".Translate();
                if (MechanoidOvermindUiStyle.DrawActionButton(
                        stuffRect,
                        "MAP_MechanoidMechanitor.PurgeDirective.Communication.Stuff".Translate(
                            stuffLabel)))
                {
                    OpenStuffMenu(entry.Def, order);
                }

                x += 230f;
            }

            if (thingUnlocked && entry.HasQuality)
            {
                Rect qualityRect = new Rect(x, y, 180f, 28f);
                if (MechanoidOvermindUiStyle.DrawActionButton(
                        qualityRect,
                        "MAP_MechanoidMechanitor.PurgeDirective.Communication.Quality".Translate(
                            selectedQuality.GetLabel())))
                {
                    OpenQualityMenu(order);
                }

                x += 190f;
            }

            if (thingUnlocked)
            {
                Rect countRect = new Rect(x, y, 56f, 28f);
                DrawCountField(countRect, order, entry);

                if (MechanoidOvermindUiStyle.DrawActionButton(
                        new Rect(countRect.xMax + 4f, y, 24f, 28f),
                        "-"))
                {
                    ApplyThingCount(order, entry, selectedCount - 1);
                }

                if (MechanoidOvermindUiStyle.DrawActionButton(
                        new Rect(countRect.xMax + 32f, y, 24f, 28f),
                        "+"))
                {
                    ApplyThingCount(order, entry, selectedCount + 1);
                }
            }
            else
            {
                MechanoidOvermindUiStyle.DrawLabel(
                    new Rect(x, y, 300f, 28f),
                    PurgeDirectiveRatingUtility.GetThingLockReason(entry.Def),
                    GameFont.Small,
                    TextAnchor.MiddleLeft,
                    MechanoidOvermindUiStyle.Error);
            }

            RefreshPriceIfNeeded(entry, order);

            float infoY = y + 36f;
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(inner.x, infoY, inner.width, 22f),
                (GameComponent_OvermindEconomy.Enabled
                    ? (selectedCount > 0 ? "MAP_OvermindEconomy.Estimate" : "MAP_OvermindEconomy.NextPrice")
                    : "MAP_MechanoidMechanitor.PurgeDirective.Communication.Goods.EstimatedCredits").Translate(
                    cachedEstimatedCredits,
                    ((double)cachedEstimatedCredits / Math.Max(1, selectedCount)).ToString("0.##")),
                GameFont.Small,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.AccentBright);

            if (GameComponent_OvermindEconomy.Enabled)
            {
                MechanoidOvermindThingSpec? spec = BuildCurrentSpec(entry);
                GameComponent_OvermindEconomy? economy = GameComponent_OvermindEconomy.Current;
                if (spec != null && economy != null)
                {
                    Rect details = new Rect(inner.x, infoY + 25f, inner.width, 44f);
                    MechanoidOvermindUiStyle.DrawLabel(details, economy.Describe(spec, order),
                        GameFont.Tiny, TextAnchor.UpperLeft, MechanoidOvermindUiStyle.TextSecondary);
                    TooltipHandler.TipRegion(details, "MAP_OvermindEconomy.DetailsTip".Translate());
                }
            }

            if (selectedCount > 0
                && MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(inner.xMax - 140f, inner.yMax - 34f, 140f, 30f),
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.ClearItem".Translate()))
            {
                ApplyThingCount(order, entry, 0);
            }
        }

        private void DrawCountField(
            Rect countRect,
            MechanoidOvermindOrder order,
            MechanoidOvermindThingCatalogEntry entry)
        {
            string orderText = selectedCount.ToString();
            const string controlName = "MAP_OvermindThingCount";
            GUI.SetNextControlName(controlName);
            string next = GUI.TextField(countRect, countEditBuffer, Text.CurTextFieldStyle);
            bool focused = GUI.GetNameOfFocusedControl() == controlName;

            if (focused)
            {
                countEditBuffer = next ?? string.Empty;
                if (int.TryParse(next, out int parsed)
                    && parsed >= 0
                    && parsed <= MechanoidOvermindOrder.ThingMaxCount)
                {
                    MechanoidOvermindThingSpec? spec = BuildCurrentSpec(entry);
                    if (spec != null)
                    {
                        int maximum = GameComponent_OvermindEconomy.Enabled
                            ? GameComponent_OvermindEconomy.Current?.AvailableCount(spec, order) ?? 0
                            : MechanoidOvermindOrder.ThingMaxCount;
                        order.SetThingCount(spec, Math.Min(parsed, maximum));
                        selectedCount = order.GetThingCount(spec);
                        syncedOrderRevision = order.Revision;
                        priceDirty = true;
                    }
                }
            }
            else
            {
                countEditBuffer = orderText;
            }
        }

        private void ApplyThingCount(
            MechanoidOvermindOrder order,
            MechanoidOvermindThingCatalogEntry entry,
            int count)
        {
            MechanoidOvermindThingSpec? spec = BuildCurrentSpec(entry);
            if (spec == null)
            {
                return;
            }

            int maximum = GameComponent_OvermindEconomy.Enabled
                ? GameComponent_OvermindEconomy.Current?.AvailableCount(spec, order) ?? 0
                : MechanoidOvermindOrder.ThingMaxCount;
            order.SetThingCount(spec, Math.Min(count, maximum));
            selectedCount = order.GetThingCount(spec);
            countEditBuffer = selectedCount.ToString();
            syncedOrderRevision = order.Revision;
            priceDirty = true;
        }

        private void RefreshPriceIfNeeded(MechanoidOvermindThingCatalogEntry entry, MechanoidOvermindOrder order)
        {
            MechanoidOvermindThingSpec? spec = BuildCurrentSpec(entry);
            if (spec == null)
            {
                cachedUnitMarketValue = 0f;
                cachedEstimatedCredits = 0;
                return;
            }

            if (GameComponent_OvermindEconomy.Enabled)
            {
                // 时间、全局需求与其他规格均能改变价格，不复用旧的静态价格缓存。
                GameComponent_OvermindEconomy? economy = GameComponent_OvermindEconomy.Current;
                cachedEstimatedCredits = 0;
                economy?.TryEstimateLine(spec, selectedCount, order, out cachedEstimatedCredits);
                priceDirty = true;
                return;
            }

            if (!priceDirty
                && pricedSpec != null
                && pricedSpec.Equals(spec)
                && cachedPricedCount == selectedCount)
            {
                return;
            }

            pricedSpec = spec;
            cachedPricedCount = selectedCount;
            priceDirty = false;
            if (selectedCount <= 0)
            {
                cachedEstimatedCredits = 0;
                if (!MechanoidOvermindPricingService.TryGetThingUnitMarketValue(
                        spec,
                        out cachedUnitMarketValue))
                {
                    cachedUnitMarketValue = 0f;
                }

                return;
            }

            if (!MechanoidOvermindPricingService.TryGetThingUnitMarketValue(
                    spec,
                    out cachedUnitMarketValue))
            {
                cachedUnitMarketValue = 0f;
                cachedEstimatedCredits = 0;
                return;
            }

            double sum = (double)cachedUnitMarketValue * selectedCount;
            int credits = (int)Math.Ceiling(sum / 5d);
            cachedEstimatedCredits = credits < 1 ? 1 : credits;

            // 物资订单按当前评级享受统一折扣（与订单最终费用、扣款共用同一折扣率）。
            float discountRate = PurgeDirectiveRatingUtility.GetDiscountRate();
            if (discountRate > 0f && cachedEstimatedCredits > 0)
            {
                MechanoidOvermindRatingPricingService.ApplyDiscount(
                    cachedEstimatedCredits,
                    discountRate,
                    out long finalCost,
                    out _);
                cachedEstimatedCredits = (int)finalCost;
            }
        }

        private MechanoidOvermindThingSpec? BuildCurrentSpec(
            MechanoidOvermindThingCatalogEntry entry)
        {
            if (entry.MadeFromStuff && selectedStuff == null)
            {
                return null;
            }

            return new MechanoidOvermindThingSpec(
                entry.Def,
                entry.MadeFromStuff ? selectedStuff : null,
                entry.HasQuality,
                entry.HasQuality ? selectedQuality : QualityCategory.Normal);
        }

        private void OpenStuffMenu(ThingDef def, MechanoidOvermindOrder order)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            IReadOnlyList<ThingDef> stuffs =
                MechanoidOvermindCatalogService.GetStuffCandidates(def);
            for (int i = 0; i < stuffs.Count; i++)
            {
                ThingDef stuff = stuffs[i];
                options.Add(
                    new FloatMenuOption(
                        stuff.LabelCap,
                        () =>
                        {
                            selectedStuff = stuff;
                            SyncSelectedCountFromOrder(order);
                            syncedOrderRevision = order.Revision;
                            priceDirty = true;
                        }));
            }

            if (options.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private void OpenQualityMenu(MechanoidOvermindOrder order)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (QualityCategory quality in (QualityCategory[])Enum.GetValues(
                         typeof(QualityCategory)))
            {
                QualityCategory local = quality;
                options.Add(
                    new FloatMenuOption(
                        local.GetLabel().CapitalizeFirst(),
                        () =>
                        {
                            selectedQuality = local;
                            SyncSelectedCountFromOrder(order);
                            syncedOrderRevision = order.Revision;
                            priceDirty = true;
                        }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static string GetCategoryLabel(MechanoidOvermindThingCategory category)
        {
            switch (category)
            {
                case MechanoidOvermindThingCategory.Weapon:
                    return "MAP_MechanoidMechanitor.PurgeDirective.Communication.Category.Weapon"
                        .Translate();
                case MechanoidOvermindThingCategory.Apparel:
                    return "MAP_MechanoidMechanitor.PurgeDirective.Communication.Category.Apparel"
                        .Translate();
                case MechanoidOvermindThingCategory.Food:
                    return "MAP_MechanoidMechanitor.PurgeDirective.Communication.Category.Food"
                        .Translate();
                case MechanoidOvermindThingCategory.Medicine:
                    return "MAP_MechanoidMechanitor.PurgeDirective.Communication.Category.Medicine"
                        .Translate();
                case MechanoidOvermindThingCategory.Material:
                    return "MAP_MechanoidMechanitor.PurgeDirective.Communication.Category.Material"
                        .Translate();
                case MechanoidOvermindThingCategory.Building:
                    return "MAP_MechanoidMechanitor.PurgeDirective.Communication.Category.Building"
                        .Translate();
                case MechanoidOvermindThingCategory.Special:
                    return "MAP_MechanoidMechanitor.PurgeDirective.Communication.Category.Special"
                        .Translate();
                default:
                    return "MAP_MechanoidMechanitor.PurgeDirective.Communication.Category.Other"
                        .Translate();
            }
        }
    }
}
