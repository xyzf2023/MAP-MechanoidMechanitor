using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 数据处理仪表盘：整合预算、即时编辑、效果和批量配置；窗口状态不写入存档。
    /// </summary>
    public sealed partial class Dialog_DataProcessingAllocationDashboard : Window
    {
        private enum DashboardMode
        {
            Monitor,
            Defaults,
            GlobalSettings,
            Batch
        }

        private enum TargetFilter
        {
            All,
            NeedsAttention,
            Automatic,
            Fixed
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
        private const float HeaderHeight = 142f;
        private const float ControlHeight = 36f;
        private const float ControlRow = ControlHeight + 8f;
        private const float GlobalControlHeight = 88f;
        private const float LeftWidth = 350f;
        private const float TargetRowHeight = 80f;
        private const float TargetRowGap = 7f;

        private readonly Pawn overseer;
        private Pawn? selectedTarget;
		private DashboardMode mode;
		private TargetFilter filter;
        private Vector2 targetScrollPosition;
        private Vector2 detailScrollPosition;
        private int lastCleanupTick = -99999;

        private const int TargetCacheRefreshIntervalFrames = 15;

        private sealed class TargetSortEntry
        {
            public Pawn Pawn = null!;
            public bool IsSelf;
            public bool IsFavorite;
            public int FavoriteOrder;
            public int Priority;
            public string SortLabel = string.Empty;
        }

        // 窗口生命周期内的目标列表缓存：不写入存档，关闭窗口即清空。
        private List<TargetSortEntry>? cachedTargets;
        private List<Pawn>? cachedTargetPawns;
        private int lastTargetRefreshFrame = -1;
        private bool targetCacheDirty = true;
        // 收藏图标实时更新，排序值在本次窗口内固定，避免点击后目标跳位。
        private readonly Dictionary<Pawn, int> windowFavoriteOrder = new Dictionary<Pawn, int>();

        // 复制/粘贴运行时剪贴板：非 static、不序列化、不跨窗口保存。
        private DataProcessingDynamicTargetSettingsSnapshot? settingsClipboard;
        private Pawn? settingsClipboardSource;
        private DataProcessingTargetCopyMode settingsClipboardMode;

        public override Vector2 InitialSize
        {
            get
            {
                float width = Mathf.Min(1160f, UI.screenWidth - 40f);
                float height = Mathf.Min(820f, UI.screenHeight - 40f);
                return new Vector2(width, height);
            }
        }

        public Dialog_DataProcessingAllocationDashboard(Pawn overseer)
        {
            this.overseer = overseer;
            forcePause = false;
            closeOnAccept = false; // Enter 提交额度输入，不关闭整个窗口。
            doCloseButton = false;
            doCloseX = true;
            absorbInputAroundWindow = false;
            draggable = true;
            doWindowBackground = false;
            drawShadow = true;
            onlyOneOfTypeAllowed = true;
        }

        public override void PostOpen()
        {
            base.PostOpen();
            if (IsOverseerValid())
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry?.EnsureDashboardConfiguration(overseer);
        }

        public override void PostClose()
        {
            FinishStepInput();
            // 关闭仪表盘后复制内容必须彻底消失。
            settingsClipboard = null;
            settingsClipboardSource = null;
            batchTargets.Clear();
            scrollHeights.Clear();

            // 目标缓存只在窗口生命周期内有效。
            cachedTargets = null;
            cachedTargetPawns = null;
            windowFavoriteOrder.Clear();
            lastTargetRefreshFrame = -1;
            targetCacheDirty = true;

            base.PostClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            stepInputDrawn = false;
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
                Rect bodyRect = new Rect(contentRect.x, bodyY, contentRect.width, Mathf.Max(1f, contentRect.yMax - bodyY));
                bool narrow = bodyRect.width < 760f;
                if (narrow)
                {
                    var dock = new Rect(bodyRect.x, bodyRect.y, bodyRect.width, GlobalControlHeight);
                    bodyRect.yMin = dock.yMax + Gap;
                    if (mode == DashboardMode.Monitor || mode == DashboardMode.Batch)
                    {
                        if (DrawSecondaryButton(new Rect(bodyRect.x, bodyRect.y, bodyRect.width, ControlHeight),
                            narrowShowTargets ? (mode == DashboardMode.Batch ? L("BatchConfigure") : L("ShowDetail")) : L("ShowTargets")))
                        { FinishStepInput(); narrowShowTargets = !narrowShowTargets; }
                        bodyRect.yMin += ControlRow;
                        if (narrowShowTargets) DrawTargetList(bodyRect, registry, targets);
                        else DrawDetail(bodyRect, registry);
                    }
                    else DrawDetail(bodyRect, registry);
                    DrawGlobalControl(dock, registry);
                }
                else
                {
                    float leftWidth = Mathf.Min(LeftWidth, bodyRect.width * 0.38f);
                    Rect leftRect = new Rect(bodyRect.x, bodyRect.y + GlobalControlHeight + Gap, leftWidth, bodyRect.height - GlobalControlHeight - Gap);
                    Rect rightRect = new Rect(leftRect.xMax + Gap, bodyRect.y,
                        bodyRect.width - leftWidth - Gap, bodyRect.height);
                    DrawTargetList(leftRect, registry, targets);
                    DrawDetail(rightRect, registry);
                    DrawGlobalControl(new Rect(leftRect.x, bodyRect.y, leftRect.width, GlobalControlHeight), registry);
                }
            }
            finally
            {
                FinishUnfocusedStepInput();
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
                List<Pawn> overseen = DataProcessingOverseerResolver.GetAllocationSubjects(overseer.mechanitor);
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
            int favoriteOrder = GetWindowFavoriteOrder(registry, target);
            bool isFavorite = favoriteOrder != int.MaxValue;
            return new TargetSortEntry
            {
                Pawn = target,
                IsSelf = isSelf,
                IsFavorite = isFavorite,
                FavoriteOrder = favoriteOrder,
                Priority = registry.GetDynamicTargetRecord(overseer, target)?.priority ?? 3,
                SortLabel = target.LabelShortCap.ToString()
            };
        }

        private int GetWindowFavoriteOrder(GameComponent_DataProcessingAllocationRegistry registry, Pawn target)
        {
            if (!windowFavoriteOrder.TryGetValue(target, out int order))
            {
                // 首次构建列表时记录；窗口期间新加入的目标在首次出现时记录。
                order = registry.GetFavoriteOrder(overseer, target);
                windowFavoriteOrder.Add(target, order);
            }
            return order;
        }

        // 比较器只读取快照字段，绝不在排序过程中查询注册表。
        // 排序规则与原实现一致：自身、收藏、收藏顺序、排序名称（不含动态优先级）。
        private static int CompareTargetEntries(
            TargetSortEntry left,
            TargetSortEntry right)
        {
            if (left.IsSelf != right.IsSelf)
            {
                return left.IsSelf ? -1 : 1;
            }

            if (left.IsFavorite != right.IsFavorite)
            {
                return left.IsFavorite ? -1 : 1;
            }

            if (left.IsFavorite && right.IsFavorite)
            {
                int pinCompare = left.FavoriteOrder.CompareTo(right.FavoriteOrder);
                if (pinCompare != 0)
                {
                    return pinCompare;
                }
            }

            return string.Compare(
                left.SortLabel,
                right.SortLabel,
                StringComparison.CurrentCulture);
        }

        // 每帧廉价校验优先级；收藏排序保持本次窗口的快照。
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

                int priority =
                    registry.GetDynamicTargetRecord(overseer, target)?.priority ?? 3;
                if (priority != entry.Priority)
                {
                    return false;
                }
            }

            return true;
        }

        private void EnsureSelectedTarget(List<Pawn> targets)
        {
            if (selectedTarget != null && targets.Contains(selectedTarget))
            {
                return;
            }

            FinishStepInput();
            selectedTarget = targets.Count > 0 ? targets[0] : null;
        }

        private enum TargetSort { Name, Deficit, Actual, Priority }
        private TargetSort targetSort;
        private string search = string.Empty;
        private bool narrowShowTargets;
        private readonly HashSet<Pawn> batchTargets = new HashSet<Pawn>();


        private readonly Dictionary<string, float> scrollHeights = new Dictionary<string, float>();
        private string inputScope = string.Empty;
        private bool showAdvanced;
        private static string L(string key) => ("MAP_MechanoidMechanitor.DataProcessing.Dashboard.V2." + key).Translate();
        private static string Percent(int steps) => DataProcessingAllocationUtility.StepsToPercent(steps).ToStringPercent();

        private void OpenPage(DashboardMode next)
        {
            FinishStepInput();
            if (next == mode) return;
            RequestBatchExit(() =>
            {
                mode = next;
                if (next == DashboardMode.Batch) { batchAdjustAll = false; batchFields.Clear(); }
                detailScrollPosition = Vector2.zero;
            });
            GUI.FocusControl(null);
        }

        private void DrawHeader(Rect rect, GameComponent_DataProcessingAllocationRegistry registry, int targetCount)
        {
            DrawPanel(rect, Accent);
            var inner = rect.ContractedBy(8f);
            Text.Font = GameFont.Small;
            GUI.color = TextMain;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 26f),
                overseer.LabelShortCap + " · " + DataProcessingTerminologyUtility.GetDashboardTitleKey(overseer).Translate());
            var budget = registry.GetBudgetSnapshotForUI(overseer);
            string[] labels = { L("Current"), L("Reserve"), L("Margin"), L("Deficit") };
            string[] values = {
                budget.available ? budget.current.ToStringPercent() : L("Pending"),
                budget.threshold.ToStringPercent(),
                budget.available ? (budget.current - budget.threshold).ToStringPercent() : L("Pending"),
                Percent(budget.unmetSteps) + " / " + budget.limitedCount + L("Targets")
            };
            float width = (inner.width - 18f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                Rect stat = new Rect(inner.x + i * (width + 6f), inner.y + 30f, width, 48f);
                Text.Font = GameFont.Small;
                GUI.color = i == 3 && budget.limitedCount > 0 ? Warning : TextSecondary;
                Widgets.Label(new Rect(stat.x, stat.y, stat.width, 24f), labels[i]);
                GUI.color = i == 2 && budget.available && budget.current < budget.threshold ? Warning : Accent;
                Widgets.Label(new Rect(stat.x, stat.y + 24f, stat.width, 24f), values[i]);
                TooltipHandler.TipRegion(stat, L("BudgetHelp"));
                if (Widgets.ButtonInvisible(stat))
                {
                    if (i == 3) { filter = TargetFilter.NeedsAttention; search = string.Empty; targetScrollPosition = Vector2.zero; narrowShowTargets = true; OpenPage(DashboardMode.Monitor); }
                    else OpenPage(DashboardMode.GlobalSettings);
                }
            }
            string[] tabs = { L("TargetsPage") + " (" + targetCount + ")", L("BudgetPage"), L("DefaultsPage"), L("BatchPage") + " (" + batchTargets.Count + ")" };
            DashboardMode[] modes = { DashboardMode.Monitor, DashboardMode.GlobalSettings, DashboardMode.Defaults, DashboardMode.Batch };
            for (int i = 0; i < tabs.Length; i++)
                if (DrawTabButton(new Rect(inner.x + i * (width + 6f), inner.y + 88f, width, ControlHeight), tabs[i], mode == modes[i]))
                    OpenPage(modes[i]);
        }

        private void DrawGlobalControl(Rect rect, GameComponent_DataProcessingAllocationRegistry registry)
        {
            bool enabled = registry.IsDynamicAllocationEnabled(overseer);
            DrawPanel(rect, enabled ? Accent : Border);
            var inner = rect.ContractedBy(8f);
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = TextMain;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width - 112f, ControlHeight), L("GlobalAutomatic"));
            if (DrawFlatButton(new Rect(inner.xMax - 106f, inner.y, 106f, ControlHeight), L(enabled ? "Enabled" : "Disabled"),
                enabled ? DashboardButtonStyle.Primary : DashboardButtonStyle.Secondary, selected: enabled))
            {
                FinishStepInput();
                if (!registry.TrySetDynamicAllocationEnabled(overseer, !enabled))
                    Messages.Message("MAP_MechanoidMechanitor.DataProcessing.DynamicAllocationFailed".Translate(), overseer, MessageTypeDefOf.RejectInput, false);
            }
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small; GUI.color = TextSecondary;
            Widgets.Label(new Rect(inner.x, inner.y + ControlRow, inner.width, 26f), L(enabled ? "AutomaticRunning" : "AutomaticStopped"));
            if (!enabled) TooltipHandler.TipRegion(rect, L("GlobalPaused"));
        }

        private void DrawTargetList(Rect rect, GameComponent_DataProcessingAllocationRegistry registry, List<Pawn> targets)
        {
            DrawPanel(rect, Border);
            var inner = rect.ContractedBy(8f);
            bool batch = mode == DashboardMode.Batch;
            batchTargets.RemoveWhere(p => p == null || !targets.Contains(p) || DataProcessingOverseerResolver.IsFrozenSelf(overseer, p));
            float top = inner.y;
            // 研究解锁后，自身固定在普通目标上方，不受搜索和排序影响。
            if (ResearchFeatureUnlockUtility.IsSelfDirectiveFocusUnlocked())
            {
                DrawTargetRow(new Rect(inner.x, top, inner.width, TargetRowHeight), registry, overseer,
                    registry.GetTargetSnapshotForUI(overseer, overseer));
                top += TargetRowHeight + TargetRowGap;
            }
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            GUI.SetNextControlName("MAP_DP_Search");
            Rect searchRect = new Rect(inner.x, top, inner.width, ControlHeight);
            string nextSearch = Widgets.TextField(searchRect, search);
            if (nextSearch.Length == 0 && GUI.GetNameOfFocusedControl() != "MAP_DP_Search")
            {
                GUI.color = TextSecondary;
                Widgets.Label(new Rect(searchRect.x + 5f, searchRect.y + 3f, searchRect.width - 10f, 22f), L("SearchPlaceholder"));
            }
            if (nextSearch != search) { search = nextSearch; targetScrollPosition = Vector2.zero; }
            top += ControlRow;
            float half = (inner.width - 6f) / 2f;
            string[] filters = { L("All"), L("Limited"), L("Automatic"), L("Fixed") };
            if (DrawSecondaryButton(new Rect(inner.x, top, half, ControlHeight), L("Filter") + filters[(int)filter]))
            {
                var options = new List<FloatMenuOption>();
                for (int i = 0; i < filters.Length; i++)
                {
                    TargetFilter captured = (TargetFilter)i;
                    options.Add(new FloatMenuOption(filters[i], () => { filter = captured; targetScrollPosition = Vector2.zero; }));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            if (DrawSecondaryButton(new Rect(inner.x + half + 6f, top, half, ControlHeight), L("Sort") + L("Sort" + targetSort)))
            {
                var options = new List<FloatMenuOption>();
                foreach (TargetSort value in Enum.GetValues(typeof(TargetSort)))
                {
                    TargetSort captured = value;
                    options.Add(new FloatMenuOption(L("Sort" + value), () => { targetSort = captured; targetScrollPosition = Vector2.zero; }));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            top += ControlRow;
            var visible = new List<Pawn>();
            var snapshots = new Dictionary<Pawn, DataProcessingTargetUISnapshot>();
            var favoriteOrder = new Dictionary<Pawn, int>();
            foreach (var target in targets)
            {
                if (ReferenceEquals(target, overseer)) continue;
                var snapshot = registry.GetTargetSnapshotForUI(overseer, target);
                snapshots[target] = snapshot;
                favoriteOrder[target] = GetWindowFavoriteOrder(registry, target);
                if (target.LabelShortCap.ToString().IndexOf(search, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                if (filter == TargetFilter.NeedsAttention && !snapshot.IsLimited) continue;
                if (filter == TargetFilter.Automatic && !snapshot.dynamicManaged) continue;
                if (filter == TargetFilter.Fixed && snapshot.dynamicManaged) continue;
                visible.Add(target);
            }
            visible.Sort((a, b) =>
            {
                int favorites = favoriteOrder[a].CompareTo(favoriteOrder[b]);
                if (favorites != 0) return favorites;
                // 收藏组只按收藏先后排列，普通排序仅用于非收藏目标。
                if (favoriteOrder[a] != int.MaxValue) return a.thingIDNumber.CompareTo(b.thingIDNumber);
                int order = 0;
                var sa = snapshots[a]; var sb = snapshots[b];
                if (targetSort == TargetSort.Deficit) order = (sb.IsLimited ? sb.requestedSteps - sb.actualSteps : 0).CompareTo(sa.IsLimited ? sa.requestedSteps - sa.actualSteps : 0);
                if (targetSort == TargetSort.Actual) order = sb.actualSteps.CompareTo(sa.actualSteps);
                if (targetSort == TargetSort.Priority) order = (sa.config?.priority ?? 3).CompareTo(sb.config?.priority ?? 3);
                if (order == 0) order = string.Compare(a.LabelShortCap.ToString(), b.LabelShortCap.ToString(), StringComparison.CurrentCulture);
                return order != 0 ? order : a.thingIDNumber.CompareTo(b.thingIDNumber);
            });
            if (batch)
            {
                if (DrawSecondaryButton(new Rect(inner.x, top, half, ControlHeight), L("SelectVisible")))
                {
                    if (targets.Contains(overseer) && overseer.RaceProps?.IsMechanoid == true && !DataProcessingOverseerResolver.IsFrozenSelf(overseer, overseer)) batchTargets.Add(overseer);
                    foreach (var target in visible) if (target.RaceProps?.IsMechanoid == true && !DataProcessingOverseerResolver.IsFrozenSelf(overseer, target)) batchTargets.Add(target);
                }
                if (DrawSecondaryButton(new Rect(inner.x + half + 6f, top, half, ControlHeight), L("ClearSelection"))) batchTargets.Clear();
                top += ControlRow;
            }
            var listRect = new Rect(inner.x, top, inner.width, Mathf.Max(1f, inner.yMax - top));
            if (visible.Count == 0) { DrawCenteredMessage(listRect, L("NoMatches")); return; }
            var view = new Rect(0f, 0f, listRect.width - 16f, visible.Count * (TargetRowHeight + TargetRowGap));
            Widgets.BeginScrollView(listRect, ref targetScrollPosition, view);
            try
            {
                for (int i = 0; i < visible.Count; i++)
                    DrawTargetRow(new Rect(0f, i * (TargetRowHeight + TargetRowGap), view.width, TargetRowHeight), registry, visible[i], snapshots[visible[i]]);
            }
            finally { Widgets.EndScrollView(); }
        }

        private void DrawTargetRow(Rect rect, GameComponent_DataProcessingAllocationRegistry registry, Pawn target, DataProcessingTargetUISnapshot snapshot)
        {
            bool batch = mode == DashboardMode.Batch;
            bool selected = batch ? batchTargets.Contains(target) : ReferenceEquals(target, selectedTarget);
            bool self = ReferenceEquals(target, overseer);
            bool selectable = registry.IsValidAllocationPairForList(overseer, target)
                && (!batch || (target.RaceProps?.IsMechanoid == true && !snapshot.frozenSelf));
            Solid(rect, selected ? PanelSelected : RowBackground);
            GUI.color = selected ? Accent : Border;
            Widgets.DrawBox(rect, selected ? 2 : 1);
            float portraitX = rect.x + 8f;
            if (batch)
            {
                Rect check = new Rect(rect.x + 6f, rect.y + 25f, 24f, 24f);
                bool marked = batchTargets.Contains(target);
                // 复选框仅显示状态；整张卡片统一处理一次点击，避免重复切换。
                GUI.color = selectable ? (marked ? Accent : Border) : Disabled;
                Widgets.DrawBox(check, 1);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(check, marked ? "✓" : "");
                Text.Anchor = TextAnchor.UpperLeft;
                TooltipHandler.TipRegion(check, L(marked ? "Unselect" : "Select"));
                portraitX += 30f;
            }
            DrawPortrait(new Rect(portraitX, rect.y + 16f, 48f, 48f), target);
            Rect star = new Rect(rect.xMax - 32f, rect.y + 6f, 26f, 26f);
            if (!self && !batch)
            {
                bool favorite = registry.IsFavorite(overseer, target);
                if (DrawFlatButton(star, favorite ? "★" : "☆", DashboardButtonStyle.Compact, selected: favorite))
                { registry.ToggleFavorite(overseer, target); }
                TooltipHandler.TipRegion(star, L(favorite ? "Unfavorite" : "Favorite"));
            }
            Rect clickRect = batch ? rect : new Rect(portraitX, rect.y, rect.xMax - portraitX - 36f, rect.height);
            float textX = portraitX + 56f;
            float textWidth = Mathf.Max(1f, rect.xMax - textX - (batch ? 6f : 38f));
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = false; GUI.color = TextMain;
            Widgets.Label(new Rect(textX, rect.y + 5f, textWidth, 24f), target.LabelShortCap);
            Text.Font = GameFont.Small; GUI.color = TextSecondary;
            string state = snapshot.frozenSelf ? "合体中：自身分配已冻结" : self ? L("Self") : snapshot.evaluationPending ? L("Pending")
                : snapshot.dynamicManaged ? registry.GetCachedDynamicStateLabelForUI(target) : L("Fixed");
            Widgets.Label(new Rect(textX, rect.y + 29f, rect.xMax - textX - 6f, 24f),
                state + " · " + DataProcessingAllocationUtility.GetSpecializationLabel(snapshot.specialization));
            Text.Font = GameFont.Small; GUI.color = snapshot.IsLimited ? Warning : Accent;
            Widgets.Label(new Rect(textX, rect.y + 53f, rect.xMax - textX - 6f, 24f),
                Percent(snapshot.actualSteps) + (snapshot.IsLimited ? " / " + Percent(snapshot.requestedSteps) : ""));
            Text.WordWrap = true;
            TooltipHandler.TipRegion(clickRect, target.LabelShortCap + "\n" + L("ActualRequest") + ": "
                + Percent(snapshot.actualSteps) + " / " + (snapshot.evaluationPending ? L("Pending") : Percent(snapshot.requestedSteps)));
            if (!selectable) TooltipHandler.TipRegion(clickRect, L("SelfUnavailable"));
            if (Widgets.ButtonInvisible(clickRect) && selectable)
            {
                FinishStepInput();
                if (batch)
                {
                    if (!batchTargets.Remove(target)) batchTargets.Add(target);
                }
                else
                {
                    selectedTarget = target;
                    mode = DashboardMode.Monitor;
                    narrowShowTargets = false;
                }
                GUI.FocusControl(null);
            }
        }
        private void DrawDetail(Rect rect, GameComponent_DataProcessingAllocationRegistry registry)
        {
            DrawPanel(rect, Accent);
            var inner = rect.ContractedBy(10f);
            if (mode == DashboardMode.GlobalSettings) DrawGlobalSettings(inner, registry);
            else if (mode == DashboardMode.Defaults) DrawDefaultsPage(inner, registry);
            else if (mode == DashboardMode.Batch) DrawBatchPage(inner, registry);
            else if (selectedTarget != null) DrawTargetMonitor(inner, registry, selectedTarget);
            else DrawCenteredMessage(inner, L("NoMatches"));
        }

    }
}
