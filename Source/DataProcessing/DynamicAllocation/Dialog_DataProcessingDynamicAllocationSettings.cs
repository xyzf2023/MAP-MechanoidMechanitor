using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 动态分配总设置窗口：监管者级阈值、预算不足提示与目标列表。
    /// </summary>
    public sealed class Dialog_DataProcessingDynamicAllocationSettings : Window
    {
        private readonly Pawn overseer;
        private Vector2 scrollPosition;

        private const float SectionGap = 8f;
        private const float RowGap = 4f;
        private const float RowHeight = 26f;

        public override Vector2 InitialSize => new Vector2(760f, 600f);

        public Dialog_DataProcessingDynamicAllocationSettings(Pawn overseer)
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

            Rect contentRect = inRect;
            contentRect.yMax -= Window.CloseButSize.y + 8f;

            float curY = contentRect.y;
            Text.Font = GameFont.Medium;
            Widgets.Label(
                new Rect(contentRect.x, curY, contentRect.width, Text.LineHeight),
                "MAP_DataProcessingAllocation_DynamicSettingsTitle".Translate(overseer.LabelShortCap));
            curY += Text.LineHeight + SectionGap;
            Text.Font = GameFont.Small;

            if (registry == null)
            {
                return;
            }

            DataProcessingDynamicAllocationRecord? overseerRecord =
                registry.FindDynamicAllocationRecordForUI(overseer);

            // 当前数据处理与阈值。
            float current = DataProcessingAllocationUtility.GetCurrentConsciousness(overseer);
            Widgets.Label(
                new Rect(contentRect.x, curY, contentRect.width, Text.LineHeight),
                "MAP_DataProcessingAllocation_DynamicCurrentProcessing".Translate(
                    current.ToStringPercent()));
            curY += Text.LineHeight + RowGap;

            if (overseerRecord != null)
            {
                float labelWidth = 360f;
                Rect labelRect = new Rect(contentRect.x, curY, labelWidth, Text.LineHeight);
                Widgets.Label(labelRect,
                    "MAP_DataProcessingAllocation_DynamicMinConsciousness".Translate());
                Rect minusRect = new Rect(contentRect.x + labelWidth, curY, 28f, Text.LineHeight + 4f);
                Rect plusRect = new Rect(contentRect.x + labelWidth + 36f, curY, 28f, Text.LineHeight + 4f);
                Rect valueRect = new Rect(contentRect.x + labelWidth + 72f, curY, 160f, Text.LineHeight + 4f);

                int percent = overseerRecord.minConsciousnessPercent;
                if (Widgets.ButtonText(minusRect, "-") && percent > 55)
                {
                    registry.SetDynamicMinConsciousnessPercent(overseer, percent - 5);
                }

                if (Widgets.ButtonText(plusRect, "+") && percent < 1000)
                {
                    registry.SetDynamicMinConsciousnessPercent(overseer, percent + 5);
                }

                Widgets.Label(valueRect, percent + "%");
                TooltipHandler.TipRegion(labelRect,
                    "MAP_DataProcessingAllocation_DynamicMinConsciousnessTip".Translate());
                curY += Text.LineHeight + RowGap;

                float threshold = percent / 100f;
                if (current < threshold - 0.0001f)
                {
                    GUI.color = new Color(0.9f, 0.6f, 0.2f);
                    Widgets.Label(
                        new Rect(contentRect.x, curY, contentRect.width, Text.LineHeight),
                        "MAP_DataProcessingAllocation_DynamicBudgetInsufficient".Translate());
                    GUI.color = Color.white;
                    curY += Text.LineHeight + RowGap;
                }
            }

            curY += SectionGap;

            // 目标列表。
            Text.Font = GameFont.Small;
            Widgets.Label(
                new Rect(contentRect.x, curY, contentRect.width, Text.LineHeight),
                "MAP_DataProcessingAllocation_DynamicTargetsHeader".Translate());
            curY += Text.LineHeight + RowGap;

            List<Pawn> targets = new List<Pawn>();
            if (overseer.mechanitor != null)
            {
                if (registry.IsValidAllocationPairForList(overseer, overseer))
                {
                    targets.Add(overseer);
                }

                foreach (Pawn p in overseer.mechanitor.OverseenPawns)
                {
                    if (!ReferenceEquals(p, overseer)
                        && registry.IsValidAllocationPairForList(overseer, p))
                    {
                        targets.Add(p);
                    }
                }
            }

            Rect listRect = new Rect(
                contentRect.x,
                curY,
                contentRect.width,
                Mathf.Max(0f, contentRect.yMax - curY));
            float viewHeight = targets.Count * RowHeight + RowGap;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);
            try
            {
                float rowY = 0f;
                for (int i = 0; i < targets.Count; i++)
                {
                    DrawTargetRow(
                        new Rect(0f, rowY, viewRect.width, RowHeight),
                        registry,
                        targets[i]);
                    rowY += RowHeight + RowGap;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawTargetRow(Rect rowRect, GameComponent_DataProcessingAllocationRegistry registry, Pawn target)
        {
            DataProcessingDynamicTargetRecord? config =
                registry.GetDynamicTargetRecord(overseer, target);
            bool enabled = config?.enabled ?? true;
            DataProcessingSpecialization currentSpec =
                registry.GetSpecializationForOverseerTarget(overseer, target);
            int actual = registry.GetStepsForOverseerTarget(overseer, target);
            int normal = config?.normalSteps ?? actual;

            Rect toggleRect = new Rect(rowRect.x, rowRect.y, 22f, 22f);
            bool toggle = enabled;
            Widgets.Checkbox(toggleRect.x, toggleRect.y, ref toggle, 22f, false);
            if (toggle != enabled)
            {
                registry.SetDynamicAllocationEnabledForTarget(overseer, target, toggle);
            }

            float textX = toggleRect.xMax + 6f;
            Widgets.Label(
                new Rect(textX, rowRect.y, 160f, rowRect.height),
                target.LabelShortCap);

            // 详情按钮固定在行尾右侧，避免与信息文本重叠。
            float detailWidth = 90f;
            float detailX = rowRect.xMax - detailWidth;
            Rect detailRect = new Rect(detailX, rowRect.y, detailWidth, rowRect.height);

            float infoX = textX + 168f;
            // 信息文本宽度限制到详情按钮左侧，避免重叠。
            float infoWidth = Mathf.Max(0f, detailRect.x - infoX - 8f);
            Rect infoRect = new Rect(infoX, rowRect.y, infoWidth, rowRect.height);

            // 统一通过注册表入口获取运行时状态标签，避免重复拼接翻译键。
            string stateLabel = registry.GetCachedDynamicStateLabelForUI(target);
            string info = string.Format(
                "{0}: {1} | {2}: {3} | {4}: {5}",
                "MAP_DataProcessingAllocation_DynamicCurrentMode".Translate(),
                DataProcessingAllocationUtility.GetSpecializationLabel(currentSpec),
                "MAP_DataProcessingAllocation_DynamicCurrentActualSteps".Translate(),
                DataProcessingAllocationUtility.StepsToPercent(actual).ToStringPercent(),
                "MAP_DataProcessingAllocation_DynamicNormalSteps".Translate(),
                DataProcessingAllocationUtility.StepsToPercent(normal).ToStringPercent());
            Widgets.Label(infoRect, info);

            // 完整信息（含状态与优先级）放入悬停提示，避免一行过长重叠。
            string fullInfo = string.Format(
                "{0}\n{1}: {2}\n{3}: {4}\n{5}: {6}",
                info,
                "MAP_DataProcessingAllocation_DynamicCurrentState".Translate(),
                stateLabel,
                "MAP_DataProcessingAllocation_DynamicPriority".Translate(),
                (config?.priority ?? 3).ToString(),
                "MAP_DataProcessingAllocation_DynamicCurrentActualSteps".Translate(),
                DataProcessingAllocationUtility.StepsToPercent(actual).ToStringPercent());
            TooltipHandler.TipRegion(infoRect, fullInfo);

            if (Widgets.ButtonText(
                    detailRect,
                    "MAP_DataProcessingAllocation_DynamicDetailButton".Translate()))
            {
                Find.WindowStack.Add(
                    new Dialog_DataProcessingDynamicTargetSettings(overseer, target));
            }
        }
    }
}
