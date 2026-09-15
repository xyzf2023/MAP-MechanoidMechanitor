using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed partial class Dialog_DataProcessingAllocationDashboard
    {
        private void DrawBasicEdit(Rect rect, ref float y, GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target, DataProcessingTargetUISnapshot snapshot)
        {
            var config = snapshot.config ?? new DataProcessingDynamicTargetRecord(overseer, target,
                snapshot.actualSteps, snapshot.specialization);
            bool global = registry.IsDynamicAllocationEnabled(overseer);
            bool changed = DrawCheckboxRow(new Rect(rect.x, y, rect.width, 28f), L("TargetAutomatic"),
                config.enabled, out bool enabled, global);
            if (changed) registry.SetDynamicAllocationEnabledForTarget(overseer, target, enabled);
            y += 36f;
            if (!global) TooltipHandler.TipRegion(new Rect(rect.x, y - 36f, rect.width, 28f), L("GlobalPaused"));
            if (!global && config.normalSteps != snapshot.actualSteps)
                Paragraph(rect, ref y, L("SavedRequest") + Percent(config.normalSteps), TextSecondary);
            bool automatic = global && config.enabled;
            DrawStepEditor(rect, ref y, automatic ? L("NormalRequest") : L("FixedRequest"),
                global ? config.normalSteps : snapshot.actualSteps,
                next => SetQuota(registry, target, next), readValue: () => registry.IsDynamicAllocationEnabled(overseer)
                    ? registry.GetDynamicTargetRecord(overseer, target)?.normalSteps ?? snapshot.actualSteps
                    : registry.GetStepsForOverseerTarget(overseer, target));
            if (automatic)
            {
                if (config.advancedMaxEnabled)
                {
                    var specialization = snapshot.requestedSpecialization;
                    DrawStepEditor(rect, ref y, L("ActiveMaximum") + " · " + DataProcessingAllocationUtility.GetSpecializationLabel(specialization),
                        ReadMaximum(config, specialization),
                        next => registry.SetDynamicTargetMaxStepsForSpecialization(overseer, target, specialization, next), config.normalSteps,
                        () => ReadMaximum(config, specialization));
                }
                else DrawStepEditor(rect, ref y, L("TaskMaximum"), config.commonMaxSteps,
                    next => registry.SetDynamicTargetCommonMaxSteps(overseer, target, next), config.normalSteps, () => config.commonMaxSteps);
                TooltipHandler.TipRegion(new Rect(rect.x, rect.y, rect.width, y - rect.y), L("RequestHelp"));
            }
            Paragraph(rect, ref y, L("Priority"), TextMain);
            DrawPriorityButtons(new Rect(rect.x, y, rect.width, ControlHeight), config.priority,
                next => registry.SetDynamicTargetPriority(overseer, target, next));
            y += ControlRow;
            TooltipHandler.TipRegion(new Rect(rect.x, y - ControlRow, rect.width, ControlHeight), L("PriorityHelp"));
            Paragraph(rect, ref y, L("CurrentMode"), TextMain);
            DrawSpecializationButtons(new Rect(rect.x, y, rect.width, ControlHeight * 2f + 6f), registry, target,
                snapshot.specialization);
            y += ControlHeight * 2f + 14f;
            DrawCurrentEffects(rect, ref y, registry, target, registry.GetTargetSnapshotForUI(overseer, target));
            if (automatic && Foldout(rect, ref y, L("AdvancedOptions"), ref showAdvanced))
            {
                DrawRulesEdit(rect, ref y, registry, target, config);
                DrawAdvancedEdit(rect, ref y, registry, target, config);
            }
        }

        private void SetQuota(GameComponent_DataProcessingAllocationRegistry registry, Pawn target, int next)
        {
            if (registry.IsDynamicAllocationEnabled(overseer)) registry.SetDynamicTargetNormalSteps(overseer, target, next);
            else
            {
                int current = registry.GetStepsForOverseerTarget(overseer, target);
                if (next <= current) registry.SetSteps(overseer, target, next);
                else
                {
                    // 复用原有加档安全检查，不用 SetSteps 绕过监管者保护。
                    long capacity = (long)DataProcessingAllocationUtility.GetAdditionalAssignableSteps(overseer)
                        * (ReferenceEquals(overseer, target) ? 2 : 1);
                    int attempts = (int)System.Math.Min(next - current, System.Math.Max(0L, capacity));
                    for (int i = 0; i < attempts; i++)
                    {
                        int before = registry.GetStepsForOverseerTarget(overseer, target);
                        if (!registry.TryAddStep(overseer, target)
                            || registry.GetStepsForOverseerTarget(overseer, target) <= before) break;
                    }
                    if (registry.GetStepsForOverseerTarget(overseer, target) < next)
                        Messages.Message(L("ManualLimited"), target, RimWorld.MessageTypeDefOf.RejectInput, false);
                }
            }
        }

        private static int ReadMaximum(DataProcessingDynamicTargetRecord config, DataProcessingSpecialization specialization)
        {
            var copy = new DataProcessingDynamicTargetRecord();
            DataProcessingDynamicTargetSettingsSnapshot.Capture(config).ApplyTo(copy, DataProcessingTargetCopyMode.QuotaOnly);
            return copy.GetMaxStepsForSpecialization(specialization);
        }

        private void DrawRulesEdit(Rect rect, ref float y, GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target, DataProcessingDynamicTargetRecord config)
        {
            Paragraph(rect, ref y, L("RuleOrder"), TextSecondary);
            var state = registry.GetCachedDynamicStateForTarget(target);
            DrawRuleCheckbox(rect, ref y, (state == DataProcessingDynamicState.CloseMelee ? "✓ " : "") + L("RuleMelee"), L("RuleMeleeHelp"), config.switchForCloseMelee,
                next => registry.SetDynamicTargetRule(overseer, target, "CloseMelee", next));
            DrawRuleCheckbox(rect, ref y, (state == DataProcessingDynamicState.DraftedMelee || state == DataProcessingDynamicState.DraftedRanged ? "✓ " : "") + L("RuleDraft"), L("RuleDraftHelp"), config.switchForDraftedWeapon,
                next => registry.SetDynamicTargetRule(overseer, target, "DraftedWeapon", next));
            DrawRuleCheckbox(rect, ref y, (state == DataProcessingDynamicState.Working ? "✓ " : "") + L("RuleWork"), L("RuleWorkHelp"), config.switchForWork,
                next => registry.SetDynamicTargetRule(overseer, target, "Work", next));
            DrawRuleCheckbox(rect, ref y, L("RuleFallback"), L("RuleFallbackHelp"), config.applyUndraftedFallback,
                next => registry.SetDynamicTargetRule(overseer, target, "UndraftedFallback", next));
            Paragraph(rect, ref y, L("Interval") + " · " + (config.checkIntervalTicks / 60) + L("Seconds"), TextMain);
            DrawIntervalButtons(new Rect(rect.x, y, rect.width, ControlHeight), config.checkIntervalTicks / 60,
                next => registry.SetDynamicTargetCheckInterval(overseer, target, next));
            y += ControlRow;
            if (Prefs.DevMode) DrawWorkRecognitionDiagnostic(rect, target, ref y);
        }

        private void DrawAdvancedEdit(Rect rect, ref float y, GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target, DataProcessingDynamicTargetRecord config)
        {
            if (DrawCheckboxRow(new Rect(rect.x, y, rect.width, ControlHeight), L("SeparateMaximum"), config.advancedMaxEnabled, out bool next))
                registry.SetDynamicTargetAdvancedMaxEnabled(overseer, target, next);
            y += ControlRow;
            if (!config.advancedMaxEnabled) return;
            foreach (DataProcessingSpecialization specialization in System.Enum.GetValues(typeof(DataProcessingSpecialization)))
            {
                var captured = specialization;
                DrawStepEditor(rect, ref y, DataProcessingAllocationUtility.GetSpecializationLabel(captured), ReadMaximum(config, captured),
                    value => registry.SetDynamicTargetMaxStepsForSpecialization(overseer, target, captured, value), config.normalSteps,
                    () => ReadMaximum(config, captured));
            }
        }
    }
}
