using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 意识分配矩阵：在一个窗口中整合监管者总览、目标浏览、实时分配和动态策略。
    /// 该窗口仅调用注册表公开接口，不改变数据处理分配的业务规则。
    /// </summary>
    public sealed class Dialog_DataProcessingAllocationMatrix : Window
    {
        private enum TargetFilter
        {
            All,
            Active,
            Limited,
            Fixed
        }

        private static readonly Color WindowBackground = new Color(0.055f, 0.082f, 0.102f, 0.985f);
        private static readonly Color PanelBackground = new Color(0.082f, 0.125f, 0.153f, 0.96f);
        private static readonly Color PanelSelected = new Color(0.105f, 0.158f, 0.19f, 0.98f);
        private static readonly Color CardBackground = new Color(0.065f, 0.105f, 0.128f, 0.98f);
        private static readonly Color FieldBackground = new Color(0.055f, 0.092f, 0.112f, 0.98f);
        private static readonly Color BorderColor = new Color(0.20f, 0.31f, 0.36f, 1f);
        private static readonly Color AccentColor = new Color(0.33f, 0.84f, 0.91f, 1f);
        private static readonly Color WarningColor = new Color(0.90f, 0.68f, 0.29f, 1f);
        private static readonly Color DangerColor = new Color(0.89f, 0.33f, 0.33f, 1f);
        private static readonly Color PrimaryText = new Color(0.90f, 0.95f, 0.97f, 1f);
        private static readonly Color SecondaryText = new Color(0.58f, 0.67f, 0.71f, 1f);
        private static readonly Color MutedFill = new Color(0.15f, 0.22f, 0.26f, 1f);
        private static readonly Color GeneralColor = new Color(0.34f, 0.84f, 0.86f, 1f);
        private static readonly Color ProductionColor = new Color(0.85f, 0.72f, 0.35f, 1f);
        private static readonly Color FireControlColor = new Color(0.37f, 0.66f, 1f, 1f);
        private static readonly Color AssaultColor = new Color(0.89f, 0.42f, 0.39f, 1f);
        private static readonly Vector2 PortraitCameraOffset = default;

        private const float OuterPadding = 10f;
        private const float PanelGap = 10f;
        private const float HeaderHeight = 116f;
        private const float FooterHeight = 32f;
        private const float LeftWidth = 300f;
        private const float CenterWidth = 370f;
        private const float TargetCardHeight = 74f;
        private const float TargetCardGap = 6f;

        private readonly Pawn overseer;
        private Pawn? selectedTarget;
        private Vector2 targetScrollPosition;
        private Vector2 strategyScrollPosition;
        private TargetFilter filter;
        private bool showGlobalStrategy;
        private int lastCleanupTick = -99999;

        private const int TargetCacheRefreshIntervalFrames = 15;

        private sealed class TargetSortEntry
        {
            public Pawn Pawn = null!;
            public bool IsSelf;
            public bool IsPinned;
            public int PinOrder;
            public int Priority;
            public string SortLabel = string.Empty;
        }

        // 窗口生命周期内的目标列表缓存：不写入存档，关闭窗口即清空。
        private List<TargetSortEntry>? cachedTargets;
        private List<Pawn>? cachedTargetPawns;
        private int lastTargetRefreshFrame = -1;
        private bool targetCacheDirty = true;

        public override Vector2 InitialSize => new Vector2(1080f, 700f);

        public Dialog_DataProcessingAllocationMatrix(Pawn overseer)
        {
            this.overseer = overseer;
            forcePause = false;
            doCloseButton = false;
            doCloseX = true;
            absorbInputAroundWindow = false;
            draggable = true;
            doWindowBackground = false;
            drawShadow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;

            try
            {
                Solid(inRect, WindowBackground);
                GUI.color = BorderColor;
                Widgets.DrawBox(inRect, 1);
                GUI.color = Color.white;

                GameComponent_DataProcessingAllocationRegistry? registry =
                    GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;

                if (registry == null || !IsOverseerValid())
                {
                    DrawCenteredMessage(
                        inRect.ContractedBy(24f),
                        "MAP_MechanoidMechanitor.DataProcessing.OverseerInvalid".Translate());
                    return;
                }

                int currentTick = Find.TickManager != null
                    ? Find.TickManager.TicksGame
                    : 0;
                if (currentTick - lastCleanupTick >= 120)
                {
                    registry.CleanupInvalidRecords();
                    lastCleanupTick = currentTick;
                }

                List<Pawn> targets = CollectTargets(registry);
                EnsureSelectedTarget(targets);

                Rect contentRect = inRect.ContractedBy(OuterPadding);
                Rect headerRect = new Rect(
                    contentRect.x,
                    contentRect.y,
                    contentRect.width,
                    HeaderHeight);
                DrawHeader(headerRect, registry, targets);

                float bodyY = headerRect.yMax + PanelGap;
                float bodyHeight = contentRect.yMax - bodyY - FooterHeight - PanelGap;
                Rect bodyRect = new Rect(contentRect.x, bodyY, contentRect.width, bodyHeight);

                Rect leftRect = new Rect(bodyRect.x, bodyRect.y, LeftWidth, bodyRect.height);
                Rect centerRect = new Rect(
                    leftRect.xMax + PanelGap,
                    bodyRect.y,
                    CenterWidth,
                    bodyRect.height);
                Rect rightRect = new Rect(
                    centerRect.xMax + PanelGap,
                    bodyRect.y,
                    bodyRect.xMax - centerRect.xMax - PanelGap,
                    bodyRect.height);

                DrawTargetBrowser(leftRect, registry, targets);
                DrawTargetControl(centerRect, registry, selectedTarget);
                DrawStrategyPanel(rightRect, registry, selectedTarget);

                Rect footerRect = new Rect(
                    contentRect.x,
                    bodyRect.yMax + PanelGap,
                    contentRect.width,
                    FooterHeight);
                DrawFooter(footerRect);
            }
            finally
            {
                GUI.color = oldColor;
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
            }
        }

        private bool IsOverseerValid()
        {
            return overseer != null
                && !overseer.Dead
                && !overseer.Destroyed
                && overseer.mechanitor != null
                && DataProcessingAllocatorEligibilityUtility.IsEligibleDataProcessingOverseer(overseer)
                && overseer.Faction != null
                && overseer.Faction.IsPlayerSafe();
        }

        private List<Pawn> CollectTargets(
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            int frame = Time.frameCount;
            bool cacheExpired = frame < lastTargetRefreshFrame
                || frame - lastTargetRefreshFrame
                    >= TargetCacheRefreshIntervalFrames;
            if (targetCacheDirty
                || cachedTargets == null
                || cachedTargetPawns == null
                || cacheExpired
                || !TargetCacheMatchesRegistry(registry))
            {
                RebuildTargetCache(registry);
                lastTargetRefreshFrame = frame;
                targetCacheDirty = false;
            }

            return cachedTargetPawns!;
        }

        private void RebuildTargetCache(
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            cachedTargets ??= new List<TargetSortEntry>();
            cachedTargets.Clear();
            cachedTargetPawns ??= new List<Pawn>();
            cachedTargetPawns.Clear();

            if (registry.IsValidAllocationPairForList(overseer, overseer))
            {
                cachedTargets.Add(CreateTargetSortEntry(
                    registry, overseer, isSelf: true));
            }

            if (overseer.mechanitor != null)
            {
                List<Pawn> overseen = overseer.mechanitor.OverseenPawns;
                for (int i = 0; i < overseen.Count; i++)
                {
                    Pawn target = overseen[i];
                    if (!ReferenceEquals(target, overseer)
                        && registry.IsValidAllocationPairForList(overseer, target))
                    {
                        cachedTargets.Add(CreateTargetSortEntry(
                            registry, target, isSelf: false));
                    }
                }
            }

            cachedTargets.Sort(CompareTargetEntries);
            for (int i = 0; i < cachedTargets.Count; i++)
            {
                cachedTargetPawns.Add(cachedTargets[i].Pawn);
            }
        }

        private TargetSortEntry CreateTargetSortEntry(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target,
            bool isSelf)
        {
            bool isPinned = registry.IsPinned(overseer, target);
            return new TargetSortEntry
            {
                Pawn = target,
                IsSelf = isSelf,
                IsPinned = isPinned,
                PinOrder = isPinned ? registry.GetPinOrder(overseer, target) : 0,
                Priority = registry.GetDynamicTargetRecord(overseer, target)?.priority ?? 3,
                SortLabel = target.LabelShortCap.ToString()
            };
        }

        // 比较器只读取快照字段，绝不在排序过程中查询注册表。
        private static int CompareTargetEntries(
            TargetSortEntry left,
            TargetSortEntry right)
        {
            if (left.IsSelf != right.IsSelf)
            {
                return left.IsSelf ? -1 : 1;
            }

            if (left.IsPinned != right.IsPinned)
            {
                return left.IsPinned ? -1 : 1;
            }

            if (left.IsPinned && right.IsPinned)
            {
                int pinCompare = left.PinOrder.CompareTo(right.PinOrder);
                if (pinCompare != 0)
                {
                    return pinCompare;
                }
            }

            int priorityCompare = left.Priority.CompareTo(right.Priority);
            if (priorityCompare != 0)
            {
                return priorityCompare;
            }

            return string.Compare(
                left.SortLabel,
                right.SortLabel,
                StringComparison.CurrentCulture);
        }

        // 每帧廉价校验快照关键字段：钉选/顺序/优先级变化立即触发重建；
        // 目标死亡、销毁或 Discarded 时立即刷新。
        private bool TargetCacheMatchesRegistry(
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            if (cachedTargets == null)
            {
                return false;
            }

            for (int i = 0; i < cachedTargets.Count; i++)
            {
                TargetSortEntry entry = cachedTargets[i];
                Pawn target = entry.Pawn;
                if (target == null || target.Dead || target.Destroyed
                    || target.Discarded)
                {
                    return false;
                }

                bool isPinned = registry.IsPinned(overseer, target);
                if (isPinned != entry.IsPinned)
                {
                    return false;
                }

                if (isPinned
                    && registry.GetPinOrder(overseer, target) != entry.PinOrder)
                {
                    return false;
                }

                int priority =
                    registry.GetDynamicTargetRecord(overseer, target)?.priority ?? 3;
                if (priority != entry.Priority)
                {
                    return false;
                }
            }

            return true;
        }

        private void MarkTargetCacheDirty()
        {
            targetCacheDirty = true;
        }

        public override void PostClose()
        {
            cachedTargets = null;
            cachedTargetPawns = null;
            lastTargetRefreshFrame = -1;
            targetCacheDirty = true;
            base.PostClose();
        }

        private void EnsureSelectedTarget(List<Pawn> targets)
        {
            if (selectedTarget != null && targets.Contains(selectedTarget))
            {
                return;
            }

            selectedTarget = targets.Count > 0 ? targets[0] : null;
        }

        private void DrawHeader(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry,
            List<Pawn> targets)
        {
            Panel(rect, PanelBackground, AccentColor);
            Rect inner = rect.ContractedBy(12f);

            Rect portraitRect = new Rect(inner.x, inner.y, 72f, 72f);
            DrawPortrait(portraitRect, overseer);

            Rect identityRect = new Rect(
                portraitRect.xMax + 12f,
                inner.y,
                210f,
                inner.height);

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = PrimaryText;
            Widgets.Label(
                new Rect(identityRect.x, identityRect.y, identityRect.width, 30f),
                "MAP_MechanoidMechanitor.DataProcessing.Matrix.Title".Translate());

            Text.Font = GameFont.Small;
            GUI.color = AccentColor;
            Widgets.Label(
                new Rect(identityRect.x, identityRect.y + 34f, identityRect.width, Text.LineHeight),
                overseer.LabelShortCap);

            GUI.color = SecondaryText;
            Widgets.Label(
                new Rect(identityRect.x, identityRect.y + 56f, identityRect.width, Text.LineHeight),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixSubjectCount".Translate(targets.Count));

            float controlsWidth = 170f;
            Rect controlsRect = new Rect(
                inner.xMax - controlsWidth,
                inner.y,
                controlsWidth,
                inner.height);

            bool dynamicEnabled = registry.IsDynamicAllocationEnabled(overseer);
            Rect toggleRect = new Rect(controlsRect.x, controlsRect.y, controlsRect.width, 34f);
            if (ToggleButton(
                    toggleRect,
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicAllocation".Translate(),
                    dynamicEnabled))
            {
                if (!registry.TrySetDynamicAllocationEnabled(overseer, !dynamicEnabled))
                {
                    Messages.Message(
                        "MAP_MechanoidMechanitor.DataProcessing.DynamicAllocationFailed".Translate(),
                        overseer,
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                }
            }

            Rect strategyRect = new Rect(
                controlsRect.x,
                toggleRect.yMax + 8f,
                controlsRect.width,
                30f);
            if (FlatButton(
                    strategyRect,
                    showGlobalStrategy
                        ? "MAP_MechanoidMechanitor.DataProcessing.MatrixTargetStrategy".Translate()
                        : "MAP_MechanoidMechanitor.DataProcessing.MatrixGlobalStrategy".Translate(),
                    showGlobalStrategy))
            {
                showGlobalStrategy = !showGlobalStrategy;
                strategyScrollPosition = Vector2.zero;
            }

            Rect processingRect = new Rect(
                identityRect.xMax + 16f,
                inner.y,
                controlsRect.x - identityRect.xMax - 28f,
                inner.height);
            DrawProcessingOverview(processingRect, registry);

            GUI.color = Color.white;
        }

        private void DrawProcessingOverview(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            float current = DataProcessingAllocationUtility.GetCurrentConsciousness(overseer);
            int totalSteps = registry.GetTotalStepsForOverseer(overseer);
            int selfSteps = registry.GetStepsForOverseerTarget(overseer, overseer);
            float baseProcessing = current
                + totalSteps * DataProcessingAllocationUtility.StepPercent
                - selfSteps * DataProcessingAllocationUtility.StepPercent * 0.5f;

            DataProcessingDynamicAllocationRecord? global =
                registry.FindDynamicAllocationRecordForUI(overseer);
            float threshold = global != null
                ? global.minConsciousnessPercent / 100f
                : 1f;
            float dynamicAvailable = Mathf.Max(0f, current - threshold);

            Text.Font = GameFont.Tiny;
            GUI.color = SecondaryText;
            Widgets.Label(
                new Rect(rect.x, rect.y, rect.width * 0.33f, Text.LineHeight),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixBaseProcessing".Translate(
                    baseProcessing.ToStringPercent()));
            Widgets.Label(
                new Rect(rect.x + rect.width * 0.33f, rect.y, rect.width * 0.33f, Text.LineHeight),
                "MAP_MechanoidMechanitor.DataProcessing.CurrentProcessing".Translate(
                    current.ToStringPercent()));
            Widgets.Label(
                new Rect(rect.x + rect.width * 0.66f, rect.y, rect.width * 0.34f, Text.LineHeight),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixDynamicAvailable".Translate(
                    dynamicAvailable.ToStringPercent()));

            Rect railRect = new Rect(rect.x, rect.y + 28f, rect.width, 18f);
            float scale = Mathf.Max(2f, Mathf.Max(baseProcessing + 0.1f, threshold + 0.1f));
            DrawProcessingRail(railRect, current, baseProcessing, threshold, scale);

            GUI.color = SecondaryText;
            Widgets.Label(
                new Rect(rect.x, railRect.yMax + 6f, rect.width * 0.33f, Text.LineHeight),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixSafetyLine".Translate("50%"));
            Widgets.Label(
                new Rect(rect.x + rect.width * 0.33f, railRect.yMax + 6f, rect.width * 0.37f, Text.LineHeight),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixDynamicThreshold".Translate(
                    threshold.ToStringPercent()));
            Widgets.Label(
                new Rect(rect.x + rect.width * 0.70f, railRect.yMax + 6f, rect.width * 0.30f, Text.LineHeight),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixAssigned".Translate(
                    DataProcessingAllocationUtility.StepsToPercent(totalSteps).ToStringPercent()));
        }

        private static void DrawProcessingRail(
            Rect rect,
            float current,
            float baseProcessing,
            float threshold,
            float scale)
        {
            Solid(rect, MutedFill);
            Color fill = current < 0.50f
                ? DangerColor
                : current < threshold
                    ? WarningColor
                    : AccentColor;
            float fillWidth = rect.width * Mathf.Clamp01(current / scale);
            Solid(new Rect(rect.x, rect.y, fillWidth, rect.height), fill);

            RailMarker(rect, 0.50f / scale, DangerColor, 2f);
            RailMarker(rect, threshold / scale, WarningColor, 2f);
            RailMarker(rect, baseProcessing / scale, PrimaryText, 2f);

            GUI.color = BorderColor;
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;
        }

        private static void RailMarker(Rect rect, float normalized, Color color, float width)
        {
            float x = rect.x + rect.width * Mathf.Clamp01(normalized);
            Solid(new Rect(x - width * 0.5f, rect.y - 2f, width, rect.height + 4f), color);
        }

        private void DrawTargetBrowser(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry,
            List<Pawn> allTargets)
        {
            Panel(rect, PanelBackground, BorderColor);
            Rect inner = rect.ContractedBy(10f);

            Text.Font = GameFont.Small;
            GUI.color = PrimaryText;
            Widgets.Label(
                new Rect(inner.x, inner.y, inner.width, Text.LineHeight),
                "MAP_MechanoidMechanitor.DataProcessing.SubjectsHeader".Translate());

            float filterY = inner.y + 28f;
            DrawFilterTabs(new Rect(inner.x, filterY, inner.width, 28f));

            List<Pawn> visibleTargets = new List<Pawn>();
            for (int i = 0; i < allTargets.Count; i++)
            {
                if (MatchesFilter(registry, allTargets[i]))
                {
                    visibleTargets.Add(allTargets[i]);
                }
            }

            Rect listRect = new Rect(
                inner.x,
                filterY + 36f,
                inner.width,
                inner.yMax - filterY - 36f);

            if (visibleTargets.Count == 0)
            {
                DrawCenteredMessage(listRect, "MAP_MechanoidMechanitor.DataProcessing.NoSubjects".Translate());
                return;
            }

            float viewHeight = visibleTargets.Count * TargetCardHeight
                + Mathf.Max(0, visibleTargets.Count - 1) * TargetCardGap;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(listRect, ref targetScrollPosition, viewRect);
            try
            {
                float y = 0f;
                for (int i = 0; i < visibleTargets.Count; i++)
                {
                    DrawTargetCard(
                        new Rect(0f, y, viewRect.width, TargetCardHeight),
                        registry,
                        visibleTargets[i]);
                    y += TargetCardHeight + TargetCardGap;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawFilterTabs(Rect rect)
        {
            TargetFilter[] values =
            {
                TargetFilter.All,
                TargetFilter.Active,
                TargetFilter.Limited,
                TargetFilter.Fixed
            };
            string[] keys =
            {
                "MAP_MechanoidMechanitor.DataProcessing.MatrixFilterAll",
                "MAP_MechanoidMechanitor.DataProcessing.MatrixFilterActive",
                "MAP_MechanoidMechanitor.DataProcessing.MatrixFilterLimited",
                "MAP_MechanoidMechanitor.DataProcessing.MatrixFilterFixed"
            };

            float width = (rect.width - 12f) / 4f;
            for (int i = 0; i < values.Length; i++)
            {
                Rect buttonRect = new Rect(
                    rect.x + i * (width + 4f),
                    rect.y,
                    width,
                    rect.height);
                if (FlatButton(buttonRect, keys[i].Translate(), filter == values[i]))
                {
                    filter = values[i];
                    targetScrollPosition = Vector2.zero;
                }
            }
        }

        private bool MatchesFilter(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target)
        {
            if (filter == TargetFilter.All)
            {
                return true;
            }

            DataProcessingDynamicTargetRecord? config =
                registry.GetDynamicTargetRecord(overseer, target);
            bool dynamic = registry.IsDynamicAllocationEnabled(overseer)
                && (config?.enabled ?? true);
            DataProcessingDynamicState state = registry.GetCachedDynamicStateForTarget(target);

            if (filter == TargetFilter.Active)
            {
                return dynamic && state != DataProcessingDynamicState.Idle;
            }

            if (filter == TargetFilter.Fixed)
            {
                return !dynamic;
            }

            int actual = registry.GetStepsForOverseerTarget(overseer, target);
            return actual < GetRequestedSteps(registry, target, config);
        }

        private void DrawTargetCard(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target)
        {
            bool selected = ReferenceEquals(selectedTarget, target);
            Panel(rect, selected ? PanelSelected : CardBackground, selected ? AccentColor : BorderColor);
            Widgets.DrawHighlightIfMouseover(rect);

            DataProcessingDynamicTargetRecord? config =
                registry.GetDynamicTargetRecord(overseer, target);
            bool globalEnabled = registry.IsDynamicAllocationEnabled(overseer);
            bool targetDynamic = globalEnabled && (config?.enabled ?? true);
            int actual = registry.GetStepsForOverseerTarget(overseer, target);
            int normal = config?.normalSteps ?? actual;
            int requested = GetRequestedSteps(registry, target, config);
            bool limited = actual < requested;
            DataProcessingSpecialization specialization =
                registry.GetSpecializationForOverseerTarget(overseer, target);

            Rect portraitRect = new Rect(rect.x + 8f, rect.y + 10f, 48f, 48f);
            DrawPortrait(portraitRect, target);

            float infoX = portraitRect.xMax + 8f;
            float controlsWidth = 50f;
            float infoWidth = rect.xMax - infoX - controlsWidth - 8f;

            Text.Font = GameFont.Small;
            GUI.color = PrimaryText;
            Widgets.Label(
                new Rect(infoX, rect.y + 7f, infoWidth, Text.LineHeight),
                target.LabelShortCap);

            Text.Font = GameFont.Tiny;
            GUI.color = limited ? WarningColor : GetSpecializationColor(specialization);
            string status = targetDynamic
                ? registry.GetCachedDynamicStateLabelForUI(target)
                    + " · "
                    + DataProcessingAllocationUtility.GetSpecializationLabel(specialization)
                : "MAP_MechanoidMechanitor.DataProcessing.MatrixFixedStatus".Translate()
                    + " · "
                    + DataProcessingAllocationUtility.GetSpecializationLabel(specialization);
            Widgets.Label(
                new Rect(infoX, rect.y + 29f, infoWidth, Text.LineHeight),
                status);

            Rect railRect = new Rect(infoX, rect.y + 53f, infoWidth - 42f, 8f);
            DrawAllocationRail(railRect, actual, normal, requested, limited);
            GUI.color = limited ? WarningColor : PrimaryText;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(
                new Rect(railRect.xMax + 4f, railRect.y - 7f, 40f, 22f),
                DataProcessingAllocationUtility.StepsToPercent(actual).ToStringPercent());
            Text.Anchor = TextAnchor.UpperLeft;

            Rect dynamicRect = new Rect(rect.xMax - 50f, rect.y + 8f, 42f, 24f);
            if (globalEnabled)
            {
                if (MiniButton(dynamicRect, targetDynamic ? "●" : "○", targetDynamic))
                {
                    registry.SetDynamicAllocationEnabledForTarget(overseer, target, !targetDynamic);
                }
                TooltipHandler.TipRegion(
                    dynamicRect,
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicTargetEnabled".Translate());
            }
            else
            {
                DrawBadge(dynamicRect, "FIX", SecondaryText);
            }

            if (!ReferenceEquals(target, overseer))
            {
                bool pinned = registry.IsPinned(overseer, target);
                Rect pinRect = new Rect(rect.xMax - 50f, rect.y + 40f, 42f, 24f);
                if (MiniButton(pinRect, pinned ? "★" : "☆", pinned))
                {
                    if (pinned)
                    {
                        registry.TryUnpinTarget(overseer, target);
                    }
                    else
                    {
                        registry.TryPinTarget(overseer, target);
                    }

                    MarkTargetCacheDirty();
                }
            }

            Rect selectRect = new Rect(rect.x, rect.y, rect.width - controlsWidth - 4f, rect.height);
            if (Widgets.ButtonInvisible(selectRect))
            {
                selectedTarget = target;
                showGlobalStrategy = false;
                strategyScrollPosition = Vector2.zero;
            }
        }

        private void DrawTargetControl(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn? target)
        {
            Panel(rect, PanelBackground, BorderColor);
            Rect inner = rect.ContractedBy(12f);

            if (target == null)
            {
                DrawCenteredMessage(inner, "MAP_MechanoidMechanitor.DataProcessing.NoSubjects".Translate());
                return;
            }

            DataProcessingDynamicTargetRecord? config =
                registry.GetDynamicTargetRecord(overseer, target);
            int actual = registry.GetStepsForOverseerTarget(overseer, target);
            int normal = config?.normalSteps ?? actual;
            int requested = GetRequestedSteps(registry, target, config);
            DataProcessingSpecialization specialization =
                registry.GetSpecializationForOverseerTarget(overseer, target);
            bool dynamicManaged = registry.IsDynamicAllocationEnabled(overseer)
                && (config?.enabled ?? true);

            Rect portraitRect = new Rect(inner.x, inner.y, 64f, 64f);
            DrawPortrait(portraitRect, target);

            Text.Font = GameFont.Medium;
            GUI.color = PrimaryText;
            Widgets.Label(
                new Rect(portraitRect.xMax + 12f, inner.y, inner.width - 76f, 30f),
                target.LabelShortCap);
            Text.Font = GameFont.Small;
            GUI.color = GetSpecializationColor(specialization);
            Widgets.Label(
                new Rect(portraitRect.xMax + 12f, inner.y + 34f, inner.width - 76f, Text.LineHeight),
                DataProcessingAllocationUtility.GetSpecializationLabel(specialization));

            float y = portraitRect.yMax + 12f;
            DrawSectionTitle(
                new Rect(inner.x, y, inner.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixCurrentDecision".Translate());
            y += 30f;

            string stateLabel = dynamicManaged
                ? registry.GetCachedDynamicStateLabelForUI(target)
                : "MAP_MechanoidMechanitor.DataProcessing.MatrixFixedStatus".Translate();
            string decision = "MAP_MechanoidMechanitor.DataProcessing.MatrixDecisionPath".Translate(
                stateLabel,
                DataProcessingAllocationUtility.GetSpecializationLabel(specialization),
                DataProcessingAllocationUtility.StepsToPercent(requested).ToStringPercent());
            DrawInfoBox(
                new Rect(inner.x, y, inner.width, 48f),
                decision,
                GetSpecializationColor(specialization));
            y += 60f;

            DrawSectionTitle(
                new Rect(inner.x, y, inner.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixAllocationControl".Translate());
            y += 30f;

            DrawAllocationMetrics(
                new Rect(inner.x, y, inner.width, 48f),
                actual,
                normal,
                requested);
            y += 54f;

            Rect railRect = new Rect(inner.x, y, inner.width, 18f);
            DrawAllocationRail(railRect, actual, normal, requested, actual < requested);
            y += 30f;

            Text.Font = GameFont.Tiny;
            GUI.color = SecondaryText;
            Widgets.Label(
                new Rect(inner.x, y, inner.width, Text.LineHeight),
                dynamicManaged
                    ? "MAP_MechanoidMechanitor.DataProcessing.MatrixEditingNormal".Translate()
                    : "MAP_MechanoidMechanitor.DataProcessing.MatrixEditingActual".Translate());
            y += 24f;

            DrawAllocationButtons(
                new Rect(inner.x, y, inner.width, 32f),
                registry,
                target,
                dynamicManaged,
                normal,
                actual);
            y += 46f;

            DrawSectionTitle(
                new Rect(inner.x, y, inner.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixCurrentEffects".Translate());
            y += 30f;

            DrawEffectsPreview(
                new Rect(inner.x, y, inner.width, inner.yMax - y),
                actual,
                specialization);
        }

        private static void DrawAllocationMetrics(Rect rect, int actual, int normal, int requested)
        {
            float width = (rect.width - 12f) / 3f;
            DrawMetric(
                new Rect(rect.x, rect.y, width, rect.height),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixActual".Translate(),
                DataProcessingAllocationUtility.StepsToPercent(actual).ToStringPercent(),
                AccentColor);
            DrawMetric(
                new Rect(rect.x + width + 6f, rect.y, width, rect.height),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixNormal".Translate(),
                DataProcessingAllocationUtility.StepsToPercent(normal).ToStringPercent(),
                SecondaryText);
            DrawMetric(
                new Rect(rect.x + (width + 6f) * 2f, rect.y, width, rect.height),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixMaximum".Translate(),
                DataProcessingAllocationUtility.StepsToPercent(requested).ToStringPercent(),
                requested > actual ? WarningColor : PrimaryText);
        }

        private static void DrawMetric(Rect rect, string label, string value, Color valueColor)
        {
            Solid(rect, FieldBackground);
            GUI.color = BorderColor;
            Widgets.DrawBox(rect, 1);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            GUI.color = SecondaryText;
            Widgets.Label(new Rect(rect.x, rect.y + 4f, rect.width, Text.LineHeight), label);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.LowerCenter;
            GUI.color = valueColor;
            Widgets.Label(new Rect(rect.x, rect.y + 20f, rect.width, 24f), value);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawAllocationButtons(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target,
            bool dynamicManaged,
            int normal,
            int actual)
        {
            string[] labels = { "-25%", "-5%", "+5%", "+25%" };
            int[] deltas = { -5, -1, 1, 5 };
            float width = (rect.width - 12f) / 4f;

            for (int i = 0; i < labels.Length; i++)
            {
                Rect buttonRect = new Rect(
                    rect.x + i * (width + 4f),
                    rect.y,
                    width,
                    rect.height);
                int source = dynamicManaged ? normal : actual;
                bool enabled = source + deltas[i] >= 0;

                if (FlatButton(buttonRect, labels[i], false, enabled))
                {
                    int next = Mathf.Max(0, source + deltas[i]);
                    bool succeeded;
                    if (dynamicManaged)
                    {
                        succeeded = registry.SetDynamicTargetNormalSteps(overseer, target, next);
                    }
                    else
                    {
                        registry.SetSteps(overseer, target, next);
                        succeeded = registry.GetStepsForOverseerTarget(overseer, target) == next;
                    }

                    if (!succeeded)
                    {
                        Messages.Message(
                            "MAP_MechanoidMechanitor.DataProcessing.AdjustFailed".Translate(),
                            overseer,
                            MessageTypeDefOf.RejectInput,
                            historical: false);
                    }
                }
            }
        }

        private static void DrawEffectsPreview(
            Rect rect,
            int steps,
            DataProcessingSpecialization specialization)
        {
            if (steps <= 0)
            {
                DrawCenteredMessage(rect, "MAP_MechanoidMechanitor.DataProcessing.EffectNone".Translate());
                return;
            }

            List<string> effects = BuildEffectLabels(steps, specialization);
            float y = rect.y;
            for (int i = 0; i < effects.Count; i++)
            {
                Rect lineRect = new Rect(rect.x, y, rect.width, 28f);
                Solid(
                    lineRect,
                    i % 2 == 0
                        ? new Color(0.065f, 0.105f, 0.128f, 0.78f)
                        : new Color(0.08f, 0.125f, 0.15f, 0.78f));
                GUI.color = i == 0 ? GetSpecializationColor(specialization) : PrimaryText;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Rect textRect = Inset(lineRect, 8f, 0f);
                Widgets.Label(textRect, effects[i]);
                y += 32f;
            }
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private static List<string> BuildEffectLabels(
            int steps,
            DataProcessingSpecialization specialization)
        {
            List<string> result = new List<string>();
            float work = DataProcessingAllocationUtility.GetWorkSpeedOffset(steps, specialization);
            float move = DataProcessingAllocationUtility.GetMoveSpeedOffset(steps, specialization);
            float aim = DataProcessingAllocationUtility.GetAimingDelayFactor(steps, specialization);
            float ranged = DataProcessingAllocationUtility.GetRangedCooldownFactor(steps, specialization);
            float melee = DataProcessingAllocationUtility.GetMeleeCooldownFactor(steps, specialization);
            float damage = DataProcessingAllocationUtility.GetIncomingDamageFactor(steps, specialization);
            float stagger = DataProcessingAllocationUtility.GetStaggerDurationFactor(steps, specialization);
            float energy = DataProcessingAllocationUtility.GetMechEnergyUsageFactor(steps, specialization);

            if (work > 0.0001f)
            {
                result.Add("MAP_MechanoidMechanitor.DataProcessing.EffectWorkSpeed".Translate(work.ToStringPercent()));
            }
            if (move > 0.0001f)
            {
                result.Add("MAP_MechanoidMechanitor.DataProcessing.EffectMoveSpeed".Translate(move.ToStringPercent()));
            }
            if (aim < 0.9999f)
            {
                result.Add("MAP_MechanoidMechanitor.DataProcessing.EffectAimingDelay".Translate(aim.ToString("0.##")));
            }
            if (ranged < 0.9999f)
            {
                result.Add("MAP_MechanoidMechanitor.DataProcessing.EffectRangedCooldown".Translate(ranged.ToString("0.##")));
            }
            if (melee < 0.9999f)
            {
                result.Add("MAP_MechanoidMechanitor.DataProcessing.EffectMeleeCooldown".Translate(melee.ToString("0.##")));
            }
            if (damage < 0.9999f)
            {
                result.Add("MAP_MechanoidMechanitor.DataProcessing.EffectIncomingDamage".Translate(damage.ToString("0.##")));
            }
            if (stagger < 0.9999f)
            {
                result.Add("MAP_MechanoidMechanitor.DataProcessing.EffectStaggerDuration".Translate(stagger.ToString("0.##")));
            }
            if (energy < 0.9999f)
            {
                result.Add("MAP_MechanoidMechanitor.DataProcessing.EffectMechEnergyUsage".Translate(energy.ToString("0.##")));
            }

            if (result.Count == 0)
            {
                result.Add("MAP_MechanoidMechanitor.DataProcessing.EffectNone".Translate());
            }

            return result;
        }

        private void DrawStrategyPanel(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn? target)
        {
            Panel(rect, PanelBackground, showGlobalStrategy ? WarningColor : BorderColor);
            Rect inner = rect.ContractedBy(10f);

            Text.Font = GameFont.Small;
            GUI.color = PrimaryText;
            Widgets.Label(
                new Rect(inner.x, inner.y, inner.width, Text.LineHeight),
                showGlobalStrategy
                    ? "MAP_MechanoidMechanitor.DataProcessing.MatrixGlobalStrategy".Translate()
                    : "MAP_MechanoidMechanitor.DataProcessing.MatrixTargetStrategy".Translate());

            Rect listRect = new Rect(
                inner.x,
                inner.y + 28f,
                inner.width,
                inner.height - 28f);
            float viewHeight = showGlobalStrategy ? 520f : 760f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, Mathf.Max(viewHeight, listRect.height));

            Widgets.BeginScrollView(listRect, ref strategyScrollPosition, viewRect);
            try
            {
                if (showGlobalStrategy)
                {
                    DrawGlobalStrategy(viewRect, registry);
                }
                else if (target != null)
                {
                    DrawTargetStrategy(viewRect, registry, target);
                }
                else
                {
                    DrawCenteredMessage(viewRect, "MAP_MechanoidMechanitor.DataProcessing.NoSubjects".Translate());
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawGlobalStrategy(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            float y = rect.y;
            DataProcessingDynamicAllocationRecord? global =
                registry.FindDynamicAllocationRecordForUI(overseer);
            int threshold = global?.minConsciousnessPercent ?? 100;

            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicMinConsciousness".Translate());
            y += 30f;

            DrawInfoBox(
                new Rect(rect.x, y, rect.width, 48f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicMinConsciousness.Tooltip".Translate(),
                WarningColor);
            y += 60f;

            Rect valueRect = new Rect(rect.x, y, rect.width, 42f);
            Solid(valueRect, FieldBackground);
            GUI.color = BorderColor;
            Widgets.DrawBox(valueRect, 1);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = threshold <= 55 ? DangerColor : WarningColor;
            Widgets.Label(valueRect, threshold + "%");
            Text.Anchor = TextAnchor.UpperLeft;
            y += 50f;

            string[] presetKeys =
            {
                "MAP_MechanoidMechanitor.DataProcessing.MatrixPresetConservative",
                "MAP_MechanoidMechanitor.DataProcessing.MatrixPresetBalanced",
                "MAP_MechanoidMechanitor.DataProcessing.MatrixPresetAggressive"
            };
            int[] presetValues = { 100, 75, 55 };
            float presetWidth = (rect.width - 8f) / 3f;
            for (int i = 0; i < presetValues.Length; i++)
            {
                Rect buttonRect = new Rect(
                    rect.x + i * (presetWidth + 4f),
                    y,
                    presetWidth,
                    32f);
                if (FlatButton(buttonRect, presetKeys[i].Translate(), threshold == presetValues[i]))
                {
                    registry.SetDynamicMinConsciousnessPercent(overseer, presetValues[i]);
                }
            }
            y += 42f;

            Rect minusRect = new Rect(rect.x, y, 56f, 30f);
            Rect plusRect = new Rect(rect.x + 62f, y, 56f, 30f);
            if (FlatButton(minusRect, "-5%", false, threshold > 55))
            {
                registry.SetDynamicMinConsciousnessPercent(overseer, threshold - 5);
            }
            if (FlatButton(plusRect, "+5%", false, threshold < 1000))
            {
                registry.SetDynamicMinConsciousnessPercent(overseer, threshold + 5);
            }
            y += 44f;

            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixBudgetStatistics".Translate());
            y += 32f;

            DrawGlobalBudgetStats(new Rect(rect.x, y, rect.width, 220f), registry, threshold);
        }

        private void DrawGlobalBudgetStats(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry,
            int thresholdPercent)
        {
            float current = DataProcessingAllocationUtility.GetCurrentConsciousness(overseer);
            int totalSteps = registry.GetTotalStepsForOverseer(overseer);
            int selfSteps = registry.GetStepsForOverseerTarget(overseer, overseer);
            float baseProcessing = current
                + totalSteps * DataProcessingAllocationUtility.StepPercent
                - selfSteps * DataProcessingAllocationUtility.StepPercent * 0.5f;

            int fixedSteps = 0;
            int dynamicSteps = 0;
            int unmetSteps = 0;
            bool globalEnabled = registry.IsDynamicAllocationEnabled(overseer);
            List<Pawn> targets = CollectTargets(registry);

            for (int i = 0; i < targets.Count; i++)
            {
                Pawn target = targets[i];
                DataProcessingDynamicTargetRecord? config =
                    registry.GetDynamicTargetRecord(overseer, target);
                int actual = registry.GetStepsForOverseerTarget(overseer, target);
                bool dynamic = globalEnabled && (config?.enabled ?? true);
                if (dynamic)
                {
                    dynamicSteps += actual;
                }
                else
                {
                    fixedSteps += actual;
                }

                unmetSteps += Mathf.Max(0, GetRequestedSteps(registry, target, config) - actual);
            }

            string[] labels =
            {
                "MAP_MechanoidMechanitor.DataProcessing.MatrixBaseProcessing".Translate(baseProcessing.ToStringPercent()),
                "MAP_MechanoidMechanitor.DataProcessing.CurrentProcessing".Translate(current.ToStringPercent()),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixDynamicThreshold".Translate(thresholdPercent + "%"),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixFixedUsage".Translate(
                    DataProcessingAllocationUtility.StepsToPercent(fixedSteps).ToStringPercent()),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixDynamicUsage".Translate(
                    DataProcessingAllocationUtility.StepsToPercent(dynamicSteps).ToStringPercent()),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixUnmetRequest".Translate(
                    DataProcessingAllocationUtility.StepsToPercent(unmetSteps).ToStringPercent())
            };

            float y = rect.y;
            for (int i = 0; i < labels.Length; i++)
            {
                Rect lineRect = new Rect(rect.x, y, rect.width, 30f);
                Solid(
                    lineRect,
                    i % 2 == 0
                        ? new Color(0.065f, 0.105f, 0.128f, 0.78f)
                        : new Color(0.08f, 0.125f, 0.15f, 0.78f));
                GUI.color = i == labels.Length - 1 && unmetSteps > 0
                    ? WarningColor
                    : PrimaryText;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(Inset(lineRect, 8f, 0f), labels[i]);
                y += 34f;
            }
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawTargetStrategy(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target)
        {
            DataProcessingDynamicTargetRecord? config =
                registry.GetDynamicTargetRecord(overseer, target);
            bool globalEnabled = registry.IsDynamicAllocationEnabled(overseer);
            bool targetEnabled = config?.enabled ?? true;
            DataProcessingSpecialization defaultSpec = config?.defaultSpecialization
                ?? registry.GetSpecializationForOverseerTarget(overseer, target);
            int normal = config?.normalSteps ?? registry.GetStepsForOverseerTarget(overseer, target);
            int commonMax = config?.commonMaxSteps ?? normal;
            int priority = config?.priority ?? 3;
            int intervalSeconds = config?.checkIntervalTicks / 60 ?? 10;

            float y = rect.y;
            Rect toggleRect = new Rect(rect.x, y, rect.width, 34f);
            if (ToggleButton(
                    toggleRect,
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicTargetEnabled".Translate(),
                    globalEnabled && targetEnabled,
                    globalEnabled))
            {
                registry.SetDynamicAllocationEnabledForTarget(overseer, target, !targetEnabled);
            }
            y += 44f;

            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicDefaultMode".Translate());
            y += 30f;
            DrawSpecializationGrid(
                new Rect(rect.x, y, rect.width, 74f),
                registry,
                target,
                defaultSpec,
                globalEnabled && targetEnabled);
            y += 84f;

            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicNormalSteps".Translate());
            y += 28f;
            DrawStepEditor(
                new Rect(rect.x, y, rect.width, 32f),
                normal,
                next => registry.SetDynamicTargetNormalSteps(overseer, target, next));
            y += 42f;

            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicCommonMaxSteps".Translate());
            y += 28f;
            DrawStepEditor(
                new Rect(rect.x, y, rect.width, 32f),
                commonMax,
                next => registry.SetDynamicTargetCommonMaxSteps(overseer, target, next));
            y += 42f;

            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicPriority".Translate());
            y += 28f;
            DrawPriorityButtons(
                new Rect(rect.x, y, rect.width, 32f),
                priority,
                next =>
                {
                    registry.SetDynamicTargetPriority(overseer, target, next);
                    MarkTargetCacheDirty();
                });
            y += 42f;

            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicCheckInterval".Translate());
            y += 28f;
            DrawIntervalButtons(
                new Rect(rect.x, y, rect.width, 32f),
                intervalSeconds,
                next => registry.SetDynamicTargetCheckInterval(overseer, target, next));
            y += 46f;

            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixAutomaticRules".Translate());
            y += 30f;

            bool work = config?.switchForWork ?? true;
            bool drafted = config?.switchForDraftedWeapon ?? true;
            bool closeMelee = config?.switchForCloseMelee ?? true;
            bool fallback = config?.applyUndraftedFallback ?? true;

            DrawRuleToggle(
                new Rect(rect.x, y, rect.width, 48f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicRuleWork".Translate(),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixRuleWork.Description".Translate(),
                work,
                next => registry.SetDynamicTargetRule(overseer, target, "Work", next));
            y += 54f;
            DrawRuleToggle(
                new Rect(rect.x, y, rect.width, 48f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicRuleDraftedWeapon".Translate(),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixRuleDrafted.Description".Translate(),
                drafted,
                next => registry.SetDynamicTargetRule(overseer, target, "DraftedWeapon", next));
            y += 54f;
            DrawRuleToggle(
                new Rect(rect.x, y, rect.width, 48f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicRuleCloseMelee".Translate(),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixRuleMelee.Description".Translate(),
                closeMelee,
                next => registry.SetDynamicTargetRule(overseer, target, "CloseMelee", next));
            y += 54f;
            DrawRuleToggle(
                new Rect(rect.x, y, rect.width, 48f),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicRuleUndraftedFallback".Translate(),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixRuleFallback.Description".Translate(),
                fallback,
                next => registry.SetDynamicTargetRule(overseer, target, "UndraftedFallback", next));
            y += 60f;

            bool advanced = config?.advancedMaxEnabled ?? false;
            Rect advancedRect = new Rect(rect.x, y, rect.width, 34f);
            if (ToggleButton(
                    advancedRect,
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicAdvancedMax".Translate(),
                    advanced))
            {
                registry.SetDynamicTargetAdvancedMaxEnabled(overseer, target, !advanced);
            }
            y += 44f;

            if (advanced && config != null)
            {
                DrawAdvancedMaxRows(
                    new Rect(rect.x, y, rect.width, 150f),
                    registry,
                    target,
                    config);
            }
        }

        private void DrawSpecializationGrid(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target,
            DataProcessingSpecialization current,
            bool dynamicManaged)
        {
            DataProcessingSpecialization[] values =
            {
                DataProcessingSpecialization.GeneralTuning,
                DataProcessingSpecialization.ProductionCoordination,
                DataProcessingSpecialization.FireControlCalculation,
                DataProcessingSpecialization.AssaultProtocol
            };

            for (int i = 0; i < values.Length; i++)
            {
                int row = i / 2;
                int column = i % 2;
                Rect buttonRect = new Rect(
                    rect.x + column * (rect.width * 0.5f + 2f),
                    rect.y + row * 38f,
                    rect.width * 0.5f - 2f,
                    34f);
                DataProcessingSpecialization specialization = values[i];
                if (ModeButton(
                        buttonRect,
                        DataProcessingAllocationUtility.GetSpecializationLabel(specialization),
                        GetSpecializationColor(specialization),
                        specialization == current))
                {
                    if (dynamicManaged)
                    {
                        registry.SetDynamicTargetDefaultSpecialization(overseer, target, specialization);
                    }
                    else
                    {
                        registry.TrySetManualSpecialization(overseer, target, specialization);
                    }
                }
            }
        }

        private static void DrawPriorityButtons(Rect rect, int current, Action<int> setter)
        {
            string[] keys =
            {
                "MAP_MechanoidMechanitor.DataProcessing.MatrixPriorityCritical",
                "MAP_MechanoidMechanitor.DataProcessing.MatrixPriorityHigh",
                "MAP_MechanoidMechanitor.DataProcessing.MatrixPriorityStandard",
                "MAP_MechanoidMechanitor.DataProcessing.MatrixPriorityLow"
            };
            float width = (rect.width - 12f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                int value = i + 1;
                Rect buttonRect = new Rect(
                    rect.x + i * (width + 4f),
                    rect.y,
                    width,
                    rect.height);
                if (FlatButton(buttonRect, keys[i].Translate(), current == value))
                {
                    setter(value);
                }
            }
        }

        private static void DrawIntervalButtons(Rect rect, int current, Action<int> setter)
        {
            int[] values = { 1, 5, 10, 30 };
            float width = (rect.width - 12f) / 4f;
            for (int i = 0; i < values.Length; i++)
            {
                int value = values[i];
                Rect buttonRect = new Rect(
                    rect.x + i * (width + 4f),
                    rect.y,
                    width,
                    rect.height);
                if (FlatButton(
                        buttonRect,
                        "MAP_MechanoidMechanitor.DataProcessing.DynamicSeconds".Translate(value),
                        current == value))
                {
                    setter(value);
                }

                if (value == 1)
                {
                    TooltipHandler.TipRegion(
                        buttonRect,
                        "MAP_MechanoidMechanitor.DataProcessing.MatrixIntervalHighFrequency".Translate());
                }
                else if (value == 10)
                {
                    TooltipHandler.TipRegion(
                        buttonRect,
                        "MAP_MechanoidMechanitor.DataProcessing.MatrixIntervalRecommended".Translate());
                }
            }
        }

        private static void DrawRuleToggle(
            Rect rect,
            string label,
            string description,
            bool value,
            Action<bool> setter)
        {
            Solid(rect, CardBackground);
            GUI.color = BorderColor;
            Widgets.DrawBox(rect, 1);

            Rect toggleRect = new Rect(rect.xMax - 54f, rect.y + 10f, 44f, 28f);
            if (MiniButton(toggleRect, value ? "ON" : "OFF", value))
            {
                setter(!value);
            }

            Text.Font = GameFont.Small;
            GUI.color = PrimaryText;
            Widgets.Label(
                new Rect(rect.x + 8f, rect.y + 5f, rect.width - 72f, Text.LineHeight),
                label);
            Text.Font = GameFont.Tiny;
            GUI.color = SecondaryText;
            Widgets.Label(
                new Rect(rect.x + 8f, rect.y + 25f, rect.width - 72f, Text.LineHeight),
                description);
            GUI.color = Color.white;
        }

        private static void DrawStepEditor(Rect rect, int steps, Action<int> setter)
        {
            Rect minusLarge = new Rect(rect.x, rect.y, 54f, rect.height);
            Rect minusSmall = new Rect(minusLarge.xMax + 4f, rect.y, 46f, rect.height);
            Rect valueRect = new Rect(minusSmall.xMax + 4f, rect.y, rect.width - 212f, rect.height);
            Rect plusSmall = new Rect(valueRect.xMax + 4f, rect.y, 46f, rect.height);
            Rect plusLarge = new Rect(plusSmall.xMax + 4f, rect.y, 54f, rect.height);

            if (FlatButton(minusLarge, "-25%", false, steps >= 5))
            {
                setter(Mathf.Max(0, steps - 5));
            }
            if (FlatButton(minusSmall, "-5%", false, steps >= 1))
            {
                setter(Mathf.Max(0, steps - 1));
            }

            Solid(valueRect, FieldBackground);
            GUI.color = BorderColor;
            Widgets.DrawBox(valueRect, 1);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = AccentColor;
            Widgets.Label(
                valueRect,
                DataProcessingAllocationUtility.StepsToPercent(steps).ToStringPercent());
            Text.Anchor = TextAnchor.UpperLeft;

            if (FlatButton(plusSmall, "+5%", false))
            {
                setter(steps + 1);
            }
            if (FlatButton(plusLarge, "+25%", false))
            {
                setter(steps + 5);
            }
            GUI.color = Color.white;
        }

        private void DrawAdvancedMaxRows(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target,
            DataProcessingDynamicTargetRecord config)
        {
            DataProcessingSpecialization[] specializations =
            {
                DataProcessingSpecialization.GeneralTuning,
                DataProcessingSpecialization.ProductionCoordination,
                DataProcessingSpecialization.FireControlCalculation,
                DataProcessingSpecialization.AssaultProtocol
            };
            int[] values =
            {
                config.generalMaxSteps,
                config.productionMaxSteps,
                config.fireControlMaxSteps,
                config.assaultMaxSteps
            };

            float y = rect.y;
            for (int i = 0; i < specializations.Length; i++)
            {
                DataProcessingSpecialization specialization = specializations[i];
                int steps = values[i];
                Rect lineRect = new Rect(rect.x, y, rect.width, 32f);
                Solid(lineRect, CardBackground);
                GUI.color = BorderColor;
                Widgets.DrawBox(lineRect, 1);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = GetSpecializationColor(specialization);
                Widgets.Label(
                    new Rect(lineRect.x + 8f, lineRect.y, lineRect.width - 132f, lineRect.height),
                    DataProcessingAllocationUtility.GetSpecializationLabel(specialization));

                Rect minusRect = new Rect(lineRect.xMax - 122f, lineRect.y + 3f, 30f, 26f);
                Rect valueRect = new Rect(minusRect.xMax + 4f, lineRect.y, 54f, lineRect.height);
                Rect plusRect = new Rect(valueRect.xMax + 4f, lineRect.y + 3f, 30f, 26f);

                if (FlatButton(minusRect, "-", false, steps > config.normalSteps))
                {
                    registry.SetDynamicTargetMaxStepsForSpecialization(
                        overseer,
                        target,
                        specialization,
                        steps - 1);
                }

                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = PrimaryText;
                Widgets.Label(
                    valueRect,
                    DataProcessingAllocationUtility.StepsToPercent(steps).ToStringPercent());

                if (FlatButton(plusRect, "+", false))
                {
                    registry.SetDynamicTargetMaxStepsForSpecialization(
                        overseer,
                        target,
                        specialization,
                        steps + 1);
                }

                Text.Anchor = TextAnchor.UpperLeft;
                y += 36f;
            }
            GUI.color = Color.white;
        }

        private int GetRequestedSteps(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target,
            DataProcessingDynamicTargetRecord? config)
        {
            int actual = registry.GetStepsForOverseerTarget(overseer, target);
            int normal = config?.normalSteps ?? actual;
            bool dynamic = registry.IsDynamicAllocationEnabled(overseer)
                && (config?.enabled ?? true);
            if (!dynamic || config == null)
            {
                return normal;
            }

            DataProcessingDynamicState state = registry.GetCachedDynamicStateForTarget(target);
            if (state == DataProcessingDynamicState.Idle)
            {
                return normal;
            }

            DataProcessingSpecialization current =
                registry.GetSpecializationForOverseerTarget(overseer, target);
            return config.GetMaxStepsForSpecialization(current);
        }

        private static void DrawAllocationRail(
            Rect rect,
            int actual,
            int normal,
            int maximum,
            bool limited)
        {
            int scaleSteps = Mathf.Max(1, Mathf.Max(actual, Mathf.Max(normal, maximum)));
            Solid(rect, MutedFill);

            float actualWidth = rect.width * Mathf.Clamp01(actual / (float)scaleSteps);
            Solid(
                new Rect(rect.x, rect.y, actualWidth, rect.height),
                limited ? WarningColor : AccentColor);

            float normalX = rect.x + rect.width * Mathf.Clamp01(normal / (float)scaleSteps);
            Solid(new Rect(normalX - 1f, rect.y - 3f, 2f, rect.height + 6f), PrimaryText);

            float maxX = rect.x + rect.width * Mathf.Clamp01(maximum / (float)scaleSteps);
            Solid(new Rect(maxX - 1f, rect.y - 4f, 2f, rect.height + 8f), WarningColor);
            Solid(new Rect(maxX - 6f, rect.y - 4f, 6f, 2f), WarningColor);
            Solid(new Rect(maxX - 6f, rect.yMax + 2f, 6f, 2f), WarningColor);

            GUI.color = BorderColor;
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;
        }

        private void DrawFooter(Rect rect)
        {
            Panel(rect, new Color(0.06f, 0.095f, 0.115f, 0.95f), BorderColor);
            Rect inner = Inset(rect, 8f, 4f);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = SecondaryText;
            Widgets.Label(
                new Rect(inner.x, inner.y, inner.width - 110f, inner.height),
                "MAP_MechanoidMechanitor.DataProcessing.MatrixLegend".Translate());

            Rect closeRect = new Rect(inner.xMax - 96f, inner.y, 96f, inner.height);
            if (FlatButton(closeRect, "CloseButton".Translate(), false))
            {
                Close();
            }
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static Color GetSpecializationColor(DataProcessingSpecialization specialization)
        {
            switch (DataProcessingAllocationUtility.NormalizeSpecialization(specialization))
            {
                case DataProcessingSpecialization.ProductionCoordination:
                    return ProductionColor;
                case DataProcessingSpecialization.FireControlCalculation:
                    return FireControlColor;
                case DataProcessingSpecialization.AssaultProtocol:
                    return AssaultColor;
                default:
                    return GeneralColor;
            }
        }

        private static void DrawPortrait(Rect rect, Pawn target)
        {
            try
            {
                float zoom = target.kindDef != null
                    ? target.kindDef.controlGroupPortraitZoom
                    : 1f;
                RenderTexture image = PortraitsCache.Get(
                    target,
                    rect.size,
                    Rot4.East,
                    PortraitCameraOffset,
                    zoom);
                GUI.DrawTexture(rect, image);
            }
            catch
            {
                Solid(rect, new Color(0.12f, 0.17f, 0.20f, 1f));
            }

            GUI.color = BorderColor;
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;
        }

        private static void Panel(Rect rect, Color background, Color border)
        {
            Solid(rect, background);
            GUI.color = border;
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;
        }

        private static void DrawSectionTitle(Rect rect, string label)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = PrimaryText;
            Widgets.Label(rect, label);
            Solid(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), BorderColor);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawInfoBox(Rect rect, string text, Color accent)
        {
            Solid(rect, FieldBackground);
            Solid(new Rect(rect.x, rect.y, 3f, rect.height), accent);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = PrimaryText;
            Widgets.Label(Inset(rect, 10f, 2f), text);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static bool ToggleButton(Rect rect, string label, bool enabled, bool active = true)
        {
            Color background = enabled
                ? new Color(0.13f, 0.34f, 0.38f, 1f)
                : new Color(0.10f, 0.14f, 0.16f, 1f);
            if (!active)
            {
                background = new Color(
                    background.r * 0.60f,
                    background.g * 0.60f,
                    background.b * 0.60f,
                    background.a);
            }

            Solid(rect, background);
            GUI.color = active ? (enabled ? AccentColor : BorderColor) : SecondaryText;
            Widgets.DrawBox(rect, 1);

            Rect indicator = new Rect(rect.xMax - 46f, rect.y + 7f, 36f, rect.height - 14f);
            Solid(
                indicator,
                enabled
                    ? AccentColor
                    : new Color(0.25f, 0.30f, 0.32f, 1f));

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = active ? PrimaryText : SecondaryText;
            Widgets.Label(new Rect(rect.x + 10f, rect.y, rect.width - 62f, rect.height), label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            return active && Widgets.ButtonInvisible(rect);
        }

        private static bool FlatButton(Rect rect, string label, bool selected, bool enabled = true)
        {
            Color background = selected
                ? new Color(0.13f, 0.31f, 0.35f, 1f)
                : new Color(0.075f, 0.115f, 0.135f, 1f);
            if (Mouse.IsOver(rect) && enabled)
            {
                background = new Color(
                    Mathf.Min(1f, background.r + 0.035f),
                    Mathf.Min(1f, background.g + 0.045f),
                    Mathf.Min(1f, background.b + 0.05f),
                    background.a);
            }
            if (!enabled)
            {
                background = new Color(
                    background.r * 0.60f,
                    background.g * 0.60f,
                    background.b * 0.60f,
                    background.a);
            }

            Solid(rect, background);
            GUI.color = selected ? AccentColor : BorderColor;
            Widgets.DrawBox(rect, 1);

            bool oldWrap = Text.WordWrap;
            Text.WordWrap = false;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = enabled ? PrimaryText : SecondaryText;
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = oldWrap;
            GUI.color = Color.white;

            return enabled && Widgets.ButtonInvisible(rect);
        }

        private static bool MiniButton(Rect rect, string label, bool selected)
        {
            Solid(
                rect,
                selected
                    ? new Color(0.13f, 0.31f, 0.35f, 1f)
                    : new Color(0.075f, 0.115f, 0.135f, 1f));
            GUI.color = selected ? AccentColor : BorderColor;
            Widgets.DrawBox(rect, 1);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = selected ? AccentColor : SecondaryText;
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            return Widgets.ButtonInvisible(rect);
        }

        private static bool ModeButton(Rect rect, string label, Color color, bool selected)
        {
            Solid(
                rect,
                selected
                    ? new Color(color.r * 0.25f, color.g * 0.25f, color.b * 0.25f, 1f)
                    : new Color(0.075f, 0.115f, 0.135f, 1f));
            GUI.color = selected ? color : BorderColor;
            Widgets.DrawBox(rect, selected ? 2 : 1);

            bool oldWrap = Text.WordWrap;
            Text.WordWrap = false;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = selected ? color : PrimaryText;
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = oldWrap;
            GUI.color = Color.white;
            return Widgets.ButtonInvisible(rect);
        }

        private static void DrawBadge(Rect rect, string label, Color color)
        {
            Solid(rect, new Color(0.075f, 0.115f, 0.135f, 1f));
            GUI.color = color;
            Widgets.DrawBox(rect, 1);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private static void DrawCenteredMessage(Rect rect, string message)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = SecondaryText;
            Widgets.Label(rect, message);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private static Rect Inset(Rect rect, float horizontal, float vertical)
        {
            return new Rect(
                rect.x + horizontal,
                rect.y + vertical,
                Mathf.Max(0f, rect.width - horizontal * 2f),
                Mathf.Max(0f, rect.height - vertical * 2f));
        }

        private static void Solid(Rect rect, Color color)
        {
            Widgets.DrawBoxSolid(rect, color);
        }
    }
}
