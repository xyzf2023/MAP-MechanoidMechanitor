using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidOvermindPage_BandwidthSupport
    {
        private readonly GameComponent_OvermindBandwidthSupport state;
        public BandwidthSupportOrder Order { get; }
        private readonly List<Pawn> allocationTargets;
        private readonly Dictionary<Pawn, int> amounts = new Dictionary<Pawn, int>();
        private readonly Dictionary<Pawn, string> buffers = new Dictionary<Pawn, string>();
        private int requested;
        private string requestBuffer;
        private bool requestDirty;
        private readonly HashSet<Pawn> dirtyAllocations = new HashSet<Pawn>();
        private const float RowHeight = 86f;

        public float ContentHeight => (GameComponent_OvermindBandwidthSupport.TakenOver ? 324f : 406f)
                + Math.Max(1, allocationTargets.Count) * RowHeight;

        public MechanoidOvermindPage_BandwidthSupport(GameComponent_OvermindBandwidthSupport state)
        {
            this.state = state;
            state.Reconcile();
            state.Activate(); // 打开协议即启用基础配额；已启用时不会重置账期。
            Order = new BandwidthSupportOrder(state);
            allocationTargets = state.Candidates().FindAll(pawn => state.AllocatedTo(pawn) > 0);
            requested = state.Requested;
            requestBuffer = requested.ToString();
        }

        private static string T(string key, params NamedArgument[] args) =>
            ("MAP_BandwidthSupport." + key).Translate(args);

        public void Draw(Rect rect)
        {
            MechanoidOvermindUiStyle.DrawPanel(rect, alt: true, cornerMarks: false);
            Rect inner = rect.ContractedBy(12f);
            GUI.BeginGroup(inner);
            try
            {
                DrawContents(new Rect(0f, 0f, inner.width, inner.height));
                CommitPendingEdits(force: false);
            }
            finally { GUI.EndGroup(); }
        }

        private void DrawContents(Rect rect)
        {
            using (MechanoidOvermindUiStyle.Push())
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = false;
                GUI.color = Color.white;
                bool takeover = GameComponent_OvermindBandwidthSupport.TakenOver;
                if (!GameComponent_OvermindBandwidthSupport.Available)
                {
                    MechanoidOvermindUiStyle.DrawLabel(new Rect(0f, 0f, rect.width, 32f), T("Unavailable"));
                    return;
                }
                // 概览采用等宽数值块，避免把多个属性堆在同一行说明中。
                MechanoidOvermindUiStyle.DrawLabel(new Rect(0f, 0f, rect.width, 32f),
                    T("ExpandedDescription"), wordWrap: true);
                int tileCount = takeover ? 2 : 3;
                float tileWidth = (rect.width - (tileCount - 1) * 8f) / tileCount;
                DrawMetric(new Rect(0f, 40f, tileWidth, 66f),
                    T(takeover ? "TotalLabel" : "BaseLabel"),
                    (takeover ? state.Total : state.BaseBandwidth).ToString(),
                    T(takeover ? "TotalTooltip" : "BaseTooltip"));
                if (!takeover)
                    DrawMetric(new Rect(tileWidth + 8f, 40f, tileWidth, 66f), T("ExtensionLabel"),
                        state.Extension.ToString(), T("ExtensionTooltip"));
                DrawMetric(new Rect((tileCount - 1) * (tileWidth + 8f), 40f, tileWidth, 66f),
                    T("UnallocatedLabel"), Math.Max(0L, Order.Total - Order.Allocated).ToString(),
                    T("UnallocatedTooltip"));

                float y = 120f;
                if (!takeover)
                {
                    DrawInfo(y, rect.width, T("CycleLabel"),
                        T("CycleValue", state.NextCost, GameComponent_OvermindBandwidthSupport.PeriodDays));
                    DrawInfo(y + 24f, rect.width, T("SettlementLabel"),
                        T("DaysValue", (state.RemainingTicks / (float)GenDate.TicksPerDay).ToString("0.0")));
                    DrawInfo(y + 48f, rect.width, T("CreditsLabel"),
                        GameComponent_MechanoidMechanitorStoryState.GetPurgeDirectiveRewardPoints().ToString());
                    y += 82f;
                }

                DrawSection(y, rect.width, T(takeover ? "RequestedTotal" : "RequestedExtension"));
                y += 30f;
                float selectorWidth = Mathf.Min(240f, rect.width - 124f);
                string previousRequestBuffer = requestBuffer;
                MechanoidOvermindUiStyle.DrawNumberStepper(
                    new Rect(0f, y, selectorWidth, 34f), "MAP_BandwidthRequest",
                    ref requested, ref requestBuffer,
                    takeover ? 5 : GameComponent_OvermindBandwidthSupport.Unit);
                if (requestBuffer != previousRequestBuffer) requestDirty = true;
                int desired = takeover ? requested
                    : requested - requested % GameComponent_OvermindBandwidthSupport.Unit;
                y += 42f;
                if (!takeover)
                {
                    long cost = state.ChangeCost(desired);
                    string hint = desired < state.Extension ? T("NextCycle")
                        : cost > 0 ? T("ImmediateCost", cost) : string.Empty;
                    MechanoidOvermindUiStyle.DrawLabel(new Rect(0f, y, rect.width - 124f, 30f), hint,
                        GameFont.Tiny, color: cost > GameComponent_MechanoidMechanitorStoryState.GetPurgeDirectiveRewardPoints()
                            ? MechanoidOvermindUiStyle.Warning : MechanoidOvermindUiStyle.TextSecondary);
                    if (MechanoidOvermindUiStyle.DrawActionButton(new Rect(rect.width - 112f, y, 112f, 30f),
                        T("Withdraw"), Order.Requested > 0))
                    {
                        requestDirty = false;
                        if (!Order.TrySetRequested(0)) Reject();
                        requested = Order.Requested;
                        requestBuffer = requested.ToString();
                    }
                }
                // 两种模式保留一致的分区间距；接管模式不绘制额度/周期控件。
                y += 44f;
                DrawSection(y, rect.width - 150f, T("AllocationTitle"));
                if (MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(rect.width - 144f, y, 144f, 26f), T("AddTarget")))
                    OpenAddTargetMenu();
                y += 30f;
                MechanoidOvermindUiStyle.DrawSecondaryLabel(new Rect(0f, y, rect.width, 22f),
                    T("Allocated", Order.Allocated, Order.Total));
                y += 26f;
                Rect track = new Rect(0f, y, rect.width, 4f);
                Widgets.DrawBoxSolid(track, MechanoidOvermindUiStyle.Background);
                float fraction = Order.Total > 0 ? Mathf.Clamp01((float)Order.Allocated / Order.Total) : 0f;
                if (fraction > 0f)
                    Widgets.DrawBoxSolid(new Rect(track.x, track.y, track.width * fraction, track.height),
                        MechanoidOvermindUiStyle.AccentBright);
                y += 14f;

                int shown = 0;
                foreach (Pawn pawn in allocationTargets)
                {
                    if (!GameComponent_OvermindBandwidthSupport.Eligible(pawn)) continue;
                    DrawPawn(pawn, new Rect(0f, y, rect.width, RowHeight - 8f));
                    y += RowHeight;
                    shown++;
                }
                if (shown == 0)
                    MechanoidOvermindUiStyle.DrawSecondaryLabel(new Rect(0f, y, rect.width, 40f), T("NoTargets"));
            }
        }

        private void OpenAddTargetMenu()
        {
            var options = new List<FloatMenuOption>();
            foreach (Pawn pawn in state.Candidates())
            {
                if (allocationTargets.Contains(pawn)) continue;
                Pawn target = pawn;
                options.Add(new FloatMenuOption(target.LabelShortCap, () =>
                {
                    if (!GameComponent_OvermindBandwidthSupport.Eligible(target)
                        || allocationTargets.Contains(target)) return;
                    allocationTargets.Add(target);
                    amounts[target] = state.AllocatedTo(target);
                    buffers[target] = amounts[target].ToString();
                }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption(T("NoAdditionalTargets"), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void DrawMetric(Rect rect, string label, string value, string tooltip)
        {
            TooltipHandler.TipRegion(rect, tooltip);
            Widgets.DrawBoxSolid(rect, MechanoidOvermindUiStyle.Background);
            MechanoidOvermindUiStyle.DrawBorder(rect);
            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(rect.x + 10f, rect.y + 6f, rect.width - 20f, 20f), label);
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.x + 10f, rect.y + 27f, rect.width - 20f, 30f), value,
                GameFont.Medium, color: MechanoidOvermindUiStyle.AccentBright);
        }

        private static void DrawInfo(float y, float width, string label, string value)
        {
            MechanoidOvermindUiStyle.DrawSecondaryLabel(new Rect(0f, y, width * 0.4f, 22f), label);
            MechanoidOvermindUiStyle.DrawLabel(new Rect(width * 0.4f, y, width * 0.6f, 22f),
                value, anchor: TextAnchor.MiddleRight);
        }

        private static void DrawSection(float y, float width, string title)
        {
            Widgets.DrawBoxSolid(new Rect(0f, y + 5f, 3f, 16f), MechanoidOvermindUiStyle.AccentBright);
            MechanoidOvermindUiStyle.DrawLabel(new Rect(12f, y, width - 12f, 26f), title,
                color: MechanoidOvermindUiStyle.AccentBright);
        }

        private void DrawPawn(Pawn pawn, Rect row)
        {
            Widgets.DrawBoxSolid(row, MechanoidOvermindUiStyle.Panel);
            MechanoidOvermindUiStyle.DrawBorder(row);
            int current = state.AllocatedTo(pawn);
            MechanoidOvermindUiStyle.DrawLabel(new Rect(row.x + 10f, row.y + 4f, row.width - 170f, 26f),
                pawn.LabelShortCap.ToString().Truncate(Mathf.Max(40f, row.width - 170f)));
            TooltipHandler.TipRegion(new Rect(row.x + 10f, row.y + 4f, row.width - 170f, 26f), pawn.LabelCap);
            MechanoidOvermindUiStyle.DrawLabel(new Rect(row.xMax - 154f, row.y + 4f, 144f, 26f),
                T("CurrentAllocation", current), GameFont.Tiny, TextAnchor.MiddleRight,
                MechanoidOvermindUiStyle.TextSecondary);

            if (!dirtyAllocations.Contains(pawn))
            {
                amounts[pawn] = Order.AmountFor(pawn);
                buffers[pawn] = amounts[pawn].ToString();
            }
            if (!amounts.TryGetValue(pawn, out int amount)) amount = current;
            string buffer = buffers.TryGetValue(pawn, out string existing) ? existing : amount.ToString();
            string previousBuffer = buffer;
            float fieldWidth = Mathf.Min(220f, row.width - 92f);
            MechanoidOvermindUiStyle.DrawNumberStepper(
                new Rect(row.x + 10f, row.y + 36f, fieldWidth, 32f),
                "MAP_BandwidthAllocation_" + pawn.ThingID, ref amount, ref buffer,
                max: Order.MaxFor(pawn));
            amounts[pawn] = amount;
            buffers[pawn] = buffer;
            if (buffer != previousBuffer) dirtyAllocations.Add(pawn);
            if (MechanoidOvermindUiStyle.DrawActionButton(new Rect(row.xMax - 66f, row.y + 36f, 56f, 32f),
                T("Clear"), Order.AmountFor(pawn) > 0))
            {
                dirtyAllocations.Remove(pawn);
                Order.SetAllocation(pawn, 0);
                amounts[pawn] = 0;
                buffers[pawn] = "0";
            }
        }

        public void CommitPendingEdits(bool force = true)
        {
            string focused = force ? string.Empty : GUI.GetNameOfFocusedControl();
            if (requestDirty && focused != "MAP_BandwidthRequest")
            {
                requestDirty = false;
                if (TryReadAmount(requestBuffer, out int desired))
                {
                    if (!GameComponent_OvermindBandwidthSupport.TakenOver)
                        desired -= desired % GameComponent_OvermindBandwidthSupport.Unit;
                    if (!Order.TrySetRequested(desired)) Reject();
                }
                requested = Order.Requested;
                requestBuffer = requested.ToString();
            }
            foreach (Pawn pawn in new List<Pawn>(dirtyAllocations))
            {
                if (focused == "MAP_BandwidthAllocation_" + pawn.ThingID) continue;
                dirtyAllocations.Remove(pawn);
                if (buffers.TryGetValue(pawn, out string buffer) && TryReadAmount(buffer, out int desired))
                    Order.SetAllocation(pawn, desired);
                amounts[pawn] = Order.AmountFor(pawn);
                buffers[pawn] = amounts[pawn].ToString();
            }
        }

        private static bool TryReadAmount(string buffer, out int amount) => int.TryParse(buffer,
            System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out amount);

        public void ResetOrder()
        {
            Order.Reset();
            requestDirty = false;
            dirtyAllocations.Clear();
            amounts.Clear();
            buffers.Clear();
            requested = Order.Requested;
            requestBuffer = requested.ToString();
        }

        public bool ExecuteOrder()
        {
            CommitPendingEdits();
            if (!Order.Execute()) { Reject(); return false; }
            ResetOrder();
            return true;
        }

        private static void Reject() => Messages.Message(T("Rejected"), MessageTypeDefOf.RejectInput, false);
    }
}
