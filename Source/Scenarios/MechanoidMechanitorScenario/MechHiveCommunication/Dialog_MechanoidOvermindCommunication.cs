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

        private const float BootConfirmSeconds = 0.1f;

        private const double BootBreathPeriod = 1.2d;

        private const double BootScanPeriod = 2.8d;

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

        private readonly double bootStartRealtime;

        private readonly float bootDurationSeconds;

        private bool bootComplete;

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

            // 独立随机，不触碰 Verse.Rand / 对话选择器；总时长 1～2 秒（含末尾 0.1 秒确认）。
            var bootRandom = new System.Random();
            bootDurationSeconds = bootRandom.Next(1000, 2001) / 1000f;
            bootStartRealtime = Time.realtimeSinceStartupAsDouble;
            bootComplete = false;
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
                MechanoidOvermindUiStyle.DrawBackground(inRect);

                if (!bootComplete)
                {
                    if (!TryFinishBootSequence())
                    {
                        DrawConnectionBootPage(inRect);
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

            bootComplete = true;
            statusKey = "MAP_MechanoidMechanitor.MechHiveCommunication.Status.WaitingInput";
            PlayDialogueFromPool("MAP_OvermindDialogue_HomeOpen");
            return true;
        }

        private void DrawConnectionBootPage(Rect inRect)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            double elapsed = now - bootStartRealtime;
            float fillDuration = Mathf.Max(0.01f, bootDurationSeconds - BootConfirmSeconds);
            bool confirmPhase = elapsed >= fillDuration;
            float fillRawT = confirmPhase
                ? 1f
                : Mathf.Clamp01((float)(elapsed / fillDuration));
            float visualT = confirmPhase
                ? 1f
                : 1f - (1f - fillRawT) * (1f - fillRawT);
            int activeStage = GetBootActiveStageIndex(fillRawT, confirmPhase);
            int percent = confirmPhase
                ? 100
                : Mathf.Clamp(Mathf.FloorToInt(visualT * 100f + 0.0001f), 0, 100);

            DrawBootTerminalGrid(inRect);
            DrawBootScanline(inRect, now, confirmPhase);

            float panelWidth = Mathf.Clamp(inRect.width * 0.72f, 420f, 860f);
            panelWidth = Mathf.Min(panelWidth, Mathf.Max(0f, inRect.width - 32f));
            float panelHeight = Mathf.Clamp(inRect.height * 0.55f, 280f, 390f);
            panelHeight = Mathf.Min(panelHeight, Mathf.Max(0f, inRect.height - 32f));
            if (panelWidth < 8f || panelHeight < 8f)
            {
                return;
            }

            Rect panel = new Rect(
                inRect.x + (inRect.width - panelWidth) * 0.5f,
                inRect.y + (inRect.height - panelHeight) * 0.5f,
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
                "MAP_MechanoidMechanitor.MechHiveCommunication.Title".Translate(),
                GameFont.Medium);
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.x, rect.y + 26f, leftW, 18f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Subtitle".Translate(),
                GameFont.Tiny,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.TextSecondary);

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.xMax - rightW, rect.y, rightW, 20f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Boot.AccessPermission".Translate(
                    GetNodePermissionLabel()),
                GameFont.Tiny,
                TextAnchor.MiddleRight,
                MechanoidOvermindUiStyle.TextSecondary);
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.xMax - rightW, rect.y + 22f, rightW, 20f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Boot.LinkStatus".Translate(
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

            DrawBootNodeDiagram(leftRect, activeStage, confirmPhase, now);
            DrawBootStageList(rightRect, activeStage, confirmPhase, now);
        }

        private void DrawBootNodeDiagram(
            Rect rect,
            int activeStage,
            bool confirmPhase,
            double now)
        {
            float breath = GetBootBreath01(now);
            float centerSize = Mathf.Clamp(Mathf.Min(rect.width, rect.height) * 0.38f, 56f, 110f);
            float satellite = Mathf.Clamp(centerSize * 0.42f, 28f, 44f);
            float maxReachX = Mathf.Max(0f, (rect.width - satellite) * 0.5f - 2f);
            float maxReachY = Mathf.Max(0f, (rect.height - satellite) * 0.5f - 2f);
            float reach = Mathf.Min(
                Mathf.Min(
                    (rect.width - centerSize) * 0.5f - 4f,
                    (rect.height - centerSize) * 0.5f - 4f),
                Mathf.Min(maxReachX, maxReachY));
            reach = Mathf.Max(reach, 8f);

            Vector2 center = new Vector2(rect.x + rect.width * 0.5f, rect.y + rect.height * 0.5f);
            Rect centerRect = new Rect(
                center.x - centerSize * 0.5f,
                center.y - centerSize * 0.5f,
                centerSize,
                centerSize);

            Vector2[] satCenters =
            {
                new Vector2(center.x, Mathf.Clamp(center.y - reach, rect.y + satellite * 0.5f, rect.yMax - satellite * 0.5f)),
                new Vector2(Mathf.Clamp(center.x + reach, rect.x + satellite * 0.5f, rect.xMax - satellite * 0.5f), center.y),
                new Vector2(center.x, Mathf.Clamp(center.y + reach, rect.y + satellite * 0.5f, rect.yMax - satellite * 0.5f)),
                new Vector2(Mathf.Clamp(center.x - reach, rect.x + satellite * 0.5f, rect.xMax - satellite * 0.5f), center.y)
            };

            for (int i = 0; i < 4; i++)
            {
                Color lineColor = GetBootNodeColor(i, activeStage, confirmPhase, breath, link: true);
                Widgets.DrawLine(satCenters[i], center, lineColor, 1.5f);
            }

            for (int i = 0; i < 4; i++)
            {
                Rect satRect = new Rect(
                    satCenters[i].x - satellite * 0.5f,
                    satCenters[i].y - satellite * 0.5f,
                    satellite,
                    satellite);
                Color nodeColor = GetBootNodeColor(i, activeStage, confirmPhase, breath, link: false);
                Widgets.DrawBoxSolid(satRect, MechanoidOvermindUiStyle.PanelAlt);
                Color previous = GUI.color;
                GUI.color = nodeColor;
                Widgets.DrawBox(satRect, 1);
                GUI.color = previous;
                MechanoidOvermindUiStyle.DrawLabel(
                    satRect,
                    GetBootStageShortLabel(i),
                    GameFont.Tiny,
                    TextAnchor.MiddleCenter,
                    nodeColor,
                    wordWrap: true);
            }

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
            Rect inset = centerRect.ContractedBy(4f);
            GUI.color = Color.Lerp(MechanoidOvermindUiStyle.Border, coreBorder, 0.7f);
            Widgets.DrawBox(inset, 1);
            GUI.color = previousCore;

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(centerRect.x + 4f, centerRect.y + centerSize * 0.28f, centerSize - 8f, 18f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Boot.RemoteCoreNode".Translate(),
                GameFont.Tiny,
                TextAnchor.MiddleCenter,
                MechanoidOvermindUiStyle.TextPrimary);
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(centerRect.x + 4f, centerRect.y + centerSize * 0.52f, centerSize - 8f, 16f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Boot.NodeCode".Translate(),
                GameFont.Tiny,
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
                        "MAP_MechanoidMechanitor.MechHiveCommunication.Boot.Complete".Translate(),
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

        private static Color GetBootNodeColor(
            int stageIndex,
            int activeStage,
            bool confirmPhase,
            float breath,
            bool link)
        {
            if (confirmPhase || stageIndex < activeStage)
            {
                return MechanoidOvermindUiStyle.Accent;
            }

            if (stageIndex == activeStage)
            {
                return Color.Lerp(
                    MechanoidOvermindUiStyle.Accent,
                    MechanoidOvermindUiStyle.AccentBright,
                    link ? 0.35f + 0.45f * breath : breath);
            }

            return MechanoidOvermindUiStyle.Disabled;
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
                return "MAP_MechanoidMechanitor.MechHiveCommunication.Status.Connected";
            }

            switch (activeStage)
            {
                case 0:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Status.Connecting";
                case 1:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Status.Verifying";
                case 2:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Status.SyncingCredits";
                default:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Status.Connected";
            }
        }

        private static string GetBootStageName(int stageIndex)
        {
            switch (stageIndex)
            {
                case 0:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Boot.Stage.Link"
                        .Translate();
                case 1:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Boot.Stage.Verify"
                        .Translate();
                case 2:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Boot.Stage.SyncCredits"
                        .Translate();
                default:
                    return "MAP_MechanoidMechanitor.MechHiveCommunication.Boot.Stage.JoinNode"
                        .Translate();
            }
        }

        private static string GetBootStageShortLabel(int stageIndex)
        {
            return (stageIndex + 1).ToString("00");
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
