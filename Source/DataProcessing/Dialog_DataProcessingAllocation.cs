using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MAP_MechanoidMechanitor
{
    public sealed class Dialog_DataProcessingAllocation : Window
    {
        private const float PortraitSize = 48f;
        private const float PinButtonSize = 28f;
        private const float ActionButtonWidth = 52f;
        private const float PercentWidth = 52f;
        private const float SpecializationButtonWidth = 112f;
        private const float ActionGap = 4f;
        private const float RowGap = 4f;
        private const float RowPadding = 6f;
        private const float LeftColumnGap = 6f;
        private const float TitleHeight = 32f;
        private const float SectionGap = 8f;

        private static readonly Color HintTextColor = new Color(0.65f, 0.65f, 0.65f);
        private static readonly Color EffectTextColor = new Color(0.75f, 0.75f, 0.75f);
        private static readonly Vector2 PortraitCameraOffset = default;
        private static readonly StringComparer LabelComparer = StringComparer.CurrentCulture;

        private readonly Pawn overseer;
        private readonly Dictionary<string, string> truncateCache = new Dictionary<string, string>();
        private readonly List<Pawn> displayOrder = new List<Pawn>();
        private bool displayOrderInitialized;
        private Vector2 scrollPosition;

        public override Vector2 InitialSize => new Vector2(760f, 520f);

        public Dialog_DataProcessingAllocation(Pawn overseer)
        {
            this.overseer = overseer;
            forcePause = false;
            doCloseButton = true;
            doCloseX = true;
            absorbInputAroundWindow = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;

            try
            {
                GameComponent_DataProcessingAllocationRegistry? registry =
                    GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
                registry?.CleanupInvalidRecords();

                Rect contentRect = inRect;
                contentRect.yMax -= Window.CloseButSize.y + 8f;

                float curY = contentRect.y;
                DrawTitle(contentRect, ref curY);
                DrawSummary(contentRect, ref curY, registry);
                DrawListHeader(contentRect, ref curY);

                Rect listRect = new Rect(
                    contentRect.x,
                    curY,
                    contentRect.width,
                    Mathf.Max(0f, contentRect.yMax - curY));

                if (!IsOverseerCapable())
                {
                    DrawCenteredMessage(
                        listRect,
                        "MAP_MechanoidMechanitor.DataProcessing.OverseerInvalid".Translate());
                    return;
                }

                EnsureDisplayOrder(registry);
                List<SubjectRowData> rows = BuildRowsInDisplayOrder(registry);
                if (rows.Count == 0)
                {
                    DrawCenteredMessage(
                        listRect,
                        "MAP_MechanoidMechanitor.DataProcessing.NoSubjects".Translate());
                    return;
                }

                DrawSubjectList(listRect, rows, registry);
            }
            finally
            {
                GUI.color = oldColor;
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
            }
        }

        private void DrawTitle(Rect contentRect, ref float curY)
        {
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(
                new Rect(contentRect.x, curY, contentRect.width, TitleHeight),
                "MAP_MechanoidMechanitor.DataProcessing.Window.Title".Translate(GetOverseerDisplayName()));
            curY += TitleHeight + 4f;
        }

        private void DrawSummary(
            Rect contentRect,
            ref float curY,
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            float currentProcessing =
                DataProcessingAllocationUtility.GetCurrentConsciousness(overseer);
            int remainingSteps =
                DataProcessingAllocationUtility.GetAdditionalAssignableSteps(overseer);

            float halfWidth = contentRect.width * 0.5f;
            Widgets.Label(
                new Rect(contentRect.x, curY, halfWidth - 4f, Text.LineHeight),
                "MAP_MechanoidMechanitor.DataProcessing.CurrentProcessing".Translate(
                    currentProcessing.ToStringPercent()));
            Widgets.Label(
                new Rect(contentRect.x + halfWidth, curY, halfWidth - 4f, Text.LineHeight),
                "MAP_MechanoidMechanitor.DataProcessing.RemainingProcessing".Translate(
                    DataProcessingAllocationUtility.StepsToPercent(remainingSteps)
                        .ToStringPercent()));
            curY += Text.LineHeight + 2f;

            string hint = "MAP_MechanoidMechanitor.DataProcessing.MinReserveHint"
                .Translate(GetOverseerDisplayName());
            float hintHeight = Text.CalcHeight(hint, contentRect.width);
            GUI.color = HintTextColor;
            Widgets.Label(new Rect(contentRect.x, curY, contentRect.width, hintHeight), hint);
            GUI.color = Color.white;
            curY += hintHeight + SectionGap;

            DrawDynamicAllocationToggle(contentRect, ref curY, registry);
        }

        private void DrawDynamicAllocationToggle(
            Rect contentRect,
            ref float curY,
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            if (registry == null || !IsOverseerCapable())
            {
                return;
            }

            bool enabled = registry.IsDynamicAllocationEnabled(overseer);
            string label = "MAP_MechanoidMechanitor.DataProcessing.DynamicAllocation".Translate();

            float checkboxSize = 22f;
            Rect checkboxRect = new Rect(contentRect.x, curY, checkboxSize, checkboxSize);
            Widgets.Checkbox(checkboxRect.x, checkboxRect.y, ref enabled, checkboxSize, false);
            TooltipHandler.TipRegion(
                checkboxRect,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicAllocation.Tooltip".Translate());

            Rect labelRect = new Rect(
                checkboxRect.xMax + 4f,
                curY,
                Mathf.Max(0f, contentRect.width - checkboxRect.width - 4f),
                Text.LineHeight);
            Widgets.Label(labelRect, label);

            bool stored = registry.IsDynamicAllocationEnabled(overseer);
            if (enabled != stored)
            {
                if (registry.TrySetDynamicAllocationEnabled(overseer, enabled))
                {
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
                else
                {
                    Messages.Message(
                        "MAP_MechanoidMechanitor.DataProcessing.DynamicAllocationFailed".Translate(),
                        overseer,
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                }
            }

            // 仅当全局动态分配开启时显示“动态分配设置”入口。
            if (registry.IsDynamicAllocationEnabled(overseer))
            {
                curY += Text.LineHeight + 2f;
                Rect settingsRect = new Rect(
                    contentRect.x,
                    curY,
                    Mathf.Max(0f, contentRect.width),
                    Text.LineHeight + 4f);
                if (Widgets.ButtonText(
                        settingsRect,
                        "MAP_MechanoidMechanitor.DataProcessing.DynamicSettings.Button".Translate()))
                {
                    Find.WindowStack.Add(
                        new Dialog_DataProcessingDynamicAllocationSettings(overseer));
                }

                curY += Text.LineHeight + 4f;
            }

            curY += SectionGap;
        }

        private void DrawListHeader(Rect contentRect, ref float curY)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(
                new Rect(contentRect.x, curY, contentRect.width, Text.LineHeight),
                "MAP_MechanoidMechanitor.DataProcessing.SubjectsHeader".Translate());
            curY += Text.LineHeight + 2f;
            Widgets.DrawLineHorizontal(contentRect.x, curY, contentRect.width);
            curY += SectionGap;
        }

        private void DrawSubjectList(
            Rect listRect,
            List<SubjectRowData> rows,
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            float viewHeight = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                viewHeight += rows[i].rowHeight;
                if (i < rows.Count - 1)
                {
                    viewHeight += RowGap;
                }
            }

            float maxScrollY = Mathf.Max(0f, viewHeight - listRect.height);
            if (scrollPosition.y > maxScrollY)
            {
                scrollPosition.y = maxScrollY;
            }

            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);
            try
            {
                float rowY = 0f;
                for (int i = 0; i < rows.Count; i++)
                {
                    DrawSubjectRow(
                        new Rect(0f, rowY, viewRect.width, rows[i].rowHeight),
                        rows[i],
                        i,
                        registry);
                    rowY += rows[i].rowHeight + RowGap;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawSubjectRow(
            Rect rowRect,
            SubjectRowData row,
            int index,
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            if (index % 2 == 1)
            {
                Widgets.DrawLightHighlight(rowRect);
            }

            Widgets.DrawHighlightIfMouseover(rowRect);

            float contentTop = rowRect.y + RowPadding;
            float leftX = rowRect.x + 2f;

            Rect portraitRect = new Rect(leftX, contentTop, PortraitSize, PortraitSize);
            DrawPortrait(portraitRect, row.target);
            leftX = portraitRect.xMax + LeftColumnGap;

            float actionBlockWidth =
                SpecializationButtonWidth
                + ActionGap
                + PinButtonSize
                + ActionGap
                + ActionButtonWidth
                + ActionGap
                + PercentWidth
                + ActionGap
                + ActionButtonWidth;
            float actionHeight = Mathf.Max(PinButtonSize, Text.LineHeight + 4f);
            Rect actionRect = new Rect(
                rowRect.xMax - actionBlockWidth - 2f,
                contentTop,
                actionBlockWidth,
                actionHeight);
            DrawActionButtons(actionRect, row, registry);

            float nameWidth = Mathf.Max(40f, actionRect.x - leftX - 8f);
            Rect nameRect = new Rect(leftX, contentTop, nameWidth, Text.LineHeight);
            DrawName(nameRect, row.target);

            float effectY = nameRect.yMax + 2f;
            DrawEffects(
                new Rect(leftX, effectY, actionRect.x - leftX - 8f, rowRect.yMax - effectY - 2f),
                row);
        }

        private void DrawActionButtons(
            Rect actionRect,
            SubjectRowData row,
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            float buttonY = actionRect.y + (actionRect.height - PinButtonSize) / 2f;
            float adjustY = actionRect.y + (actionRect.height - (Text.LineHeight + 4f)) / 2f;
            float adjustHeight = Text.LineHeight + 4f;

            Rect specRect = new Rect(actionRect.x, adjustY, SpecializationButtonWidth, adjustHeight);
            Rect pinRect = new Rect(
                specRect.xMax + ActionGap,
                buttonY,
                PinButtonSize,
                PinButtonSize);
            Rect removeRect = new Rect(
                pinRect.xMax + ActionGap,
                adjustY,
                ActionButtonWidth,
                adjustHeight);
            Rect percentRect = new Rect(
                removeRect.xMax + ActionGap,
                adjustY,
                PercentWidth,
                adjustHeight);
            Rect addRect = new Rect(
                percentRect.xMax + ActionGap,
                adjustY,
                ActionButtonWidth,
                adjustHeight);

            DrawSpecializationButton(specRect, row, registry);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            int actualSteps = row.steps;
            int normalSteps = registry != null
                ? registry.GetDynamicTargetNormalSteps(overseer, row.target)
                : actualSteps;
            bool differs = registry != null
                && registry.IsDynamicAllocationEnabled(overseer)
                && registry.IsDynamicAllocationEnabledForTarget(overseer, row.target)
                && normalSteps != actualSteps;

            // 百分比列仅显示实际档数；差异信息仅在悬停 tooltip 中展示。
            string percentText = DataProcessingAllocationUtility.StepsToPercent(actualSteps)
                .ToStringPercent();
            Widgets.Label(percentRect, percentText);
            Text.Anchor = TextAnchor.UpperLeft;

            if (differs)
            {
                TooltipHandler.TipRegion(
                    percentRect,
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicSteps.Tooltip".Translate(
                        DataProcessingAllocationUtility.StepsToPercent(actualSteps).ToStringPercent(),
                        DataProcessingAllocationUtility.StepsToPercent(normalSteps).ToStringPercent()));
            }

            DrawPinButton(pinRect, row, registry);

            AllocationAdjustBlockReason removeReason = GetRemoveBlockReason(row, registry);
            bool canRemove = removeReason == AllocationAdjustBlockReason.None;
            string removeTip = GetRemoveTip(removeReason);
            if (!removeTip.NullOrEmpty())
            {
                TooltipHandler.TipRegion(removeRect, removeTip);
            }

            if (DrawActionButton(
                    removeRect,
                    "MAP_MechanoidMechanitor.DataProcessing.Remove".Translate(),
                    canRemove)
                && registry != null)
            {
                if (!registry.TryRemoveStep(overseer, row.target))
                {
                    Messages.Message(
                        "MAP_MechanoidMechanitor.DataProcessing.AdjustFailed".Translate(),
                        overseer,
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                }
            }

            AllocationAdjustBlockReason addReason = GetAddBlockReason(row, registry);
            bool canAdd = addReason == AllocationAdjustBlockReason.None;
            string addTip = GetAddTip(addReason);
            if (!addTip.NullOrEmpty())
            {
                TooltipHandler.TipRegion(addRect, addTip);
            }

            if (DrawActionButton(
                    addRect,
                    "MAP_MechanoidMechanitor.DataProcessing.Add".Translate(),
                    canAdd)
                && registry != null)
            {
                if (!registry.TryAddStep(overseer, row.target))
                {
                    Messages.Message(
                        "MAP_MechanoidMechanitor.DataProcessing.AdjustFailed".Translate(),
                        overseer,
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                }
            }
        }

        private void DrawSpecializationButton(
            Rect rect,
            SubjectRowData row,
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            string label = DataProcessingAllocationUtility.GetSpecializationLabel(row.specialization);
            string tip = GetSpecializationTip(row.specialization);

            bool dynamicLocked = registry != null
                && registry.IsDynamicAllocationEnabled(overseer)
                && registry.IsDynamicAllocationEnabledForTarget(overseer, row.target);
            bool enabled = registry != null
                && !dynamicLocked
                && DataProcessingAllocationUtility.IsValidAllocationPair(overseer, row.target);

            if (dynamicLocked)
            {
                string lockTip =
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicLockedMode.Tooltip"
                        .Translate();

                string combinedTip = tip.NullOrEmpty()
                    ? lockTip
                    : tip + "\n\n" + lockTip;

                TooltipHandler.TipRegion(
                    rect,
                    combinedTip);
            }
            else if (!tip.NullOrEmpty())
            {
                TooltipHandler.TipRegion(rect, tip);
            }

            if (!DrawActionButton(rect, label, enabled) || registry == null)
            {
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            DataProcessingSpecialization[] allSpecializations =
            {
                DataProcessingSpecialization.GeneralTuning,
                DataProcessingSpecialization.ProductionCoordination,
                DataProcessingSpecialization.FireControlCalculation,
                DataProcessingSpecialization.AssaultProtocol
            };

            for (int i = 0; i < allSpecializations.Length; i++)
            {
                DataProcessingSpecialization selected = allSpecializations[i];
                bool isCurrent = selected == row.specialization;
                string optionLabel = (isCurrent ? "✓ " : string.Empty)
                    + DataProcessingAllocationUtility.GetSpecializationLabel(selected);

                if (isCurrent)
                {
                    options.Add(new FloatMenuOption(optionLabel, null));
                    continue;
                }

                DataProcessingSpecialization captured = selected;
                options.Add(new FloatMenuOption(
                    optionLabel,
                    () =>
                    {
                        if (!registry.TrySetManualSpecialization(overseer, row.target, captured)
                            && overseer != null)
                        {
                            Messages.Message(
                                "MAP_MechanoidMechanitor.DataProcessing.AdjustFailed".Translate(),
                                overseer,
                                MessageTypeDefOf.RejectInput,
                                historical: false);
                        }
                    }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static string GetSpecializationTip(DataProcessingSpecialization specialization)
        {
            specialization = DataProcessingAllocationUtility.NormalizeSpecialization(specialization);
            switch (specialization)
            {
                case DataProcessingSpecialization.GeneralTuning:
                    return "MAP_MechanoidMechanitor.DataProcessing.Specialization.GeneralTuning.Tooltip".Translate();
                case DataProcessingSpecialization.ProductionCoordination:
                    return "MAP_MechanoidMechanitor.DataProcessing.Specialization.ProductionCoordination.Tooltip".Translate();
                case DataProcessingSpecialization.FireControlCalculation:
                    return "MAP_MechanoidMechanitor.DataProcessing.Specialization.FireControlCalculation.Tooltip".Translate();
                case DataProcessingSpecialization.AssaultProtocol:
                    return "MAP_MechanoidMechanitor.DataProcessing.Specialization.AssaultProtocol.Tooltip".Translate();
                default:
                    return "MAP_MechanoidMechanitor.DataProcessing.Specialization.GeneralTuning.Tooltip".Translate();
            }
        }

        private void DrawPinButton(
            Rect pinRect,
            SubjectRowData row,
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            // 自身永久居顶，保留横向占位但不提供顶置操作。
            if (row.isSelf)
            {
                return;
            }

            string label = row.isPinned ? "-" : "♡";
            bool canPin = registry != null
                && DataProcessingAllocationUtility.IsValidAllocationPair(overseer, row.target);
            string tip = !canPin
                ? (registry == null || !IsOverseerCapable()
                    ? "MAP_MechanoidMechanitor.DataProcessing.OverseerInvalid".Translate()
                    : "MAP_MechanoidMechanitor.DataProcessing.TargetInvalid".Translate())
                : (row.isPinned
                    ? "MAP_MechanoidMechanitor.DataProcessing.Unpin.Tooltip".Translate()
                    : "MAP_MechanoidMechanitor.DataProcessing.Pin.Tooltip".Translate());
            TooltipHandler.TipRegion(pinRect, tip);

            if (!DrawActionButton(pinRect, label, canPin) || registry == null)
            {
                return;
            }

            if (row.isPinned)
            {
                if (registry.TryUnpinTarget(overseer, row.target))
                {
                    ApplyUnpinToDisplayOrder(row.target, registry);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
            }
            else if (registry.TryPinTarget(overseer, row.target))
            {
                ApplyPinToDisplayOrder(row.target, registry);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }

        /// <summary>
        /// 启用时走原版 ButtonText；禁用时固定普通底图并套 InactiveColor，
        /// 避免原版 active:false 仍绘制悬停/按下背景的问题。
        /// </summary>
        private static bool DrawActionButton(Rect rect, string label, bool enabled)
        {
            if (enabled)
            {
                return Widgets.ButtonText(
                    rect,
                    label,
                    drawBackground: true,
                    doMouseoverSound: true,
                    active: true);
            }

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            try
            {
                GUI.color = Widgets.InactiveColor;
                Widgets.DrawAtlas(rect, Widgets.ButtonBGAtlas);
                Text.Anchor = TextAnchor.MiddleCenter;
                if (rect.height < Text.LineHeight * 2f)
                {
                    Text.WordWrap = false;
                }

                Widgets.Label(rect, label);
            }
            finally
            {
                GUI.color = oldColor;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
            }

            return false;
        }

        private static void DrawPortrait(Rect portraitRect, Pawn target)
        {
            try
            {
                float zoom = target.kindDef != null
                    ? target.kindDef.controlGroupPortraitZoom
                    : 1f;
                RenderTexture image = PortraitsCache.Get(
                    target,
                    portraitRect.size,
                    Rot4.East,
                    PortraitCameraOffset,
                    zoom);
                GUI.DrawTexture(portraitRect, image);
            }
            catch
            {
                Widgets.DrawBoxSolid(portraitRect, new Color(0.2f, 0.2f, 0.2f, 0.5f));
            }

            if (Mouse.IsOver(portraitRect))
            {
                Widgets.DrawHighlight(portraitRect);
            }
        }

        private void DrawName(Rect nameRect, Pawn target)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            string fullName = target.LabelShortCap;
            string drawnName = fullName.Truncate(nameRect.width, truncateCache);
            Widgets.Label(nameRect, drawnName);
            if (drawnName != fullName)
            {
                TooltipHandler.TipRegion(nameRect, fullName);
            }

            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawEffects(Rect effectRect, SubjectRowData row)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = EffectTextColor;

            float y = effectRect.y;
            Widgets.Label(
                new Rect(effectRect.x, y, effectRect.width, Text.LineHeight),
                "MAP_MechanoidMechanitor.DataProcessing.CurrentEffects".Translate());
            y += Text.LineHeight;

            for (int i = 0; i < row.effectLabels.Count; i++)
            {
                Rect lineRect = new Rect(effectRect.x, y, effectRect.width, Text.LineHeight);
                Widgets.Label(lineRect, " - " + row.effectLabels[i]);
                if (!row.effectTips[i].NullOrEmpty())
                {
                    TooltipHandler.TipRegion(lineRect, row.effectTips[i]);
                }

                y += Text.LineHeight;
            }

            GUI.color = Color.white;
        }

        private static void DrawCenteredMessage(Rect rect, string message)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, message);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void EnsureDisplayOrder(
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            List<Pawn> validTargets = CollectValidTargets();
            if (!displayOrderInitialized)
            {
                InitializeDisplayOrder(validTargets, registry);
                return;
            }

            SyncDisplayOrder(validTargets, registry);
        }

        private void InitializeDisplayOrder(
            List<Pawn> validTargets,
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            displayOrder.Clear();
            List<SubjectRowData> sorted = new List<SubjectRowData>(validTargets.Count);
            for (int i = 0; i < validTargets.Count; i++)
            {
                SubjectRowData? row = TryCreateRowData(validTargets[i], registry);
                if (row != null)
                {
                    sorted.Add(row);
                }
            }

            sorted.Sort(CompareRows);
            for (int i = 0; i < sorted.Count; i++)
            {
                displayOrder.Add(sorted[i].target);
            }

            displayOrderInitialized = true;
        }

        private void SyncDisplayOrder(
            List<Pawn> validTargets,
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            HashSet<Pawn> validSet = new HashSet<Pawn>(validTargets);
            for (int i = displayOrder.Count - 1; i >= 0; i--)
            {
                Pawn pawn = displayOrder[i];
                if (pawn == null || !validSet.Contains(pawn))
                {
                    displayOrder.RemoveAt(i);
                }
            }

            // 顺序固定为：自身 → 顶置 → 未顶置；保留未顶置相对顺序，避免加减档位跳动。
            Pawn? self = null;
            List<Pawn> pinned = new List<Pawn>();
            List<Pawn> unpinned = new List<Pawn>();
            for (int i = 0; i < displayOrder.Count; i++)
            {
                Pawn pawn = displayOrder[i];
                if (ReferenceEquals(pawn, overseer))
                {
                    self = pawn;
                }
                else if (registry?.IsPinned(overseer, pawn) == true)
                {
                    pinned.Add(pawn);
                }
                else
                {
                    unpinned.Add(pawn);
                }
            }

            pinned.Sort((left, right) =>
            {
                int leftOrder = registry?.GetPinOrder(overseer, left) ?? int.MaxValue;
                int rightOrder = registry?.GetPinOrder(overseer, right) ?? int.MaxValue;
                int orderCompare = leftOrder.CompareTo(rightOrder);
                if (orderCompare != 0)
                {
                    return orderCompare;
                }

                return left.thingIDNumber.CompareTo(right.thingIDNumber);
            });

            displayOrder.Clear();
            if (self != null)
            {
                displayOrder.Add(self);
            }

            displayOrder.AddRange(pinned);
            displayOrder.AddRange(unpinned);

            for (int i = 0; i < validTargets.Count; i++)
            {
                Pawn pawn = validTargets[i];
                if (displayOrder.Contains(pawn))
                {
                    continue;
                }

                if (ReferenceEquals(pawn, overseer))
                {
                    displayOrder.Insert(0, pawn);
                }
                else if (registry?.IsPinned(overseer, pawn) == true)
                {
                    InsertPinnedTarget(pawn, registry);
                }
                else
                {
                    displayOrder.Add(pawn);
                }
            }
        }

        private List<SubjectRowData> BuildRowsInDisplayOrder(
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            List<SubjectRowData> rows = new List<SubjectRowData>(displayOrder.Count);
            for (int i = 0; i < displayOrder.Count; i++)
            {
                SubjectRowData? row = TryCreateRowData(displayOrder[i], registry);
                if (row != null)
                {
                    rows.Add(row);
                }
            }

            return rows;
        }

        private List<Pawn> CollectValidTargets()
        {
            List<Pawn> targets = new List<Pawn>();
            if (overseer?.mechanitor == null)
            {
                return targets;
            }

            if (DataProcessingAllocationUtility.IsValidAllocationPair(overseer, overseer))
            {
                targets.Add(overseer);
            }

            List<Pawn> overseenPawns = overseer.mechanitor.OverseenPawns;
            for (int i = 0; i < overseenPawns.Count; i++)
            {
                Pawn target = overseenPawns[i];
                if (!ReferenceEquals(target, overseer)
                    && DataProcessingAllocationUtility.IsValidAllocationPair(overseer, target))
                {
                    targets.Add(target);
                }
            }

            return targets;
        }

        private SubjectRowData? TryCreateRowData(
            Pawn? target,
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            if (target == null
                || !DataProcessingAllocationUtility.IsValidAllocationPair(overseer, target))
            {
                return null;
            }

            bool isSelf = ReferenceEquals(target, overseer);
            int steps = registry?.GetStepsForOverseerTarget(overseer, target) ?? 0;
            bool isPinned = !isSelf && registry?.IsPinned(overseer, target) == true;
            int pinOrder = isPinned
                ? registry!.GetPinOrder(overseer, target)
                : int.MaxValue;
            int controlGroupIndex = target.GetMechControlGroup()?.Index ?? int.MaxValue;
            DataProcessingSpecialization specialization =
                registry?.GetSpecializationForOverseerTarget(overseer, target)
                ?? DataProcessingSpecialization.GeneralTuning;

            List<string> effectLabels = new List<string>();
            List<string> effectTips = new List<string>();
            BuildEffectTexts(steps, specialization, effectLabels, effectTips);

            return new SubjectRowData(
                target,
                isSelf,
                steps,
                isPinned,
                pinOrder,
                controlGroupIndex,
                specialization,
                effectLabels,
                effectTips,
                CalculateRowHeight(effectLabels.Count));
        }

        private void ApplyPinToDisplayOrder(
            Pawn target,
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            displayOrder.Remove(target);
            InsertPinnedTarget(target, registry);
        }

        private void ApplyUnpinToDisplayOrder(
            Pawn target,
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            displayOrder.Remove(target);
            InsertUnpinnedTarget(target, registry);
        }

        private void InsertPinnedTarget(
            Pawn target,
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            int targetPinOrder = registry?.GetPinOrder(overseer, target) ?? int.MaxValue;
            int insertIndex = GetSelfRowCount();
            while (insertIndex < displayOrder.Count)
            {
                Pawn candidate = displayOrder[insertIndex];
                if (registry?.IsPinned(overseer, candidate) != true)
                {
                    break;
                }

                int candidateOrder = registry.GetPinOrder(overseer, candidate);
                if (candidateOrder > targetPinOrder
                    || (candidateOrder == targetPinOrder
                        && candidate.thingIDNumber > target.thingIDNumber))
                {
                    break;
                }

                insertIndex++;
            }

            displayOrder.Insert(insertIndex, target);
        }

        private void InsertUnpinnedTarget(
            Pawn target,
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            SubjectRowData? moving = TryCreateRowData(target, registry);
            if (moving == null)
            {
                displayOrder.Add(target);
                return;
            }

            int insertIndex = CountPinnedTargets(registry);
            while (insertIndex < displayOrder.Count)
            {
                SubjectRowData? existing = TryCreateRowData(displayOrder[insertIndex], registry);
                if (existing == null || CompareUnpinnedRows(moving, existing) < 0)
                {
                    break;
                }

                insertIndex++;
            }

            displayOrder.Insert(insertIndex, target);
        }

        private int GetSelfRowCount()
        {
            return displayOrder.Count > 0 && ReferenceEquals(displayOrder[0], overseer)
                ? 1
                : 0;
        }

        private int CountPinnedTargets(
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            int index = GetSelfRowCount();
            int count = index;
            for (int i = index; i < displayOrder.Count; i++)
            {
                if (registry?.IsPinned(overseer, displayOrder[i]) == true)
                {
                    count++;
                }
                else
                {
                    break;
                }
            }

            return count;
        }

        private static int CompareRows(SubjectRowData left, SubjectRowData right)
        {
            if (left.isSelf != right.isSelf)
            {
                return left.isSelf ? -1 : 1;
            }

            if (left.isPinned != right.isPinned)
            {
                return left.isPinned ? -1 : 1;
            }

            if (left.isPinned)
            {
                int pinCompare = left.pinOrder.CompareTo(right.pinOrder);
                if (pinCompare != 0)
                {
                    return pinCompare;
                }

                return left.target.thingIDNumber.CompareTo(right.target.thingIDNumber);
            }

            return CompareUnpinnedRows(left, right);
        }

        private static int CompareUnpinnedRows(SubjectRowData left, SubjectRowData right)
        {
            int stepsCompare = right.steps.CompareTo(left.steps);
            if (stepsCompare != 0)
            {
                return stepsCompare;
            }

            int groupCompare = left.controlGroupIndex.CompareTo(right.controlGroupIndex);
            if (groupCompare != 0)
            {
                return groupCompare;
            }

            int kindCompare = LabelComparer.Compare(left.target.KindLabel, right.target.KindLabel);
            if (kindCompare != 0)
            {
                return kindCompare;
            }

            int labelCompare = LabelComparer.Compare(left.target.Label, right.target.Label);
            if (labelCompare != 0)
            {
                return labelCompare;
            }

            return left.target.thingIDNumber.CompareTo(right.target.thingIDNumber);
        }

        private static void BuildEffectTexts(
            int steps,
            DataProcessingSpecialization specialization,
            List<string> effectLabels,
            List<string> effectTips)
        {
            specialization = DataProcessingAllocationUtility.NormalizeSpecialization(specialization);

            if (steps < DataProcessingAllocationUtility.CommandRangeThresholdSteps)
            {
                effectLabels.Add("MAP_MechanoidMechanitor.DataProcessing.EffectNone".Translate());
                effectTips.Add(string.Empty);
                return;
            }

            // 通用功能权限（脱离指挥范围、带远行队、驾驶穿梭机）始终按真实档数显示。
            effectLabels.Add("MAP_MechanoidMechanitor.DataProcessing.EffectCommandRange".Translate());
            effectTips.Add("MAP_MechanoidMechanitor.DataProcessing.EffectCommandRange.Tooltip".Translate());

            if (steps >= DataProcessingAllocationUtility.TravelNodeThresholdSteps)
            {
                effectLabels.Add("MAP_MechanoidMechanitor.DataProcessing.EffectTravelLead".Translate());
                effectTips.Add("MAP_MechanoidMechanitor.DataProcessing.EffectTravelLead.Tooltip".Translate());
            }

            if (ModsConfig.OdysseyActive
                && steps >= DataProcessingAllocationUtility.ShuttlePilotThresholdSteps)
            {
                effectLabels.Add("MAP_MechanoidMechanitor.DataProcessing.EffectShuttlePilot".Translate());
                effectTips.Add("MAP_MechanoidMechanitor.DataProcessing.EffectShuttlePilot.Tooltip".Translate());
            }

            // 仅显示当前特化实际提供的数值属性。
            switch (specialization)
            {
                case DataProcessingSpecialization.GeneralTuning:
                    AddGeneralTuningEffects(steps, effectLabels, effectTips);
                    break;
                case DataProcessingSpecialization.ProductionCoordination:
                    AddProductionCoordinationEffects(steps, effectLabels, effectTips);
                    break;
                case DataProcessingSpecialization.FireControlCalculation:
                    AddFireControlEffects(steps, effectLabels, effectTips);
                    break;
                case DataProcessingSpecialization.AssaultProtocol:
                    AddAssaultEffects(steps, effectLabels, effectTips);
                    break;
                default:
                    AddGeneralTuningEffects(steps, effectLabels, effectTips);
                    break;
            }
        }

        private static void AddGeneralTuningEffects(
            int steps,
            List<string> effectLabels,
            List<string> effectTips)
        {
            float workSpeedOffset = DataProcessingAllocationUtility.GetWorkSpeedOffset(
                steps, DataProcessingSpecialization.GeneralTuning);
            effectLabels.Add(
                "MAP_MechanoidMechanitor.DataProcessing.EffectWorkSpeed".Translate(
                    workSpeedOffset.ToStringPercent()));
            effectTips.Add(string.Empty);

            // 通用调谐下瞄准、射击冷却与近战冷却倍率相同，使用组合文本。
            float timingFactor = DataProcessingAllocationUtility.GetAimingDelayFactor(
                steps, DataProcessingSpecialization.GeneralTuning);
            if (timingFactor < 1f)
            {
                effectLabels.Add(
                    "MAP_MechanoidMechanitor.DataProcessing.EffectAttackTiming".Translate(
                        timingFactor.ToStringPercent()));
                effectTips.Add(string.Empty);
            }

            float moveSpeedOffset = DataProcessingAllocationUtility.GetMoveSpeedOffset(
                steps, DataProcessingSpecialization.GeneralTuning);
            if (moveSpeedOffset > 0f)
            {
                effectLabels.Add(
                    "MAP_MechanoidMechanitor.DataProcessing.EffectMoveSpeed".Translate(
                        moveSpeedOffset.ToString("F1")));
                effectTips.Add(string.Empty);
            }

            float staggerDurationFactor = DataProcessingAllocationUtility.GetStaggerDurationFactor(
                steps, DataProcessingSpecialization.GeneralTuning);
            if (staggerDurationFactor < 1f)
            {
                effectLabels.Add(
                    "MAP_MechanoidMechanitor.DataProcessing.EffectStaggerDuration".Translate(
                        staggerDurationFactor.ToStringPercent()));
                effectTips.Add(string.Empty);
            }

            float incomingDamageFactor = DataProcessingAllocationUtility.GetIncomingDamageFactor(
                steps, DataProcessingSpecialization.GeneralTuning);
            if (incomingDamageFactor < 1f)
            {
                effectLabels.Add(
                    "MAP_MechanoidMechanitor.DataProcessing.EffectIncomingDamage".Translate(
                        incomingDamageFactor.ToStringPercent()));
                effectTips.Add(string.Empty);
            }
        }

        private static void AddProductionCoordinationEffects(
            int steps,
            List<string> effectLabels,
            List<string> effectTips)
        {
            float workSpeedOffset = DataProcessingAllocationUtility.GetWorkSpeedOffset(
                steps, DataProcessingSpecialization.ProductionCoordination);
            effectLabels.Add(
                "MAP_MechanoidMechanitor.DataProcessing.EffectWorkSpeed".Translate(
                    workSpeedOffset.ToStringPercent()));
            effectTips.Add(string.Empty);

            float moveSpeedOffset = DataProcessingAllocationUtility.GetMoveSpeedOffset(
                steps, DataProcessingSpecialization.ProductionCoordination);
            if (moveSpeedOffset > 0f)
            {
                effectLabels.Add(
                    "MAP_MechanoidMechanitor.DataProcessing.EffectMoveSpeed".Translate(
                        moveSpeedOffset.ToString("F1")));
                effectTips.Add(string.Empty);
            }

            float mechEnergyUsageFactor = DataProcessingAllocationUtility.GetMechEnergyUsageFactor(
                steps, DataProcessingSpecialization.ProductionCoordination);
            if (mechEnergyUsageFactor < 1f)
            {
                effectLabels.Add(
                    "MAP_MechanoidMechanitor.DataProcessing.EffectMechEnergyUsage".Translate(
                        mechEnergyUsageFactor.ToStringPercent()));
                effectTips.Add(string.Empty);
            }
        }

        private static void AddFireControlEffects(
            int steps,
            List<string> effectLabels,
            List<string> effectTips)
        {
            float aimingDelayFactor = DataProcessingAllocationUtility.GetAimingDelayFactor(
                steps, DataProcessingSpecialization.FireControlCalculation);
            if (aimingDelayFactor < 1f)
            {
                effectLabels.Add(
                    "MAP_MechanoidMechanitor.DataProcessing.EffectAimingDelay".Translate(
                        aimingDelayFactor.ToStringPercent()));
                effectTips.Add(string.Empty);
            }

            float rangedCooldownFactor = DataProcessingAllocationUtility.GetRangedCooldownFactor(
                steps, DataProcessingSpecialization.FireControlCalculation);
            if (rangedCooldownFactor < 1f)
            {
                effectLabels.Add(
                    "MAP_MechanoidMechanitor.DataProcessing.EffectRangedCooldown".Translate(
                        rangedCooldownFactor.ToStringPercent()));
                effectTips.Add(string.Empty);
            }
        }

        private static void AddAssaultEffects(
            int steps,
            List<string> effectLabels,
            List<string> effectTips)
        {
            float meleeCooldownFactor = DataProcessingAllocationUtility.GetMeleeCooldownFactor(
                steps, DataProcessingSpecialization.AssaultProtocol);
            if (meleeCooldownFactor < 1f)
            {
                effectLabels.Add(
                    "MAP_MechanoidMechanitor.DataProcessing.EffectMeleeCooldown".Translate(
                        meleeCooldownFactor.ToStringPercent()));
                effectTips.Add(string.Empty);
            }

            float moveSpeedOffset = DataProcessingAllocationUtility.GetMoveSpeedOffset(
                steps, DataProcessingSpecialization.AssaultProtocol);
            if (moveSpeedOffset > 0f)
            {
                effectLabels.Add(
                    "MAP_MechanoidMechanitor.DataProcessing.EffectMoveSpeed".Translate(
                        moveSpeedOffset.ToString("F1")));
                effectTips.Add(string.Empty);
            }

            float staggerDurationFactor = DataProcessingAllocationUtility.GetStaggerDurationFactor(
                steps, DataProcessingSpecialization.AssaultProtocol);
            if (staggerDurationFactor < 1f)
            {
                effectLabels.Add(
                    "MAP_MechanoidMechanitor.DataProcessing.EffectStaggerDuration".Translate(
                        staggerDurationFactor.ToStringPercent()));
                effectTips.Add(string.Empty);
            }

            float incomingDamageFactor = DataProcessingAllocationUtility.GetIncomingDamageFactor(
                steps, DataProcessingSpecialization.AssaultProtocol);
            if (incomingDamageFactor < 1f)
            {
                effectLabels.Add(
                    "MAP_MechanoidMechanitor.DataProcessing.EffectIncomingDamage".Translate(
                        incomingDamageFactor.ToStringPercent()));
                effectTips.Add(string.Empty);
            }
        }

        private static float CalculateRowHeight(int effectLineCount)
        {
            Text.Font = GameFont.Small;
            float lineHeight = Text.LineHeight;
            // 效果文本画在名称下方、头像右侧，行高取头像与文字块的较大值。
            float textBlockHeight = lineHeight + 2f + lineHeight * (1 + effectLineCount);
            return Mathf.Max(PortraitSize, textBlockHeight) + RowPadding * 2f;
        }

        private bool IsOverseerCapable()
        {
            return ModsConfig.BiotechActive
                && overseer != null
                && !overseer.Dead
                && !overseer.Destroyed
                && overseer.RaceProps.IsMechanoid
                && overseer.Faction != null
                && overseer.Faction.IsPlayerSafe()
                && overseer.mechanitor != null
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(overseer);
        }

        private string GetOverseerDisplayName()
        {
            return overseer != null && !overseer.Destroyed
                ? overseer.LabelShortCap
                : "???";
        }

        private AllocationAdjustBlockReason GetRemoveBlockReason(
            SubjectRowData row,
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            if (!IsOverseerCapable())
            {
                return AllocationAdjustBlockReason.OverseerInvalid;
            }

            if (row.target == null
                || row.target.Dead
                || row.target.Destroyed
                || !DataProcessingAllocationUtility
                    .IsValidAllocationPair(
                        overseer,
                        row.target))
            {
                return AllocationAdjustBlockReason.TargetInvalid;
            }

            bool dynamicManaged =
                registry != null
                && registry.IsDynamicAllocationEnabledForTarget(
                    overseer,
                    row.target);

            int removableSteps = dynamicManaged
                ? registry!.GetDynamicTargetNormalSteps(
                    overseer,
                    row.target)
                : row.steps;

            return removableSteps > 0
                ? AllocationAdjustBlockReason.None
                : AllocationAdjustBlockReason.NothingToRemove;
        }

        private AllocationAdjustBlockReason GetAddBlockReason(
            SubjectRowData row,
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            if (registry == null)
            {
                return AllocationAdjustBlockReason.RegistryMissing;
            }

            if (!IsOverseerCapable())
            {
                return AllocationAdjustBlockReason.OverseerInvalid;
            }

            if (row.target == null || row.target.Dead || row.target.Destroyed)
            {
                return AllocationAdjustBlockReason.TargetInvalid;
            }

            if (!DataProcessingAllocationUtility.IsValidAllocationPair(overseer, row.target))
            {
                return AllocationAdjustBlockReason.TargetInvalid;
            }

            // 动态托管目标只检查科研、监管关系与目标合法性；
            // 不阻止玩家提高其常态额度请求（动态预算内会自行约束）。
            bool dynamicManaged =
                registry.IsDynamicAllocationEnabledForTarget(
                    overseer,
                    row.target);
            if (dynamicManaged)
            {
                return AllocationAdjustBlockReason.None;
            }

            // 非动态目标继续使用 55% 绝对安全限制。
            if (!DataProcessingAllocationUtility.CanAddStep(overseer))
            {
                return AllocationAdjustBlockReason.NotEnoughProcessing;
            }

            return AllocationAdjustBlockReason.None;
        }

        private string GetRemoveTip(AllocationAdjustBlockReason reason)
        {
            switch (reason)
            {
                case AllocationAdjustBlockReason.None:
                    return string.Empty;
                case AllocationAdjustBlockReason.NothingToRemove:
                    return "MAP_MechanoidMechanitor.DataProcessing.NothingToRemove".Translate();
                case AllocationAdjustBlockReason.TargetInvalid:
                    return "MAP_MechanoidMechanitor.DataProcessing.TargetInvalid".Translate();
                case AllocationAdjustBlockReason.OverseerInvalid:
                    return "MAP_MechanoidMechanitor.DataProcessing.OverseerInvalid".Translate();
                default:
                    return "MAP_MechanoidMechanitor.DataProcessing.AdjustFailed".Translate();
            }
        }

        private string GetAddTip(AllocationAdjustBlockReason reason)
        {
            switch (reason)
            {
                case AllocationAdjustBlockReason.None:
                    return string.Empty;
                case AllocationAdjustBlockReason.NotEnoughProcessing:
                    return "MAP_MechanoidMechanitor.DataProcessing.NotEnoughProcessing".Translate(
                        GetOverseerDisplayName());
                case AllocationAdjustBlockReason.TargetInvalid:
                    return "MAP_MechanoidMechanitor.DataProcessing.TargetInvalid".Translate();
                case AllocationAdjustBlockReason.OverseerInvalid:
                case AllocationAdjustBlockReason.RegistryMissing:
                    return "MAP_MechanoidMechanitor.DataProcessing.OverseerInvalid".Translate();
                default:
                    return "MAP_MechanoidMechanitor.DataProcessing.AdjustFailed".Translate();
            }
        }

        private enum AllocationAdjustBlockReason
        {
            None,
            RegistryMissing,
            OverseerInvalid,
            TargetInvalid,
            NotEnoughProcessing,
            NothingToRemove
        }

        private sealed class SubjectRowData
        {
            public readonly Pawn target;
            public readonly bool isSelf;
            public readonly int steps;
            public readonly bool isPinned;
            public readonly int pinOrder;
            public readonly int controlGroupIndex;
            public readonly DataProcessingSpecialization specialization;
            public readonly List<string> effectLabels;
            public readonly List<string> effectTips;
            public readonly float rowHeight;

            public SubjectRowData(
                Pawn target,
                bool isSelf,
                int steps,
                bool isPinned,
                int pinOrder,
                int controlGroupIndex,
                DataProcessingSpecialization specialization,
                List<string> effectLabels,
                List<string> effectTips,
                float rowHeight)
            {
                this.target = target;
                this.isSelf = isSelf;
                this.steps = steps;
                this.isPinned = isPinned;
                this.pinOrder = pinOrder;
                this.controlGroupIndex = controlGroupIndex;
                this.specialization = specialization;
                this.effectLabels = effectLabels;
                this.effectTips = effectTips;
                this.rowHeight = rowHeight;
            }
        }
    }
}
