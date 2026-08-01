using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 数据处理仪表盘：默认专注于监控当前状态，只有明确进入编辑模式后才展示设置控件。
    /// </summary>
    public sealed partial class Dialog_DataProcessingAllocationDashboard : Window
    {
        private enum DashboardMode
        {
            Monitor,
            EditTarget,
            GlobalSettings
        }

        private enum EditTab
        {
            Basic,
            Rules,
            Advanced
        }

        private enum TargetFilter
        {
            All,
            NeedsAttention
        }

        private static readonly Color Background = new Color(0.06f, 0.085f, 0.10f, 0.985f);
        private static readonly Color Panel = new Color(0.095f, 0.125f, 0.14f, 0.98f);
        private static readonly Color PanelSelected = new Color(0.11f, 0.17f, 0.19f, 0.98f);
        private static readonly Color RowBackground = new Color(0.075f, 0.105f, 0.12f, 0.98f);
        private static readonly Color Border = new Color(0.25f, 0.33f, 0.36f, 1f);
        private static readonly Color Accent = new Color(0.30f, 0.78f, 0.83f, 1f);
        private static readonly Color Warning = new Color(0.90f, 0.66f, 0.27f, 1f);
        private static readonly Color Danger = new Color(0.90f, 0.34f, 0.34f, 1f);
        private static readonly Color TextMain = new Color(0.91f, 0.94f, 0.95f, 1f);
        private static readonly Color TextSecondary = new Color(0.62f, 0.69f, 0.72f, 1f);
        private static readonly Color Disabled = new Color(0.35f, 0.39f, 0.40f, 1f);
        private static readonly Vector2 PortraitCameraOffset = default;

        private const float OuterPadding = 12f;
        private const float Gap = 10f;
        private const float HeaderHeight = 84f;
        private const float FooterHeight = 24f;
        private const float LeftWidth = 310f;
        private const float TargetRowHeight = 64f;
        private const float TargetRowGap = 5f;

        private readonly Pawn overseer;
        private Pawn? selectedTarget;
        private DashboardMode mode;
        private EditTab editTab;
        private TargetFilter filter;
        private Vector2 targetScrollPosition;
        private Vector2 detailScrollPosition;
        private int lastCleanupTick = -99999;

        public override Vector2 InitialSize
        {
            get
            {
                float width = Mathf.Min(1000f, Mathf.Max(760f, UI.screenWidth - 80f));
                float height = Mathf.Min(680f, Mathf.Max(540f, UI.screenHeight - 80f));
                return new Vector2(width, height);
            }
        }

        public Dialog_DataProcessingAllocationDashboard(Pawn overseer)
        {
            this.overseer = overseer;
            forcePause = false;
            doCloseButton = false;
            doCloseX = true;
            absorbInputAroundWindow = false;
            draggable = true;
            doWindowBackground = false;
            drawShadow = true;
            onlyOneOfTypeAllowed = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;

            try
            {
                Solid(inRect, Background);
                GUI.color = Border;
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
                DrawHeader(headerRect, registry, targets.Count);

                float bodyY = headerRect.yMax + Gap;
                float bodyHeight = contentRect.yMax - bodyY - FooterHeight - Gap;
                Rect bodyRect = new Rect(contentRect.x, bodyY, contentRect.width, bodyHeight);
                Rect leftRect = new Rect(bodyRect.x, bodyRect.y, LeftWidth, bodyRect.height);
                Rect rightRect = new Rect(
                    leftRect.xMax + Gap,
                    bodyRect.y,
                    bodyRect.width - LeftWidth - Gap,
                    bodyRect.height);

                DrawTargetList(leftRect, registry, targets);
                DrawDetail(rightRect, registry);

                DrawFooter(new Rect(
                    contentRect.x,
                    bodyRect.yMax + Gap,
                    contentRect.width,
                    FooterHeight));
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
            List<Pawn> result = new List<Pawn>();
            if (registry.IsValidAllocationPairForList(overseer, overseer))
            {
                result.Add(overseer);
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
                        result.Add(target);
                    }
                }
            }

            result.Sort((left, right) => CompareTargets(registry, left, right));
            return result;
        }

        private int CompareTargets(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn left,
            Pawn right)
        {
            bool leftSelf = ReferenceEquals(left, overseer);
            bool rightSelf = ReferenceEquals(right, overseer);
            if (leftSelf != rightSelf)
            {
                return leftSelf ? -1 : 1;
            }

            bool leftPinned = registry.IsPinned(overseer, left);
            bool rightPinned = registry.IsPinned(overseer, right);
            if (leftPinned != rightPinned)
            {
                return leftPinned ? -1 : 1;
            }

            if (leftPinned && rightPinned)
            {
                int pinCompare = registry.GetPinOrder(overseer, left)
                    .CompareTo(registry.GetPinOrder(overseer, right));
                if (pinCompare != 0)
                {
                    return pinCompare;
                }
            }

            return string.Compare(
                left.LabelShortCap.ToString(),
                right.LabelShortCap.ToString(),
                StringComparison.CurrentCulture);
        }

        private void EnsureSelectedTarget(List<Pawn> targets)
        {
            if (selectedTarget != null && targets.Contains(selectedTarget))
            {
                return;
            }

            selectedTarget = targets.Count > 0 ? targets[0] : null;
            mode = DashboardMode.Monitor;
        }

        private void DrawHeader(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry,
            int targetCount)
        {
            DrawPanel(rect, Accent);
            Rect inner = rect.ContractedBy(10f);

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = TextMain;
            Widgets.Label(
                new Rect(inner.x, inner.y, 220f, 28f),
                DataProcessingTerminologyUtility.GetDashboardTitleKey(overseer).Translate());

            Text.Font = GameFont.Small;
            GUI.color = Accent;
            Widgets.Label(
                new Rect(inner.x, inner.y + 32f, 220f, Text.LineHeight),
                overseer.LabelShortCap);
            GUI.color = TextSecondary;
            Widgets.Label(
                new Rect(inner.x, inner.y + 52f, 220f, Text.LineHeight),
                DataProcessingTerminologyUtility.GetTargetCountKey(overseer)
                    .Translate(targetCount));

            float current = DataProcessingAllocationUtility.GetCurrentConsciousness(overseer);
            DataProcessingDynamicAllocationRecord? global =
                registry.FindDynamicAllocationRecordForUI(overseer);
            int thresholdPercent = global?.minConsciousnessPercent ?? 100;
            float threshold = thresholdPercent / 100f;
            float thresholdMargin = Mathf.Max(0f, current - threshold);

            float statsX = inner.x + 240f;
            const float controlsWidth = 250f;
            float statsWidth = Mathf.Max(240f, inner.xMax - statsX - controlsWidth - 16f);
            DrawHeaderStats(
                new Rect(statsX, inner.y + 6f, statsWidth, 58f),
                current,
                threshold,
                thresholdMargin);

            Rect controlsRect = new Rect(
                inner.xMax - controlsWidth,
                inner.y + 5f,
                controlsWidth,
                58f);
            bool dynamic = registry.IsDynamicAllocationEnabled(overseer);
            Rect toggleRect = new Rect(
                controlsRect.x,
                controlsRect.y,
                controlsRect.width,
                27f);
            if (DrawToggleRow(
                    toggleRect,
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicAllocation".Translate(),
                    dynamic))
            {
                if (!registry.TrySetDynamicAllocationEnabled(overseer, !dynamic))
                {
                    Messages.Message(
                        "MAP_MechanoidMechanitor.DataProcessing.DynamicAllocationFailed".Translate(),
                        overseer,
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                }
            }

            Rect globalRect = new Rect(
                controlsRect.xMax - 142f,
                controlsRect.y + 32f,
                142f,
                28f);
            if (DrawSecondaryButton(
                    globalRect,
                    "MAP_MechanoidMechanitor.DataProcessing.Dashboard.GlobalSettings".Translate(),
                    selected: mode == DashboardMode.GlobalSettings))
            {
                mode = DashboardMode.GlobalSettings;
                detailScrollPosition = Vector2.zero;
            }
        }

        private static void DrawHeaderStats(
            Rect rect,
            float current,
            float threshold,
            float margin)
        {
            float width = (rect.width - 12f) / 3f;
            DrawPlainStat(
                new Rect(rect.x, rect.y, width, rect.height),
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.Current".Translate(),
                current.ToStringPercent(),
                current < 0.50f ? Danger : TextMain);
            DrawPlainStat(
                new Rect(rect.x + width + 6f, rect.y, width, rect.height),
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.Threshold".Translate(),
                threshold.ToStringPercent(),
                TextMain);
            DrawPlainStat(
                new Rect(rect.x + (width + 6f) * 2f, rect.y, width, rect.height),
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.Margin".Translate(),
                margin.ToStringPercent(),
                margin <= 0.0001f ? Warning : Accent);
        }

        private static void DrawPlainStat(Rect rect, string label, string value, Color valueColor)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = TextSecondary;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 20f), label);
            Text.Font = GameFont.Medium;
            GUI.color = valueColor;
            Widgets.Label(new Rect(rect.x, rect.y + 22f, rect.width, 30f), value);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawTargetList(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry,
            List<Pawn> targets)
        {
            DrawPanel(rect, Border);
            Rect inner = rect.ContractedBy(10f);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = TextMain;
            Widgets.Label(
                new Rect(inner.x, inner.y, inner.width, Text.LineHeight),
                "MAP_MechanoidMechanitor.DataProcessing.SubjectsHeader".Translate());

            Rect allRect = new Rect(inner.x, inner.y + 28f, 92f, 28f);
            Rect attentionRect = new Rect(allRect.xMax + 6f, allRect.y, 116f, 28f);
            if (DrawTabButton(
                    allRect,
                    "MAP_MechanoidMechanitor.DataProcessing.Dashboard.FilterAll".Translate(),
                    filter == TargetFilter.All))
            {
                filter = TargetFilter.All;
                targetScrollPosition = Vector2.zero;
            }
            if (DrawTabButton(
                    attentionRect,
                    "MAP_MechanoidMechanitor.DataProcessing.Dashboard.FilterAttention".Translate(),
                    filter == TargetFilter.NeedsAttention))
            {
                filter = TargetFilter.NeedsAttention;
                targetScrollPosition = Vector2.zero;
            }

            List<Pawn> visible = new List<Pawn>();
            for (int i = 0; i < targets.Count; i++)
            {
                Pawn target = targets[i];
                if (filter == TargetFilter.All || NeedsAttention(registry, target))
                {
                    visible.Add(target);
                }
            }

            Rect listRect = new Rect(
                inner.x,
                inner.y + 66f,
                inner.width,
                inner.height - 66f);
            if (visible.Count == 0)
            {
                DrawCenteredMessage(
                    listRect,
                    "MAP_MechanoidMechanitor.DataProcessing.Dashboard.NoAttention".Translate());
                return;
            }

            float viewHeight = visible.Count * TargetRowHeight
                + Mathf.Max(0, visible.Count - 1) * TargetRowGap;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(listRect, ref targetScrollPosition, viewRect);
            try
            {
                float y = 0f;
                for (int i = 0; i < visible.Count; i++)
                {
                    DrawTargetRow(
                        new Rect(0f, y, viewRect.width, TargetRowHeight),
                        registry,
                        visible[i]);
                    y += TargetRowHeight + TargetRowGap;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawTargetRow(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target)
        {
            bool selected = ReferenceEquals(selectedTarget, target);
            Solid(rect, selected ? PanelSelected : RowBackground);
            GUI.color = selected ? Accent : Border;
            Widgets.DrawBox(rect, selected ? 2 : 1);
            Widgets.DrawHighlightIfMouseover(rect);

            Rect portraitRect = new Rect(rect.x + 8f, rect.y + 8f, 48f, 48f);
            DrawPortrait(portraitRect, target);

            int actual = registry.GetStepsForOverseerTarget(overseer, target);
            DataProcessingSpecialization specialization =
                registry.GetSpecializationForOverseerTarget(overseer, target);
            bool dynamic = registry.IsDynamicAllocationEnabledForTarget(overseer, target);
            int requested = GetRequestedSteps(registry, target, out _);
            bool limited = actual < requested;

            float infoX = portraitRect.xMax + 9f;
            float infoWidth = rect.xMax - infoX - 56f;
            Text.Font = GameFont.Small;
            GUI.color = TextMain;
            Widgets.Label(
                new Rect(infoX, rect.y + 7f, infoWidth, Text.LineHeight),
                target.LabelShortCap);

            Text.Font = GameFont.Tiny;
            GUI.color = limited ? Warning : TextSecondary;
            string stateLabel = dynamic
                ? registry.GetCachedDynamicStateLabelForUI(target)
                : "MAP_MechanoidMechanitor.DataProcessing.MatrixFixedStatus".Translate();
            Widgets.Label(
                new Rect(infoX, rect.y + 29f, infoWidth, Text.LineHeight),
                stateLabel + " · "
                    + DataProcessingAllocationUtility.GetSpecializationLabel(specialization));

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = limited ? Warning : Accent;
            Widgets.Label(
                new Rect(rect.xMax - 54f, rect.y + 7f, 46f, 24f),
                DataProcessingAllocationUtility.StepsToPercent(actual).ToStringPercent());
            Text.Anchor = TextAnchor.UpperLeft;

            if (limited)
            {
                GUI.color = Warning;
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(
                    new Rect(rect.xMax - 72f, rect.y + 33f, 64f, 20f),
                    "MAP_MechanoidMechanitor.DataProcessing.Dashboard.LimitedShort".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
            }

            if (Widgets.ButtonInvisible(rect))
            {
                selectedTarget = target;
                mode = DashboardMode.Monitor;
                detailScrollPosition = Vector2.zero;
            }
        }

        private bool NeedsAttention(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target)
        {
            int actual = registry.GetStepsForOverseerTarget(overseer, target);
            int requested = GetRequestedSteps(registry, target, out _);
            return actual < requested
                || (registry.IsDynamicAllocationEnabled(overseer)
                    && !registry.IsDynamicAllocationEnabledForTarget(overseer, target));
        }

        private void DrawDetail(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            DrawPanel(rect, mode == DashboardMode.Monitor ? Border : Accent);
            Rect inner = rect.ContractedBy(12f);

            if (mode == DashboardMode.GlobalSettings)
            {
                DrawGlobalSettings(inner, registry);
                return;
            }

            if (selectedTarget == null)
            {
                DrawCenteredMessage(
                    inner,
                    "MAP_MechanoidMechanitor.DataProcessing.NoSubjects".Translate());
                return;
            }

            if (mode == DashboardMode.EditTarget)
            {
                DrawTargetEditor(inner, registry, selectedTarget);
            }
            else
            {
                DrawTargetMonitor(inner, registry, selectedTarget);
            }
        }

        private void DrawFooter(Rect rect)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = TextSecondary;
            Widgets.Label(
                rect,
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.FooterHint".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
