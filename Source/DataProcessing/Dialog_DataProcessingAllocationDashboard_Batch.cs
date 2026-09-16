using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed partial class Dialog_DataProcessingAllocationDashboard
    {
        private DataProcessingDynamicTargetRecord? batchDraft;
        private bool batchDirty;
        private bool batchConfirmationOpen;
        private bool batchAdvanced;
        private bool batchAdjustAll;
        private readonly HashSet<DataProcessingBatchField> batchFields = new HashSet<DataProcessingBatchField>();


        private void SwitchBatchEditor(bool all)
        {
            RequestBatchExit(() =>
            {
                batchAdjustAll = all;
                detailScrollPosition = Vector2.zero;
            });
        }

        private string SingleBatchValueLabel(DataProcessingBatchField batchField, int batchSingleValue)
        {
            switch (batchField)
            {
                case DataProcessingBatchField.Enabled:
                case DataProcessingBatchField.Melee:
                case DataProcessingBatchField.Draft:
                case DataProcessingBatchField.Work:
                case DataProcessingBatchField.Fallback:
                case DataProcessingBatchField.Separate:
                    return L(batchSingleValue != 0 ? "On" : "Off");
                case DataProcessingBatchField.Specialization:
                    return DataProcessingAllocationUtility.GetSpecializationLabel((DataProcessingSpecialization)batchSingleValue);
                case DataProcessingBatchField.Interval:
                    return (batchSingleValue / 60) + L("Seconds");
                case DataProcessingBatchField.Priority:
                    return batchSingleValue.ToString();
                default:
                    return Percent(batchSingleValue);
            }
        }
        private float DrawSingleBatch(Rect view, float y)
        {
            DataProcessingBatchField[] fields = {
                DataProcessingBatchField.Enabled, DataProcessingBatchField.Normal, DataProcessingBatchField.CommonMax,
                DataProcessingBatchField.Priority, DataProcessingBatchField.Specialization,
                DataProcessingBatchField.Melee, DataProcessingBatchField.Draft, DataProcessingBatchField.Work,
                DataProcessingBatchField.Fallback, DataProcessingBatchField.Interval, DataProcessingBatchField.Separate,
                DataProcessingBatchField.GeneralMax, DataProcessingBatchField.ProductionMax,
                DataProcessingBatchField.FireMax, DataProcessingBatchField.AssaultMax
            };
            foreach (var field in fields)
            {
                if (field == DataProcessingBatchField.Melee) Section(view, ref y, L("AdvancedOptions"));
                var check = new Rect(view.x, y + 2f, 24f, 24f);
                if (DrawMiniButton(check, batchFields.Contains(field) ? "✓" : ""))
                {
                    FinishStepInput();
                    if (!batchFields.Remove(field)) batchFields.Add(field);
                    batchDirty = true;
                }
                var content = new Rect(view.x + 34f, view.y, view.width - 34f, view.height);
                y = DrawSingleBatchField(content, y, field);
            }
            return y;
        }

        private float DrawSingleBatchField(Rect view, float y, DataProcessingBatchField batchField)
        {
            int batchSingleValue = DataProcessingBatchFieldUtility.Read(batchDraft!, batchField);
            string label = L(DataProcessingBatchFieldUtility.LabelKey(batchField));
            // 草稿编辑不联动其他字段；应用时按各目标现有设置统一规范化。
            Action<int> save = value =>
            {
                DataProcessingBatchFieldUtility.Write(batchDraft!, batchField, value);
                batchDirty = true;
            };
            switch (batchField)
            {
                case DataProcessingBatchField.Enabled:
                case DataProcessingBatchField.Melee:
                case DataProcessingBatchField.Draft:
                case DataProcessingBatchField.Work:
                case DataProcessingBatchField.Fallback:
                case DataProcessingBatchField.Separate:
                    if (DrawCheckboxRow(new Rect(view.x, y, view.width, 28f), label, batchSingleValue != 0, out bool next))
                        save(next ? 1 : 0);
                    y += 36f;
                    break;
                case DataProcessingBatchField.Priority:
                    Paragraph(view, ref y, label, TextMain);
                    DrawPriorityButtons(new Rect(view.x, y, view.width, ControlHeight), batchSingleValue, save);
                    y += ControlRow;
                    break;
                case DataProcessingBatchField.Interval:
                    Paragraph(view, ref y, label, TextMain);
                    DrawIntervalButtons(new Rect(view.x, y, view.width, ControlHeight), batchSingleValue / 60, n => save(n * 60));
                    y += ControlRow;
                    break;
                case DataProcessingBatchField.Specialization:
                    Paragraph(view, ref y, label, TextMain);
                    float width = (view.width - 6f) / 2f;
                    foreach (DataProcessingSpecialization specialization in Enum.GetValues(typeof(DataProcessingSpecialization)))
                    {
                        int i = (int)specialization;
                        if (DrawTabButton(new Rect(view.x + i % 2 * (width + 6f), y + i / 2 * (ControlHeight + 6f), width, ControlHeight),
                            DataProcessingAllocationUtility.GetSpecializationLabel(specialization), batchSingleValue == i)) save(i);
                    }
                    y += ControlHeight * 2f + 14f;
                    break;
                default:
                    DrawStepEditor(view, ref y, label, batchSingleValue, save, readValue: () => DataProcessingBatchFieldUtility.Read(batchDraft!, batchField));
                    break;
            }
            return y;
        }

        private void RequestBatchExit(Action leave)
        {
            FinishStepInput();
            if (batchConfirmationOpen) return;
            if (mode != DashboardMode.Batch || !batchDirty)
            {
                batchDraft = null;
                leave();
                return;
            }
            batchConfirmationOpen = true;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(L("BatchUnsaved"), () =>
            {
                batchConfirmationOpen = false;
                batchDirty = false;
                batchDraft = null;
                leave();
            }, () => batchConfirmationOpen = false));
        }

        private void ChangeBatch(Action<DataProcessingDynamicTargetRecord> change)
        {
            if (batchDraft == null) return;
            change(batchDraft);
            batchDraft.Normalize();
            batchDirty = true;
        }

        private void DrawBatchPage(Rect rect, GameComponent_DataProcessingAllocationRegistry registry)
        {
            float tabWidth = (rect.width - 6f) / 2f;
            if (DrawTabButton(new Rect(rect.x, rect.y, tabWidth, ControlHeight), L("BatchSingle"), !batchAdjustAll) && batchAdjustAll)
                SwitchBatchEditor(false);
            if (DrawTabButton(new Rect(rect.x + tabWidth + 6f, rect.y, tabWidth, ControlHeight), L("BatchAll"), batchAdjustAll) && !batchAdjustAll)
                SwitchBatchEditor(true);
            rect.yMin += ControlRow;
            inputScope = "Batch:" + (batchAdjustAll ? "All" : "Selected");
            var targets = CollectTargets(registry);
            batchTargets.RemoveWhere(p => p == null || !targets.Contains(p) || DataProcessingOverseerResolver.IsFrozenSelf(overseer, p));
            if (batchDraft == null)
            {
                batchDraft = new DataProcessingDynamicTargetRecord();
                var source = batchAdjustAll && selectedTarget != null ? registry.GetDynamicTargetRecord(overseer, selectedTarget) : null;
                if (source != null)
                {
                    DataProcessingDynamicTargetSettingsSnapshot.Capture(source).ApplyTo(batchDraft, DataProcessingTargetCopyMode.AllSettings);
                    batchDraft.defaultSpecialization = source.defaultSpecialization;
                }
                else
                {
                    registry.GetDynamicTargetDefaultsForUI(overseer)?.ApplyTo(batchDraft);
                    if (batchAdjustAll && selectedTarget != null)
                    {
                        batchDraft.normalSteps = registry.GetStepsForOverseerTarget(overseer, selectedTarget);
                        batchDraft.defaultSpecialization = registry.GetSpecializationForOverseerTarget(overseer, selectedTarget);
                    }
                }
                batchDraft.Normalize();
                batchFields.Clear();
            }
            var draft = batchDraft;
            // 应用按钮固定在底部，设置在独立滚动区内编辑。
            var applyRect = new Rect(rect.x, rect.yMax - ControlHeight, rect.width, ControlHeight);
            DrawScrollable(new Rect(rect.x, rect.y, rect.width, Mathf.Max(1f, rect.height - ControlRow)), "Batch", view =>
            {
                float y = 0f;
                Section(view, ref y, L("BatchPage") + " · " + batchTargets.Count + L("Targets"));
                if (!batchAdjustAll) return DrawSingleBatch(view, y);
                if (DrawCheckboxRow(new Rect(view.x, y, view.width, 28f), L("TargetAutomatic"), draft.enabled, out bool enabled))
                    ChangeBatch(d => d.enabled = enabled);
                y += 36f;
                DrawStepEditor(view, ref y, L("NormalRequest"), draft.normalSteps,
                    n => ChangeBatch(d => d.normalSteps = n), readValue: () => draft.normalSteps);
                if (!draft.advancedMaxEnabled)
                    DrawStepEditor(view, ref y, L("TaskMaximum"), draft.commonMaxSteps,
                        n => ChangeBatch(d => d.commonMaxSteps = n), draft.normalSteps, () => draft.commonMaxSteps);
                Paragraph(view, ref y, L("Priority"), TextMain);
                DrawPriorityButtons(new Rect(view.x, y, view.width, ControlHeight), draft.priority,
                    n => ChangeBatch(d => d.priority = n));
                y += ControlRow;
                Paragraph(view, ref y, L("DefaultMode"), TextMain);
                float width = (view.width - 6f) / 2f;
                foreach (DataProcessingSpecialization specialization in Enum.GetValues(typeof(DataProcessingSpecialization)))
                {
                    int i = (int)specialization;
                    var button = new Rect(view.x + (i % 2) * (width + 6f), y + (i / 2) * (ControlHeight + 6f), width, ControlHeight);
                    if (DrawTabButton(button, DataProcessingAllocationUtility.GetSpecializationLabel(specialization), draft.defaultSpecialization == specialization))
                        ChangeBatch(d => d.defaultSpecialization = specialization);
                }
                y += ControlHeight * 2f + 14f;
                if (Foldout(view, ref y, L("AdvancedOptions"), ref batchAdvanced))
                {
                    DrawRuleCheckbox(view, ref y, L("RuleMelee"), L("RuleMeleeHelp"), draft.switchForCloseMelee,
                        v => ChangeBatch(d => d.switchForCloseMelee = v));
                    DrawRuleCheckbox(view, ref y, L("RuleDraft"), L("RuleDraftHelp"), draft.switchForDraftedWeapon,
                        v => ChangeBatch(d => d.switchForDraftedWeapon = v));
                    DrawRuleCheckbox(view, ref y, L("RuleWork"), L("RuleWorkHelp"), draft.switchForWork,
                        v => ChangeBatch(d => d.switchForWork = v));
                    DrawRuleCheckbox(view, ref y, L("RuleFallback"), L("RuleFallbackHelp"), draft.applyUndraftedFallback,
                        v => ChangeBatch(d => d.applyUndraftedFallback = v));
                    Paragraph(view, ref y, L("Interval"), TextMain);
                    DrawIntervalButtons(new Rect(view.x, y, view.width, ControlHeight), draft.checkIntervalTicks / 60,
                        n => ChangeBatch(d => d.checkIntervalTicks = n * 60));
                    y += ControlRow;
                    if (DrawCheckboxRow(new Rect(view.x, y, view.width, ControlHeight), L("SeparateMaximum"), draft.advancedMaxEnabled, out bool advanced))
                        ChangeBatch(d => d.advancedMaxEnabled = advanced);
                    y += ControlRow;
                    if (draft.advancedMaxEnabled)
                        foreach (DataProcessingSpecialization specialization in Enum.GetValues(typeof(DataProcessingSpecialization)))
                        {
                            var captured = specialization;
                            DrawStepEditor(view, ref y, DataProcessingAllocationUtility.GetSpecializationLabel(captured), ReadMaximum(draft, captured),
                                n => ChangeBatch(d =>
                                {
                                    switch (captured)
                                    {
                                        case DataProcessingSpecialization.GeneralTuning: d.generalMaxSteps = n; break;
                                        case DataProcessingSpecialization.ProductionCoordination: d.productionMaxSteps = n; break;
                                        case DataProcessingSpecialization.FireControlCalculation: d.fireControlMaxSteps = n; break;
                                        case DataProcessingSpecialization.AssaultProtocol: d.assaultMaxSteps = n; break;
                                    }
                                }), draft.normalSteps, () => ReadMaximum(draft, captured));
                        }
                }
                return y;
            });
            if (DrawFlatButton(applyRect, L(batchAdjustAll ? "BatchApplyAll" : "BatchApplySingle"), DashboardButtonStyle.Primary, enabled: batchTargets.Count > 0 && (batchAdjustAll || batchFields.Count > 0) && !batchConfirmationOpen))
            {
                FinishStepInput();
                var chosen = new List<Pawn>(batchTargets);
                var snapshot = DataProcessingDynamicTargetSettingsSnapshot.Capture(draft);
                Dictionary<DataProcessingBatchField, int>? fields = null;
                var confirmation = new StringBuilder();
                if (!batchAdjustAll)
                {
                    fields = new Dictionary<DataProcessingBatchField, int>();
                    confirmation.Append(L("BatchSingleConfirm"));
                    foreach (DataProcessingBatchField field in Enum.GetValues(typeof(DataProcessingBatchField)))
                    {
                        if (!batchFields.Contains(field)) continue;
                        int value = DataProcessingBatchFieldUtility.Read(draft, field);
                        fields.Add(field, value);
                        confirmation.AppendLine(L(DataProcessingBatchFieldUtility.LabelKey(field)) + "： " + SingleBatchValueLabel(field, value));
                    }
                    confirmation.Append(chosen.Count + L("Targets"));
                }
                DataProcessingSpecialization? specialization = batchAdjustAll ? draft.defaultSpecialization
                    : fields!.ContainsKey(DataProcessingBatchField.Specialization) ? (DataProcessingSpecialization?)draft.defaultSpecialization : null;
                batchConfirmationOpen = true;
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(batchAdjustAll ? L("BatchApplyConfirm") : confirmation.ToString(), () =>
                {
                    batchConfirmationOpen = false;
                    int count = registry.ApplyTargetSettingsBatch(overseer, chosen, snapshot, DataProcessingTargetCopyMode.AllSettings, specialization, fields);
                    if (count == chosen.Count) batchDirty = false;
                    Messages.Message(L("Applied") + count + L("Targets") + " · " + L("Skipped") + (chosen.Count - count),
                        count > 0 ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput, false);
                }, () => batchConfirmationOpen = false));
            }
        }
        private void ConfirmBatch(GameComponent_DataProcessingAllocationRegistry registry, List<Pawn> targets,
            DataProcessingDynamicTargetSettingsSnapshot snapshot, DataProcessingTargetCopyMode copyMode)
        {
            var names = new List<string>();
            foreach (var target in targets) names.Add(target.LabelShortCap.ToString());
            var text = new StringBuilder();
            text.Append("MAP_MechanoidMechanitor.DataProcessing.Dashboard.V2.PasteConfirm".Translate(string.Join("、", names)));
            text.AppendLine(L("NormalRequest") + "： " + Percent(snapshot.normalSteps));
            text.AppendLine(L("TaskMaximum") + "： " + Percent(snapshot.commonMaxSteps));
            text.AppendLine(L("SeparateMaximum") + "： " + L(snapshot.advancedMaxEnabled ? "On" : "Off"));
            string[] maximumLabels = { "SingleGeneralMax", "SingleProductionMax", "SingleFireMax", "SingleAssaultMax" };
            int[] maxima = { snapshot.generalMaxSteps, snapshot.productionMaxSteps, snapshot.fireControlMaxSteps, snapshot.assaultMaxSteps };
            for (int i = 0; i < maxima.Length; i++)
                text.AppendLine(L(maximumLabels[i]) + "： " + Percent(maxima[i]));
            if (copyMode == DataProcessingTargetCopyMode.AllSettings)
            {
                text.AppendLine(L("TargetAutomatic") + "： " + L(snapshot.enabled ? "On" : "Off"));
                text.AppendLine(L("Priority") + "： " + snapshot.priority);
                text.AppendLine(L("Interval") + "： " + snapshot.checkIntervalTicks / 60 + L("Seconds"));
                text.AppendLine(L("RuleMelee") + "： " + L(snapshot.switchForCloseMelee ? "On" : "Off"));
                text.AppendLine(L("RuleDraft") + "： " + L(snapshot.switchForDraftedWeapon ? "On" : "Off"));
                text.AppendLine(L("RuleWork") + "： " + L(snapshot.switchForWork ? "On" : "Off"));
                text.AppendLine(L("RuleFallback") + "： " + L(snapshot.applyUndraftedFallback ? "On" : "Off"));
            }
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text.ToString().TrimEnd(), () =>
            {
                FinishStepInput();
                registry.ApplyTargetSettingsBatch(overseer, targets, snapshot, copyMode);
            }));
        }
    }
}