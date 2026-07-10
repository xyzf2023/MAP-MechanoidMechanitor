using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class Dialog_DataProcessingAllocation : Window
    {
        private const float RowHeight = 32f;
        private const float SummaryLineHeight = 24f;
        private const float ButtonWidth = 52f;

        private readonly Pawn overseer;
        private Vector2 scrollPosition;

        public override Vector2 InitialSize => new Vector2(520f, 460f);

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
            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            registry?.CleanupInvalidRecords();

            Text.Font = GameFont.Medium;
            Widgets.Label(
                new Rect(inRect.x, inRect.y, inRect.width, 32f),
                "MAP_DataProcessingAllocation_Label".Translate());
            float curY = inRect.y + 36f;

            Text.Font = GameFont.Small;
            float currentProcessing =
                DataProcessingAllocationUtility.GetCurrentConsciousness(overseer);
            int assignedSteps = registry?.GetTotalStepsForOverseer(overseer) ?? 0;
            int remainingSteps =
                DataProcessingAllocationUtility.GetAdditionalAssignableSteps(overseer);

            Widgets.Label(
                new Rect(inRect.x, curY, inRect.width, SummaryLineHeight),
                "MAP_DataProcessingAllocation_CurrentProcessing".Translate(
                    currentProcessing.ToStringPercent()));
            curY += SummaryLineHeight;
            Widgets.Label(
                new Rect(inRect.x, curY, inRect.width, SummaryLineHeight),
                "MAP_DataProcessingAllocation_AssignedProcessing".Translate(
                    DataProcessingAllocationUtility.StepsToPercent(assignedSteps)
                        .ToStringPercent()));
            curY += SummaryLineHeight;
            Widgets.Label(
                new Rect(inRect.x, curY, inRect.width, SummaryLineHeight),
                "MAP_DataProcessingAllocation_RemainingProcessing".Translate(
                    DataProcessingAllocationUtility.StepsToPercent(remainingSteps)
                        .ToStringPercent()));
            curY += SummaryLineHeight + 8f;

            List<Pawn> subjects = BuildSubjectList();
            if (subjects.Count == 0)
            {
                Widgets.Label(
                    new Rect(inRect.x, curY, inRect.width, 30f),
                    "MAP_DataProcessingAllocation_NoSubjects".Translate());
                return;
            }

            Rect outRect = new Rect(
                inRect.x,
                curY,
                inRect.width,
                inRect.height - (curY - inRect.y));
            float viewHeight = subjects.Count * RowHeight;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, viewHeight);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            float rowY = 0f;
            for (int i = 0; i < subjects.Count; i++)
            {
                DrawSubjectRow(viewRect, subjects[i], rowY, registry);
                rowY += RowHeight;
            }

            Widgets.EndScrollView();
        }

        private List<Pawn> BuildSubjectList()
        {
            List<Pawn> subjects = new List<Pawn>();
            if (overseer?.mechanitor == null)
            {
                return subjects;
            }

            List<Pawn> overseenPawns = overseer.mechanitor.OverseenPawns;
            for (int i = 0; i < overseenPawns.Count; i++)
            {
                Pawn target = overseenPawns[i];
                if (!DataProcessingAllocationUtility.IsValidAllocationPair(
                        overseer,
                        target))
                {
                    continue;
                }

                subjects.Add(target);
            }

            subjects.Sort((left, right) => string.Compare(
                left.LabelShortCap,
                right.LabelShortCap,
                System.StringComparison.Ordinal));
            return subjects;
        }

        private void DrawSubjectRow(
            Rect viewRect,
            Pawn target,
            float rowY,
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            Rect rowRect = new Rect(0f, rowY, viewRect.width, RowHeight - 4f);
            Widgets.DrawHighlightIfMouseover(rowRect);

            int steps = registry?.GetStepsForOverseerTarget(overseer, target) ?? 0;
            float nameWidth = rowRect.width - ButtonWidth * 2f - 140f;
            Widgets.Label(
                new Rect(rowRect.x, rowRect.y, nameWidth, rowRect.height),
                target.LabelShortCap);
            Widgets.Label(
                new Rect(rowRect.x + nameWidth, rowRect.y, 140f, rowRect.height),
                "MAP_DataProcessingAllocation_TargetAssigned".Translate(
                    DataProcessingAllocationUtility.StepsToPercent(steps).ToStringPercent()));

            Rect removeRect = new Rect(
                rowRect.xMax - ButtonWidth * 2f - 4f,
                rowRect.y + 4f,
                ButtonWidth,
                rowRect.height - 8f);
            Rect addRect = new Rect(
                rowRect.xMax - ButtonWidth,
                rowRect.y + 4f,
                ButtonWidth,
                rowRect.height - 8f);

            bool canRemove = steps > 0;
            bool canAdd = DataProcessingAllocationUtility.CanAddStep(overseer);

            if (!canRemove)
            {
                GUI.color = Color.gray;
            }

            if (Widgets.ButtonText(
                    removeRect,
                    "MAP_DataProcessingAllocation_Remove".Translate())
                && canRemove)
            {
                registry?.TryRemoveStep(overseer, target);
            }

            GUI.color = Color.white;

            if (!canAdd)
            {
                GUI.color = Color.gray;
            }

            if (Widgets.ButtonText(
                    addRect,
                    "MAP_DataProcessingAllocation_Add".Translate()))
            {
                if (canAdd)
                {
                    if (registry?.TryAddStep(overseer, target) != true)
                    {
                        Messages.Message(
                            "MAP_DataProcessingAllocation_NotEnoughProcessing".Translate(),
                            overseer,
                            MessageTypeDefOf.RejectInput,
                            historical: false);
                    }
                }
                else
                {
                    Messages.Message(
                        "MAP_DataProcessingAllocation_NotEnoughProcessing".Translate(),
                        overseer,
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                }
            }

            GUI.color = Color.white;
        }
    }
}
