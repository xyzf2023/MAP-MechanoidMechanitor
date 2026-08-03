using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed partial class Dialog_DataProcessingAllocationDashboard : Window
    {
        private void DrawTargetMonitor(
            Rect rect,
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target)
        {
            TargetSnapshot snapshot = BuildSnapshot(registry, target);
            float y = rect.y;

            DrawTargetIdentity(rect, target, snapshot.specialization, ref y);
            y += 8f;

            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.Runtime".Translate());
            y += 30f;

            string state = snapshot.dynamicManaged
                ? registry.GetCachedDynamicStateLabelForUI(target)
                : "MAP_MechanoidMechanitor.DataProcessing.MatrixFixedStatus".Translate();
            DrawInfoRow(rect, ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicCurrentState".Translate(),
                state,
                snapshot.IsLimited ? Warning : TextMain);
            DrawInfoRow(rect, ref y,
                "MAP_MechanoidMechanitor.DataProcessing.DynamicCurrentMode".Translate(),
                DataProcessingAllocationUtility.GetSpecializationLabel(snapshot.specialization),
                Accent);
            DrawInfoRow(rect, ref y,
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.QuotaSource".Translate(),
                snapshot.quotaSource,
                TextMain);

            y += 6f;
            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.Allocation".Translate());
            y += 30f;

            DrawInfoRow(rect, ref y,
                "MAP_MechanoidMechanitor.DataProcessing.MatrixActual".Translate(),
                DataProcessingAllocationUtility.StepsToPercent(snapshot.actualSteps).ToStringPercent(),
                Accent);
            DrawInfoRow(rect, ref y,
                "MAP_MechanoidMechanitor.DataProcessing.MatrixNormal".Translate(),
                DataProcessingAllocationUtility.StepsToPercent(snapshot.normalSteps).ToStringPercent(),
                TextSecondary);
            DrawInfoRow(rect, ref y,
                "MAP_MechanoidMechanitor.DataProcessing.MatrixMaximum".Translate(),
                DataProcessingAllocationUtility.StepsToPercent(snapshot.requestedSteps).ToStringPercent(),
                snapshot.IsLimited ? Warning : TextMain);

            if (snapshot.IsLimited)
            {
                Rect warningRect = new Rect(rect.x, y + 4f, rect.width, 34f);
                Widgets.DrawBoxSolid(warningRect, new Color(0.24f, 0.17f, 0.07f, 0.9f));
                GUI.color = Warning;
                Widgets.Label(
                    Inset(warningRect, 8f, 6f),
                    "MAP_MechanoidMechanitor.DataProcessing.Dashboard.Limited".Translate(
                        DataProcessingAllocationUtility.StepsToPercent(
                            snapshot.requestedSteps - snapshot.actualSteps).ToStringPercent()));
                y = warningRect.yMax + 8f;
            }
            else
            {
                y += 8f;
            }

            // 第一行：三等分按钮（编辑策略 | 复制设置 | 粘贴设置）
            float buttonGap = 8f;
            float buttonWidth = (rect.width - buttonGap * 2f) / 3f;
            bool targetIsMechanoid = target.RaceProps?.IsMechanoid == true;

            Rect editRect = new Rect(rect.x, y, buttonWidth, 32f);
            if (DrawPrimaryButton(
                    editRect,
                    "MAP_MechanoidMechanitor.DataProcessing.Dashboard.EditStrategy".Translate()))
            {
                registry.GetOrCreateDynamicTargetRecord(overseer, target);
                mode = DashboardMode.EditTarget;
                editTab = EditTab.Basic;
                detailScrollPosition = Vector2.zero;
            }

            Rect copyRect = new Rect(editRect.xMax + buttonGap, y, buttonWidth, 32f);
            if (DrawSecondaryButton(
                    copyRect,
                    "MAP_MechanoidMechanitor.DataProcessing.Dashboard.CopySettings".Translate(),
                    enabled: targetIsMechanoid))
            {
                DataProcessingDynamicTargetRecord config =
                    registry.GetOrCreateDynamicTargetRecord(overseer, target);

                List<FloatMenuOption> options = new List<FloatMenuOption>
                {
                    new FloatMenuOption(
                        "MAP_MechanoidMechanitor.DataProcessing.Dashboard.CopyQuotaOnly".Translate(),
                        () =>
                        {
                            settingsClipboard =
                                DataProcessingDynamicTargetSettingsSnapshot.Capture(config);
                            settingsClipboardSource = target;
                            settingsClipboardMode = DataProcessingTargetCopyMode.QuotaOnly;
                        }),
                    new FloatMenuOption(
                        "MAP_MechanoidMechanitor.DataProcessing.Dashboard.CopyAllSettings".Translate(),
                        () =>
                        {
                            settingsClipboard =
                                DataProcessingDynamicTargetSettingsSnapshot.Capture(config);
                            settingsClipboardSource = target;
                            settingsClipboardMode = DataProcessingTargetCopyMode.AllSettings;
                        })
                };
                Find.WindowStack.Add(new FloatMenu(options));
            }

            bool canPaste =
                targetIsMechanoid
                && settingsClipboard != null
                && settingsClipboardSource != null
                && settingsClipboardSource.RaceProps?.IsMechanoid == true
                && !ReferenceEquals(settingsClipboardSource, target);
            Rect pasteRect = new Rect(copyRect.xMax + buttonGap, y, buttonWidth, 32f);
            if (DrawFlatButton(
                    pasteRect,
                    "MAP_MechanoidMechanitor.DataProcessing.Dashboard.PasteSettings".Translate(),
                    DashboardButtonStyle.Primary,
                    selected: false,
                    enabled: canPaste))
            {
                if (canPaste
                    && registry.ApplyDynamicTargetSettingsSnapshot(
                        overseer,
                        target,
                        settingsClipboard,
                        settingsClipboardMode))
                {
                    string modeLabel =
                        settingsClipboardMode == DataProcessingTargetCopyMode.AllSettings
                            ? "MAP_MechanoidMechanitor.DataProcessing.Dashboard.CopyModeAll".Translate()
                            : "MAP_MechanoidMechanitor.DataProcessing.Dashboard.CopyModeQuota".Translate();
                    Messages.Message(
                        "MAP_MechanoidMechanitor.DataProcessing.Dashboard.PasteComplete".Translate(
                            settingsClipboardSource!.LabelShort,
                            modeLabel,
                            target.LabelShort),
                        MessageTypeDefOf.NeutralEvent,
                        historical: false);
                }
            }

            y += 40f;

            // 第二行：顶置按钮 + 复制状态
            bool showPin = !ReferenceEquals(target, overseer);
            float rowStartX = rect.x;
            if (showPin)
            {
                bool pinned = registry.IsPinned(overseer, target);
                Rect pinRect = new Rect(rowStartX, y, 124f, 30f);
                if (DrawSecondaryButton(
                        pinRect,
                        pinned
                            ? "MAP_MechanoidMechanitor.DataProcessing.Dashboard.Unpin".Translate()
                            : "MAP_MechanoidMechanitor.DataProcessing.Dashboard.Pin".Translate(),
                        selected: pinned))
                {
                    if (pinned)
                    {
                        registry.TryUnpinTarget(overseer, target);
                    }
                    else
                    {
                        registry.TryPinTarget(overseer, target);
                    }
                }
                rowStartX = pinRect.xMax + 8f;
            }

            if (settingsClipboardSource != null)
            {
                string modeLabel =
                    settingsClipboardMode == DataProcessingTargetCopyMode.AllSettings
                        ? "MAP_MechanoidMechanitor.DataProcessing.Dashboard.CopyModeAll".Translate()
                        : "MAP_MechanoidMechanitor.DataProcessing.Dashboard.CopyModeQuota".Translate();
                string status =
                    "MAP_MechanoidMechanitor.DataProcessing.Dashboard.ClipboardStatus".Translate(
                        settingsClipboardSource.LabelShort,
                        modeLabel);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = TextSecondary;
                Widgets.Label(
                    new Rect(rowStartX, y, Mathf.Max(0f, rect.xMax - rowStartX - 70f), 30f),
                    status);
                Text.Anchor = TextAnchor.UpperLeft;

                Rect clearRect = new Rect(rect.xMax - 62f, y, 62f, 28f);
                if (DrawMiniButton(
                        clearRect,
                        "MAP_MechanoidMechanitor.DataProcessing.Dashboard.ClearClipboard".Translate()))
                {
                    settingsClipboard = null;
                    settingsClipboardSource = null;
                }
            }
            else
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = TextSecondary;
                Widgets.Label(
                    new Rect(rowStartX, y, rect.width - (rowStartX - rect.x), 30f),
                    "MAP_MechanoidMechanitor.DataProcessing.Dashboard.NoClipboard".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
            }
            y += 40f;

            Rect scrollRect = new Rect(rect.x, y, rect.width, rect.yMax - y);
            float viewHeight = Prefs.DevMode ? 640f : 460f;
            Rect viewRect = new Rect(0f, 0f, scrollRect.width - 16f, Mathf.Max(viewHeight, scrollRect.height));
            Widgets.BeginScrollView(scrollRect, ref detailScrollPosition, viewRect);
            try
            {
                float innerY = 0f;
                DrawPermissionsSection(viewRect, snapshot.actualSteps, ref innerY);
                innerY += 12f;
                DrawSpecializationEffectsSection(
                    viewRect,
                    snapshot.actualSteps,
                    snapshot.specialization,
                    ref innerY);

                if (Prefs.DevMode)
                {
                    innerY += 12f;
                    DrawWorkRecognitionDiagnostic(viewRect, target, ref innerY);
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private static void DrawTargetIdentity(
            Rect rect,
            Pawn target,
            DataProcessingSpecialization specialization,
            ref float y)
        {
            Rect portraitRect = new Rect(rect.x, y, 60f, 60f);
            DrawPortrait(portraitRect, target);

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = TextMain;
            Widgets.Label(
                new Rect(portraitRect.xMax + 12f, y + 3f, rect.width - 72f, 30f),
                target.LabelShortCap);
            Text.Font = GameFont.Small;
            GUI.color = Accent;
            Widgets.Label(
                new Rect(portraitRect.xMax + 12f, y + 36f, rect.width - 72f, Text.LineHeight),
                DataProcessingAllocationUtility.GetSpecializationLabel(specialization));
            y = portraitRect.yMax;
        }

        private static void DrawPermissionsSection(Rect rect, int steps, ref float y)
        {
            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.Permissions".Translate());
            y += 30f;

            List<DataProcessingEffectDisplayEntry> permissions =
                DataProcessingAllocationEffectDisplayUtility.BuildFunctionalPermissions(steps);
            for (int i = 0; i < permissions.Count; i++)
            {
                DataProcessingEffectDisplayEntry entry = permissions[i];
                Rect lineRect = new Rect(rect.x, y, rect.width, 28f);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = entry.available ? Accent : TextSecondary;
                Widgets.Label(
                    lineRect,
                    (entry.available ? "✓  " : "○  ") + entry.label);
                if (!entry.tooltip.NullOrEmpty())
                {
                    TooltipHandler.TipRegion(lineRect, entry.tooltip);
                }
                y += 30f;
            }
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawSpecializationEffectsSection(
            Rect rect,
            int steps,
            DataProcessingSpecialization specialization,
            ref float y)
        {
            DrawSectionTitle(
                new Rect(rect.x, y, rect.width, 24f),
                "MAP_MechanoidMechanitor.DataProcessing.Dashboard.SpecializationEffects".Translate(
                    DataProcessingAllocationUtility.GetSpecializationLabel(specialization)));
            y += 30f;

            List<DataProcessingEffectDisplayEntry> effects =
                DataProcessingAllocationEffectDisplayUtility.BuildSpecializationEffects(
                    steps,
                    specialization);
            for (int i = 0; i < effects.Count; i++)
            {
                Rect lineRect = new Rect(rect.x, y, rect.width, 26f);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = TextMain;
                Widgets.Label(lineRect, "•  " + effects[i].label);
                y += 28f;
            }
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
