using System;
using System.Collections.Generic;
using MAP_MechanoidMechanitor;
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

        private const float BaseTopBarHeight = 80f;

        private const float RatingTopBarHeight = 90f;

        private const float BottomBarHeight = 36f;

        private const float SidePanelWidth = 316f;

        private const float PageHeaderHeight = 40f;

        private const float Gap = 8f;

        private const float OrderRowHeight = 28f;

        private const float OrderRowStride = 30f;

        private const float DropCacheRealtimeSeconds = 1f;

        private const int DropCacheTickInterval = 60;

        private const float TransitionDuration = 0.35f;

        private const float CoreLargeWidth = 460f;

        private const float CoreLargeHeight = 480f;

        private const float CoreSmallHeight = 130f;

        private const float DialogueHomeRatio = 0.42f;

        private const float FloatPeriodSeconds = 3.2f;

        private const float FloatAmpLarge = 7.5f;

        private const float FloatAmpSmall = 3.5f;

        private const float BootConfirmSeconds = 0.1f;

        private const double BootBreathPeriod = 1.2d;

        private const double BootScanPeriod = 2.8d;

        private readonly Faction mechHive;

        private readonly Map? preferredDeliveryMap;

        private readonly string contactPawnDisplayName;

        private readonly string contactLocalTimeText;

        private readonly string overmindDisplayName;

        private MechanoidOvermindOrder? activeOrder;

        private MechClusterDeploymentOrder? activeProtocolOrder;

        private MechForceSupportOrder? activeForceSupportOrder;

        private MechClusterDeploymentSession? preparedClusterSession;

        private readonly MechanoidOvermindPage_Home homePage = new MechanoidOvermindPage_Home();

        private readonly MechanoidOvermindPage_Mechs mechsPage = new MechanoidOvermindPage_Mechs();

        private readonly MechanoidOvermindPage_Goods goodsPage = new MechanoidOvermindPage_Goods();

        private readonly MechanoidOvermindPage_SpecialProtocols specialProtocolsPage =
            new MechanoidOvermindPage_SpecialProtocols();

        private readonly MechanoidOvermindPage_Communication communicationPage = new MechanoidOvermindPage_Communication();

        private readonly MechanoidOvermindDialogueTyper dialogueTyper =
            new MechanoidOvermindDialogueTyper();

        private MechanoidOvermindCommunicationQueryKind? activeCommunicationQuery;

        private readonly MechanoidOvermindDialoguePoolSelector dialogueSelector =
            new MechanoidOvermindDialoguePoolSelector();

        private MechanoidOvermindPageKind currentPage = MechanoidOvermindPageKind.Home;

        private Vector2 windowScroll;

        private Vector2 orderScroll;

        private readonly System.Random bootRandom = new System.Random();

        private double bootStartRealtime;

        private float bootDurationSeconds;

        private bool bootComplete;

        private bool bootLoopTestEnabled;

        private bool forceMojibakeDialogue;

        private string statusKey =
            "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.Connecting";

        private Map? cachedDropMap;

        private IntVec3 cachedDropCell = IntVec3.Invalid;

        private bool cachedDropValid;

        private MechanoidOvermindOrder? cachedDropOrder;

        private int cachedDropRevision = int.MinValue;

        private float cachedDropRealtime = -999f;

        private int cachedDropTick = int.MinValue;

        private bool devControlsEnabled;

        private bool showDetailedGoodsPrice;

        private bool transitioning;

        private bool suspendingForMapTargeting;

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
            Map? preferredDeliveryMap,
            Pawn? contactPawn)
        {
            this.mechHive = mechHive;
            this.preferredDeliveryMap = preferredDeliveryMap;
            contactPawnDisplayName =
                MechanoidMechanitorMechHiveCommunicationUtility.ResolveContactPawnDisplayName(
                    contactPawn);
            contactLocalTimeText =
                MechanoidMechanitorMechHiveCommunicationUtility.ResolveContactLocalTimeText();
            overmindDisplayName =
                GameComponent_MechanoidMechanitorStoryState.GetOrCreateMechanoidOvermindName();
            forcePause = false;
            doCloseX = true;
            doCloseButton = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            forceCatchAcceptAndCancelEventEvenIfUnfocused = true;

            bool loadingScreenEnabled =
                MAPMechanitorMod.Settings?.enablePurgeDirectiveUiLoadingScreen ?? true;

            if (loadingScreenEnabled)
            {
                StartBootSequence();
            }
            else
            {
                CompleteBootSequence();
            }
        }

        public override void PreOpen()
        {
            base.PreOpen();
            Find.TickManager?.Pause();
        }

        public override void OnAcceptKeyPressed()
        {
            // Enter 既不关闭窗口，也不提交订单或触发当前控件。
            Event.current?.Use();
        }

        public override void PreClose()
        {
            base.PreClose();
            if (suspendingForMapTargeting)
            {
                return;
            }

            DiscardActiveOrder();
            MechanoidOvermindPricingService.ClearThingMarketValueCache();
        }

        public override void DoWindowContents(Rect inRect)
        {
            using (MechanoidOvermindUiStyle.Push())
            {
                MechanoidOvermindUiStyle.DrawBackground(inRect);

                if (!bootComplete)
                {
                    if (!TryFinishBootSequence())
                    {
                        DrawConnectionBootPage(inRect);
                        Rect bootBottom = new Rect(
                            inRect.x,
                            inRect.yMax - BottomBarHeight,
                            inRect.width,
                            BottomBarHeight);
                        DrawBottomBar(bootBottom, bootPage: true);
                        return;
                    }
                }

                UpdateTransition();

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

        private void StartBootSequence()
        {
            // 独立随机，不触碰 Verse.Rand；总时长 2～4 秒（含末尾 0.1 秒确认）。
            bootDurationSeconds = bootRandom.Next(2000, 4001) / 1000f;
            bootStartRealtime = Time.realtimeSinceStartupAsDouble;
            bootComplete = false;
        }

        private void CompleteBootSequence()
        {
            bootComplete = true;
            statusKey =
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.WaitingInput";
            PlayHomeOpenDialogue();
        }

        private bool IsBootLoopTestActive =>
            Prefs.DevMode && devControlsEnabled && bootLoopTestEnabled;

        private bool TryFinishBootSequence()
        {
            if (bootComplete)
            {
                return true;
            }

            double elapsed = Time.realtimeSinceStartupAsDouble - bootStartRealtime;
            if (elapsed < bootDurationSeconds)
            {
                return false;
            }

            if (IsBootLoopTestActive)
            {
                StartBootSequence();
                return false;
            }

            CompleteBootSequence();
            return true;
        }

        private void DrawConnectionBootPage(Rect inRect)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            GetBootTiming(
                out bool confirmPhase,
                out float fillRawT,
                out float visualT,
                out int activeStage,
                out int percent);

            // 主体避开底栏，避免低分辨率下与 DEV 控件重叠。
            Rect contentRect = new Rect(
                inRect.x,
                inRect.y,
                inRect.width,
                Mathf.Max(0f, inRect.height - BottomBarHeight));

            DrawBootTerminalGrid(contentRect);
            DrawBootScanline(contentRect, now, confirmPhase);

            float panelWidth = Mathf.Clamp(contentRect.width * 0.72f, 420f, 860f);
            panelWidth = Mathf.Min(panelWidth, Mathf.Max(0f, contentRect.width - 32f));
            float panelHeight = Mathf.Clamp(contentRect.height * 0.55f, 280f, 390f);
            panelHeight = Mathf.Min(panelHeight, Mathf.Max(0f, contentRect.height - 32f));
            if (panelWidth < 8f || panelHeight < 8f)
            {
                return;
            }

            Rect panel = new Rect(
                contentRect.x + (contentRect.width - panelWidth) * 0.5f,
                contentRect.y + (contentRect.height - panelHeight) * 0.5f,
                panelWidth,
                panelHeight);
            MechanoidOvermindUiStyle.DrawPanel(panel);
            if (confirmPhase)
            {
                Color previous = GUI.color;
                GUI.color = MechanoidOvermindUiStyle.AccentBright;
                Widgets.DrawBox(panel, 1);
                GUI.color = previous;
                MechanoidOvermindUiStyle.DrawCornerMarks(
                    panel,
                    MechanoidOvermindUiStyle.AccentBright);
            }

            Rect inner = panel.ContractedBy(14f);
            const float headerH = 54f;
            const float footerH = 56f;
            float bodyH = Mathf.Max(40f, inner.height - headerH - footerH - 8f);
            Rect headerRect = new Rect(inner.x, inner.y, inner.width, headerH);
            Rect bodyRect = new Rect(inner.x, headerRect.yMax + 4f, inner.width, bodyH);
            Rect footerRect = new Rect(
                inner.x,
                inner.yMax - footerH,
                inner.width,
                footerH);

            DrawBootHeader(headerRect, activeStage, confirmPhase);
            DrawBootBody(bodyRect, activeStage, confirmPhase, now);
            DrawBootFooter(footerRect, visualT, percent, activeStage, confirmPhase, now);
        }

        private void GetBootTiming(
            out bool confirmPhase,
            out float fillRawT,
            out float visualT,
            out int activeStage,
            out int percent)
        {
            double elapsed = Time.realtimeSinceStartupAsDouble - bootStartRealtime;
            float fillDuration = Mathf.Max(0.01f, bootDurationSeconds - BootConfirmSeconds);
            confirmPhase = elapsed >= fillDuration;
            fillRawT = confirmPhase
                ? 1f
                : Mathf.Clamp01((float)(elapsed / fillDuration));
            visualT = confirmPhase
                ? 1f
                : 1f - (1f - fillRawT) * (1f - fillRawT);
            activeStage = GetBootActiveStageIndex(fillRawT, confirmPhase);
            percent = confirmPhase
                ? 100
                : Mathf.Clamp(Mathf.FloorToInt(visualT * 100f + 0.0001f), 0, 100);
        }

        private void DrawBootTerminalGrid(Rect inRect)
        {
            float step = Mathf.Clamp(inRect.width / 24f, 36f, 56f);
            Color previous = GUI.color;
            GUI.color = new Color(
                MechanoidOvermindUiStyle.Border.r,
                MechanoidOvermindUiStyle.Border.g,
                MechanoidOvermindUiStyle.Border.b,
                0.07f);
            for (float x = inRect.x; x <= inRect.xMax; x += step)
            {
                Widgets.DrawLineVertical(x, inRect.y, inRect.height);
            }

            for (float y = inRect.y; y <= inRect.yMax; y += step)
            {
                Widgets.DrawLineHorizontal(inRect.x, y, inRect.width);
            }

            GUI.color = previous;
        }

        private static void DrawBootScanline(Rect inRect, double now, bool confirmPhase)
        {
            double period = confirmPhase ? 0.85d : BootScanPeriod;
            double cycle = now % period;
            if (cycle < 0d)
            {
                cycle += period;
            }

            float y = inRect.y + (float)(cycle / period) * inRect.height;
            float lineH = confirmPhase ? 3f : 2f;
            Color previous = GUI.color;
            GUI.color = new Color(
                MechanoidOvermindUiStyle.AccentBright.r,
                MechanoidOvermindUiStyle.AccentBright.g,
                MechanoidOvermindUiStyle.AccentBright.b,
                confirmPhase ? 0.20f : 0.08f);
            Widgets.DrawBoxSolid(
                new Rect(inRect.x, y, inRect.width, lineH),
                GUI.color);
            GUI.color = previous;
        }

        private void DrawBootHeader(Rect rect, int activeStage, bool confirmPhase)
        {
            float leftW = rect.width * 0.58f;
            float rightW = rect.width - leftW - 8f;
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.x, rect.y, leftW, 24f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Title".Translate(),
                GameFont.Medium);
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.x, rect.y + 26f, leftW, 18f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Subtitle".Translate(
                    overmindDisplayName),
                GameFont.Tiny,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.TextSecondary);

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.xMax - rightW, rect.y, rightW, 20f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Boot.AccessPermission".Translate(
                    GetAccessPermissionLabel()),
                GameFont.Tiny,
                TextAnchor.MiddleRight,
                MechanoidOvermindUiStyle.TextSecondary);
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.xMax - rightW, rect.y + 22f, rightW, 20f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Boot.LinkStatus".Translate(
                    GetBootStageStatusKey(activeStage, confirmPhase).Translate()),
                GameFont.Tiny,
                TextAnchor.MiddleRight,
                confirmPhase
                    ? MechanoidOvermindUiStyle.AccentBright
                    : MechanoidOvermindUiStyle.Accent);

            Color previous = GUI.color;
            GUI.color = MechanoidOvermindUiStyle.Accent;
            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, rect.width);
            GUI.color = previous;
        }

        private void DrawBootBody(
            Rect rect,
            int activeStage,
            bool confirmPhase,
            double now)
        {
            float gap = Mathf.Clamp(rect.width * 0.02f, 12f, 18f);
            float leftW = rect.width * 0.46f;
            float rightW = rect.width - leftW - gap;
            if (rightW < 40f || leftW < 40f)
            {
                leftW = rect.width * 0.5f - gap * 0.5f;
                rightW = rect.width - leftW - gap;
            }

            Rect leftRect = new Rect(rect.x, rect.y, Mathf.Max(0f, leftW), rect.height);
            Rect rightRect = new Rect(
                leftRect.xMax + gap,
                rect.y,
                Mathf.Max(0f, rightW),
                rect.height);

            Color previous = GUI.color;
            GUI.color = new Color(
                MechanoidOvermindUiStyle.Border.r,
                MechanoidOvermindUiStyle.Border.g,
                MechanoidOvermindUiStyle.Border.b,
                0.45f);
            Widgets.DrawLineVertical(leftRect.xMax + gap * 0.5f, rect.y + 4f, rect.height - 8f);
            GUI.color = previous;

            DrawBootNodeDiagram(leftRect, confirmPhase, now);
            DrawBootStageList(rightRect, activeStage, confirmPhase, now);
        }

        private void DrawBootNodeDiagram(Rect rect, bool confirmPhase, double now)
        {
            float breath = GetBootBreath01(now);
            float side = Mathf.Min(rect.width, rect.height);
            float centerSize = Mathf.Clamp(side * 0.62f, 110f, 165f);
            centerSize = Mathf.Min(centerSize, Mathf.Max(0f, side - 8f));
            if (centerSize < 8f)
            {
                return;
            }

            Rect centerRect = new Rect(
                rect.x + (rect.width - centerSize) * 0.5f,
                rect.y + (rect.height - centerSize) * 0.5f,
                centerSize,
                centerSize);

            Widgets.DrawBoxSolid(centerRect, MechanoidOvermindUiStyle.Panel);
            Color coreBorder = confirmPhase
                ? MechanoidOvermindUiStyle.AccentBright
                : Color.Lerp(
                    MechanoidOvermindUiStyle.Border,
                    MechanoidOvermindUiStyle.AccentBright,
                    0.35f + 0.45f * breath);
            Color previousCore = GUI.color;
            GUI.color = coreBorder;
            Widgets.DrawBox(centerRect, 2);
            Rect inset = centerRect.ContractedBy(5f);
            GUI.color = Color.Lerp(MechanoidOvermindUiStyle.Border, coreBorder, 0.7f);
            Widgets.DrawBox(inset, 1);
            GUI.color = previousCore;

            // 极淡内部分隔，保持机械终端层次。
            Color previous = GUI.color;
            GUI.color = new Color(
                MechanoidOvermindUiStyle.Border.r,
                MechanoidOvermindUiStyle.Border.g,
                MechanoidOvermindUiStyle.Border.b,
                0.35f);
            float midY = centerRect.y + centerSize * 0.52f;
            Widgets.DrawLineHorizontal(inset.x + 8f, midY, Mathf.Max(0f, inset.width - 16f));
            Widgets.DrawBoxSolid(
                new Rect(centerRect.x + centerSize * 0.5f - 10f, inset.y + 8f, 20f, 2f),
                Color.Lerp(
                    MechanoidOvermindUiStyle.Accent,
                    MechanoidOvermindUiStyle.AccentBright,
                    confirmPhase ? 1f : breath));
            GUI.color = previous;

            float titleH = 20f;
            float codeH = 18f;
            float titleY = centerRect.y + centerSize * 0.30f;
            float codeY = centerRect.y + centerSize * 0.58f;
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(centerRect.x + 8f, titleY, centerSize - 16f, titleH),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Boot.RemoteCoreNode".Translate(),
                GameFont.Small,
                TextAnchor.MiddleCenter,
                MechanoidOvermindUiStyle.TextPrimary);
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(centerRect.x + 8f, codeY, centerSize - 16f, codeH),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Boot.NodeCode".Translate(
                    overmindDisplayName),
                GameFont.Small,
                TextAnchor.MiddleCenter,
                MechanoidOvermindUiStyle.AccentBright);
        }

        private void DrawBootStageList(
            Rect rect,
            int activeStage,
            bool confirmPhase,
            double now)
        {
            float breath = GetBootBreath01(now);
            float rowH = Mathf.Max(28f, (rect.height - 9f) / 4f);
            for (int i = 0; i < 4; i++)
            {
                Rect row = new Rect(rect.x, rect.y + i * (rowH + 3f), rect.width, rowH);
                bool done = confirmPhase || i < activeStage;
                bool current = !confirmPhase && i == activeStage;
                Color textColor = done
                    ? MechanoidOvermindUiStyle.Accent
                    : current
                        ? Color.Lerp(
                            MechanoidOvermindUiStyle.Accent,
                            MechanoidOvermindUiStyle.AccentBright,
                            breath)
                        : MechanoidOvermindUiStyle.Disabled;

                if (current)
                {
                    Widgets.DrawBoxSolid(
                        new Rect(row.x, row.y + 2f, 3f, row.height - 4f),
                        Color.Lerp(
                            MechanoidOvermindUiStyle.Accent,
                            MechanoidOvermindUiStyle.AccentBright,
                            breath));
                }

                Rect lamp = new Rect(row.x + 10f, row.y + (row.height - 10f) * 0.5f, 10f, 10f);
                if (done || current)
                {
                    Widgets.DrawBoxSolid(lamp, textColor);
                }
                else
                {
                    Color previous = GUI.color;
                    GUI.color = MechanoidOvermindUiStyle.Disabled;
                    Widgets.DrawBox(lamp, 1);
                    GUI.color = previous;
                }

                string indexLabel = (i + 1).ToString("00");
                MechanoidOvermindUiStyle.DrawLabel(
                    new Rect(lamp.xMax + 8f, row.y, 28f, row.height),
                    indexLabel,
                    GameFont.Tiny,
                    TextAnchor.MiddleLeft,
                    textColor);
                MechanoidOvermindUiStyle.DrawLabel(
                    new Rect(lamp.xMax + 38f, row.y, row.width - 110f, row.height),
                    GetBootStageName(i),
                    GameFont.Small,
                    TextAnchor.MiddleLeft,
                    textColor);

                if (done)
                {
                    MechanoidOvermindUiStyle.DrawLabel(
                        new Rect(row.xMax - 48f, row.y, 48f, row.height),
                        "MAP_MechanoidMechanitor.PurgeDirective.Communication.Boot.Complete".Translate(),
                        GameFont.Tiny,
                        TextAnchor.MiddleRight,
                        MechanoidOvermindUiStyle.Accent);
                }
            }
        }

        private void DrawBootFooter(
            Rect rect,
            float visualT,
            int percent,
            int activeStage,
            bool confirmPhase,
            double now)
        {
            Rect barRect = new Rect(rect.x, rect.y, rect.width, 16f);
            MechanoidOvermindUiStyle.DrawAccentProgressBar(
                barRect,
                visualT,
                now,
                confirmPhase);

            string stageName = confirmPhase
                ? GetBootStageName(3)
                : GetBootStageName(Mathf.Clamp(activeStage, 0, 3));
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.x, barRect.yMax + 8f, rect.width * 0.7f, 20f),
                stageName,
                GameFont.Tiny,
                TextAnchor.MiddleLeft,
                confirmPhase
                    ? MechanoidOvermindUiStyle.AccentBright
                    : MechanoidOvermindUiStyle.TextSecondary);
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.xMax - rect.width * 0.28f, barRect.yMax + 8f, rect.width * 0.28f, 20f),
                percent.ToString("000") + "%",
                GameFont.Small,
                TextAnchor.MiddleRight,
                MechanoidOvermindUiStyle.TextPrimary);
        }

        private static float GetBootBreath01(double now)
        {
            double cycle = now % BootBreathPeriod;
            if (cycle < 0d)
            {
                cycle += BootBreathPeriod;
            }

            return 0.5f
                + 0.5f * (float)System.Math.Sin(cycle * (System.Math.PI * 2d / BootBreathPeriod));
        }

        private static int GetBootActiveStageIndex(float fillRawT, bool confirmPhase)
        {
            if (confirmPhase || fillRawT >= 1f)
            {
                return 4;
            }

            if (fillRawT < 0.25f)
            {
                return 0;
            }

            if (fillRawT < 0.5f)
            {
                return 1;
            }

            if (fillRawT < 0.75f)
            {
                return 2;
            }

            return 3;
        }

        private static string GetBootStageStatusKey(int activeStage, bool confirmPhase)
        {
            if (confirmPhase || activeStage >= 4)
            {
                return "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.Connected";
            }

            switch (activeStage)
            {
                case 0:
                    return "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.Connecting";
                case 1:
                    return GameComponent_CerebrexTakeoverState.IsActive
                        ? "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.VerifyingSource"
                        : "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.Verifying";
                case 2:
                    return GameComponent_CerebrexTakeoverState.IsActive
                        ? "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.CalculatingResources"
                        : "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.SyncingCredits";
                default:
                    return "MAP_MechanoidMechanitor.PurgeDirective.Communication.Boot.Status.JoiningNode";
            }
        }

        private static string GetBootStageName(int stageIndex)
        {
            switch (stageIndex)
            {
                case 0:
                    return "MAP_MechanoidMechanitor.PurgeDirective.Communication.Boot.Stage.Link"
                        .Translate();
                case 1:
                    return (GameComponent_CerebrexTakeoverState.IsActive
                        ? "MAP_MechanoidMechanitor.PurgeDirective.Communication.Boot.Stage.VerifyControl"
                        : "MAP_MechanoidMechanitor.PurgeDirective.Communication.Boot.Stage.Verify")
                        .Translate();
                case 2:
                    return (GameComponent_CerebrexTakeoverState.IsActive
                        ? "MAP_MechanoidMechanitor.PurgeDirective.Communication.Boot.Stage.CalculateResources"
                        : "MAP_MechanoidMechanitor.PurgeDirective.Communication.Boot.Stage.SyncCredits")
                        .Translate();
                default:
                    return "MAP_MechanoidMechanitor.PurgeDirective.Communication.Boot.Stage.JoinNode"
                        .Translate();
            }
        }

        private void DrawLayout(Rect inRect)
        {
            float topBarHeight = PurgeDirectiveRatingUtility.IsRatingSystemActive()
                ? RatingTopBarHeight
                : BaseTopBarHeight;
            Rect topRect = new Rect(inRect.x, inRect.y, inRect.width, topBarHeight);
            DrawTopBar(topRect);

            Rect bottomRect = new Rect(
                inRect.x,
                inRect.yMax - BottomBarHeight,
                inRect.width,
                BottomBarHeight);
            DrawBottomBar(bottomRect, bootPage: false);

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
            MechanoidOvermindPageKind? selected = homePage.Draw(
                cardsRect,
                inputEnabled: true,
                showDevEntry: Prefs.DevMode && devControlsEnabled,
                openDevPanel: OpenPurgeDirectiveDevPanel);
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
            if (currentPage == MechanoidOvermindPageKind.SpecialProtocols)
            {
                showOrder =
                    (specialProtocolsPage.ExpandedProtocol
                            == SpecialProtocolKind.MechClusterDeployment
                        && activeProtocolOrder != null)
                    || (specialProtocolsPage.ExpandedProtocol
                            == SpecialProtocolKind.MechForceSupport
                        && activeForceSupportOrder != null)
                    || (specialProtocolsPage.ExpandedProtocol == SpecialProtocolKind.BandwidthSupport
                        && specialProtocolsPage.BandwidthPage != null);
            }

            float dialogueH;
            float preferredDialogueH = GetPreferredDialogueSubHeight();
            if (showOrder)
            {
                float minOrderH = GetMinOrderPanelHeight();
                float available =
                    sideRect.height - subCore.height - Gap * 2f;
                if (available >= preferredDialogueH + minOrderH)
                {
                    dialogueH = preferredDialogueH;
                }
                else
                {
                    // 低分辨率下优先保证订单区最小完整高度，必要时压缩通讯输出。
                    dialogueH = Mathf.Max(0f, available - minOrderH);
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

            if (showOrder)
            {
                Rect orderRect = new Rect(
                    sideRect.x,
                    dialogueRect.yMax + Gap,
                    sideRect.width,
                    Mathf.Max(0f, sideRect.yMax - (dialogueRect.yMax + Gap)));
                if (currentPage == MechanoidOvermindPageKind.SpecialProtocols)
                {
                    if (specialProtocolsPage.ExpandedProtocol
                            == SpecialProtocolKind.MechClusterDeployment
                        && activeProtocolOrder != null)
                    {
                        DrawSpecialProtocolOrderPanel(orderRect, activeProtocolOrder);
                    }
                    else if (specialProtocolsPage.ExpandedProtocol
                            == SpecialProtocolKind.MechForceSupport
                        && activeForceSupportOrder != null)
                    {
                        DrawMechForceSupportOrderPanel(
                            orderRect,
                            activeForceSupportOrder);
                    }
                    else if (specialProtocolsPage.ExpandedProtocol == SpecialProtocolKind.BandwidthSupport
                        && specialProtocolsPage.BandwidthPage is MechanoidOvermindPage_BandwidthSupport bandwidthPage)
                    {
                        DrawBandwidthSupportOrderPanel(orderRect, bandwidthPage);
                    }
                }
                else if (activeOrder != null)
                {
                    DrawOrderPanel(orderRect, activeOrder);
                }
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
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.BackToHome".Translate(),
                    enabled: !transitioning)
                && !transitioning)
            {
                BeginTransitionHome();
            }

            string pageTitle = GetPageTitle(currentPage);
            Rect titleRect = new Rect(
                backRect.xMax + 10f, inner.y, inner.width - backRect.width - 10f, inner.height);
            MechanoidOvermindUiStyle.DrawLabel(
                titleRect,
                pageTitle,
                GameFont.Small,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.AccentBright);

            if (currentPage == MechanoidOvermindPageKind.Goods
                && PurgeDirectiveRatingUtility.IsRatingSystemActive()
                && !PurgeDirectiveRatingUtility.IsMaxRatingLevel())
            {
                using (MechanoidOvermindUiStyle.Push())
                {
                    Text.Font = GameFont.Small;
                    float hintX = titleRect.x + Text.CalcSize(pageTitle).x + 16f;
                    MechanoidOvermindUiStyle.DrawLabel(
                        new Rect(hintX, inner.y, Mathf.Max(0f, inner.xMax - hintX), inner.height),
                        "MAP_MechanoidMechanitor.PurgeDirective.Communication.Goods.RatingUnlockHint".Translate(),
                        GameFont.Small,
                        TextAnchor.MiddleLeft,
                        Color.Lerp(MechanoidOvermindUiStyle.TextSecondary, MechanoidOvermindUiStyle.TextPrimary, 0.5f));
                }
            }

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
                        goodsPage.Draw(contentRect, activeOrder, DevShowDetailedGoodsPrice);
                    }

                    break;
                case MechanoidOvermindPageKind.SpecialProtocols:
                    if (activeProtocolOrder != null
                        && activeForceSupportOrder != null)
                    {
                        SpecialProtocolKind previousExpanded =
                            specialProtocolsPage.ExpandedProtocol;
                        int previousClusterRevision = activeProtocolOrder.Revision;
                        specialProtocolsPage.Draw(
                            contentRect,
                            activeProtocolOrder,
                            activeForceSupportOrder);
                        if (specialProtocolsPage.ExpandedProtocol != previousExpanded
                            || activeProtocolOrder.Revision
                                != previousClusterRevision)
                        {
                            preparedClusterSession = null;
                        }
                    }

                    break;
                case MechanoidOvermindPageKind.Communication:
                    MechanoidOvermindCommunicationQueryKind? query =
                        communicationPage.Draw(contentRect);
                    if (query.HasValue)
                    {
                        PlayCommunicationQueryResponse(query.Value);
                    }

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
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.Core.Missing".Translate(),
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
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Title".Translate(),
                GameFont.Medium);

            int credits = GameComponent_MechanoidMechanitorStoryState.GetPurgeDirectiveRewardPoints();
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(inner.x, inner.y + 28f, inner.width * 0.55f, 30f),
                (GameComponent_CerebrexTakeoverState.IsActive
                    ? "MAP_MechanoidMechanitor.PurgeDirective.Communication.CallableCredits"
                    : "MAP_MechanoidMechanitor.PurgeDirective.Communication.Credits").Translate(credits),
                GameFont.Medium,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.AccentBright);

            float rightWidth = inner.width * 0.42f;
            float rightX = inner.xMax - rightWidth;
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rightX, inner.y, rightWidth, 20f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Subtitle".Translate(
                    overmindDisplayName),
                GameFont.Small,
                TextAnchor.MiddleRight,
                MechanoidOvermindUiStyle.TextSecondary);

            bool ratingActive = PurgeDirectiveRatingUtility.IsRatingSystemActive();
            float connectionY = ratingActive ? 38f : 20f;

            if (ratingActive)
            {
                int ratingLevel = PurgeDirectiveRatingUtility.CurrentRatingLevel;
                int ratingValue = PurgeDirectiveRatingUtility.CurrentRatingValue();
                int nextThreshold = PurgeDirectiveRatingUtility.Config.GetNextLevelStart(ratingLevel);
                string ratingText = PurgeDirectiveRatingUtility.IsMaxRatingLevel()
                    ? "MAP_PurgeDirectiveRating.TopBar.Maxed".Translate(
                        PurgeDirectiveRatingDisplay.RatingName(ratingLevel),
                        ratingValue,
                        PurgeDirectiveRatingUtility.Config.maxRatingValue)
                    : "MAP_PurgeDirectiveRating.TopBar.Level".Translate(
                        PurgeDirectiveRatingDisplay.RatingName(ratingLevel),
                        ratingValue,
                        nextThreshold);
                MechanoidOvermindUiStyle.DrawLabel(
                    new Rect(rightX, inner.y + 20f, rightWidth, 18f),
                    ratingText,
                    GameFont.Tiny,
                    TextAnchor.MiddleRight,
                    MechanoidOvermindUiStyle.AccentBright);
            }

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rightX, inner.y + connectionY, rightWidth, 18f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.ConnectionStable".Translate(),
                GameFont.Tiny,
                TextAnchor.MiddleRight,
                MechanoidOvermindUiStyle.TextSecondary);
        }

        private void DrawBottomBar(Rect rect, bool bootPage)
        {
            MechanoidOvermindUiStyle.DrawPanel(rect, cornerMarks: false);

            if (!Prefs.DevMode)
            {
                devControlsEnabled = false;
                bootLoopTestEnabled = false;
                forceMojibakeDialogue = false;
                showDetailedGoodsPrice = false;
            }

            if (!devControlsEnabled)
            {
                bootLoopTestEnabled = false;
                forceMojibakeDialogue = false;
                showDetailedGoodsPrice = false;
            }

            const float edgePad = 8f;
            const float itemGap = 4f;
            const float disconnectW = 110f;
            const float devToggleW = 54f;
            const float creditsW = 96f;
            const float maxRatingW = 112f;
            const float loopW = 144f;

            bool showDevToggle = Prefs.DevMode;
            bool showQuickControls = showDevToggle && devControlsEnabled;
            bool freezeDev = !bootPage && transitioning;
            float rightClusterW = disconnectW
                + (showDevToggle ? itemGap + devToggleW : 0f)
                + (showQuickControls ? creditsW + maxRatingW + loopW + itemGap * 3f : 0f);

            string statusText;
            Color color = MechanoidOvermindUiStyle.TextSecondary;
            if (bootPage)
            {
                GetBootTiming(out bool confirmPhase, out _, out _, out int activeStage, out _);
                string phaseKey = GetBootStageStatusKey(activeStage, confirmPhase);
                statusText = phaseKey.Translate();
                color = confirmPhase
                    ? MechanoidOvermindUiStyle.AccentBright
                    : MechanoidOvermindUiStyle.Accent;
            }
            else
            {
                statusText = statusKey.Translate();
                if (statusKey.IndexOf("Error", StringComparison.Ordinal) >= 0
                    || statusKey.IndexOf("Insufficient", StringComparison.Ordinal) >= 0)
                {
                    color = MechanoidOvermindUiStyle.Error;
                }
                else if (statusKey.IndexOf("Accepted", StringComparison.Ordinal) >= 0
                    || statusKey.IndexOf("Connected", StringComparison.Ordinal) >= 0)
                {
                    color = MechanoidOvermindUiStyle.AccentBright;
                }
            }

            float statusWidth = Mathf.Max(
                40f,
                rect.width - edgePad * 2f - rightClusterW - itemGap);
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.x + edgePad, rect.y + 4f, statusWidth, rect.height - 8f),
                statusText,
                GameFont.Tiny,
                TextAnchor.MiddleLeft,
                color);

            float cursorX = rect.xMax - edgePad;
            float buttonY = rect.y + 4f;
            float buttonH = rect.height - 8f;

            cursorX -= disconnectW;
            if (MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(cursorX, buttonY, disconnectW, buttonH),
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.Nav.Disconnect".Translate()))
            {
                Close(doCloseSound: true);
            }

            if (showDevToggle)
            {
                cursorX -= itemGap + devToggleW;
                Rect toggleRect = new Rect(cursorX, rect.y + 6f, devToggleW, rect.height - 12f);
                if (freezeDev)
                {
                    bool frozen = devControlsEnabled;
                    Widgets.CheckboxLabeled(
                        toggleRect,
                        "DEV",
                        ref frozen);
                }
                else
                {
                    bool enabled = devControlsEnabled;
                    Widgets.CheckboxLabeled(
                        toggleRect,
                        "DEV",
                        ref enabled);
                    devControlsEnabled = enabled;
                    if (!devControlsEnabled)
                    {
                        bootLoopTestEnabled = false;
                        forceMojibakeDialogue = false;
                        showDetailedGoodsPrice = false;
                    }
                }
            }

            if (showQuickControls)
            {
                bool previousEnabled = GUI.enabled;
                bool shortcutsEnabled = previousEnabled && !freezeDev && devControlsEnabled;
                GUI.enabled = shortcutsEnabled;
                cursorX -= itemGap + loopW;
                bool loop = DevBootLoopTest;
                Widgets.CheckboxLabeled(new Rect(cursorX, rect.y + 6f, loopW, rect.height - 12f),
                    "循环播放启动动画", ref loop);
                DevBootLoopTest = loop;
                cursorX -= itemGap + maxRatingW;
                if (MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(cursorX, buttonY, maxRatingW, buttonH), "等级设为满级", shortcutsEnabled))
                    PurgeDirectiveRatingUtility.SetRatingDirect(PurgeDirectiveRatingUtility.Config.maxRatingValue);
                cursorX -= itemGap + creditsW;
                if (MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(cursorX, buttonY, creditsW, buttonH), "额度+1000", shortcutsEnabled))
                    DevAdjustPurgeCredits(1000);
                GUI.enabled = previousEnabled;
            }
        }

        internal bool DevShowDetailedGoodsPrice
        {
            get => Prefs.DevMode && devControlsEnabled && showDetailedGoodsPrice;
            set => showDetailedGoodsPrice = Prefs.DevMode && devControlsEnabled && value;
        }

        internal bool DevForceMojibake
        {
            get => forceMojibakeDialogue;
            set => forceMojibakeDialogue = Prefs.DevMode && devControlsEnabled && value;
        }

        internal bool DevBootLoopTest
        {
            get => bootLoopTestEnabled;
            set
            {
                if (!Prefs.DevMode || !devControlsEnabled)
                {
                    bootLoopTestEnabled = false;
                    return;
                }

                bool wasEnabled = bootLoopTestEnabled;
                bootLoopTestEnabled = value;
                if (value && !wasEnabled && bootComplete)
                {
                    StartBootSequence();
                }
            }
        }

        internal void DevAdjustPurgeCredits(int delta)
        {
            if (!Prefs.DevMode || !devControlsEnabled || delta == 0)
            {
                return;
            }

            try
            {
                if (delta > 0)
                {
                    GameComponent_MechanoidMechanitorStoryState.RefundPurgeDirectiveCredits(delta);
                    return;
                }

                int current =
                    GameComponent_MechanoidMechanitorStoryState.GetPurgeDirectiveRewardPoints();
                int spend = Mathf.Min(current, -delta);
                if (spend > 0)
                {
                    GameComponent_MechanoidMechanitorStoryState.TrySpendPurgeDirectiveCredits(spend);
                }
            }
            catch (Exception)
            {
                // DEV 调试失败时静默，避免中断通讯窗口。
            }
        }

        private void OpenPurgeDirectiveDevPanel()
        {
            if (Prefs.DevMode && devControlsEnabled)
            {
                Find.WindowStack.Add(new Dialog_PurgeDirectiveDev(this));
            }
        }

        private void DrawDialoguePanel(Rect rect)
        {
            dialogueTyper.Tick();

            MechanoidOvermindUiStyle.DrawPanel(rect);
            Rect inner = rect.ContractedBy(8f);

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(inner.x, inner.y, inner.width, 22f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Dialogue.Title".Translate(),
                GameFont.Small);

            Rect bodyRect = new Rect(
                inner.x,
                inner.y + 26f,
                inner.width,
                Mathf.Max(0f, inner.height - 26f));
            MechanoidOvermindUiStyle.DrawPanel(bodyRect, alt: true, cornerMarks: false);

            Rect contentRect = bodyRect.ContractedBy(8f);
            string visibleText = dialogueTyper.VisibleText;

            float contentWidth = Mathf.Max(1f, contentRect.width);
            float lineHeight;
            float textHeight;
            using (MechanoidOvermindUiStyle.Push())
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = true;
                lineHeight = Text.LineHeight;
                textHeight = string.IsNullOrEmpty(visibleText)
                    ? 0f
                    : Text.CalcHeight(visibleText, contentWidth);
            }

            // 终端式淘汰：按完整行高向上偏移，裁剪可见区，不截断富文本字符串。
            float offsetY = 0f;
            if (textHeight > contentRect.height && lineHeight > 0.01f)
            {
                float excess = textHeight - contentRect.height;
                int linesUp = Mathf.CeilToInt(excess / lineHeight);
                offsetY = linesUp * lineHeight;
            }

            GUI.BeginGroup(contentRect);
            if (!string.IsNullOrEmpty(visibleText))
            {
                MechanoidOvermindUiStyle.DrawLabel(
                    new Rect(0f, -offsetY, contentWidth, Mathf.Max(textHeight, lineHeight)),
                    visibleText,
                    GameFont.Small,
                    TextAnchor.UpperLeft,
                    MechanoidOvermindUiStyle.TextPrimary,
                    wordWrap: true);
            }

            GUI.EndGroup();
        }

        private static float GetPreferredDialogueSubHeight()
        {
            float smallLineHeight;
            using (MechanoidOvermindUiStyle.Push())
            {
                Text.Font = GameFont.Small;
                smallLineHeight = Text.LineHeight;
            }

            // 外框内边距 8×2 + 标题区 26 + 正文区内边距 8×2 + 至少五行 Small 正文。
            const float outerPad = 16f;
            const float titleBlock = 26f;
            const float bodyPad = 16f;
            return outerPad + titleBlock + bodyPad + smallLineHeight * 5f;
        }

        private static void GetOrderFooterLineHeights(
            out float smallLineHeight,
            out float tinyLineHeight)
        {
            using (MechanoidOvermindUiStyle.Push())
            {
                Text.Font = GameFont.Small;
                smallLineHeight = Text.LineHeight;
                Text.Font = GameFont.Tiny;
                tinyLineHeight = Text.LineHeight;
            }
        }

        private static float GetOrderFooterRequiredHeight()
        {
            GetOrderFooterLineHeights(out float smallLineHeight, out float tinyLineHeight);
            const float summaryRowGap = 2f;
            const float statusToButtonsGap = 6f;
            const float buttonHeight = 26f;
            return smallLineHeight
                + tinyLineHeight
                + tinyLineHeight
                + summaryRowGap * 2f
                + tinyLineHeight
                + statusToButtonsGap
                + buttonHeight;
        }

        private static float GetMinOrderPanelHeight()
        {
            const float outerPad = 16f;
            const float titleBlock = 28f;
            const float minListHeight = 40f;
            const float listFooterGap = 6f;
            return outerPad
                + titleBlock
                + minListHeight
                + listFooterGap
                + GetOrderFooterRequiredHeight();
        }

        private void DrawOrderPanel(Rect rect, MechanoidOvermindOrder order)
        {
            MechanoidOvermindUiStyle.DrawPanel(rect);
            Rect inner = rect.ContractedBy(8f);

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(inner.x, inner.y, inner.width, 24f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.Title".Translate(),
                GameFont.Small);

            bool costsOk = order.TryGetCosts(out _, out _, out int totalCost);
            int credits = GameComponent_MechanoidMechanitorStoryState
                .GetPurgeDirectiveRewardPoints();
            RefreshDropSpotCache(order, force: false);

            const float titleBlock = 28f;
            const float listFooterGap = 6f;
            float footerHeight = GetOrderFooterRequiredHeight();
            float listHeight = inner.height - titleBlock - listFooterGap - footerHeight;
            if (listHeight < 0f)
            {
                listHeight = 0f;
                footerHeight = Mathf.Max(0f, inner.height - titleBlock - listFooterGap);
            }

            Rect listRect = new Rect(
                inner.x,
                inner.y + titleBlock,
                inner.width,
                listHeight);
            DrawOrderLines(listRect, order, costsOk);

            Rect footer = new Rect(
                inner.x,
                inner.yMax - footerHeight,
                inner.width,
                footerHeight);
            DrawOrderFooter(footer, order, costsOk, totalCost, credits);
        }

        private void DrawOrderLines(Rect listRect, MechanoidOvermindOrder order, bool costsOk)
        {
            MechanoidOvermindUiStyle.DrawPanel(listRect, alt: true, cornerMarks: false);
            int lineCount = order.MechLines.Count + order.ThingLines.Count;
            float viewHeight = Mathf.Max(listRect.height, lineCount * OrderRowStride + 4f);
            bool scrolling = viewHeight > listRect.height;
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(1f, listRect.width - (scrolling ? 16f : 0f)), viewHeight);
            orderScroll.y = Mathf.Clamp(orderScroll.y, 0f, Mathf.Max(0f, viewHeight - listRect.height));
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
                    Rect rowRect = new Rect(3f, y, Mathf.Max(1f, viewRect.width - 6f), OrderRowHeight);
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

            string meta = "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.MechLineMeta"
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
                meta = "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.ThingLineMetaStuffQuality"
                    .Translate(
                        line.Count,
                        line.Spec.Stuff!.LabelCap,
                        line.Spec.Quality.GetLabel().CapitalizeFirst());
            }
            else if (hasStuff)
            {
                meta = "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.ThingLineMetaStuff"
                    .Translate(line.Count, line.Spec.Stuff!.LabelCap);
            }
            else if (hasQuality)
            {
                meta = "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.ThingLineMetaQuality"
                    .Translate(
                        line.Count,
                        line.Spec.Quality.GetLabel().CapitalizeFirst());
            }
            else
            {
                meta = "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.ThingLineMeta"
                    .Translate(line.Count);
            }

            string compactMeta = "×" + line.Count;
            if (hasStuff)
            {
                compactMeta += " · " + line.Spec.Stuff!.LabelCap;
            }
            if (hasQuality)
            {
                compactMeta += " · " + line.Spec.Quality.GetLabel().CapitalizeFirst();
            }
            return DrawOrderLine(rect, line.Spec.Def.LabelCap, meta, compactMeta);
        }

        private bool DrawOrderLine(Rect rect, string name, string meta, string? compactMeta = null)
        {
            Widgets.DrawBoxSolid(rect, MechanoidOvermindUiStyle.Panel);
            Rect removeRect = new Rect(rect.xMax - 26f, rect.y + 2f, 24f, rect.height - 4f);
            float contentWidth = Mathf.Max(1f, removeRect.x - rect.x - 12f);
            using (MechanoidOvermindUiStyle.Push())
            {
                Text.Font = GameFont.Tiny;
                string displayMeta = compactMeta ?? meta;
                float metaWidth = Mathf.Min(Text.CalcSize(displayMeta).x + 1f, Mathf.Max(1f, contentWidth - 30f));
                float nameWidth = Mathf.Min(Text.CalcSize(name).x + 1f, Mathf.Max(1f, contentWidth - metaWidth - 6f));
                Rect nameRect = new Rect(rect.x + 6f, rect.y, nameWidth, rect.height);
                Rect metaRect = new Rect(nameRect.xMax + 6f, rect.y, metaWidth, rect.height);
                MechanoidOvermindUiStyle.DrawLabel(nameRect, name.Truncate(nameRect.width), GameFont.Tiny);
                MechanoidOvermindUiStyle.DrawSecondaryLabel(metaRect, displayMeta.Truncate(metaRect.width), TextAnchor.MiddleLeft);
            }
            TooltipHandler.TipRegion(new Rect(rect.x, rect.y, contentWidth + 6f, rect.height), name + "\n" + meta);
            TooltipHandler.TipRegion(removeRect, "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.Remove".Translate());
            return MechanoidOvermindUiStyle.DrawActionButton(removeRect, "×");
        }

        private void DrawOrderFooter(
            Rect rect,
            MechanoidOvermindOrder order,
            bool costsOk,
            int totalCost,
            int credits)
        {
            GetOrderFooterLineHeights(out float smallLineHeight, out float tinyLineHeight);

            // 统一折扣计价：显示与扣款共用同一最终费用，避免折扣显示 / 余额检查 / 实际扣款漂移。
            bool hasDiscount = MechanoidOvermindRatingPricingService.TryCalculateFinalOrderCosts(
                order,
                out _,
                out _,
                out float discountRate,
                out int discountAmount,
                out int finalCost);
            if (!hasDiscount)
            {
                finalCost = totalCost;
                discountAmount = 0;
                discountRate = 0f;
            }

            const float summaryRowGap = 2f;
            float summaryHeight = smallLineHeight + tinyLineHeight + tinyLineHeight
                + summaryRowGap * 2f;
            float statusHeight = tinyLineHeight;
            const float statusToButtonsGap = 6f;
            const float buttonWidth = 94f;
            const float buttonHeight = 26f;
            const float buttonGap = 8f;

            bool connected = MechanoidMechanitorMechHiveCommunicationUtility
                .TryGetContactableMechHive(out _);
            bool supplyAvailable = GameComponent_OvermindEconomy.HasSupply(order);
            bool canConfirm = costsOk
                && supplyAvailable
                && !order.IsEmpty
                && connected
                && cachedDropValid
                && credits >= finalCost
                && currentPage != MechanoidOvermindPageKind.SpecialProtocols;

            GUI.BeginGroup(rect);

            float summaryY = 0f;
            int shownTotal = costsOk ? finalCost : 0;
            string totalLabel = discountAmount > 0
                ? "MAP_PurgeDirectiveRating.Order.TotalWithDiscount".Translate(shownTotal, discountAmount)
                : "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.Total".Translate(shownTotal);
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(0f, summaryY, rect.width, smallLineHeight),
                totalLabel,
                GameFont.Small,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.AccentBright);
            summaryY += smallLineHeight + summaryRowGap;
            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(0f, summaryY, rect.width, tinyLineHeight),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.CurrentCredits".Translate(
                    credits));
            summaryY += tinyLineHeight + summaryRowGap;
            int balance = costsOk ? credits - finalCost : credits;
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(0f, summaryY, rect.width, tinyLineHeight),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.BalanceAfter".Translate(
                    balance),
                GameFont.Tiny,
                TextAnchor.MiddleLeft,
                balance < 0
                    ? MechanoidOvermindUiStyle.Error
                    : MechanoidOvermindUiStyle.TextSecondary);

            Rect statusRect = new Rect(0f, summaryHeight, rect.width, statusHeight);
            if (!supplyAvailable)
            {
                MechanoidOvermindUiStyle.DrawLabel(statusRect,
                    GameComponent_OvermindEconomy.SupplyError.Translate(), GameFont.Tiny,
                    TextAnchor.MiddleLeft, MechanoidOvermindUiStyle.Error);
            }
            else if (!cachedDropValid)
            {
                MechanoidOvermindUiStyle.DrawLabel(
                    statusRect,
                    (cachedDropMap == null
                        ? "MAP_MechanoidMechanitor.PurgeDirective.Communication.Error.NoMap"
                        : "MAP_MechanoidMechanitor.PurgeDirective.Communication.Error.NoDropSpot")
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

            float buttonsY = statusRect.yMax + statusToButtonsGap;
            float buttonsGroupWidth = buttonWidth * 2f + buttonGap;
            float buttonsX = (rect.width - buttonsGroupWidth) * 0.5f;
            if (MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(buttonsX, buttonsY, buttonWidth, buttonHeight),
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.Clear".Translate()))
            {
                order.Clear();
                statusKey = "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.WaitingInput";
            }

            if (MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(
                        buttonsX + buttonWidth + buttonGap,
                        buttonsY,
                        buttonWidth,
                        buttonHeight),
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.Confirm".Translate(),
                    enabled: canConfirm))
            {
                TryConfirmDelivery(order);
            }

            GUI.EndGroup();
        }


        private Vector2 bandwidthOrderScroll;

        private void DrawBandwidthSupportOrderPanel(Rect rect, MechanoidOvermindPage_BandwidthSupport page)
        {
            BandwidthSupportOrder order = page.Order;
            MechanoidOvermindUiStyle.DrawPanel(rect);
            Rect inner = rect.ContractedBy(8f);
            MechanoidOvermindUiStyle.DrawLabel(new Rect(inner.x, inner.y, inner.width, 24f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.Title".Translate());
            const float footerHeight = 126f;
            Rect listRect = new Rect(inner.x, inner.y + 28f, inner.width,
                Mathf.Max(0f, inner.height - 28f - footerHeight - 6f));
            var state = GameComponent_OvermindBandwidthSupport.Current;
            var lines = new List<string>();
            if (state != null)
            {
                if (order.Requested != state.Requested)
                    lines.Add((GameComponent_OvermindBandwidthSupport.TakenOver
                        ? "MAP_BandwidthSupport.RequestedTotal" : "MAP_BandwidthSupport.RequestedExtension")
                        .Translate() + ": " + state.Requested + " → " + order.Requested);
                foreach (var pair in order.Allocations)
                {
                    int current = state.AllocatedTo(pair.Key);
                    if (current != pair.Value)
                        lines.Add(pair.Key.LabelShort + ": " + current + " → " + pair.Value);
                }
            }
            if (lines.Count == 0) lines.Add("MAP_BandwidthSupport.NoPendingOrder".Translate());
            if (listRect.height > 12f)
            {
                MechanoidOvermindUiStyle.DrawPanel(listRect, alt: true, cornerMarks: false);
                Rect viewport = listRect.ContractedBy(6f);
                Rect view = new Rect(0f, 0f, Mathf.Max(1f, viewport.width - 20f),
                    Mathf.Max(viewport.height, 26f + lines.Count * 24f));
                Widgets.BeginScrollView(viewport, ref bandwidthOrderScroll, view);
                MechanoidOvermindUiStyle.DrawLabel(new Rect(0f, 0f, view.width, 24f),
                    "MAP_BandwidthSupport.Title".Translate(), color: MechanoidOvermindUiStyle.AccentBright);
                for (int i = 0; i < lines.Count; i++)
                {
                    Rect row = new Rect(0f, 26f + i * 24f, view.width, 22f);
                    MechanoidOvermindUiStyle.DrawSecondaryLabel(row, lines[i]);
                    TooltipHandler.TipRegion(row, lines[i]);
                }
                Widgets.EndScrollView();
            }
            float availableFooterHeight = Mathf.Max(0f, Mathf.Min(footerHeight, inner.height - 28f));
            Rect footer = new Rect(inner.x, inner.yMax - availableFooterHeight,
                inner.width, availableFooterHeight);
            if (availableFooterHeight < 26f) return;
            int credits = GameComponent_MechanoidMechanitorStoryState.GetPurgeDirectiveRewardPoints();
            MechanoidOvermindUiStyle.DrawLabel(new Rect(footer.x, footer.y, footer.width, 24f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.Total".Translate(order.Cost),
                color: MechanoidOvermindUiStyle.AccentBright);
            MechanoidOvermindUiStyle.DrawSecondaryLabel(new Rect(footer.x, footer.y + 26f, footer.width, 20f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.CurrentCredits".Translate(credits));
            MechanoidOvermindUiStyle.DrawSecondaryLabel(new Rect(footer.x, footer.y + 48f, footer.width, 20f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.BalanceAfter".Translate(credits - order.Cost));
            if (order.HasChanges && !order.CanExecute)
                MechanoidOvermindUiStyle.DrawSecondaryLabel(new Rect(footer.x, footer.y + 70f, footer.width, 20f),
                    "MAP_BandwidthSupport.InvalidOrder".Translate());
            float buttonWidth = Mathf.Min(94f, (footer.width - 8f) * 0.5f);
            float x = footer.x + (footer.width - buttonWidth * 2f - 8f) * 0.5f;
            if (MechanoidOvermindUiStyle.DrawActionButton(new Rect(x, footer.yMax - 26f, buttonWidth, 26f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.Clear".Translate(), !transitioning))
                page.ResetOrder();
            if (MechanoidOvermindUiStyle.DrawActionButton(
                new Rect(x + buttonWidth + 8f, footer.yMax - 26f, buttonWidth, 26f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.Confirm".Translate(),
                !transitioning && order.CanExecute, emphasized: true))
                page.ExecuteOrder();
        }

        private void DrawSpecialProtocolOrderPanel(
            Rect rect,
            MechClusterDeploymentOrder order)
        {
            MechanoidOvermindUiStyle.DrawPanel(rect);
            Rect inner = rect.ContractedBy(8f);

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(inner.x, inner.y, inner.width, 24f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.Title".Translate(),
                GameFont.Small);

            const float titleBlock = 28f;
            const float listFooterGap = 6f;
            float footerHeight = GetOrderFooterRequiredHeight();
            float listHeight = inner.height - titleBlock - listFooterGap - footerHeight;
            if (listHeight < 0f)
            {
                listHeight = 0f;
                footerHeight = Mathf.Max(0f, inner.height - titleBlock - listFooterGap);
            }

            Rect listRect = new Rect(
                inner.x,
                inner.y + titleBlock,
                inner.width,
                listHeight);
            DrawSpecialProtocolOrderLine(listRect, order);

            Rect footer = new Rect(
                inner.x,
                inner.yMax - footerHeight,
                inner.width,
                footerHeight);
            DrawSpecialProtocolOrderFooter(footer, order);
        }

        private static void DrawSpecialProtocolOrderLine(
            Rect rect,
            MechClusterDeploymentOrder order)
        {
            MechanoidOvermindUiStyle.DrawPanel(rect, alt: true, cornerMarks: false);
            if (rect.height <= 2f)
            {
                return;
            }

            string conditionLabel = order.ConditionCauser?.LabelCap
                ?? "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.NoConditionCauser"
                    .Translate();
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, 20f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.Title"
                    .Translate(),
                GameFont.Small,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.TextPrimary);
            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(rect.x + 8f, rect.y + 25f, rect.width - 16f, 20f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.OrderMeta"
                    .Translate(order.ThreatPoints, conditionLabel));
            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(rect.x + 8f, rect.y + 46f, rect.width - 16f, 20f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.OrderCost"
                    .Translate(order.Cost));
        }

        private void DrawSpecialProtocolOrderFooter(
            Rect rect,
            MechClusterDeploymentOrder order)
        {
            GetOrderFooterLineHeights(out float smallLineHeight, out float tinyLineHeight);

            const float summaryRowGap = 2f;
            float summaryHeight = smallLineHeight + tinyLineHeight + tinyLineHeight
                + summaryRowGap * 2f;
            float statusHeight = tinyLineHeight;
            const float statusToButtonsGap = 6f;
            const float buttonWidth = 94f;
            const float buttonHeight = 26f;
            const float buttonGap = 8f;

            int totalCost = order.Cost;
            int credits = GameComponent_MechanoidMechanitorStoryState
                .GetPurgeDirectiveRewardPoints();
            bool available = MechClusterDeploymentService.TryResolveAvailableMap(out _);
            bool validCondition = order.ConditionCauser == null
                || MechClusterDeploymentService.IsConditionCauser(
                    order.ConditionCauser,
                    order.ThreatPoints);
            bool canConfirm = available
                && validCondition
                && credits >= totalCost
                && !transitioning;

            GUI.BeginGroup(rect);

            float summaryY = 0f;
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(0f, summaryY, rect.width, smallLineHeight),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.Total".Translate(
                    totalCost),
                GameFont.Small,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.AccentBright);
            summaryY += smallLineHeight + summaryRowGap;
            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(0f, summaryY, rect.width, tinyLineHeight),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.CurrentCredits".Translate(
                    credits));
            summaryY += tinyLineHeight + summaryRowGap;
            int balance = credits - totalCost;
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(0f, summaryY, rect.width, tinyLineHeight),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.BalanceAfter".Translate(
                    balance),
                GameFont.Tiny,
                TextAnchor.MiddleLeft,
                balance < 0
                    ? MechanoidOvermindUiStyle.Error
                    : MechanoidOvermindUiStyle.TextSecondary);

            Rect statusRect = new Rect(0f, summaryHeight, rect.width, statusHeight);
            if (!available)
            {
                MechanoidOvermindUiStyle.DrawLabel(
                    statusRect,
                    MechClusterDeploymentService.ErrorUnavailable.Translate(),
                    GameFont.Tiny,
                    TextAnchor.MiddleLeft,
                    MechanoidOvermindUiStyle.Error);
            }
            else if (credits < totalCost)
            {
                MechanoidOvermindUiStyle.DrawLabel(
                    statusRect,
                    MechClusterDeploymentService.ErrorInsufficientCredits.Translate(),
                    GameFont.Tiny,
                    TextAnchor.MiddleLeft,
                    MechanoidOvermindUiStyle.Error);
            }

            float buttonsY = statusRect.yMax + statusToButtonsGap;
            float buttonsGroupWidth = buttonWidth * 2f + buttonGap;
            float buttonsX = (rect.width - buttonsGroupWidth) * 0.5f;
            if (MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(buttonsX, buttonsY, buttonWidth, buttonHeight),
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.Clear".Translate()))
            {
                specialProtocolsPage.CollapseExpandedProtocol(
                    order,
                    activeForceSupportOrder);
                preparedClusterSession = null;
                statusKey = "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.WaitingInput";
            }

            if (MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(
                        buttonsX + buttonWidth + buttonGap,
                        buttonsY,
                        buttonWidth,
                        buttonHeight),
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.Confirm".Translate(),
                    enabled: canConfirm))
            {
                TryBeginMechClusterDeployment(order);
            }

            GUI.EndGroup();
        }

        private void DrawMechForceSupportOrderPanel(
            Rect rect,
            MechForceSupportOrder order)
        {
            MechanoidOvermindUiStyle.DrawPanel(rect);
            Rect inner = rect.ContractedBy(8f);

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(inner.x, inner.y, inner.width, 24f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.Title".Translate(),
                GameFont.Small);

            const float titleBlock = 28f;
            const float listFooterGap = 6f;
            float footerHeight = GetOrderFooterRequiredHeight();
            float listHeight = inner.height - titleBlock - listFooterGap - footerHeight;
            if (listHeight < 0f)
            {
                listHeight = 0f;
                footerHeight = Mathf.Max(0f, inner.height - titleBlock - listFooterGap);
            }

            Rect listRect = new Rect(
                inner.x,
                inner.y + titleBlock,
                inner.width,
                listHeight);
            DrawMechForceSupportOrderLine(listRect, order);

            Rect footer = new Rect(
                inner.x,
                inner.yMax - footerHeight,
                inner.width,
                footerHeight);
            DrawMechForceSupportOrderFooter(footer, order);
        }

        private static void DrawMechForceSupportOrderLine(
            Rect rect,
            MechForceSupportOrder order)
        {
            MechanoidOvermindUiStyle.DrawPanel(rect, alt: true, cornerMarks: false);
            if (rect.height <= 2f)
            {
                return;
            }

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, 20f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.ForceSupport.Title"
                    .Translate(),
                GameFont.Small,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.TextPrimary);
            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(rect.x + 8f, rect.y + 25f, rect.width - 16f, 20f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.ForceSupport.OrderMeta"
                    .Translate(order.ThreatPoints, order.GetTemplateLabel()));
            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(rect.x + 8f, rect.y + 46f, rect.width - 16f, 20f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.ForceSupport.OrderCost"
                    .Translate(order.Cost));
        }

        private void DrawMechForceSupportOrderFooter(
            Rect rect,
            MechForceSupportOrder order)
        {
            GetOrderFooterLineHeights(out float smallLineHeight, out float tinyLineHeight);

            const float summaryRowGap = 2f;
            float summaryHeight = smallLineHeight + tinyLineHeight + tinyLineHeight
                + summaryRowGap * 2f;
            float statusHeight = tinyLineHeight;
            const float statusToButtonsGap = 6f;
            const float buttonWidth = 94f;
            const float buttonHeight = 26f;
            const float buttonGap = 8f;

            int totalCost = order.Cost;
            int credits = GameComponent_MechanoidMechanitorStoryState
                .GetPurgeDirectiveRewardPoints();
            bool valid = MechForceSupportService.TryValidateOrder(
                order,
                out string validationError);
            bool canConfirm = valid
                && credits >= totalCost
                && !transitioning;

            GUI.BeginGroup(rect);

            float summaryY = 0f;
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(0f, summaryY, rect.width, smallLineHeight),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.Total".Translate(
                    totalCost),
                GameFont.Small,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.AccentBright);
            summaryY += smallLineHeight + summaryRowGap;
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(0f, summaryY, rect.width, tinyLineHeight),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.CurrentCredits"
                    .Translate(credits),
                GameFont.Tiny,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.TextSecondary);
            summaryY += tinyLineHeight + summaryRowGap;
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(0f, summaryY, rect.width, tinyLineHeight),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.BalanceAfter"
                    .Translate(credits - totalCost),
                GameFont.Tiny,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.TextSecondary);

            Rect statusRect = new Rect(
                0f,
                summaryHeight,
                rect.width,
                statusHeight);
            if (!valid)
            {
                MechanoidOvermindUiStyle.DrawLabel(
                    statusRect,
                    validationError.Translate(),
                    GameFont.Tiny,
                    TextAnchor.MiddleLeft,
                    MechanoidOvermindUiStyle.Error);
            }
            else if (credits < totalCost)
            {
                MechanoidOvermindUiStyle.DrawLabel(
                    statusRect,
                    MechForceSupportService.ErrorInsufficientCredits.Translate(),
                    GameFont.Tiny,
                    TextAnchor.MiddleLeft,
                    MechanoidOvermindUiStyle.Error);
            }

            float buttonsY = statusRect.yMax + statusToButtonsGap;
            float buttonsGroupWidth = buttonWidth * 2f + buttonGap;
            float buttonsX = (rect.width - buttonsGroupWidth) * 0.5f;
            if (MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(buttonsX, buttonsY, buttonWidth, buttonHeight),
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.Clear".Translate()))
            {
                specialProtocolsPage.CollapseExpandedProtocol(
                    activeProtocolOrder,
                    order);
                preparedClusterSession = null;
                statusKey =
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.WaitingInput";
            }

            if (MechanoidOvermindUiStyle.DrawActionButton(
                    new Rect(
                        buttonsX + buttonWidth + buttonGap,
                        buttonsY,
                        buttonWidth,
                        buttonHeight),
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.Order.Confirm".Translate(),
                    enabled: canConfirm))
            {
                TryBeginMechForceSupport(order);
            }

            GUI.EndGroup();
        }

        private void TryBeginMechForceSupport(MechForceSupportOrder order)
        {
            statusKey =
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.Validating";
            if (!MechForceSupportService.TryValidateOrder(
                    order,
                    out string errorKey))
            {
                statusKey = errorKey;
                return;
            }

            int credits = GameComponent_MechanoidMechanitorStoryState
                .GetPurgeDirectiveRewardPoints();
            if (credits < order.Cost)
            {
                statusKey = MechForceSupportService.ErrorInsufficientCredits;
                return;
            }

            if (order.ThreatPoints
                > MechForceSupportOrder.LargeRequestWarningThreshold)
            {
                Find.WindowStack.Add(
                    Dialog_MessageBox.CreateConfirmation(
                        "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.ForceSupport.LargeRequestWarning"
                            .Translate(),
                        () => BeginMechForceSupportWorldTargeting(order)));
                return;
            }

            BeginMechForceSupportWorldTargeting(order);
        }

        private void BeginMechForceSupportWorldTargeting(
            MechForceSupportOrder order)
        {
            statusKey =
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.SelectingForceSupportMap";
            suspendingForMapTargeting = true;
            Close(doCloseSound: false);

            if (!CameraJumper.TryShowWorld())
            {
                RestoreAfterMechForceSupportWorldTargeting(
                    MechForceSupportService.ErrorUnavailable);
                return;
            }

            MechForceSupportWorldTargeterWatcher watcher =
                new MechForceSupportWorldTargeterWatcher(
                    () => RestoreAfterMechForceSupportWorldTargeting());

            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (!MechForceSupportService.TryResolveLoadedMap(
                            target,
                            out Map? targetMap)
                        || targetMap == null)
                    {
                        return false;
                    }

                    watcher.MarkCompleted();
                    BeginMechForceSupportMapTargeting(order, targetMap);
                    return true;
                },
                canTargetTiles: false,
                mouseAttachment:
                    MechanoidMechanitorMechHiveCommunicationUtility
                        .ContactOvermindIcon,
                closeWorldTabWhenFinished: false,
                onUpdate: null,
                extraLabelGetter: MechForceSupportService.GetWorldTargetLabel,
                canSelectTarget: MechForceSupportService.CanSelectWorldTarget,
                showCancelButton: true);
            Find.WindowStack.Add(watcher);
        }

        private void RestoreAfterMechForceSupportWorldTargeting(
            string? errorKey = null)
        {
            suspendingForMapTargeting = false;
            statusKey = errorKey
                ?? "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.WaitingInput";
            CameraJumper.TryHideWorld();
            if (!IsOpen && Current.Game != null)
            {
                Find.WindowStack.Add(this);
            }
        }

        private void BeginMechForceSupportMapTargeting(
            MechForceSupportOrder order,
            Map map)
        {
            if (!MechForceSupportService.IsLoadedMap(map))
            {
                RestoreAfterMechForceSupportWorldTargeting(
                    MechForceSupportService.ErrorUnavailable);
                return;
            }

            statusKey =
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.SelectingForceSupportLocation";
            CameraJumper.TryJump(
                map.Center,
                map,
                CameraJumper.MovementMode.Pan);

            bool actionAttempted = false;
            bool closeWithoutReopen = false;
            Find.Targeter.BeginTargeting(
                TargetingParameters.ForCell(),
                target =>
                {
                    actionAttempted = true;
                    MechForceSupportDeploymentResult result =
                        MechForceSupportService.TryDeploy(
                            order,
                            map,
                            target.Cell);
                    if (result.Success)
                    {
                        closeWithoutReopen = true;
                        order.Clear();
                        SoundDefOf.Click.PlayOneShotOnCamera();
                    }
                    else
                    {
                        statusKey = result.ErrorKey
                            ?? MechForceSupportService.ErrorGenerationFailed;
                    }
                },
                target =>
                {
                    if (target.IsValid
                        && MechForceSupportService
                            .ValidateTargetCell(map, target.Cell)
                            .Accepted)
                    {
                        GenDraw.DrawTargetHighlight(target);
                    }
                },
                target => target.IsValid
                    && MechForceSupportService
                        .ValidateTargetCell(map, target.Cell)
                        .Accepted,
                caster: null,
                actionWhenFinished: () =>
                {
                    suspendingForMapTargeting = false;
                    if (closeWithoutReopen)
                    {
                        DiscardActiveOrder();
                        return;
                    }

                    if (!actionAttempted)
                    {
                        statusKey =
                            "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.WaitingInput";
                    }

                    if (!IsOpen && Current.Game != null)
                    {
                        Find.WindowStack.Add(this);
                    }
                },
                mouseAttachment:
                    MechanoidMechanitorMechHiveCommunicationUtility
                        .ContactOvermindIcon,
                playSoundOnAction: true,
                onGuiAction: target =>
                {
                    AcceptanceReport report = target.IsValid
                        ? MechForceSupportService.ValidateTargetCell(
                            map,
                            target.Cell)
                        : MechForceSupportService.ErrorInvalidRequest.Translate();
                    string label = report.Accepted
                        ? "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.ForceSupport.TargetingHint"
                            .Translate()
                        : report.Reason;
                    Widgets.MouseAttachedLabel(
                        label,
                        0f,
                        0f,
                        report.Accepted
                            ? MechanoidOvermindUiStyle.TextPrimary
                            : ColorLibrary.RedReadable);
                });
        }

        private void TryBeginMechClusterDeployment(
            MechClusterDeploymentOrder order)
        {
            statusKey =
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.Validating";

            if (!MechClusterDeploymentService.IsSessionValidForOrder(
                    preparedClusterSession,
                    order))
            {
                if (!MechClusterDeploymentService.TryPrepare(
                        order,
                        out MechClusterDeploymentSession? prepared,
                        out string errorKey)
                    || prepared == null)
                {
                    preparedClusterSession = null;
                    statusKey = errorKey;
                    return;
                }

                preparedClusterSession = prepared;
            }

            MechClusterDeploymentSession session = preparedClusterSession!;
            // 每次重新进入选点前恢复默认朝向，保留同一份草图内容。
            session.ResetPlacementRotationToNorth();
            statusKey =
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.SelectingClusterLocation";

            CameraJumper.TryJump(
                session.Map.Center,
                session.Map,
                CameraJumper.MovementMode.Pan);

            bool actionAttempted = false;
            bool closeWithoutReopen = false;
            suspendingForMapTargeting = true;
            Close(doCloseSound: false);

            Find.Targeter.BeginTargeting(
                TargetingParameters.ForCell(),
                target =>
                {
                    actionAttempted = true;
                    MechClusterDeploymentResult result =
                        MechClusterDeploymentService.TryDeploy(session, target.Cell);
                    if (result.Success)
                    {
                        closeWithoutReopen = true;
                        order.Clear();
                        preparedClusterSession = null;
                        SoundDefOf.Click.PlayOneShotOnCamera();
                    }
                    else
                    {
                        statusKey = result.ErrorKey
                            ?? MechClusterDeploymentService.ErrorGenerationFailed;
                    }
                },
                target =>
                {
                    if (target.IsValid)
                    {
                        MechClusterDeploymentService.DrawPlacementBounds(
                            session,
                            target.Cell);
                    }
                },
                target => target.IsValid
                    && MechClusterDeploymentService
                        .ValidatePlacement(session, target.Cell)
                        .Accepted,
                caster: null,
                actionWhenFinished: () =>
                {
                    suspendingForMapTargeting = false;
                    if (closeWithoutReopen)
                    {
                        DiscardActiveOrder();
                        return;
                    }

                    if (!actionAttempted)
                    {
                        statusKey =
                            "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.WaitingInput";
                    }

                    if (!IsOpen && Current.Game != null)
                    {
                        Find.WindowStack.Add(this);
                    }
                },
                mouseAttachment:
                    MechanoidMechanitorMechHiveCommunicationUtility.ContactOvermindIcon,
                playSoundOnAction: true,
                onGuiAction: target =>
                {
                    MechClusterDeploymentService.TryHandlePlacementRotation(session);

                    AcceptanceReport report = target.IsValid
                        ? MechClusterDeploymentService.ValidatePlacement(
                            session,
                            target.Cell)
                        : MechClusterDeploymentService.ErrorInvalidRequest.Translate();
                    string label = report.Accepted
                        ? "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.TargetingHint"
                            .Translate()
                        : report.Reason;
                    Widgets.MouseAttachedLabel(
                        label,
                        0f,
                        0f,
                        report.Accepted
                            ? MechanoidOvermindUiStyle.TextPrimary
                            : ColorLibrary.RedReadable);
                });
        }

        private void TryConfirmDelivery(MechanoidOvermindOrder order)
        {
            if (currentPage == MechanoidOvermindPageKind.SpecialProtocols)
            {
                return;
            }

            statusKey = "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.Validating";
            RefreshDropSpotCache(order, force: true);

            if (!MechanoidMechanitorMechHiveCommunicationUtility.TryGetContactableMechHive(out _))
            {
                statusKey = MechanoidOvermindDeliveryService.ErrorConnection;
                return;
            }

            statusKey = "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.CheckingPermission";
            MechanoidOvermindDeliveryResult result =
                MechanoidOvermindDeliveryService.TryDeliver(order, preferredDeliveryMap);
            if (result.Success)
            {
                order.Clear();
                statusKey =
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.Accepted";
                SoundDefOf.Click.PlayOneShotOnCamera();
                return;
            }

            if (result.ErrorKey == MechanoidOvermindDeliveryService.ErrorInsufficientCredits)
            {
                statusKey =
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.InsufficientCredits";
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
            statusKey = "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.WaitingInput";
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
            statusKey = "MAP_MechanoidMechanitor.PurgeDirective.Communication.Status.WaitingInput";
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

            if (transitionTargetPage == MechanoidOvermindPageKind.SpecialProtocols)
            {
                DiscardActiveOrder();
                specialProtocolsPage.ResetExpansionState();
                activeProtocolOrder = new MechClusterDeploymentOrder();
                activeForceSupportOrder = new MechForceSupportOrder();
            }
            else if (ShowsOrderPanel(transitionTargetPage))
            {
                DiscardActiveOrder();
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

            if (activeProtocolOrder != null)
            {
                activeProtocolOrder.Clear();
                activeProtocolOrder = null;
            }

            if (activeForceSupportOrder != null)
            {
                activeForceSupportOrder.Clear();
                activeForceSupportOrder = null;
            }

            specialProtocolsPage.ResetExpansionState();
            preparedClusterSession = null;
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

        private void PlayHomeOpenDialogue()
        {
            // 必须用 RawText，避免 TaggedString→string 隐式转换触发 StripTags。
            TaggedString translated =
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Dialogue.HomeOpenLink".Translate(
                    contactLocalTimeText,
                    contactPawnDisplayName);
            PlayDialogueText(translated.RawText);
        }

        private void PlayDialogueFromPool(string poolDefName)
        {
            MechanoidOvermindDialoguePoolDef? pool =
                DefDatabase<MechanoidOvermindDialoguePoolDef>.GetNamedSilentFail(poolDefName);
            // {0} = 使用通讯台的 pawn 显示名（与 HomeOpenLink 的 {1} 同源）。
            string text = dialogueSelector.PickTranslatedText(pool, contactPawnDisplayName);
            PlayDialogueText(text);
        }

        private void PlayCommunicationQueryResponse(
            MechanoidOvermindCommunicationQueryKind query)
        {
            if (activeCommunicationQuery == query)
            {
                return;
            }

            switch (query)
            {
                case MechanoidOvermindCommunicationQueryKind.PurgeCredits:
                    PlayPurgeCreditsQueryResponse();
                    break;
                case MechanoidOvermindCommunicationQueryKind.ControlPermission:
                    PlayControlPermissionQueryResponse();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(query), query, null);
            }
        }

        private void PlayPurgeCreditsQueryResponse()
        {
            // 必须用 RawText，避免 TaggedString→string 隐式转换触发 StripTags。
            TaggedString translated =
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Communication.Response.PurgeCredits"
                    .Translate();
            PlayDialogueText(translated.RawText);
            activeCommunicationQuery =
                MechanoidOvermindCommunicationQueryKind.PurgeCredits;
        }

        private void PlayControlPermissionQueryResponse()
        {
            if (!GameComponent_CerebrexTakeoverState.IsActive) return;

            TaggedString translated =
                "MAP_PurgeDirectiveRating.Communication.Response.ControlPermission".Translate();

            PlayDialogueText(translated.RawText);
            activeCommunicationQuery = MechanoidOvermindCommunicationQueryKind.ControlPermission;
        }

        private void PlayDialogueText(string text)
        {
            activeCommunicationQuery = null;

            if (ShouldPlayMojibakeEasterEgg())
            {
                // 必须用 RawText，保留血红色加粗标签。
                text = "MAP_MechanoidMechanitor.PurgeDirective.Communication.Dialogue.MojibakeEasterEgg"
                    .Translate()
                    .RawText;
            }

            dialogueTyper.Clear();
            dialogueTyper.Start(text);
        }

        private bool ShouldPlayMojibakeEasterEgg()
        {
            if (Prefs.DevMode && forceMojibakeDialogue)
            {
                return true;
            }

            // 独立随机，不触碰 Verse.Rand。
            return bootRandom.NextDouble() < 0.05d;
        }

        private static bool ShowsOrderPanel(MechanoidOvermindPageKind page)
        {
            return page == MechanoidOvermindPageKind.Mechs
                || page == MechanoidOvermindPageKind.Goods
                || page == MechanoidOvermindPageKind.SpecialProtocols;
        }

        private static string GetEnterPoolDefName(MechanoidOvermindPageKind page)
        {
            switch (page)
            {
                case MechanoidOvermindPageKind.Communication:
                    return "MAP_OvermindDialogue_EnterCommunication";
                case MechanoidOvermindPageKind.Mechs:
                    return "MAP_OvermindDialogue_EnterMechs";
                case MechanoidOvermindPageKind.Goods:
                    return "MAP_OvermindDialogue_EnterGoods";
                case MechanoidOvermindPageKind.SpecialProtocols:
                    return "MAP_OvermindDialogue_EnterSpecialProtocols";
                default:
                    throw new ArgumentOutOfRangeException(nameof(page), page, null);
            }
        }

        private static string GetPageTitle(MechanoidOvermindPageKind page)
        {
            switch (page)
            {
                case MechanoidOvermindPageKind.Communication:
                    return "MAP_MechanoidMechanitor.PurgeDirective.Communication.Nav.Communication".Translate();
                case MechanoidOvermindPageKind.Mechs:
                    return "MAP_MechanoidMechanitor.PurgeDirective.Communication.Nav.Mechs".Translate();
                case MechanoidOvermindPageKind.Goods:
                    return "MAP_MechanoidMechanitor.PurgeDirective.Communication.Nav.Goods".Translate();
                case MechanoidOvermindPageKind.SpecialProtocols:
                    return "MAP_MechanoidMechanitor.PurgeDirective.Communication.Nav.SpecialProtocols"
                        .Translate();
                default:
                    return string.Empty;
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

        private static string GetAccessPermissionLabel()
        {
            if (GameComponent_CerebrexTakeoverState.IsActive)
            {
                return "MAP_MechanoidMechanitor.PurgeDirective.Communication.Boot.AccessPermission.Full"
                    .Translate();
            }

            return PurgeDirectiveRatingDisplay.RatingName(PurgeDirectiveRatingUtility.CurrentRatingLevel);
        }
    }
}
