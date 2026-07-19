using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

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

        private const float SpecPanelHeight = 150f;

        private string search = string.Empty;

        private MechanoidOvermindThingCategory? categoryFilter;

        private SortMode sortMode = SortMode.Name;

        private Vector2 listScroll;

        private string? cachedSearch;

        private MechanoidOvermindThingCategory? cachedCategory;

        private SortMode cachedSort = SortMode.Name;

        private bool filterBuilt;

        private readonly List<MechanoidOvermindThingCatalogEntry> filtered =
            new List<MechanoidOvermindThingCatalogEntry>();

        private MechanoidOvermindThingCatalogEntry? selected;

        private ThingDef? selectedStuff;

        private QualityCategory selectedQuality = QualityCategory.Normal;

        private int selectedCount = 1;

        private MechanoidOvermindThingSpec? pricedSpec;

        private float cachedUnitMarketValue;

        private int cachedEstimatedCredits;

        private bool priceDirty = true;

        public void Draw(Rect inRect, MechanoidOvermindOrder order)
        {
            using (MechanoidOvermindUiStyle.Push())
            {
                MechanoidOvermindUiStyle.DrawPanel(inRect);
                Rect inner = inRect.ContractedBy(8f);

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
                DrawList(listRect);
                y = listRect.yMax + 8f;

                Rect specRect = new Rect(inner.x, y, inner.width, SpecPanelHeight);
                DrawSpecPanel(specRect, order);
            }
        }

        private void DrawCategoryRow(Rect rect)
        {
            string[] labels =
            {
                "MAP_MechanoidMechanitor.MechHiveCommunication.Category.All".Translate(),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Category.Weapon".Translate(),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Category.Apparel".Translate(),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Category.Food".Translate(),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Category.Medicine".Translate(),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Category.Material".Translate(),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Category.Building".Translate(),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Category.Special".Translate(),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Category.Other".Translate()
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
                "MAP_MechanoidMechanitor.MechHiveCommunication.Sort.Name".Translate(),
                SortMode.Name);
            DrawSortButton(
                new Rect(rect.x + w, rect.y, w - 2f, rect.height),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Sort.ValueAsc".Translate(),
                SortMode.MarketValueAsc);
            DrawSortButton(
                new Rect(rect.x + w * 2f, rect.y, w - 2f, rect.height),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Sort.ValueDesc".Translate(),
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
            if (filterBuilt
                && cachedSearch == search
                && cachedCategory == categoryFilter
                && cachedSort == sortMode)
            {
                return;
            }

            cachedSearch = search;
            cachedCategory = categoryFilter;
            cachedSort = sortMode;
            filterBuilt = true;
            filtered.Clear();

            string needle = search.Trim();
            IReadOnlyList<MechanoidOvermindThingCatalogEntry> catalog =
                MechanoidOvermindCatalogService.GetThingCatalog();
            for (int i = 0; i < catalog.Count; i++)
            {
                MechanoidOvermindThingCatalogEntry entry = catalog[i];
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

        private void DrawList(Rect listRect)
        {
            MechanoidOvermindUiStyle.DrawPanel(listRect, alt: true, cornerMarks: false);
            Rect viewRect = new Rect(
                0f,
                0f,
                listRect.width - 16f,
                Mathf.Max(listRect.height, filtered.Count * (RowHeight + 2f)));
            Widgets.BeginScrollView(listRect, ref listScroll, viewRect);

            float y = 0f;
            for (int i = 0; i < filtered.Count; i++)
            {
                Rect rowRect = new Rect(4f, y, viewRect.width - 8f, RowHeight);
                DrawRow(rowRect, filtered[i]);
                y += RowHeight + 2f;
            }

            Widgets.EndScrollView();
        }

        private void DrawRow(Rect rowRect, MechanoidOvermindThingCatalogEntry entry)
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

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(iconRect.xMax + 8f, rowRect.y + 2f, rowRect.width - 160f, 18f),
                entry.Def.LabelCap);

            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(iconRect.xMax + 8f, rowRect.y + 20f, rowRect.width - 160f, 16f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.GoodsRowMeta".Translate(
                    GetCategoryLabel(entry.Category),
                    entry.DefaultReferenceMarketValue.ToStringMoney()));

            if (Widgets.ButtonInvisible(rowRect))
            {
                SelectEntry(entry);
            }
        }

        private void SelectEntry(MechanoidOvermindThingCatalogEntry entry)
        {
            selected = entry;
            selectedStuff = entry.MadeFromStuff
                ? GenStuff.DefaultStuffFor(entry.Def)
                : null;
            selectedQuality = QualityCategory.Normal;
            selectedCount = 1;
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
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Goods.SelectHint".Translate());
                return;
            }

            MechanoidOvermindThingCatalogEntry entry = selected;
            float x = inner.x;
            float y = inner.y;

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(x, y, inner.width, 22f),
                entry.Def.LabelCap,
                GameFont.Small);
            y += 26f;

            if (entry.MadeFromStuff)
            {
                Rect stuffRect = new Rect(x, y, 220f, 28f);
                string stuffLabel = selectedStuff != null
                    ? selectedStuff.LabelCap
                    : "MAP_MechanoidMechanitor.MechHiveCommunication.Stuff.None".Translate();
                if (MechanoidOvermindUiStyle.DrawActionButton(
                        stuffRect,
                        "MAP_MechanoidMechanitor.MechHiveCommunication.Stuff".Translate(
                            stuffLabel)))
                {
                    OpenStuffMenu(entry.Def);
                }

                x += 230f;
            }

            if (entry.HasQuality)
            {
                Rect qualityRect = new Rect(x, y, 180f, 28f);
                if (MechanoidOvermindUiStyle.DrawActionButton(
                        qualityRect,
                        "MAP_MechanoidMechanitor.MechHiveCommunication.Quality".Translate(
                            selectedQuality.GetLabel())))
                {
                    OpenQualityMenu();
                }

                x += 190f;
            }

            Rect countRect = new Rect(x, y, 56f, 28f);
            string countText = Widgets.TextField(countRect, selectedCount.ToString());
            if (int.TryParse(countText, out int parsed))
            {
                int clamped = Mathf.Clamp(parsed, 1, MechanoidOvermindOrder.MaxCount);
                if (clamped != selectedCount)
                {
                    selectedCount = clamped;
                    priceDirty = true;
                }
            }

            if (MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(countRect.xMax + 4f, y, 24f, 28f),
                    "-"))
            {
                selectedCount = Mathf.Max(1, selectedCount - 1);
                priceDirty = true;
            }

            if (MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(countRect.xMax + 32f, y, 24f, 28f),
                    "+"))
            {
                selectedCount = Mathf.Min(MechanoidOvermindOrder.MaxCount, selectedCount + 1);
                priceDirty = true;
            }

            RefreshPriceIfNeeded(entry);

            float infoY = y + 36f;
            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(inner.x, infoY, inner.width, 18f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Goods.UnitValue".Translate(
                    cachedUnitMarketValue.ToStringMoney()));
            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(inner.x, infoY + 18f, inner.width, 18f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Goods.Subtotal".Translate(
                    (cachedUnitMarketValue * selectedCount).ToStringMoney()));
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(inner.x, infoY + 38f, inner.width * 0.6f, 22f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Goods.EstimatedCredits".Translate(
                    cachedEstimatedCredits),
                GameFont.Small,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.AccentBright);

            if (MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(inner.xMax - 140f, inner.yMax - 34f, 140f, 30f),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Add".Translate()))
            {
                MechanoidOvermindThingSpec? spec = BuildCurrentSpec(entry);
                if (spec != null && order.TryAddThing(spec, selectedCount))
                {
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
            }
        }

        private void RefreshPriceIfNeeded(MechanoidOvermindThingCatalogEntry entry)
        {
            MechanoidOvermindThingSpec? spec = BuildCurrentSpec(entry);
            if (spec == null)
            {
                cachedUnitMarketValue = 0f;
                cachedEstimatedCredits = 0;
                return;
            }

            if (!priceDirty && pricedSpec != null && pricedSpec.Equals(spec))
            {
                return;
            }

            pricedSpec = spec;
            priceDirty = false;
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
            if (credits < 1)
            {
                credits = 1;
            }

            cachedEstimatedCredits = credits;
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

        private void OpenStuffMenu(ThingDef def)
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
                            priceDirty = true;
                        }));
            }

            if (options.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private void OpenQualityMenu()
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
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Category.Weapon"
                        .Translate();
                case MechanoidOvermindThingCategory.Apparel:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Category.Apparel"
                        .Translate();
                case MechanoidOvermindThingCategory.Food:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Category.Food"
                        .Translate();
                case MechanoidOvermindThingCategory.Medicine:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Category.Medicine"
                        .Translate();
                case MechanoidOvermindThingCategory.Material:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Category.Material"
                        .Translate();
                case MechanoidOvermindThingCategory.Building:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Category.Building"
                        .Translate();
                case MechanoidOvermindThingCategory.Special:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Category.Special"
                        .Translate();
                default:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Category.Other"
                        .Translate();
            }
        }
    }
}
