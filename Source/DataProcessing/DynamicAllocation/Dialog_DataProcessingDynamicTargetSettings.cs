using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 单体机械族动态分配详细设置窗口。
    /// </summary>
    public sealed class Dialog_DataProcessingDynamicTargetSettings : Window
    {
        private readonly Pawn overseer;
        private readonly Pawn target;
        private Vector2 scrollPosition;

        private const float SectionGap = 8f;
        private const float RowGap = 4f;

        public override Vector2 InitialSize => new Vector2(640f, 620f);

        public Dialog_DataProcessingDynamicTargetSettings(Pawn overseer, Pawn target)
        {
            this.overseer = overseer;
            this.target = target;
            forcePause = false;
            doCloseButton = true;
            doCloseX = true;
            absorbInputAroundWindow = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            DataProcessingDynamicTargetRecord? config =
                registry?.GetOrCreateDynamicTargetRecord(overseer, target);

            Rect contentRect = inRect;
            contentRect.yMax -= Window.CloseButSize.y + 8f;

            // 标题始终可见，内容区可滚动。
            Text.Font = GameFont.Medium;
            Widgets.Label(
                new Rect(contentRect.x, contentRect.y, contentRect.width, Text.LineHeight),
                "MAP_MechanoidMechanitor.DataProcessing.DynamicTargetSettings.Title".Translate(target.LabelShortCap));
            float headerHeight = Text.LineHeight + SectionGap;
            Rect listRect = new Rect(
                contentRect.x,
                contentRect.y + headerHeight,
                contentRect.width,
                Mathf.Max(0f, contentRect.yMax - contentRect.y - headerHeight));

            if (config == null || registry == null)
            {
                return;
            }

            // 先计算内容高度以支撑滚动。使用保守高度，确保底部规则行始终可滚动到。
            float labelWidth = 240f;
            float fieldWidth = contentRect.width - labelWidth - 8f;
            float viewHeight =
                config.advancedMaxEnabled
                    ? 760f
                    : 620f;
            viewHeight = Mathf.Max(viewHeight, listRect.height);

            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);
            try
            {
                float curY = viewRect.y;

                // 当前状态（只读）。
                DrawReadOnlyLine(viewRect, ref curY, labelWidth, fieldWidth,
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicCurrentState".Translate(),
                    GetStateLabel(registry, config));
                DrawReadOnlyLine(viewRect, ref curY, labelWidth, fieldWidth,
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicCurrentMode".Translate(),
                    DataProcessingAllocationUtility.GetSpecializationLabel(
                        registry.GetSpecializationForOverseerTarget(overseer, target)));
                DrawReadOnlyLine(viewRect, ref curY, labelWidth, fieldWidth,
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicCurrentActualSteps".Translate(),
                    DataProcessingAllocationUtility.StepsToPercent(
                        registry.GetStepsForOverseerTarget(overseer, target)).ToStringPercent());
                curY += SectionGap;

                // 单体动态开关。
                DrawToggleLine(viewRect, ref curY, labelWidth, fieldWidth,
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicTargetEnabled".Translate(),
                    config.enabled,
                    v => registry.SetDynamicAllocationEnabledForTarget(overseer, target, v));

                // 默认模式。
                DrawSpecializationLine(viewRect, ref curY, labelWidth, fieldWidth,
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicDefaultMode".Translate(),
                    config.defaultSpecialization,
                    s => registry.SetDynamicTargetDefaultSpecialization(overseer, target, s));

                // 常态额度。
                DrawStepsLine(viewRect, ref curY, labelWidth, fieldWidth,
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicNormalSteps".Translate(),
                    config.normalSteps,
                    v => registry.SetDynamicTargetNormalSteps(overseer, target, v));

                // 统一最高额度。
                DrawStepsLine(viewRect, ref curY, labelWidth, fieldWidth,
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicCommonMaxSteps".Translate(),
                    config.commonMaxSteps,
                    v => registry.SetDynamicTargetCommonMaxSteps(overseer, target, v));

                // 高级最高额度开关。
                DrawToggleLine(viewRect, ref curY, labelWidth, fieldWidth,
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicAdvancedMax".Translate(),
                    config.advancedMaxEnabled,
                    v => registry.SetDynamicTargetAdvancedMaxEnabled(overseer, target, v));

                if (config.advancedMaxEnabled)
                {
                    DrawStepsLine(viewRect, ref curY, labelWidth, fieldWidth,
                        "MAP_MechanoidMechanitor.DataProcessing.DynamicGeneralMax".Translate(),
                        config.generalMaxSteps,
                        v => registry.SetDynamicTargetMaxStepsForSpecialization(
                            overseer, target, DataProcessingSpecialization.GeneralTuning, v));
                    DrawStepsLine(viewRect, ref curY, labelWidth, fieldWidth,
                        "MAP_MechanoidMechanitor.DataProcessing.DynamicProductionMax".Translate(),
                        config.productionMaxSteps,
                        v => registry.SetDynamicTargetMaxStepsForSpecialization(
                            overseer, target, DataProcessingSpecialization.ProductionCoordination, v));
                    DrawStepsLine(viewRect, ref curY, labelWidth, fieldWidth,
                        "MAP_MechanoidMechanitor.DataProcessing.DynamicFireControlMax".Translate(),
                        config.fireControlMaxSteps,
                        v => registry.SetDynamicTargetMaxStepsForSpecialization(
                            overseer, target, DataProcessingSpecialization.FireControlCalculation, v));
                    DrawStepsLine(viewRect, ref curY, labelWidth, fieldWidth,
                        "MAP_MechanoidMechanitor.DataProcessing.DynamicAssaultMax".Translate(),
                        config.assaultMaxSteps,
                        v => registry.SetDynamicTargetMaxStepsForSpecialization(
                            overseer, target, DataProcessingSpecialization.AssaultProtocol, v));
                }

                // 优先级。
                DrawPriorityLine(viewRect, ref curY, labelWidth, fieldWidth,
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicPriority".Translate(),
                    config.priority,
                    v => registry.SetDynamicTargetPriority(overseer, target, v));

                // 检查间隔（秒）。
                DrawIntervalLine(viewRect, ref curY, labelWidth, fieldWidth,
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicCheckInterval".Translate(),
                    config.checkIntervalTicks / 60,
                    v => registry.SetDynamicTargetCheckInterval(overseer, target, v));

                // 规则开关。
                DrawToggleLine(viewRect, ref curY, labelWidth, fieldWidth,
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicRuleWork".Translate(),
                    config.switchForWork,
                    v => registry.SetDynamicTargetRule(overseer, target, "Work", v));
                DrawToggleLine(viewRect, ref curY, labelWidth, fieldWidth,
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicRuleDraftedWeapon".Translate(),
                    config.switchForDraftedWeapon,
                    v => registry.SetDynamicTargetRule(overseer, target, "DraftedWeapon", v));
                DrawToggleLine(viewRect, ref curY, labelWidth, fieldWidth,
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicRuleCloseMelee".Translate(),
                    config.switchForCloseMelee,
                    v => registry.SetDynamicTargetRule(overseer, target, "CloseMelee", v));
                DrawToggleLine(viewRect, ref curY, labelWidth, fieldWidth,
                    "MAP_MechanoidMechanitor.DataProcessing.DynamicRuleUndraftedFallback".Translate(),
                    config.applyUndraftedFallback,
                    v => registry.SetDynamicTargetRule(overseer, target, "UndraftedFallback", v));
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private string GetStateLabel(
            GameComponent_DataProcessingAllocationRegistry registry,
            DataProcessingDynamicTargetRecord config)
        {
            if (!config.enabled)
            {
                return "MAP_MechanoidMechanitor.DataProcessing.DynamicStateIdle".Translate();
            }

            // 统一通过注册表入口获取运行时状态标签，避免与调度器评估不一致或重复拼接翻译键。
            return registry.GetCachedDynamicStateLabelForUI(target);
        }

        private void DrawReadOnlyLine(
            Rect viewRect,
            ref float curY,
            float labelWidth,
            float fieldWidth,
            string label,
            string value)
        {
            Rect labelRect = new Rect(viewRect.x, curY, labelWidth, Text.LineHeight);
            Rect valueRect = new Rect(viewRect.x + labelWidth + 8f, curY, fieldWidth, Text.LineHeight);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(labelRect, label);
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            Widgets.Label(valueRect, value);
            GUI.color = Color.white;
            curY += Text.LineHeight + RowGap;
        }

        private void DrawToggleLine(
            Rect viewRect,
            ref float curY,
            float labelWidth,
            float fieldWidth,
            string label,
            bool value,
            System.Action<bool> setter)
        {
            Rect labelRect = new Rect(viewRect.x, curY, labelWidth, Text.LineHeight);
            bool current = value;
            Rect toggleRect = new Rect(viewRect.x + labelWidth + 8f, curY, 24f, 24f);
            Widgets.Checkbox(toggleRect.x, toggleRect.y, ref current, 24f, false);
            Widgets.Label(labelRect, label);
            if (current != value)
            {
                setter(current);
            }

            curY += Text.LineHeight + RowGap;
        }

        private void DrawSpecializationLine(
            Rect viewRect,
            ref float curY,
            float labelWidth,
            float fieldWidth,
            string label,
            DataProcessingSpecialization current,
            System.Action<DataProcessingSpecialization> setter)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(viewRect.x, curY, labelWidth, Text.LineHeight), label);

            float buttonWidth = fieldWidth / 4f - 4f;
            DataProcessingSpecialization[] all =
            {
                DataProcessingSpecialization.GeneralTuning,
                DataProcessingSpecialization.ProductionCoordination,
                DataProcessingSpecialization.FireControlCalculation,
                DataProcessingSpecialization.AssaultProtocol
            };

            for (int i = 0; i < all.Length; i++)
            {
                DataProcessingSpecialization spec = all[i];
                Rect buttonRect = new Rect(
                    viewRect.x + labelWidth + 8f + i * (buttonWidth + 4f),
                    curY,
                    buttonWidth,
                    Text.LineHeight + 4f);
                bool active = spec == current;
                if (Widgets.ButtonText(buttonRect,
                        DataProcessingAllocationUtility.GetSpecializationLabel(spec),
                        active,
                        active,
                        true)
                    && !active)
                {
                    setter(spec);
                }
            }

            curY += Text.LineHeight + RowGap + 4f;
        }

        private void DrawStepsLine(
            Rect viewRect,
            ref float curY,
            float labelWidth,
            float fieldWidth,
            string label,
            int steps,
            System.Action<int> setter)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(viewRect.x, curY, labelWidth, Text.LineHeight), label);

            Rect minusRect = new Rect(viewRect.x + labelWidth + 8f, curY, 28f, Text.LineHeight + 4f);
            Rect plusRect = new Rect(viewRect.x + labelWidth + 8f + 36f, curY, 28f, Text.LineHeight + 4f);
            Rect valueRect = new Rect(viewRect.x + labelWidth + 8f + 72f, curY, 120f, Text.LineHeight + 4f);

            if (Widgets.ButtonText(minusRect, "-") && steps > 0)
            {
                setter(steps - 1);
            }

            if (Widgets.ButtonText(plusRect, "+"))
            {
                setter(steps + 1);
            }

            Widgets.Label(
                valueRect,
                DataProcessingAllocationUtility.StepsToPercent(steps).ToStringPercent());

            curY += Text.LineHeight + RowGap + 4f;
        }

        private void DrawPriorityLine(
            Rect viewRect,
            ref float curY,
            float labelWidth,
            float fieldWidth,
            string label,
            int priority,
            System.Action<int> setter)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(viewRect.x, curY, labelWidth, Text.LineHeight), label);

            float buttonWidth = fieldWidth / 4f - 4f;
            for (int i = 0; i < 4; i++)
            {
                int p = i + 1;
                Rect buttonRect = new Rect(
                    viewRect.x + labelWidth + 8f + i * (buttonWidth + 4f),
                    curY,
                    buttonWidth,
                    Text.LineHeight + 4f);
                bool active = p == priority;
                if (Widgets.ButtonText(buttonRect,
                        ("MAP_MechanoidMechanitor.DataProcessing.DynamicPriority" + p + "Short").Translate(),
                        active,
                        active,
                        true)
                    && !active)
                {
                    setter(p);
                }
            }

            curY += Text.LineHeight + RowGap + 4f;
        }

        private void DrawIntervalLine(
            Rect viewRect,
            ref float curY,
            float labelWidth,
            float fieldWidth,
            string label,
            int seconds,
            System.Action<int> setter)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(viewRect.x, curY, labelWidth, Text.LineHeight), label);

            Rect minusRect = new Rect(viewRect.x + labelWidth + 8f, curY, 28f, Text.LineHeight + 4f);
            Rect plusRect = new Rect(viewRect.x + labelWidth + 8f + 36f, curY, 28f, Text.LineHeight + 4f);
            Rect valueRect = new Rect(viewRect.x + labelWidth + 8f + 72f, curY, 160f, Text.LineHeight + 4f);

            if (Widgets.ButtonText(minusRect, "-") && seconds > 1)
            {
                setter(seconds - 1);
            }

            if (Widgets.ButtonText(plusRect, "+"))
            {
                setter(seconds + 1);
            }

            Widgets.Label(
                valueRect,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicSeconds".Translate(seconds));

            curY += Text.LineHeight + RowGap + 4f;
        }
    }
}
