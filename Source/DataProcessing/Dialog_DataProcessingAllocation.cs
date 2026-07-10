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

        private readonly Pawn overseer;
        private readonly Dictionary<string, string> truncateCache = new Dictionary<string, string>();
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

                List<SubjectRowData> rows = BuildSortedRows(registry);
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

            GUI.color = HintTextColor;
            Widgets.Label(
                new Rect(contentRect.x, curY, contentRect.width, Text.LineHeight * 1.5f),
                "MAP_DataProcessingAllocation_MinReserveHint".Translate(GetOverseerDisplayName()));
            GUI.color = Color.white;
            curY += Text.LineHeight + SectionGap;
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

            Rect pinRect = new Rect(leftX, contentTop, PinButtonSize, PinButtonSize);
            DrawPinButton(pinRect, row, registry);
            leftX = pinRect.xMax + LeftColumnGap;

            Rect portraitRect = new Rect(leftX, contentTop, PortraitSize, PortraitSize);
            DrawPortrait(portraitRect, row.target);
            leftX = portraitRect.xMax + LeftColumnGap;

            float actionBlockWidth =
                ActionButtonWidth + ActionGap + PercentWidth + ActionGap + ActionButtonWidth;
            Rect actionRect = new Rect(
                rowRect.xMax - actionBlockWidth - 2f,
                contentTop,
                actionBlockWidth,
                Text.LineHeight + 4f);
            DrawActionButtons(actionRect, row, registry);

            float nameWidth = Mathf.Max(40f, actionRect.x - leftX - 8f);
            Rect nameRect = new Rect(leftX, contentTop, nameWidth, Text.LineHeight);
            DrawName(nameRect, row.target);

            float effectY = nameRect.yMax + 2f;
            DrawEffects(
                new Rect(leftX, effectY, actionRect.x - leftX - 8f, rowRect.yMax - effectY - 2f),
                row);
        }

        private void DrawPinButton(
            Rect pinRect,
            SubjectRowData row,
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            string label = row.isPinned ? "-" : "♡";
            string tip = row.isPinned
                ? "MAP_DataProcessingAllocation_UnpinTip".Translate()
                : "MAP_DataProcessingAllocation_PinTip".Translate();
            TooltipHandler.TipRegion(pinRect, tip);

            if (Widgets.ButtonText(
                    pinRect,
                    label,
                    drawBackground: true,
                    doMouseoverSound: true,
                    active: registry != null))
            {
                if (registry == null)
                {
                    return;
                }

                if (row.isPinned)
                {
                    registry.TryUnpinTarget(overseer, row.target);
                }
                else
                {
                    registry.TryPinTarget(overseer, row.target);
                }

                SoundDefOf.Click.PlayOneShotOnCamera();
            }
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

        private void DrawActionButtons(
            Rect actionRect,
            SubjectRowData row,
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            Rect removeRect = new Rect(
                actionRect.x,
                actionRect.y,
                ActionButtonWidth,
                actionRect.height);
            Rect percentRect = new Rect(
                removeRect.xMax + ActionGap,
                actionRect.y,
                PercentWidth,
                actionRect.height);
            Rect addRect = new Rect(
                percentRect.xMax + ActionGap,
                actionRect.y,
                ActionButtonWidth,
                actionRect.height);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(
                percentRect,
                DataProcessingAllocationUtility.StepsToPercent(row.steps).ToStringPercent());
            Text.Anchor = TextAnchor.UpperLeft;

            AllocationAdjustBlockReason removeReason = GetRemoveBlockReason(row);
            bool canRemove = removeReason == AllocationAdjustBlockReason.None;
            string removeTip = GetRemoveTip(removeReason);
            if (!removeTip.NullOrEmpty())
            {
                TooltipHandler.TipRegion(removeRect, removeTip);
            }

            if (Widgets.ButtonText(
                    removeRect,
                    "MAP_DataProcessingAllocation_Remove".Translate(),
                    drawBackground: true,
                    doMouseoverSound: true,
                    active: canRemove)
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

            if (Widgets.ButtonText(
                    addRect,
                    "MAP_DataProcessingAllocation_Add".Translate(),
                    drawBackground: true,
                    doMouseoverSound: true,
                    active: canAdd)
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

        private List<SubjectRowData> BuildSortedRows(
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            List<SubjectRowData> rows = new List<SubjectRowData>();
            if (overseer?.mechanitor == null)
            {
                return rows;
            }

            List<Pawn> overseenPawns = overseer.mechanitor.OverseenPawns;
            for (int i = 0; i < overseenPawns.Count; i++)
            {
                Pawn target = overseenPawns[i];
                if (!DataProcessingAllocationUtility.IsValidAllocationPair(overseer, target))
                {
                    continue;
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

                SubjectRowData row = new SubjectRowData(
                    target,
                    steps,
                    isPinned,
                    pinOrder,
                    controlGroupIndex,
                    effectLabels,
                    effectTips,
                    CalculateRowHeight(effectLabels.Count));
                rows.Add(row);
            }

            rows.Sort(CompareRows);
            return rows;
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

            int kindCompare = string.Compare(
                left.target.KindLabel,
                right.target.KindLabel,
                StringComparison.CurrentCultureIgnoreCase);
            if (kindCompare != 0)
            {
                return kindCompare;
            }

            int labelCompare = string.Compare(
                left.target.Label,
                right.target.Label,
                StringComparison.CurrentCultureIgnoreCase);
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

            effectLabels.Add("MAP_DataProcessingAllocation_EffectCommandRange".Translate());
            effectTips.Add("MAP_DataProcessingAllocation_EffectCommandRangeTip".Translate());

            if (steps >= DataProcessingAllocationUtility.TravelNodeThresholdSteps)
            {
                effectLabels.Add("MAP_DataProcessingAllocation_EffectTravelLead".Translate());
                effectTips.Add("MAP_DataProcessingAllocation_EffectTravelLeadTip".Translate());
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
