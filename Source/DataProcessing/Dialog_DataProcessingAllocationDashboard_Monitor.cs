using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed partial class Dialog_DataProcessingAllocationDashboard
    {
        private void DrawTargetMonitor(Rect rect, GameComponent_DataProcessingAllocationRegistry registry, Pawn target)
        {
            var snapshot = registry.GetTargetSnapshotForUI(overseer, target);
            DrawPortrait(new Rect(rect.x, rect.y, 36f, 36f), target);
            Text.Font = GameFont.Small; GUI.color = TextMain;
            Widgets.Label(new Rect(rect.x + 44f, rect.y, rect.width - 120f, 30f), target.LabelShortCap);
            if (DrawSecondaryButton(new Rect(rect.xMax - 72f, rect.y, 72f, ControlHeight), L("More")))
                OpenTargetActions(registry, target);
            float y = rect.y + 40f;
            Paragraph(rect, ref y, L("ActualRequest") + "  " + Percent(snapshot.actualSteps) + " / "
                + (snapshot.evaluationPending ? L("Pending") : Percent(snapshot.requestedSteps)),
                snapshot.IsLimited ? Warning : Accent);
            string source = !snapshot.dynamicManaged ? L("Fixed") : snapshot.taskActive
                ? (snapshot.config?.advancedMaxEnabled == true ? L("SeparateMaximum") : L("TaskMaximum"))
                : L("NormalRequest");
            string state = snapshot.frozenSelf || snapshot.dynamicManaged ? registry.GetCachedDynamicStateLabelForUI(target) : L("Fixed");
            string runtime = state + " → " + DataProcessingAllocationUtility.GetSpecializationLabel(snapshot.specialization)
                + " · " + source;
            // 详细因果及收益在同一可滚动区域；固定头部不会吞掉小窗口内容。
            var scroll = new Rect(rect.x, y, rect.width, Mathf.Max(1f, rect.yMax - y));
            inputScope = "Target:" + target.thingIDNumber;
            DrawScrollable(scroll, "Target", view =>
            {
                float innerY = 0f;
                Paragraph(view, ref innerY, runtime, TextSecondary);
                if (snapshot.evaluationPending)
                {
                    Paragraph(view, ref innerY, L("PendingHelp"), Warning);
                    return innerY;
                }
                else if (snapshot.IsLimited)
                {
                    float startY = innerY;
                    Paragraph(view, ref innerY, L("Missing") + Percent(snapshot.requestedSteps - snapshot.actualSteps), Warning);
                    TooltipHandler.TipRegion(new Rect(view.x, startY, view.width, innerY - startY),
                        L(snapshot.dynamicManaged ? "DynamicLimited" : "FixedLimited"));
                    if (DrawSecondaryButton(new Rect(view.x, innerY, view.width, ControlHeight), L("BudgetPage"))) OpenPage(DashboardMode.GlobalSettings);
                    innerY += ControlRow;
                }
                DrawBasicEdit(view, ref innerY, registry, target, snapshot);
                return innerY;
            });
        }

        private void DrawCurrentEffects(Rect rect, ref float y, GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target, DataProcessingTargetUISnapshot snapshot)
        {
            Section(rect, ref y, L("Effects"));
            var permissions = DataProcessingAllocationEffectDisplayUtility.BuildFunctionalPermissions(snapshot.actualSteps);
            var effects = DataProcessingAllocationEffectDisplayUtility.BuildSpecializationEffects(snapshot.actualSteps, snapshot.specialization);
            foreach (var effect in effects)
                Paragraph(rect, ref y, effect.label, Accent);
            foreach (var entry in permissions)
            {
                float start = y;
                Paragraph(rect, ref y, (entry.available ? "✓ " : "○ ") + entry.label, entry.available ? Accent : TextSecondary);
                TooltipHandler.TipRegion(new Rect(rect.x, start, rect.width, y - start), entry.tooltip);
            }

            if (snapshot.actualSteps >= DataProcessingAllocationUtility.MaxSpecializationEffectSteps)
                Paragraph(rect, ref y, L("EffectCap"), TextSecondary);
        }

        private void OpenTargetActions(GameComponent_DataProcessingAllocationRegistry registry, Pawn target)
        {
            FinishStepInput();
            var options = new List<FloatMenuOption>();
            if (target.RaceProps?.IsMechanoid == true)
            {
                options.Add(new FloatMenuOption(L("CopyQuota"), () => CaptureSettings(registry, target, DataProcessingTargetCopyMode.QuotaOnly)));
                options.Add(new FloatMenuOption(L("CopyAll"), () => CaptureSettings(registry, target, DataProcessingTargetCopyMode.AllSettings)));
                if (settingsClipboard != null && !DataProcessingOverseerResolver.IsFrozenSelf(overseer, target))
                    options.Add(new FloatMenuOption(L("Paste"), () => ConfirmBatch(registry, new List<Pawn> { target },
                        settingsClipboard!, settingsClipboardMode)));
            }
            if (options.Count == 0) options.Add(new FloatMenuOption(L("NoActions"), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }
        private string clipboardLabel = string.Empty;
        private void CaptureSettings(GameComponent_DataProcessingAllocationRegistry registry, Pawn target, DataProcessingTargetCopyMode copyMode)
        {
            if (target == null || target.Dead || target.Destroyed || !registry.IsValidAllocationPairForList(overseer, target)) return;
            var config = registry.GetOrCreateDynamicTargetRecord(overseer, target);
            settingsClipboard = DataProcessingDynamicTargetSettingsSnapshot.Capture(config);
            settingsClipboardMode = copyMode;
            settingsClipboardSource = target;
            clipboardLabel = target.LabelShortCap;
            Messages.Message(L("Copied") + clipboardLabel + " · " + L(copyMode == DataProcessingTargetCopyMode.QuotaOnly ? "CopyQuota" : "CopyAll"),
                MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
