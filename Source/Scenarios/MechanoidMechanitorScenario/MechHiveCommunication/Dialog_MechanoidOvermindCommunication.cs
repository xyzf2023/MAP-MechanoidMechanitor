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

        private const float DialoguePanelHeight = 170f;

        // 订单区下限：内边距16 + 标题28 + 列表下限40 + 间距6 + footer134
        private const float MinOrderPanelHeight = 224f;

        private const float Gap = 8f;

        private const float BootDuration = 0.5f;

        private const float OrderRowHeight = 50f;

        private const float OrderRowStride = 52f;

        private const float DropCacheRealtimeSeconds = 1f;

        private const int DropCacheTickInterval = 60;

        private readonly Faction mechHive;

        private readonly Map? preferredDeliveryMap;

        private readonly MechanoidOvermindOrder order = new MechanoidOvermindOrder();

        private readonly MechanoidOvermindPage_Home homePage = new MechanoidOvermindPage_Home();

        private readonly MechanoidOvermindPage_Mechs mechsPage = new MechanoidOvermindPage_Mechs();

        private readonly MechanoidOvermindPage_Goods goodsPage = new MechanoidOvermindPage_Goods();

        private readonly MechanoidOvermindPage_Battlefield battlefieldPage =
            new MechanoidOvermindPage_Battlefield();

        private readonly MechanoidOvermindPage_Chat chatPage = new MechanoidOvermindPage_Chat();

        private readonly MechanoidOvermindDialogueTyper dialogueTyper =
            new MechanoidOvermindDialogueTyper();

        private MechanoidOvermindPageKind currentPage = MechanoidOvermindPageKind.Home;

        private Vector2 windowScroll;

        private Vector2 orderScroll;

        private Vector2 dialogueScroll;

        private readonly float openedRealtime;

        private string statusKey =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Status.Connecting";

        private Map? cachedDropMap;

        private IntVec3 cachedDropCell = IntVec3.Invalid;

        private bool cachedDropValid;

        private int cachedDropRevision = int.MinValue;

        private float cachedDropRealtime = -999f;

        private int cachedDropTick = int.MinValue;

        private bool devControlsEnabled;

        public override Vector2 InitialSize
        {
            get
            {
                float availW = Mathf.Max(0f, UI.screenWidth - 36f);
                float availH = Mathf.Max(0f, UI.screenHeight - 36f);
                return new Vector2(
                    Mathf.Min(IdealWidth, availW),
                    Mathf.Min(IdealHeight, availH));
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
            StartPageDialogue(MechanoidOvermindPageKind.Home);
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

            Rect rightColumn = new Rect(
                inRect.xMax - OrderWidth,
                bodyY,
                OrderWidth,
                bodyHeight);

            float dialogueHeight = DialoguePanelHeight;
            float orderHeightBudget = rightColumn.height - Gap - MinOrderPanelHeight;
            if (orderHeightBudget < dialogueHeight)
            {
                dialogueHeight = Mathf.Max(80f, orderHeightBudget);
            }

            Rect dialogueRect = new Rect(
                rightColumn.x,
                rightColumn.y,
                rightColumn.width,
                dialogueHeight);
            DrawDialoguePanel(dialogueRect);

            Rect orderRect = new Rect(
                rightColumn.x,
                dialogueRect.yMax + Gap,
                rightColumn.width,
                Mathf.Max(0f, rightColumn.yMax - (dialogueRect.yMax + Gap)));
            DrawOrderPanel(orderRect);

            Rect centerRect = new Rect(
                navRect.xMax + Gap,
                bodyY,
                Mathf.Max(0f, rightColumn.x - Gap - (navRect.xMax + Gap)),
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
                MechanoidOvermindPageKind.Chat,
                "MAP_MechanoidMechanitor.MechHiveCommunication.Nav.Chat".Translate());
            y += buttonHeight + 6f;

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
            y += buttonHeight;

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

            if (!Prefs.DevMode)
            {
                devControlsEnabled = false;
            }
            else
            {
                float devTop = y + 8f;
                float devBottom = disconnectRect.y - 4f;
                if (devBottom > devTop)
                {
                    DrawDevControls(new Rect(inner.x, devTop, inner.width, devBottom - devTop));
                }
            }
        }

        private void DrawDevControls(Rect available)
        {
            const float toggleHeight = 22f;
            const float itemGap = 3f;
            const float minButtonHeight = 22f;
            const float preferredButtonHeight = 24f;
            const int buttonCount = 4;

            if (available.height < toggleHeight)
            {
                return;
            }

            Rect toggleRect = new Rect(
                available.x,
                available.y,
                available.width,
                toggleHeight);
            bool enabled = devControlsEnabled;
            Widgets.CheckboxLabeled(
                toggleRect,
                "MAP_MechanoidMechanitor.MechHiveCommunication.Dev.Toggle".Translate(),
                ref enabled);
            devControlsEnabled = enabled;

            if (!devControlsEnabled)
            {
                return;
            }

            float remaining = available.yMax - (toggleRect.yMax + itemGap);
            float gaps = itemGap * (buttonCount - 1);
            float maxButtonHeight = (remaining - gaps) / buttonCount;
            if (maxButtonHeight < minButtonHeight)
            {
                return;
            }

            float buttonHeight = Mathf.Min(preferredButtonHeight, maxButtonHeight);
            float y = toggleRect.yMax + itemGap;
            if (DrawDevCreditButton(
                    new Rect(available.x, y, available.width, buttonHeight),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Dev.Add1000"))
            {
                AdjustPurgeCreditsForDev(1000);
            }

            y += buttonHeight + itemGap;
            if (DrawDevCreditButton(
                    new Rect(available.x, y, available.width, buttonHeight),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Dev.Sub1000"))
            {
                AdjustPurgeCreditsForDev(-1000);
            }

            y += buttonHeight + itemGap;
            if (DrawDevCreditButton(
                    new Rect(available.x, y, available.width, buttonHeight),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Dev.Add100"))
            {
                AdjustPurgeCreditsForDev(100);
            }

            y += buttonHeight + itemGap;
            if (DrawDevCreditButton(
                    new Rect(available.x, y, available.width, buttonHeight),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Dev.Sub100"))
            {
                AdjustPurgeCreditsForDev(-100);
            }
        }

        private static bool DrawDevCreditButton(Rect rect, string labelKey)
        {
            return MechanoidOvermindUiStyle.DrawActionButton(rect, labelKey.Translate());
        }

        private static void AdjustPurgeCreditsForDev(int delta)
        {
            try
            {
                if (delta == 0)
                {
                    return;
                }

                if (delta > 0)
                {
                    // RefundCredits 使用 long 加法并拒绝溢出，避免 int 回绕。
                    GameComponent_MechanoidMechanitorStoryState.RefundPurgeDirectiveCredits(
                        delta);
                    return;
                }

                int current =
                    GameComponent_MechanoidMechanitorStoryState.GetPurgeDirectiveRewardPoints();
                int spend = Mathf.Min(current, -delta);
                if (spend <= 0)
                {
                    return;
                }

                GameComponent_MechanoidMechanitorStoryState.TrySpendPurgeDirectiveCredits(spend);
            }
            catch (System.Exception)
            {
                // DEV 调试失败时静默。
            }
        }

        private void DrawNavItem(Rect rect, MechanoidOvermindPageKind page, string label)
        {
            bool selected = currentPage == page;
            if (MechanoidOvermindUiStyle.DrawNavButton(rect, label, selected))
            {
                currentPage = page;
                statusKey = "MAP_MechanoidMechanitor.MechHiveCommunication.Status.WaitingInput";
                StartPageDialogue(page);
            }
        }

        private void DrawCenterPage(Rect rect)
        {
            switch (currentPage)
            {
                case MechanoidOvermindPageKind.Home:
                    homePage.Draw(rect);
                    break;
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

        private void DrawDialoguePanel(Rect rect)
        {
            dialogueTyper.Tick();

            MechanoidOvermindUiStyle.DrawPanel(rect);
            Rect inner = rect.ContractedBy(8f);

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(inner.x, inner.y, inner.width, 22f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Dialogue.Title".Translate(),
                GameFont.Small);

            Rect bodyRect = new Rect(
                inner.x,
                inner.y + 26f,
                inner.width,
                Mathf.Max(0f, inner.height - 26f));
            MechanoidOvermindUiStyle.DrawPanel(bodyRect, alt: true, cornerMarks: false);

            Rect contentRect = bodyRect.ContractedBy(8f);
            string visibleText = dialogueTyper.VisibleText;

            float contentWidth = Mathf.Max(1f, contentRect.width - 16f);
            float textHeight;
            using (MechanoidOvermindUiStyle.Push())
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = true;
                textHeight = string.IsNullOrEmpty(visibleText)
                    ? contentRect.height
                    : Mathf.Max(contentRect.height, Text.CalcHeight(visibleText, contentWidth));
            }

            if (!dialogueTyper.IsComplete && textHeight > contentRect.height)
            {
                dialogueScroll.y = textHeight - contentRect.height;
            }

            Rect viewRect = new Rect(0f, 0f, contentWidth, textHeight);
            Widgets.BeginScrollView(contentRect, ref dialogueScroll, viewRect);
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(0f, 0f, contentWidth, textHeight),
                visibleText,
                GameFont.Small,
                TextAnchor.UpperLeft,
                MechanoidOvermindUiStyle.TextPrimary,
                wordWrap: true);
            Widgets.EndScrollView();
        }

        private void StartPageDialogue(MechanoidOvermindPageKind page)
        {
            dialogueTyper.Clear();
            dialogueScroll = Vector2.zero;
            dialogueTyper.Start(GetDialogueKey(page).Translate());
        }

        private static string GetDialogueKey(MechanoidOvermindPageKind page)
        {
            switch (page)
            {
                case MechanoidOvermindPageKind.Home:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Dialogue.Home";
                case MechanoidOvermindPageKind.Chat:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Dialogue.Chat";
                case MechanoidOvermindPageKind.Mechs:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Dialogue.Mechs";
                case MechanoidOvermindPageKind.Goods:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Dialogue.Goods";
                case MechanoidOvermindPageKind.BattlefieldSupport:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Dialogue.Battlefield";
                default:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Dialogue.Home";
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

            bool costsOk = order.TryGetCosts(out _, out _, out int totalCost);
            int credits = GameComponent_MechanoidMechanitorStoryState
                .GetPurgeDirectiveRewardPoints();
            RefreshDropSpotCache(force: false);

            // 结算50 + 状态18 + 间距6 + 清空24 + 间距4 + 确认26 = 128；另含列表与footer间距6。
            const float footerHeight = 134f;
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
            DrawOrderFooter(footer, costsOk, totalCost, credits);
        }

        private void DrawOrderLines(Rect listRect, bool costsOk)
        {
            MechanoidOvermindUiStyle.DrawPanel(listRect, alt: true, cornerMarks: false);
            int lineCount = order.MechLines.Count + order.ThingLines.Count;
            float viewHeight = Mathf.Max(listRect.height, lineCount * OrderRowStride + 4f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(listRect, ref orderScroll, viewRect);

            if (lineCount > 0)
            {
                int first = Mathf.Max(0, Mathf.FloorToInt(orderScroll.y / OrderRowStride) - 1);
                int last = Mathf.Min(
                    lineCount - 1,
                    Mathf.CeilToInt((orderScroll.y + listRect.height) / OrderRowStride) + 1);
                for (int i = first; i <= last; i++)
                {
                    float y = 2f + i * OrderRowStride;
                    Rect rowRect = new Rect(4f, y, viewRect.width - 8f, OrderRowHeight);
                    if (i < order.MechLines.Count)
                    {
                        MechanoidOvermindOrderLine_Mech mechLine = order.MechLines[i];
                        if (DrawMechOrderLine(rowRect, mechLine, costsOk))
                        {
                            order.SetMechCount(mechLine.Kind, 0);
                            break;
                        }
                    }
                    else
                    {
                        int thingIndex = i - order.MechLines.Count;
                        MechanoidOvermindOrderLine_Thing line = order.ThingLines[thingIndex];
                        if (DrawThingOrderLine(rowRect, line))
                        {
                            order.SetThingCount(line.Spec, 0);
                            break;
                        }
                    }
                }
            }

            Widgets.EndScrollView();
        }

        private bool DrawMechOrderLine(
            Rect rect,
            MechanoidOvermindOrderLine_Mech line,
            bool costsOk)
        {
            int pointsSubtotal = 0;
            if (costsOk
                && MechanoidOvermindPricingService.TryGetMechUnitPrice(line.Kind, out int unit))
            {
                pointsSubtotal = unit * line.Count;
            }

            string meta = "MAP_MechanoidMechanitor.MechHiveCommunication.Order.MechLineMeta"
                .Translate(line.Count, pointsSubtotal);
            return DrawOrderLine(rect, line.Kind.LabelCap, meta);
        }

        private bool DrawThingOrderLine(Rect rect, MechanoidOvermindOrderLine_Thing line)
        {
            bool hasStuff = line.Spec.Stuff != null;
            bool hasQuality = line.Spec.HasQuality;
            string meta;
            if (hasStuff && hasQuality)
            {
                meta = "MAP_MechanoidMechanitor.MechHiveCommunication.Order.ThingLineMetaStuffQuality"
                    .Translate(
                        line.Count,
                        line.Spec.Stuff!.LabelCap,
                        line.Spec.Quality.GetLabel().CapitalizeFirst());
            }
            else if (hasStuff)
            {
                meta = "MAP_MechanoidMechanitor.MechHiveCommunication.Order.ThingLineMetaStuff"
                    .Translate(line.Count, line.Spec.Stuff!.LabelCap);
            }
            else if (hasQuality)
            {
                meta = "MAP_MechanoidMechanitor.MechHiveCommunication.Order.ThingLineMetaQuality"
                    .Translate(
                        line.Count,
                        line.Spec.Quality.GetLabel().CapitalizeFirst());
            }
            else
            {
                meta = "MAP_MechanoidMechanitor.MechHiveCommunication.Order.ThingLineMeta"
                    .Translate(line.Count);
            }

            return DrawOrderLine(rect, line.Spec.Def.LabelCap, meta);
        }

        private bool DrawOrderLine(Rect rect, string name, string meta)
        {
            Widgets.DrawBoxSolid(rect, MechanoidOvermindUiStyle.Panel);
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.x + 6f, rect.y + 4f, rect.width - 70f, 18f),
                name);
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
            int totalCost,
            int credits)
        {
            // 固定布局（自上而下，互不交叠，按钮位置不随错误状态跳动）：
            // [0,50) 结算信息 | [50,68) 状态行 | 间距6 | 清空24 | 间距4 | 确认26
            const float summaryHeight = 50f;
            const float statusHeight = 18f;
            const float statusToClearGap = 6f;
            const float clearHeight = 24f;
            const float clearToConfirmGap = 4f;
            const float confirmHeight = 26f;

            bool connected = MechanoidMechanitorMechHiveCommunicationUtility
                .TryGetContactableMechHive(out _);
            bool canConfirm = costsOk
                && !order.IsEmpty
                && connected
                && cachedDropValid
                && credits >= totalCost;

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.x, rect.y, rect.width, 18f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Order.Total".Translate(
                    costsOk ? totalCost : 0),
                GameFont.Small,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.AccentBright);
            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(rect.x, rect.y + 18f, rect.width, 16f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Order.CurrentCredits".Translate(
                    credits));
            int balance = costsOk ? credits - totalCost : credits;
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.x, rect.y + 34f, rect.width, 16f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Order.BalanceAfter".Translate(
                    balance),
                GameFont.Tiny,
                TextAnchor.MiddleLeft,
                balance < 0
                    ? MechanoidOvermindUiStyle.Error
                    : MechanoidOvermindUiStyle.TextSecondary);

            Rect statusRect = new Rect(
                rect.x,
                rect.y + summaryHeight,
                rect.width,
                statusHeight);
            if (!cachedDropValid)
            {
                MechanoidOvermindUiStyle.DrawLabel(
                    statusRect,
                    (cachedDropMap == null
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
                    statusRect,
                    MechanoidOvermindDeliveryService.ErrorConnection.Translate(),
                    GameFont.Tiny,
                    TextAnchor.MiddleLeft,
                    MechanoidOvermindUiStyle.Error);
            }

            float clearY = statusRect.yMax + statusToClearGap;
            if (MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(rect.x, clearY, rect.width, clearHeight),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Order.Clear".Translate()))
            {
                order.Clear();
                statusKey = "MAP_MechanoidMechanitor.MechHiveCommunication.Status.WaitingInput";
            }

            float confirmY = clearY + clearHeight + clearToConfirmGap;
            string confirmLabel =
                "MAP_MechanoidMechanitor.MechHiveCommunication.Order.Confirm".Translate(
                    costsOk ? totalCost : 0);
            if (MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(rect.x, confirmY, rect.width, confirmHeight),
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
            int revision = order.Revision;
            bool timeStale = Time.realtimeSinceStartup - cachedDropRealtime
                    >= DropCacheRealtimeSeconds
                || (Find.TickManager != null
                    && Find.TickManager.TicksGame - cachedDropTick >= DropCacheTickInterval);

            bool mapUsable = cachedDropMap != null && !cachedDropMap.Disposed;
            Map? expectedMap = null;
            try
            {
                expectedMap = MechanoidOvermindDeliveryService.ResolvePlayerDeliveryMap(
                    preferredDeliveryMap);
            }
            catch (System.Exception)
            {
                expectedMap = null;
            }

            bool mapChanged = expectedMap != cachedDropMap;

            if (!force
                && cachedDropRevision == revision
                && !timeStale
                && !mapChanged
                && cachedDropValid
                && mapUsable)
            {
                return;
            }

            // 不可用时仍按时间窗重试；revision / 地图变化则立即重检。
            if (!force
                && cachedDropRevision == revision
                && !timeStale
                && !mapChanged
                && !cachedDropValid)
            {
                return;
            }

            cachedDropRevision = revision;
            cachedDropRealtime = Time.realtimeSinceStartup;
            cachedDropTick = Find.TickManager != null
                ? Find.TickManager.TicksGame
                : 0;
            cachedDropMap = null;
            cachedDropCell = IntVec3.Invalid;
            cachedDropValid = false;

            try
            {
                if (!MechanoidOvermindDeliveryService.TryResolveTradeDropTarget(
                        preferredDeliveryMap,
                        out Map? map,
                        out IntVec3 cell))
                {
                    cachedDropMap = map;
                    return;
                }

                cachedDropMap = map;
                cachedDropCell = cell;
                cachedDropValid = map != null && !map.Disposed && cell.IsValid;
            }
            catch (System.Exception)
            {
                cachedDropValid = false;
            }
        }

        private static string GetNodePermissionLabel()
        {
            return "MAP_MechanoidMechanitor.MechHiveCommunication.NodePermission.EdgeExecUnit"
                .Translate();
        }
    }
}
