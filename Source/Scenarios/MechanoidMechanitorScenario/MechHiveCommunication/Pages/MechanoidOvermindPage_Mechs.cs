using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidOvermindPage_Mechs
    {
        private enum WeightFilter : byte
        {
            All,
            Light,
            Medium,
            Heavy,
            UltraHeavy
        }

        private const float ToolbarHeight = 30f;

        private const float FilterHeight = 28f;

        private const float RowHeight = 52f;

        private const float IconSize = 40f;

        private string search = string.Empty;

        private WeightFilter weightFilter = WeightFilter.All;

        private Vector2 scrollPosition;

        private string? cachedSearch;

        private WeightFilter? cachedFilter;

        private readonly List<MechanoidOvermindMechCatalogEntry> filtered =
            new List<MechanoidOvermindMechCatalogEntry>();

        private readonly Dictionary<PawnKindDef, string> countEditBuffers =
            new Dictionary<PawnKindDef, string>();

        private MechanoidOvermindOrder? syncedOrder;

        private int syncedOrderRevision = int.MinValue;

        public void Draw(Rect inRect, MechanoidOvermindOrder order)
        {
            using (MechanoidOvermindUiStyle.Push())
            {
                MechanoidOvermindUiStyle.DrawPanel(inRect);
                Rect inner = inRect.ContractedBy(8f);
                SyncBuffersFromOrderIfNeeded(order);

                Rect searchRect = new Rect(inner.x, inner.y, inner.width, ToolbarHeight);
                string nextSearch = Widgets.TextField(searchRect, search);
                if (nextSearch != search)
                {
                    search = nextSearch ?? string.Empty;
                }

                Rect filterRect = new Rect(
                    inner.x,
                    searchRect.yMax + 6f,
                    inner.width,
                    FilterHeight);
                DrawWeightFilters(filterRect);

                RebuildFilteredIfNeeded();

                Rect listRect = new Rect(
                    inner.x,
                    filterRect.yMax + 6f,
                    inner.width,
                    Mathf.Max(0f, inner.yMax - filterRect.yMax - 6f));
                DrawList(listRect, order);
            }
        }

        private void DrawWeightFilters(Rect rect)
        {
            float buttonWidth = rect.width / 5f;
            DrawFilterButton(
                new Rect(rect.x, rect.y, buttonWidth - 2f, rect.height),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Weight.All".Translate(),
                WeightFilter.All);
            DrawFilterButton(
                new Rect(rect.x + buttonWidth, rect.y, buttonWidth - 2f, rect.height),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Weight.Light".Translate(),
                WeightFilter.Light);
            DrawFilterButton(
                new Rect(rect.x + buttonWidth * 2f, rect.y, buttonWidth - 2f, rect.height),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Weight.Medium".Translate(),
                WeightFilter.Medium);
            DrawFilterButton(
                new Rect(rect.x + buttonWidth * 3f, rect.y, buttonWidth - 2f, rect.height),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Weight.Heavy".Translate(),
                WeightFilter.Heavy);
            DrawFilterButton(
                new Rect(rect.x + buttonWidth * 4f, rect.y, buttonWidth - 2f, rect.height),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Weight.UltraHeavy".Translate(),
                WeightFilter.UltraHeavy);
        }

        private void DrawFilterButton(Rect rect, string label, WeightFilter filter)
        {
            bool selected = weightFilter == filter;
            if (MechanoidOvermindUiStyle.DrawActionButton(rect, label, enabled: true)
                && !selected)
            {
                weightFilter = filter;
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
            if (cachedSearch == search && cachedFilter == weightFilter)
            {
                return;
            }

            cachedSearch = search;
            cachedFilter = weightFilter;
            filtered.Clear();

            string needle = search.Trim();
            string needleLower = needle.NullOrEmpty() ? string.Empty : needle.ToLowerInvariant();
            IReadOnlyList<MechanoidOvermindMechCatalogEntry> catalog =
                MechanoidOvermindCatalogService.GetMechCatalog();
            for (int i = 0; i < catalog.Count; i++)
            {
                MechanoidOvermindMechCatalogEntry entry = catalog[i];
                if (!MatchesWeight(entry))
                {
                    continue;
                }

                if (!needleLower.NullOrEmpty()
                    && !(entry.Kind.label?.ToLowerInvariant().Contains(needleLower) ?? false)
                    && !(entry.Kind.defName?.ToLowerInvariant().Contains(needleLower) ?? false))
                {
                    continue;
                }

                filtered.Add(entry);
            }
        }

        private bool MatchesWeight(MechanoidOvermindMechCatalogEntry entry)
        {
            switch (weightFilter)
            {
                case WeightFilter.All:
                    return true;
                case WeightFilter.Light:
                    return entry.WeightClass == MechWeightClassDefOf.Light
                        || entry.WeightClass == null;
                case WeightFilter.Medium:
                    return entry.WeightClass == MechWeightClassDefOf.Medium;
                case WeightFilter.Heavy:
                    return entry.WeightClass == MechWeightClassDefOf.Heavy;
                case WeightFilter.UltraHeavy:
                    return entry.WeightClass == MechWeightClassDefOf.UltraHeavy;
                default:
                    return true;
            }
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
            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);

            if (filtered.Count > 0)
            {
                int first = Mathf.Max(0, Mathf.FloorToInt(scrollPosition.y / rowStride) - 1);
                int last = Mathf.Min(
                    filtered.Count - 1,
                    Mathf.CeilToInt((scrollPosition.y + listRect.height) / rowStride) + 1);
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
            MechanoidOvermindMechCatalogEntry entry,
            MechanoidOvermindOrder order)
        {
            Widgets.DrawBoxSolid(rowRect, MechanoidOvermindUiStyle.Panel);

            Rect iconRect = new Rect(
                rowRect.x + 4f,
                rowRect.y + (rowRect.height - IconSize) / 2f,
                IconSize,
                IconSize);
            Widgets.DefIcon(iconRect, entry.Race);

            float textX = iconRect.xMax + 8f;
            float textWidth = rowRect.width - IconSize - 180f;
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(textX, rowRect.y + 4f, textWidth, 20f),
                entry.Kind.LabelCap);

            string weightLabel = entry.WeightClass != null
                ? entry.WeightClass.LabelCap
                : "MAP_MechanoidMechanitor.PurgeDirective.Communication.Weight.Light".Translate();
            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(textX, rowRect.y + 24f, textWidth, 20f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.MechRowMeta".Translate(
                    weightLabel,
                    entry.BandwidthCost.ToString("0.##"),
                    entry.PurgePrice));

            int orderCount = order.GetMechCount(entry.Kind);
            Rect countRect = new Rect(rowRect.xMax - 168f, rowRect.y + 12f, 48f, 28f);
            DrawCountField(countRect, entry.Kind, order, orderCount);

            if (MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(rowRect.xMax - 112f, rowRect.y + 12f, 24f, 28f),
                    "-"))
            {
                ApplyMechCount(order, entry.Kind, orderCount - 1);
            }

            if (MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(rowRect.xMax - 84f, rowRect.y + 12f, 24f, 28f),
                    "+"))
            {
                ApplyMechCount(order, entry.Kind, orderCount + 1);
            }

            if (MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(rowRect.xMax - 56f, rowRect.y + 12f, 52f, 28f),
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.ClearItem".Translate()))
            {
                ApplyMechCount(order, entry.Kind, 0);
            }
        }

        private void DrawCountField(
            Rect countRect,
            PawnKindDef kind,
            MechanoidOvermindOrder order,
            int orderCount)
        {
            string orderText = orderCount.ToString();
            if (!countEditBuffers.TryGetValue(kind, out string buffer))
            {
                buffer = orderText;
            }

            string controlName = "MAP_OvermindMechCount_" + kind.defName;
            GUI.SetNextControlName(controlName);
            string next = GUI.TextField(countRect, buffer, Text.CurTextFieldStyle);
            bool focused = GUI.GetNameOfFocusedControl() == controlName;

            if (focused)
            {
                countEditBuffers[kind] = next ?? string.Empty;
                if (int.TryParse(next, out int parsed)
                    && parsed >= 0
                    && parsed <= MechanoidOvermindOrder.MaxCount)
                {
                    order.SetMechCount(kind, parsed);
                    syncedOrderRevision = order.Revision;
                }
            }
            else
            {
                countEditBuffers[kind] = orderText;
            }
        }

        private void ApplyMechCount(MechanoidOvermindOrder order, PawnKindDef kind, int count)
        {
            order.SetMechCount(kind, count);
            countEditBuffers[kind] = order.GetMechCount(kind).ToString();
            syncedOrderRevision = order.Revision;
        }

        private void SyncBuffersFromOrderIfNeeded(MechanoidOvermindOrder order)
        {
            if (ReferenceEquals(syncedOrder, order)
                && syncedOrderRevision == order.Revision)
            {
                return;
            }

            syncedOrder = order;
            syncedOrderRevision = order.Revision;
            countEditBuffers.Clear();
        }
    }
}
