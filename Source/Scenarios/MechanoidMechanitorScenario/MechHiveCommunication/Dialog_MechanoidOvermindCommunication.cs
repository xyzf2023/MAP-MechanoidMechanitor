using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class Dialog_MechanoidOvermindCommunication : Window
    {
        private const float IdealWidth = 1180f;

        private const float IdealHeight = 720f;

        private const float MinVirtualWidth = 1100f;

        private const float MinVirtualHeight = 640f;

        private const float TopBarHeight = 72f;

        private const float BottomBarHeight = 36f;

        private const float NavWidth = 168f;

        private const float OrderWidth = 300f;

        private const float Gap = 8f;

        private const float BootDuration = 0.5f;

        private readonly Faction mechHive;

        private readonly Map? preferredDeliveryMap;

        private readonly MechanoidOvermindOrder order = new MechanoidOvermindOrder();

        private readonly MechanoidOvermindPage_Mechs mechsPage = new MechanoidOvermindPage_Mechs();

        private readonly MechanoidOvermindPage_Goods goodsPage = new MechanoidOvermindPage_Goods();

        private readonly MechanoidOvermindPage_Battlefield battlefieldPage =
            new MechanoidOvermindPage_Battlefield();

        private readonly MechanoidOvermindPage_Chat chatPage = new MechanoidOvermindPage_Chat();

        private MechanoidOvermindPageKind currentPage = MechanoidOvermindPageKind.Mechs;

        private Vector2 windowScroll;

        private Vector2 orderScroll;

        private readonly float openedRealtime;

        private string statusKey =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Status.Connecting";

        private Map? cachedDropMap;

        private IntVec3 cachedDropCell = IntVec3.Invalid;

        private bool cachedDropValid;

        private int cachedOrderSignature = int.MinValue;

        public override Vector2 InitialSize
        {
            get
            {
                float width = Mathf.Min(IdealWidth, UI.screenWidth - 36f);
                float height = Mathf.Min(IdealHeight, UI.screenHeight - 36f);
                return new Vector2(
                    Mathf.Max(720f, width),
                    Mathf.Max(480f, height));
            }
        }

        public Dialog_MechanoidOvermindCommunication(
            Faction mechHive,
            Map? preferredDeliveryMap)
        {
            this.mechHive = mechHive;
            this.preferredDeliveryMap = preferredDeliveryMap;
            forcePause = false;
            doCloseX = true;
            doCloseButton = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            openedRealtime = Time.realtimeSinceStartup;
            RefreshDropSpotCache(force: true);
        }

        public override void PreClose()
        {
            base.PreClose();
            order.Clear();
            MechanoidOvermindPricingService.ClearThingMarketValueCache();
        }

        public override void DoWindowContents(Rect inRect)
        {
            using (MechanoidOvermindUiStyle.Push())
            {
                UpdateBootStatus();
                MechanoidOvermindUiStyle.DrawBackground(inRect);

                bool needsVirtualScroll = inRect.width < MinVirtualWidth
                    || inRect.height < MinVirtualHeight;
                if (needsVirtualScroll)
                {
                    Rect viewRect = new Rect(0f, 0f, MinVirtualWidth, MinVirtualHeight);
                    Widgets.BeginScrollView(inRect, ref windowScroll, viewRect);
                    DrawLayout(viewRect);
                    Widgets.EndScrollView();
                }
                else
                {
                    DrawLayout(inRect);
                }
            }
        }

        private void DrawLayout(Rect inRect)
        {
            Rect topRect = new Rect(inRect.x, inRect.y, inRect.width, TopBarHeight);
            DrawTopBar(topRect);

            Rect bottomRect = new Rect(
                inRect.x,
                inRect.yMax - BottomBarHeight,
                inRect.width,
                BottomBarHeight);
            DrawBottomBar(bottomRect);

            float bodyY = topRect.yMax + Gap;
            float bodyHeight = Mathf.Max(0f, bottomRect.y - Gap - bodyY);
            Rect navRect = new Rect(inRect.x, bodyY, NavWidth, bodyHeight);
            DrawNavigation(navRect);

            Rect orderRect = new Rect(
                inRect.xMax - OrderWidth,
                bodyY,
                OrderWidth,
                bodyHeight);
            DrawOrderPanel(orderRect);

            Rect centerRect = new Rect(
                navRect.xMax + Gap,
                bodyY,
                Mathf.Max(0f, orderRect.x - Gap - (navRect.xMax + Gap)),
                bodyHeight);
            DrawCenterPage(centerRect);
        }

        private void DrawTopBar(Rect rect)
        {
            MechanoidOvermindUiStyle.DrawPanel(rect);
            Rect inner = rect.ContractedBy(10f);

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(inner.x, inner.y, inner.width * 0.55f, 26f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Title".Translate(),
                GameFont.Medium);

            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(inner.x, inner.y + 28f, inner.width * 0.55f, 22f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Subtitle".Translate());

            int credits = GameComponent_MechanoidMechanitorStoryState.GetPurgeDirectiveRewardPoints();
            float rightWidth = inner.width * 0.42f;
            float rightX = inner.xMax - rightWidth;
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rightX, inner.y, rightWidth, 20f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Credits".Translate(credits),
                GameFont.Small,
                TextAnchor.MiddleRight,
                MechanoidOvermindUiStyle.AccentBright);
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rightX, inner.y + 20f, rightWidth, 18f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.ConnectionStable".Translate(),
                GameFont.Tiny,
                TextAnchor.MiddleRight,
                MechanoidOvermindUiStyle.TextSecondary);
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rightX, inner.y + 38f, rightWidth, 18f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.NodePermission".Translate(
                    GetNodePermissionLabel()),
                GameFont.Tiny,
                TextAnchor.MiddleRight,
                MechanoidOvermindUiStyle.TextSecondary);
        }

        private void DrawNavigation(Rect rect)
        {
            MechanoidOvermindUiStyle.DrawPanel(rect);
            Rect inner = rect.ContractedBy(8f);
            float buttonHeight = 36f;
            float y = inner.y;

            DrawNavItem(
                new Rect(inner.x, y, inner.width, buttonHeight),
                MechanoidOvermindPageKind.Mechs,
                "MAP_MechanoidMechanitor.MechHiveCommunication.Nav.Mechs".Translate());
            y += buttonHeight + 6f;

            DrawNavItem(
                new Rect(inner.x, y, inner.width, buttonHeight),
                MechanoidOvermindPageKind.Goods,
                "MAP_MechanoidMechanitor.MechHiveCommunication.Nav.Goods".Translate());
            y += buttonHeight + 6f;

            DrawNavItem(
                new Rect(inner.x, y, inner.width, buttonHeight),
                MechanoidOvermindPageKind.BattlefieldSupport,
                "MAP_MechanoidMechanitor.MechHiveCommunication.Nav.Battlefield".Translate());
            y += buttonHeight + 6f;

            DrawNavItem(
                new Rect(inner.x, y, inner.width, buttonHeight),
                MechanoidOvermindPageKind.Chat,
                "MAP_MechanoidMechanitor.MechHiveCommunication.Nav.Chat".Translate());

            Rect disconnectRect = new Rect(
                inner.x,
                inner.yMax - buttonHeight,
                inner.width,
                buttonHeight);
            if (MechanoidOvermindUiStyle.DrawNavButton(
                    disconnectRect,
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Nav.Disconnect".Translate(),
                    selected: false))
            {
                Close(doCloseSound: true);
            }
        }

        private void DrawNavItem(Rect rect, MechanoidOvermindPageKind page, string label)
        {
            bool selected = currentPage == page;
            if (MechanoidOvermindUiStyle.DrawNavButton(rect, label, selected) && !selected)
            {
                currentPage = page;
                statusKey = "MAP_MechanoidMechanitor.MechHiveCommunication.Status.WaitingInput";
            }
        }

        private void DrawCenterPage(Rect rect)
        {
            switch (currentPage)
            {
                case MechanoidOvermindPageKind.Mechs:
                    mechsPage.Draw(rect, order);
                    break;
                case MechanoidOvermindPageKind.Goods:
                    goodsPage.Draw(rect, order);
                    break;
                case MechanoidOvermindPageKind.BattlefieldSupport:
                    battlefieldPage.Draw(rect);
                    break;
                case MechanoidOvermindPageKind.Chat:
                    chatPage.Draw(rect);
                    break;
            }
        }

        private void DrawOrderPanel(Rect rect)
        {
            MechanoidOvermindUiStyle.DrawPanel(rect);
            Rect inner = rect.ContractedBy(8f);

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(inner.x, inner.y, inner.width, 24f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Order.Title".Translate(),
                GameFont.Small);

            bool costsOk = order.TryGetCosts(
                out int mechCost,
                out int thingCost,
                out int totalCost);
            int credits = GameComponent_MechanoidMechanitorStoryState
                .GetPurgeDirectiveRewardPoints();
            RefreshDropSpotCache(force: false);

            float footerHeight = 168f;
            Rect listRect = new Rect(
                inner.x,
                inner.y + 28f,
                inner.width,
                Mathf.Max(40f, inner.height - footerHeight - 28f));
            DrawOrderLines(listRect, costsOk);

            Rect footer = new Rect(
                inner.x,
                listRect.yMax + 6f,
                inner.width,
                footerHeight - 6f);
            DrawOrderFooter(footer, costsOk, mechCost, thingCost, totalCost, credits);
        }

        private void DrawOrderLines(Rect listRect, bool costsOk)
        {
            MechanoidOvermindUiStyle.DrawPanel(listRect, alt: true, cornerMarks: false);
            int lineCount = order.MechLines.Count + order.ThingLines.Count;
            float viewHeight = Mathf.Max(listRect.height, lineCount * 54f + 4f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(listRect, ref orderScroll, viewRect);

            float y = 2f;
            for (int i = 0; i < order.MechLines.Count; i++)
            {
                MechanoidOvermindOrderLine_Mech line = order.MechLines[i];
                int subtotal = 0;
                if (costsOk
                    && MechanoidOvermindPricingService.TryGetMechUnitPrice(
                        line.Kind,
                        out int unit))
                {
                    subtotal = unit * line.Count;
                }

                if (DrawOrderLine(
                        new Rect(4f, y, viewRect.width - 8f, 50f),
                        line.Kind.LabelCap,
                        null,
                        null,
                        line.Count,
                        subtotal))
                {
                    order.RemoveMechAt(i);
                    InvalidateDropSpotSignature();
                    break;
                }

                y += 52f;
            }

            for (int i = 0; i < order.ThingLines.Count; i++)
            {
                MechanoidOvermindOrderLine_Thing line = order.ThingLines[i];
                string? stuffLabel = line.Spec.Stuff != null
                    ? line.Spec.Stuff.LabelCap
                    : null;
                string? qualityLabel = line.Spec.HasQuality
                    ? line.Spec.Quality.GetLabel().CapitalizeFirst()
                    : null;
                int subtotal = 0;
                if (costsOk
                    && MechanoidOvermindPricingService.TryGetThingUnitMarketValue(
                        line.Spec,
                        out float mv))
                {
                    subtotal = Mathf.RoundToInt(mv * line.Count);
                }

                if (DrawOrderLine(
                        new Rect(4f, y, viewRect.width - 8f, 50f),
                        line.Spec.Def.LabelCap,
                        stuffLabel,
                        qualityLabel,
                        line.Count,
                        subtotal))
                {
                    order.RemoveThingAt(i);
                    InvalidateDropSpotSignature();
                    break;
                }

                y += 52f;
            }

            Widgets.EndScrollView();
        }

        private bool DrawOrderLine(
            Rect rect,
            string name,
            string? stuff,
            string? quality,
            int count,
            int subtotal)
        {
            Widgets.DrawBoxSolid(rect, MechanoidOvermindUiStyle.Panel);
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.x + 6f, rect.y + 4f, rect.width - 70f, 18f),
                name);
            string meta = "MAP_MechanoidMechanitor.MechHiveCommunication.Order.LineMeta"
                .Translate(
                    count,
                    subtotal,
                    stuff ?? "-",
                    quality ?? "-");
            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(rect.x + 6f, rect.y + 24f, rect.width - 70f, 20f),
                meta);

            return MechanoidOvermindUiStyle.DrawActionButton(
                new Rect(rect.xMax - 58f, rect.y + 11f, 52f, 28f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Order.Remove".Translate());
        }

        private void DrawOrderFooter(
            Rect rect,
            bool costsOk,
            int mechCost,
            int thingCost,
            int totalCost,
            int credits)
        {
            bool connected = MechanoidMechanitorMechHiveCommunicationUtility
                .TryGetContactableMechHive(out _);
            bool hasMap = ResolvePreferredOrFallbackMap() != null;
            bool canConfirm = costsOk
                && !order.IsEmpty
                && connected
                && hasMap
                && cachedDropValid
                && credits >= totalCost;

            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(rect.x, rect.y, rect.width, 18f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Order.MechCost".Translate(
                    costsOk ? mechCost : 0));
            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(rect.x, rect.y + 18f, rect.width, 18f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Order.ThingCost".Translate(
                    costsOk ? thingCost : 0));
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.x, rect.y + 38f, rect.width, 20f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Order.Total".Translate(
                    costsOk ? totalCost : 0),
                GameFont.Small,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.AccentBright);
            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(rect.x, rect.y + 58f, rect.width, 18f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Order.CurrentCredits".Translate(
                    credits));
            int balance = costsOk ? credits - totalCost : credits;
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.x, rect.y + 76f, rect.width, 18f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Order.BalanceAfter".Translate(
                    balance),
                GameFont.Tiny,
                TextAnchor.MiddleLeft,
                balance < 0
                    ? MechanoidOvermindUiStyle.Error
                    : MechanoidOvermindUiStyle.TextSecondary);

            if (!cachedDropValid || !hasMap)
            {
                MechanoidOvermindUiStyle.DrawLabel(
                    new Rect(rect.x, rect.y + 96f, rect.width, 18f),
                    (!hasMap
                        ? "MAP_MechanoidMechanitor.MechHiveCommunication.Error.NoMap"
                        : "MAP_MechanoidMechanitor.MechHiveCommunication.Error.NoDropSpot")
                    .Translate(),
                    GameFont.Tiny,
                    TextAnchor.MiddleLeft,
                    MechanoidOvermindUiStyle.Error);
            }
            else if (!connected)
            {
                MechanoidOvermindUiStyle.DrawLabel(
                    new Rect(rect.x, rect.y + 96f, rect.width, 18f),
                    MechanoidOvermindDeliveryService.ErrorConnection.Translate(),
                    GameFont.Tiny,
                    TextAnchor.MiddleLeft,
                    MechanoidOvermindUiStyle.Error);
            }

            if (MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(rect.x, rect.yMax - 64f, rect.width, 28f),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Order.Clear".Translate()))
            {
                order.Clear();
                InvalidateDropSpotSignature();
                statusKey = "MAP_MechanoidMechanitor.MechHiveCommunication.Status.WaitingInput";
            }

            string confirmLabel =
                "MAP_MechanoidMechanitor.MechHiveCommunication.Order.Confirm".Translate(
                    costsOk ? totalCost : 0);
            if (MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(rect.x, rect.yMax - 30f, rect.width, 28f),
                    confirmLabel,
                    enabled: canConfirm))
            {
                TryConfirmDelivery();
            }
        }

        private void DrawBottomBar(Rect rect)
        {
            MechanoidOvermindUiStyle.DrawPanel(rect, cornerMarks: false);
            Color color = MechanoidOvermindUiStyle.TextSecondary;
            if (statusKey.IndexOf("Error", System.StringComparison.Ordinal) >= 0
                || statusKey.IndexOf("Insufficient", System.StringComparison.Ordinal) >= 0)
            {
                color = MechanoidOvermindUiStyle.Error;
            }
            else if (statusKey.IndexOf("Accepted", System.StringComparison.Ordinal) >= 0
                || statusKey.IndexOf("DropStarted", System.StringComparison.Ordinal) >= 0
                || statusKey.IndexOf("CreditsSpent", System.StringComparison.Ordinal) >= 0
                || statusKey.IndexOf("Connected", System.StringComparison.Ordinal) >= 0)
            {
                color = MechanoidOvermindUiStyle.AccentBright;
            }

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, rect.height - 8f),
                statusKey.Translate(),
                GameFont.Tiny,
                TextAnchor.MiddleLeft,
                color);
        }

        private void TryConfirmDelivery()
        {
            statusKey = "MAP_MechanoidMechanitor.MechHiveCommunication.Status.Validating";
            RefreshDropSpotCache(force: true);

            if (!MechanoidMechanitorMechHiveCommunicationUtility.TryGetContactableMechHive(out _))
            {
                statusKey = MechanoidOvermindDeliveryService.ErrorConnection;
                return;
            }

            statusKey = "MAP_MechanoidMechanitor.MechHiveCommunication.Status.CheckingPermission";
            MechanoidOvermindDeliveryResult result =
                MechanoidOvermindDeliveryService.TryDeliver(order, preferredDeliveryMap);
            if (result.Success)
            {
                order.Clear();
                InvalidateDropSpotSignature();
                // 最终状态：额度扣除与投送已在交付服务中完成。
                statusKey =
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Status.Accepted";
                SoundDefOf.Click.PlayOneShotOnCamera();
                return;
            }

            if (result.ErrorKey == MechanoidOvermindDeliveryService.ErrorInsufficientCredits)
            {
                statusKey =
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Status.InsufficientCredits";
                return;
            }

            statusKey = result.ErrorKey
                ?? MechanoidOvermindDeliveryService.ErrorInvalidOrder;
        }

        private void UpdateBootStatus()
        {
            float elapsed = Time.realtimeSinceStartup - openedRealtime;
            if (elapsed >= BootDuration)
            {
                if (statusKey.StartsWith(
                        "MAP_MechanoidMechanitor.MechHiveCommunication.Status.Connecting")
                    || statusKey.StartsWith(
                        "MAP_MechanoidMechanitor.MechHiveCommunication.Status.Verifying")
                    || statusKey.StartsWith(
                        "MAP_MechanoidMechanitor.MechHiveCommunication.Status.SyncingCredits")
                    || statusKey
                        == "MAP_MechanoidMechanitor.MechHiveCommunication.Status.Connected")
                {
                    statusKey =
                        "MAP_MechanoidMechanitor.MechHiveCommunication.Status.WaitingInput";
                }

                return;
            }

            if (elapsed < BootDuration * 0.25f)
            {
                statusKey =
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Status.Connecting";
            }
            else if (elapsed < BootDuration * 0.5f)
            {
                statusKey =
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Status.Verifying";
            }
            else if (elapsed < BootDuration * 0.75f)
            {
                statusKey =
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Status.SyncingCredits";
            }
            else
            {
                statusKey =
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Status.Connected";
            }
        }

        private void RefreshDropSpotCache(bool force)
        {
            int signature = ComputeOrderSignature();
            Map? map = ResolvePreferredOrFallbackMap();
            if (!force
                && cachedDropMap == map
                && cachedOrderSignature == signature)
            {
                return;
            }

            cachedOrderSignature = signature;
            cachedDropMap = map;
            cachedDropCell = IntVec3.Invalid;
            cachedDropValid = false;
            if (map == null)
            {
                return;
            }

            IntVec3 cell = DropCellFinder.TradeDropSpot(map);
            cachedDropCell = cell;
            cachedDropValid = cell.IsValid;
        }

        private void InvalidateDropSpotSignature()
        {
            cachedOrderSignature = int.MinValue;
        }

        private int ComputeOrderSignature()
        {
            unchecked
            {
                int hash = order.MechLines.Count * 397 ^ order.ThingLines.Count;
                for (int i = 0; i < order.MechLines.Count; i++)
                {
                    MechanoidOvermindOrderLine_Mech line = order.MechLines[i];
                    hash = (hash * 31)
                        ^ (line.Kind?.shortHash ?? 0)
                        ^ (line.Count << 8);
                }

                for (int i = 0; i < order.ThingLines.Count; i++)
                {
                    MechanoidOvermindOrderLine_Thing line = order.ThingLines[i];
                    hash = (hash * 31) ^ line.Spec.GetHashCode() ^ (line.Count << 8);
                }

                Map? map = preferredDeliveryMap;
                hash = (hash * 31) ^ (map != null ? map.uniqueID : 0);
                return hash;
            }
        }

        private Map? ResolvePreferredOrFallbackMap()
        {
            if (preferredDeliveryMap != null && !preferredDeliveryMap.Disposed)
            {
                return preferredDeliveryMap;
            }

            if (Find.CurrentMap != null && !Find.CurrentMap.Disposed)
            {
                return Find.CurrentMap;
            }

            return Find.AnyPlayerHomeMap;
        }

        private static string GetNodePermissionLabel()
        {
            return "MAP_MechanoidMechanitor.MechHiveCommunication.NodePermission.EdgeExecUnit"
                .Translate();
        }
    }
}
