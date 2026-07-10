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

        public override Vector2 InitialSize => new Vector2(680f, 520f);

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
                DrawSummary(contentRect, ref curY);
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
                        "MAP_DataProcessingAllocation_OverseerInvalid".Translate());
                    return;
                }

                EnsureDisplayOrder(registry);
                List<SubjectRowData> rows = BuildRowsInDisplayOrder(registry);
                if (rows.Count == 0)
                {
                    DrawCenteredMessage(
                        listRect,
                        "MAP_DataProcessingAllocation_NoSubjects".Translate());
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
                "MAP_DataProcessingAllocation_WindowTitle".Translate(GetOverseerDisplayName()));
            curY += TitleHeight + 4f;
        }

        private void DrawSummary(Rect contentRect, ref float curY)
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
                "MAP_DataProcessingAllocation_CurrentProcessing".Translate(
                    currentProcessing.ToStringPercent()));
            Widgets.Label(
                new Rect(contentRect.x + halfWidth, curY, halfWidth - 4f, Text.LineHeight),
                "MAP_DataProcessingAllocation_RemainingProcessing".Translate(
                    DataProcessingAllocationUtility.StepsToPercent(remainingSteps)
                        .ToStringPercent()));
            curY += Text.LineHeight + 2f;

            string hint = "MAP_DataProcessingAllocation_MinReserveHint"
                .Translate(GetOverseerDisplayName());
            float hintHeight = Text.CalcHeight(hint, contentRect.width);
            GUI.color = HintTextColor;
            Widgets.Label(new Rect(contentRect.x, curY, contentRect.width, hintHeight), hint);
            GUI.color = Color.white;
            curY += hintHeight + SectionGap;
        }

        private void DrawListHeader(Rect contentRect, ref float curY)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(
                new Rect(contentRect.x, curY, contentRect.width, Text.LineHeight),
                "MAP_DataProcessingAllocation_SubjectsHeader".Translate());
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
                PinButtonSize
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

            Rect pinRect = new Rect(actionRect.x, buttonY, PinButtonSize, PinButtonSize);
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

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(
                percentRect,
                DataProcessingAllocationUtility.StepsToPercent(row.steps).ToStringPercent());
            Text.Anchor = TextAnchor.UpperLeft;

            DrawPinButton(pinRect, row, registry);

            AllocationAdjustBlockReason removeReason = GetRemoveBlockReason(row);
            bool canRemove = removeReason == AllocationAdjustBlockReason.None;
            string removeTip = GetRemoveTip(removeReason);
            if (!removeTip.NullOrEmpty())
            {
                TooltipHandler.TipRegion(removeRect, removeTip);
            }

            if (DrawActionButton(
                    removeRect,
                    "MAP_DataProcessingAllocation_Remove".Translate(),
                    canRemove)
                && registry != null)
            {
                if (!registry.TryRemoveStep(overseer, row.target))
                {
                    Messages.Message(
                        "MAP_DataProcessingAllocation_AdjustFailed".Translate(),
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
                    "MAP_DataProcessingAllocation_Add".Translate(),
                    canAdd)
                && registry != null)
            {
                if (!registry.TryAddStep(overseer, row.target))
                {
                    Messages.Message(
                        "MAP_DataProcessingAllocation_AdjustFailed".Translate(),
                        overseer,
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                }
            }
        }

        private void DrawPinButton(
            Rect pinRect,
            SubjectRowData row,
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            string label = row.isPinned ? "-" : "♡";
            bool canPin = registry != null
                && DataProcessingAllocationUtility.IsValidAllocationPair(overseer, row.target);
            string tip = !canPin
                ? (registry == null || !IsOverseerCapable()
                    ? "MAP_DataProcessingAllocation_OverseerInvalid".Translate()
                    : "MAP_DataProcessingAllocation_TargetInvalid".Translate())
                : (row.isPinned
                    ? "MAP_DataProcessingAllocation_UnpinTip".Translate()
                    : "MAP_DataProcessingAllocation_PinTip".Translate());
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
                "MAP_DataProcessingAllocation_CurrentEffects".Translate());
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

            // 按 Registry 顶置状态拆分，保留未顶置目标的相对顺序，避免加减档位引发跳动。
            List<Pawn> pinned = new List<Pawn>();
            List<Pawn> unpinned = new List<Pawn>();
            for (int i = 0; i < displayOrder.Count; i++)
            {
                Pawn pawn = displayOrder[i];
                if (registry?.IsPinned(overseer, pawn) == true)
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
            displayOrder.AddRange(pinned);
            displayOrder.AddRange(unpinned);

            for (int i = 0; i < validTargets.Count; i++)
            {
                Pawn pawn = validTargets[i];
                if (displayOrder.Contains(pawn))
                {
                    continue;
                }

                if (registry?.IsPinned(overseer, pawn) == true)
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

            List<Pawn> overseenPawns = overseer.mechanitor.OverseenPawns;
            for (int i = 0; i < overseenPawns.Count; i++)
            {
                Pawn target = overseenPawns[i];
                if (DataProcessingAllocationUtility.IsValidAllocationPair(overseer, target))
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

            int steps = registry?.GetStepsForOverseerTarget(overseer, target) ?? 0;
            bool isPinned = registry?.IsPinned(overseer, target) == true;
            int pinOrder = isPinned
                ? registry!.GetPinOrder(overseer, target)
                : int.MaxValue;
            int controlGroupIndex = target.GetMechControlGroup()?.Index ?? int.MaxValue;

            List<string> effectLabels = new List<string>();
            List<string> effectTips = new List<string>();
            BuildEffectTexts(steps, effectLabels, effectTips);

            return new SubjectRowData(
                target,
                steps,
                isPinned,
                pinOrder,
                controlGroupIndex,
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
            int insertIndex = 0;
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

        private int CountPinnedTargets(
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            int count = 0;
            for (int i = 0; i < displayOrder.Count; i++)
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
            List<string> effectLabels,
            List<string> effectTips)
        {
            if (steps < DataProcessingAllocationUtility.CommandRangeThresholdSteps)
            {
                effectLabels.Add("MAP_DataProcessingAllocation_EffectNone".Translate());
                effectTips.Add(string.Empty);
                return;
            }

            float workSpeedOffset = DataProcessingAllocationUtility.GetWorkSpeedOffset(steps);
            effectLabels.Add(
                "MAP_DataProcessingAllocation_EffectWorkSpeed".Translate(
                    workSpeedOffset.ToStringPercent()));
            effectTips.Add(string.Empty);

            effectLabels.Add("MAP_DataProcessingAllocation_EffectCommandRange".Translate());
            effectTips.Add("MAP_DataProcessingAllocation_EffectCommandRangeTip".Translate());

            float attackTimingFactor =
                DataProcessingAllocationUtility.GetAttackTimingFactor(steps);
            if (attackTimingFactor < 1f)
            {
                effectLabels.Add(
                    "MAP_DataProcessingAllocation_EffectAttackTiming".Translate(
                        attackTimingFactor.ToStringPercent()));
                effectTips.Add(string.Empty);
            }

            if (steps >= DataProcessingAllocationUtility.TravelNodeThresholdSteps)
            {
                effectLabels.Add("MAP_DataProcessingAllocation_EffectTravelLead".Translate());
                effectTips.Add("MAP_DataProcessingAllocation_EffectTravelLeadTip".Translate());
            }

            float moveSpeedOffset = DataProcessingAllocationUtility.GetMoveSpeedOffset(steps);
            if (moveSpeedOffset > 0f)
            {
                effectLabels.Add(
                    "MAP_DataProcessingAllocation_EffectMoveSpeed".Translate(
                        moveSpeedOffset.ToString("F2")));
                effectTips.Add(string.Empty);
            }

            float incomingDamageFactor =
                DataProcessingAllocationUtility.GetIncomingDamageFactor(steps);
            if (incomingDamageFactor < 1f)
            {
                effectLabels.Add(
                    "MAP_DataProcessingAllocation_EffectIncomingDamage".Translate(
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

        private AllocationAdjustBlockReason GetRemoveBlockReason(SubjectRowData row)
        {
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

            if (row.steps <= 0)
            {
                return AllocationAdjustBlockReason.NothingToRemove;
            }

            return AllocationAdjustBlockReason.None;
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
                    return "MAP_DataProcessingAllocation_NothingToRemove".Translate();
                case AllocationAdjustBlockReason.TargetInvalid:
                    return "MAP_DataProcessingAllocation_TargetInvalid".Translate();
                case AllocationAdjustBlockReason.OverseerInvalid:
                    return "MAP_DataProcessingAllocation_OverseerInvalid".Translate();
                default:
                    return "MAP_DataProcessingAllocation_AdjustFailed".Translate();
            }
        }

        private string GetAddTip(AllocationAdjustBlockReason reason)
        {
            switch (reason)
            {
                case AllocationAdjustBlockReason.None:
                    return string.Empty;
                case AllocationAdjustBlockReason.NotEnoughProcessing:
                    return "MAP_DataProcessingAllocation_NotEnoughProcessing".Translate(
                        GetOverseerDisplayName());
                case AllocationAdjustBlockReason.TargetInvalid:
                    return "MAP_DataProcessingAllocation_TargetInvalid".Translate();
                case AllocationAdjustBlockReason.OverseerInvalid:
                case AllocationAdjustBlockReason.RegistryMissing:
                    return "MAP_DataProcessingAllocation_OverseerInvalid".Translate();
                default:
                    return "MAP_DataProcessingAllocation_AdjustFailed".Translate();
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
            public readonly int steps;
            public readonly bool isPinned;
            public readonly int pinOrder;
            public readonly int controlGroupIndex;
            public readonly List<string> effectLabels;
            public readonly List<string> effectTips;
            public readonly float rowHeight;

            public SubjectRowData(
                Pawn target,
                int steps,
                bool isPinned,
                int pinOrder,
                int controlGroupIndex,
                List<string> effectLabels,
                List<string> effectTips,
                float rowHeight)
            {
                this.target = target;
                this.steps = steps;
                this.isPinned = isPinned;
                this.pinOrder = pinOrder;
                this.controlGroupIndex = controlGroupIndex;
                this.effectLabels = effectLabels;
                this.effectTips = effectTips;
                this.rowHeight = rowHeight;
            }
        }
    }
}
