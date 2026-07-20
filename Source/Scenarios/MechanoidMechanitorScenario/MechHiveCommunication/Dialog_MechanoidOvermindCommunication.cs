using System;
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

        private const float SidePanelWidth = 316f;

        private const float PageHeaderHeight = 40f;

        private const float Gap = 8f;

        private const float BootDuration = 0.5f;

        private const float OrderRowHeight = 50f;

        private const float OrderRowStride = 52f;

        private const float DropCacheRealtimeSeconds = 1f;

        private const int DropCacheTickInterval = 60;

        private const float MinOrderPanelHeight = 224f;

        private const float TransitionDuration = 0.35f;

        private const float CoreLargeWidth = 460f;

        private const float CoreLargeHeight = 480f;

        private const float CoreSmallHeight = 130f;

        private const float DialogueHomeRatio = 0.42f;

        private const float DialogueSubHeight = 118f;

        private const float FloatPeriodSeconds = 3.2f;

        private const float FloatAmpLarge = 7.5f;

        private const float FloatAmpSmall = 3.5f;

        private readonly Faction mechHive;

        private readonly Map? preferredDeliveryMap;

        private MechanoidOvermindOrder? activeOrder;

        private readonly MechanoidOvermindPage_Home homePage = new MechanoidOvermindPage_Home();

        private readonly MechanoidOvermindPage_Mechs mechsPage = new MechanoidOvermindPage_Mechs();

        private readonly MechanoidOvermindPage_Goods goodsPage = new MechanoidOvermindPage_Goods();

        private readonly MechanoidOvermindPage_Battlefield battlefieldPage =
            new MechanoidOvermindPage_Battlefield();

        private readonly MechanoidOvermindPage_Chat chatPage = new MechanoidOvermindPage_Chat();

        private readonly MechanoidOvermindDialogueTyper dialogueTyper =
            new MechanoidOvermindDialogueTyper();

        private readonly MechanoidOvermindDialoguePoolSelector dialogueSelector =
            new MechanoidOvermindDialoguePoolSelector();

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

        private MechanoidOvermindOrder? cachedDropOrder;

        private int cachedDropRevision = int.MinValue;

        private float cachedDropRealtime = -999f;

        private int cachedDropTick = int.MinValue;

        private bool devControlsEnabled;

        private bool transitioning;

        private MechanoidOvermindPageKind transitionFromPage = MechanoidOvermindPageKind.Home;

        private MechanoidOvermindPageKind transitionTargetPage = MechanoidOvermindPageKind.Home;

        private float transitionStartRealtime;

        private Rect transitionFromRect;

        private Rect transitionToRect;

        private Rect lastHomeCoreRect;

        private Rect lastSubCoreRect;

        private bool hasLayoutRects;

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
            PlayDialogueFromPool("MAP_OvermindDialogue_HomeOpen");
        }

        public override void PreClose()
        {
            base.PreClose();
            DiscardActiveOrder();
            MechanoidOvermindPricingService.ClearThingMarketValueCache();
        }

        public override void DoWindowContents(Rect inRect)
        {
            using (MechanoidOvermindUiStyle.Push())
            {
                UpdateBootStatus();
                UpdateTransition();
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
            Rect bodyRect = new Rect(inRect.x, bodyY, inRect.width, bodyHeight);

            ComputeLayoutRects(bodyRect, out Rect homeCore, out Rect subCore);
            lastHomeCoreRect = homeCore;
            lastSubCoreRect = subCore;
            hasLayoutRects = true;

            if (transitioning)
            {
                float t = GetTransitionT();
                Rect coreRect = LerpRect(transitionFromRect, transitionToRect, t);
                if (t >= 1f)
                {
                    coreRect = transitionToRect;
                }

                DrawCoreDisplay(coreRect);
                return;
            }

            if (currentPage == MechanoidOvermindPageKind.Home)
            {
                DrawHomeLayout(bodyRect, homeCore);
            }
            else
            {
                DrawSubpageLayout(bodyRect, subCore);
            }
        }

        private void ComputeLayoutRects(Rect bodyRect, out Rect homeCore, out Rect subCore)
        {
            float homeCoreW = Mathf.Min(CoreLargeWidth, bodyRect.width * 0.45f);
            float homeCoreH = Mathf.Min(CoreLargeHeight, bodyRect.height);
            homeCore = new Rect(
                bodyRect.x,
                bodyRect.y + (bodyRect.height - homeCoreH) * 0.5f,
                homeCoreW,
                homeCoreH);

            float sideW = Mathf.Min(SidePanelWidth, Mathf.Max(200f, bodyRect.width * 0.28f));
            float smallH = Mathf.Min(CoreSmallHeight, bodyRect.height * 0.28f);
            subCore = new Rect(
                bodyRect.xMax - sideW,
                bodyRect.y,
                sideW,
                smallH);
        }

        private void DrawHomeLayout(Rect bodyRect, Rect homeCore)
        {
            DrawCoreDisplay(homeCore);

            float rightX = homeCore.xMax + Gap;
            float rightW = Mathf.Max(0f, bodyRect.xMax - rightX);
            Rect rightRect = new Rect(rightX, bodyRect.y, rightW, bodyRect.height);

            float dialogueH = Mathf.Clamp(
                rightRect.height * DialogueHomeRatio,
                100f,
                rightRect.height - 160f);
            Rect dialogueRect = new Rect(rightRect.x, rightRect.y, rightRect.width, dialogueH);
            DrawDialoguePanel(dialogueRect);

            Rect cardsRect = new Rect(
                rightRect.x,
                dialogueRect.yMax + Gap,
                rightRect.width,
                Mathf.Max(0f, rightRect.yMax - (dialogueRect.yMax + Gap)));
            MechanoidOvermindPageKind? selected = homePage.Draw(cardsRect, inputEnabled: true);
            if (selected.HasValue)
            {
                BeginTransitionTo(selected.Value);
            }
        }

        private void DrawSubpageLayout(Rect bodyRect, Rect subCore)
        {
            float sideW = subCore.width;
            Rect sideRect = new Rect(bodyRect.xMax - sideW, bodyRect.y, sideW, bodyRect.height);
            Rect leftRect = new Rect(
                bodyRect.x,
                bodyRect.y,
                Mathf.Max(0f, sideRect.x - Gap - bodyRect.x),
                bodyRect.height);

            DrawPageHeader(leftRect, out Rect contentRect);
            DrawSubpageContent(contentRect);

            DrawCoreDisplay(subCore);

            bool showOrder = ShowsOrderPanel(currentPage);
            float dialogueH;
            if (showOrder)
            {
                float orderBudget = sideRect.height - subCore.height - Gap * 2f - MinOrderPanelHeight;
                dialogueH = Mathf.Clamp(DialogueSubHeight, 80f, Mathf.Max(80f, orderBudget));
                if (sideRect.height - subCore.height - Gap * 2f - dialogueH < MinOrderPanelHeight)
                {
                    dialogueH = Mathf.Max(
                        80f,
                        sideRect.height - subCore.height - Gap * 2f - MinOrderPanelHeight);
                }
            }
            else
            {
                dialogueH = Mathf.Max(80f, sideRect.height - subCore.height - Gap);
            }

            Rect dialogueRect = new Rect(
                sideRect.x,
                subCore.yMax + Gap,
                sideRect.width,
                dialogueH);
            DrawDialoguePanel(dialogueRect);

            if (showOrder && activeOrder != null)
            {
                Rect orderRect = new Rect(
                    sideRect.x,
                    dialogueRect.yMax + Gap,
                    sideRect.width,
                    Mathf.Max(0f, sideRect.yMax - (dialogueRect.yMax + Gap)));
                DrawOrderPanel(orderRect, activeOrder);
            }
        }

        private void DrawPageHeader(Rect leftRect, out Rect contentRect)
        {
            Rect headerRect = new Rect(leftRect.x, leftRect.y, leftRect.width, PageHeaderHeight);
            MechanoidOvermindUiStyle.DrawPanel(headerRect, cornerMarks: false);
            Rect inner = headerRect.ContractedBy(6f);

            Rect backRect = new Rect(inner.x, inner.y, 120f, inner.height);
            if (MechanoidOvermindUiStyle.DrawActionButton(
                    backRect,
                    "MAP_MechanoidMechanitor.MechHiveCommunication.BackToHome".Translate(),
                    enabled: !transitioning)
                && !transitioning)
            {
                BeginTransitionHome();
            }

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(backRect.xMax + 10f, inner.y, inner.width - backRect.width - 10f, inner.height),
                GetPageTitle(currentPage),
                GameFont.Small,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.AccentBright);

            contentRect = new Rect(
                leftRect.x,
                headerRect.yMax + Gap,
                leftRect.width,
                Mathf.Max(0f, leftRect.yMax - (headerRect.yMax + Gap)));
        }

        private void DrawSubpageContent(Rect contentRect)
        {
            switch (currentPage)
            {
                case MechanoidOvermindPageKind.Mechs:
                    if (activeOrder != null)
                    {
                        mechsPage.Draw(contentRect, activeOrder);
                    }

                    break;
                case MechanoidOvermindPageKind.Goods:
                    if (activeOrder != null)
                    {
                        goodsPage.Draw(contentRect, activeOrder);
                    }

                    break;
                case MechanoidOvermindPageKind.BattlefieldSupport:
                    battlefieldPage.Draw(contentRect);
                    break;
                case MechanoidOvermindPageKind.Chat:
                    chatPage.Draw(contentRect);
                    break;
            }
        }

        private void DrawCoreDisplay(Rect frameRect)
        {
            MechanoidOvermindUiStyle.DrawCoreFrame(frameRect);

            float sizeFactor = Mathf.InverseLerp(CoreSmallHeight, CoreLargeHeight, frameRect.height);
            float amplitude = Mathf.Lerp(FloatAmpSmall, FloatAmpLarge, Mathf.Clamp01(sizeFactor));
            // 留白至少覆盖振幅，确保贴图在极值处仍不触碰边框。
            float margin = Mathf.Max(amplitude + 8f, frameRect.height * 0.05f);
            Rect inner = frameRect.ContractedBy(margin);
            if (inner.width <= 1f || inner.height <= 1f)
            {
                return;
            }

            double period = FloatPeriodSeconds;
            double cycleTime = Time.realtimeSinceStartupAsDouble % period;
            if (cycleTime < 0d)
            {
                cycleTime += period;
            }

            double phase = cycleTime * (System.Math.PI * 2d / period);
            float offsetY = (float)(System.Math.Sin(phase) * amplitude);
            float texSize = Mathf.Min(inner.width, inner.height - amplitude * 2f);
            if (texSize < 8f)
            {
                return;
            }

            Rect texRect = new Rect(
                inner.x + (inner.width - texSize) * 0.5f,
                inner.y + (inner.height - texSize) * 0.5f + offsetY,
                texSize,
                texSize);

            Texture2D? tex = MechanoidOvermindUiAssets.CerebrexCoreBrain;
            if (tex != null)
            {
                Color previous = GUI.color;
                GUI.color = Color.white;
                GUI.DrawTexture(texRect, tex, ScaleMode.ScaleToFit, alphaBlend: true);
                GUI.color = previous;
            }
            else
            {
                MechanoidOvermindUiStyle.DrawLabel(
                    inner,
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Core.Missing".Translate(),
                    GameFont.Small,
                    TextAnchor.MiddleCenter,
                    MechanoidOvermindUiStyle.TextSecondary,
                    wordWrap: true);
            }
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

        private void DrawBottomBar(Rect rect)
        {
            MechanoidOvermindUiStyle.DrawPanel(rect, cornerMarks: false);
            Color color = MechanoidOvermindUiStyle.TextSecondary;
            if (statusKey.IndexOf("Error", StringComparison.Ordinal) >= 0
                || statusKey.IndexOf("Insufficient", StringComparison.Ordinal) >= 0)
            {
                color = MechanoidOvermindUiStyle.Error;
            }
            else if (statusKey.IndexOf("Accepted", StringComparison.Ordinal) >= 0
                || statusKey.IndexOf("DropStarted", StringComparison.Ordinal) >= 0
                || statusKey.IndexOf("CreditsSpent", StringComparison.Ordinal) >= 0
                || statusKey.IndexOf("Connected", StringComparison.Ordinal) >= 0)
            {
                color = MechanoidOvermindUiStyle.AccentBright;
            }

            if (!Prefs.DevMode)
            {
                devControlsEnabled = false;
            }

            const float edgePad = 8f;
            const float itemGap = 4f;
            const float disconnectW = 110f;
            const float devToggleW = 54f;
            // 按 MinVirtualWidth=1100 预留中文标签宽度，避免与状态文字重叠。
            const float creditWideW = 102f;
            const float creditNarrowW = 92f;

            bool showDevToggle = Prefs.DevMode;
            bool showCreditButtons = showDevToggle && devControlsEnabled;
            float creditClusterW = showCreditButtons
                ? creditWideW + itemGap + creditWideW + itemGap + creditNarrowW + itemGap
                    + creditNarrowW
                : 0f;

            float rightClusterW = disconnectW;
            if (showDevToggle)
            {
                rightClusterW += itemGap + devToggleW;
            }

            if (showCreditButtons)
            {
                rightClusterW += itemGap + creditClusterW;
            }

            float statusWidth = Mathf.Max(
                40f,
                rect.width - edgePad * 2f - rightClusterW - itemGap);
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.x + edgePad, rect.y + 4f, statusWidth, rect.height - 8f),
                statusKey.Translate(),
                GameFont.Tiny,
                TextAnchor.MiddleLeft,
                color);

            float cursorX = rect.xMax - edgePad;
            float buttonY = rect.y + 4f;
            float buttonH = rect.height - 8f;

            cursorX -= disconnectW;
            if (MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(cursorX, buttonY, disconnectW, buttonH),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Nav.Disconnect".Translate()))
            {
                Close(doCloseSound: true);
            }

            if (showDevToggle)
            {
                cursorX -= itemGap + devToggleW;
                Rect toggleRect = new Rect(cursorX, rect.y + 6f, devToggleW, rect.height - 12f);
                if (transitioning)
                {
                    bool frozen = devControlsEnabled;
                    Widgets.CheckboxLabeled(
                        toggleRect,
                        "MAP_MechanoidMechanitor.MechHiveCommunication.Dev.Toggle".Translate(),
                        ref frozen);
                }
                else
                {
                    bool enabled = devControlsEnabled;
                    Widgets.CheckboxLabeled(
                        toggleRect,
                        "MAP_MechanoidMechanitor.MechHiveCommunication.Dev.Toggle".Translate(),
                        ref enabled);
                    devControlsEnabled = enabled;
                }
            }

            if (showCreditButtons)
            {
                bool creditEnabled = !transitioning;
                // 从右向左放置，保证左→右顺序为：+1000、-1000、+100、-100。
                cursorX -= itemGap + creditNarrowW;
                if (DrawDevCreditButton(
                        new Rect(cursorX, buttonY, creditNarrowW, buttonH),
                        "MAP_MechanoidMechanitor.MechHiveCommunication.Dev.Sub100",
                        creditEnabled))
                {
                    AdjustPurgeCreditsForDev(-100);
                }

                cursorX -= itemGap + creditNarrowW;
                if (DrawDevCreditButton(
                        new Rect(cursorX, buttonY, creditNarrowW, buttonH),
                        "MAP_MechanoidMechanitor.MechHiveCommunication.Dev.Add100",
                        creditEnabled))
                {
                    AdjustPurgeCreditsForDev(100);
                }

                cursorX -= itemGap + creditWideW;
                if (DrawDevCreditButton(
                        new Rect(cursorX, buttonY, creditWideW, buttonH),
                        "MAP_MechanoidMechanitor.MechHiveCommunication.Dev.Sub1000",
                        creditEnabled))
                {
                    AdjustPurgeCreditsForDev(-1000);
                }

                cursorX -= itemGap + creditWideW;
                if (DrawDevCreditButton(
                        new Rect(cursorX, buttonY, creditWideW, buttonH),
                        "MAP_MechanoidMechanitor.MechHiveCommunication.Dev.Add1000",
                        creditEnabled))
                {
                    AdjustPurgeCreditsForDev(1000);
                }
            }
        }

        private static bool DrawDevCreditButton(Rect rect, string labelKey, bool enabled)
        {
            return MechanoidOvermindUiStyle.DrawActionButton(
                rect,
                labelKey.Translate(),
                enabled);
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
                    GameComponent_MechanoidMechanitorStoryState.RefundPurgeDirectiveCredits(delta);
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
            catch (Exception)
            {
                // DEV 调试失败时静默。
            }
        }

        private void DrawDialoguePanel(Rect rect)
        {
            bool wasTyping = !dialogueTyper.IsComplete;
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

            float maxScroll = Mathf.Max(0f, textHeight - contentRect.height);
            if (wasTyping && textHeight > contentRect.height)
            {
                dialogueScroll.y = maxScroll;
            }
            else
            {
                dialogueScroll.y = Mathf.Clamp(dialogueScroll.y, 0f, maxScroll);
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

        private void DrawOrderPanel(Rect rect, MechanoidOvermindOrder order)
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
            RefreshDropSpotCache(order, force: false);

            const float footerHeight = 134f;
            Rect listRect = new Rect(
                inner.x,
                inner.y + 28f,
                inner.width,
                Mathf.Max(40f, inner.height - footerHeight - 28f));
            DrawOrderLines(listRect, order, costsOk);

            Rect footer = new Rect(
                inner.x,
                listRect.yMax + 6f,
                inner.width,
                footerHeight - 6f);
            DrawOrderFooter(footer, order, costsOk, totalCost, credits);
        }

        private void DrawOrderLines(Rect listRect, MechanoidOvermindOrder order, bool costsOk)
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
            MechanoidOvermindOrder order,
            bool costsOk,
            int totalCost,
            int credits)
        {
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
                && credits >= totalCost
                && currentPage != MechanoidOvermindPageKind.BattlefieldSupport;

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
                TryConfirmDelivery(order);
            }
        }

        private void TryConfirmDelivery(MechanoidOvermindOrder order)
        {
            if (currentPage == MechanoidOvermindPageKind.BattlefieldSupport)
            {
                return;
            }

            statusKey = "MAP_MechanoidMechanitor.MechHiveCommunication.Status.Validating";
            RefreshDropSpotCache(order, force: true);

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

        private void BeginTransitionTo(MechanoidOvermindPageKind target)
        {
            if (transitioning || target == MechanoidOvermindPageKind.Home)
            {
                return;
            }

            if (!hasLayoutRects)
            {
                return;
            }

            transitioning = true;
            transitionFromPage = currentPage;
            transitionTargetPage = target;
            transitionStartRealtime = Time.realtimeSinceStartup;
            transitionFromRect = lastHomeCoreRect;
            transitionToRect = lastSubCoreRect;
            dialogueTyper.Clear();
            dialogueScroll = Vector2.zero;
            statusKey = "MAP_MechanoidMechanitor.MechHiveCommunication.Status.WaitingInput";
        }

        private void BeginTransitionHome()
        {
            if (transitioning || currentPage == MechanoidOvermindPageKind.Home)
            {
                return;
            }

            if (!hasLayoutRects)
            {
                return;
            }

            DiscardActiveOrder();
            transitioning = true;
            transitionFromPage = currentPage;
            transitionTargetPage = MechanoidOvermindPageKind.Home;
            transitionStartRealtime = Time.realtimeSinceStartup;
            transitionFromRect = lastSubCoreRect;
            transitionToRect = lastHomeCoreRect;
            dialogueTyper.Clear();
            dialogueScroll = Vector2.zero;
            statusKey = "MAP_MechanoidMechanitor.MechHiveCommunication.Status.WaitingInput";
        }

        private void UpdateTransition()
        {
            if (!transitioning)
            {
                return;
            }

            if (GetTransitionT() < 1f)
            {
                return;
            }

            CompleteTransition();
        }

        private float GetTransitionT()
        {
            float raw = (Time.realtimeSinceStartup - transitionStartRealtime) / TransitionDuration;
            return Mathf.Clamp01(raw);
        }

        private static float SmoothStep(float t)
        {
            return t * t * (3f - 2f * t);
        }

        private static Rect LerpRect(Rect from, Rect to, float rawT)
        {
            float t = SmoothStep(Mathf.Clamp01(rawT));
            return new Rect(
                Mathf.Lerp(from.x, to.x, t),
                Mathf.Lerp(from.y, to.y, t),
                Mathf.Lerp(from.width, to.width, t),
                Mathf.Lerp(from.height, to.height, t));
        }

        private void CompleteTransition()
        {
            if (!transitioning)
            {
                return;
            }

            transitioning = false;
            currentPage = transitionTargetPage;

            if (transitionTargetPage == MechanoidOvermindPageKind.Home)
            {
                DiscardActiveOrder();
                PlayDialogueFromPool("MAP_OvermindDialogue_HomeReturn");
                return;
            }

            if (ShowsOrderPanel(transitionTargetPage))
            {
                activeOrder = new MechanoidOvermindOrder();
                InvalidateDropSpotCache();
                orderScroll = Vector2.zero;
            }
            else
            {
                DiscardActiveOrder();
            }

            PlayDialogueFromPool(GetEnterPoolDefName(transitionTargetPage));
        }

        private void DiscardActiveOrder()
        {
            if (activeOrder != null)
            {
                activeOrder.Clear();
                activeOrder = null;
            }

            InvalidateDropSpotCache();
        }

        private void InvalidateDropSpotCache()
        {
            cachedDropOrder = null;
            cachedDropRevision = int.MinValue;
            cachedDropMap = null;
            cachedDropCell = IntVec3.Invalid;
            cachedDropValid = false;
            cachedDropRealtime = -999f;
            cachedDropTick = int.MinValue;
        }

        private void PlayDialogueFromPool(string poolDefName)
        {
            MechanoidOvermindDialoguePoolDef? pool =
                DefDatabase<MechanoidOvermindDialoguePoolDef>.GetNamedSilentFail(poolDefName);
            string text = dialogueSelector.PickTranslatedText(pool);
            dialogueTyper.Clear();
            dialogueScroll = Vector2.zero;
            dialogueTyper.Start(text);
        }

        private static bool ShowsOrderPanel(MechanoidOvermindPageKind page)
        {
            return page == MechanoidOvermindPageKind.Mechs
                || page == MechanoidOvermindPageKind.Goods
                || page == MechanoidOvermindPageKind.BattlefieldSupport;
        }

        private static string GetEnterPoolDefName(MechanoidOvermindPageKind page)
        {
            switch (page)
            {
                case MechanoidOvermindPageKind.Chat:
                    return "MAP_OvermindDialogue_EnterChat";
                case MechanoidOvermindPageKind.Mechs:
                    return "MAP_OvermindDialogue_EnterMechs";
                case MechanoidOvermindPageKind.Goods:
                    return "MAP_OvermindDialogue_EnterGoods";
                case MechanoidOvermindPageKind.BattlefieldSupport:
                    return "MAP_OvermindDialogue_EnterBattlefield";
                default:
                    return "MAP_OvermindDialogue_HomeOpen";
            }
        }

        private static string GetPageTitle(MechanoidOvermindPageKind page)
        {
            switch (page)
            {
                case MechanoidOvermindPageKind.Chat:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Nav.Chat".Translate();
                case MechanoidOvermindPageKind.Mechs:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Nav.Mechs".Translate();
                case MechanoidOvermindPageKind.Goods:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Nav.Goods".Translate();
                case MechanoidOvermindPageKind.BattlefieldSupport:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Nav.Battlefield"
                        .Translate();
                default:
                    return string.Empty;
            }
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

        private void RefreshDropSpotCache(MechanoidOvermindOrder order, bool force)
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
            catch (Exception)
            {
                expectedMap = null;
            }

            bool mapChanged = expectedMap != cachedDropMap;
            bool orderChanged = !ReferenceEquals(cachedDropOrder, order);

            if (!force
                && !orderChanged
                && cachedDropRevision == revision
                && !timeStale
                && !mapChanged
                && cachedDropValid
                && mapUsable)
            {
                return;
            }

            if (!force
                && !orderChanged
                && cachedDropRevision == revision
                && !timeStale
                && !mapChanged
                && !cachedDropValid)
            {
                return;
            }

            cachedDropOrder = order;
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
            catch (Exception)
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
