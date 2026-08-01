using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 特殊协议页面级展开标识；以后新增协议时在此扩展即可。
    /// </summary>
    public enum SpecialProtocolKind
    {
        None = 0,
        MechClusterDeployment = 1,
        MechForceSupport = 2,
    }

    public sealed class MechanoidOvermindPage_SpecialProtocols
    {
        private SpecialProtocolKind expandedProtocol = SpecialProtocolKind.None;

        public SpecialProtocolKind ExpandedProtocol => expandedProtocol;

        public void ResetExpansionState()
        {
            expandedProtocol = SpecialProtocolKind.None;
        }

        public void CollapseExpandedProtocol(
            MechClusterDeploymentOrder? clusterOrder,
            MechForceSupportOrder? forceSupportOrder)
        {
            if (expandedProtocol == SpecialProtocolKind.None)
            {
                return;
            }

            ClearProtocolOrder(expandedProtocol, clusterOrder, forceSupportOrder);
            expandedProtocol = SpecialProtocolKind.None;
        }

        public void Draw(
            Rect inRect,
            MechClusterDeploymentOrder clusterOrder,
            MechForceSupportOrder forceSupportOrder)
        {
            using (MechanoidOvermindUiStyle.Push())
            {
                MechanoidOvermindUiStyle.DrawPanel(inRect);
                Rect inner = inRect.ContractedBy(16f);

                MechanoidOvermindUiStyle.DrawLabel(
                    new Rect(inner.x, inner.y, inner.width, 24f),
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Body".Translate(),
                    GameFont.Small,
                    TextAnchor.MiddleLeft,
                    MechanoidOvermindUiStyle.AccentBright);

                MechanoidOvermindUiStyle.DrawSecondaryLabel(
                    new Rect(inner.x, inner.y + 26f, inner.width, 20f),
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Description"
                        .Translate());

                bool clusterAvailable =
                    MechClusterDeploymentService.TryResolveAvailableMap(out Map? clusterMap)
                    && clusterMap != null;
                bool forceSupportAvailable = MechForceSupportService.HasAvailableMap();

                if (expandedProtocol == SpecialProtocolKind.MechClusterDeployment
                    && !clusterAvailable)
                {
                    ClearProtocolOrder(
                        expandedProtocol,
                        clusterOrder,
                        forceSupportOrder);
                    expandedProtocol = SpecialProtocolKind.None;
                }
                else if (expandedProtocol == SpecialProtocolKind.MechForceSupport
                    && !forceSupportAvailable)
                {
                    ClearProtocolOrder(
                        expandedProtocol,
                        clusterOrder,
                        forceSupportOrder);
                    expandedProtocol = SpecialProtocolKind.None;
                }

                if (!clusterAvailable && !forceSupportAvailable)
                {
                    DrawEmptyState(inner);
                    return;
                }

                float cardsHeight = Mathf.Min(
                    202f,
                    Mathf.Max(190f, inner.height * 0.38f));
                float cardHeight = (cardsHeight - 8f) * 0.5f;
                Rect clusterCardRect = new Rect(
                    inner.x,
                    inner.y + 58f,
                    inner.width,
                    cardHeight);
                Rect forceSupportCardRect = new Rect(
                    inner.x,
                    clusterCardRect.yMax + 8f,
                    inner.width,
                    cardHeight);

                string clusterMeta = clusterMap != null
                    ? "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.CardMeta"
                        .Translate(clusterMap.Parent.LabelCap)
                    : "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.ProtocolUnavailable"
                        .Translate();
                DrawProtocolCard(
                    clusterCardRect,
                    SpecialProtocolKind.MechClusterDeployment,
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.Title",
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.Description",
                    clusterMeta,
                    clusterAvailable,
                    clusterOrder,
                    forceSupportOrder);

                DrawProtocolCard(
                    forceSupportCardRect,
                    SpecialProtocolKind.MechForceSupport,
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.ForceSupport.Title",
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.ForceSupport.Description",
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.ForceSupport.CardMeta"
                        .Translate(),
                    forceSupportAvailable,
                    clusterOrder,
                    forceSupportOrder);

                if (expandedProtocol == SpecialProtocolKind.None)
                {
                    return;
                }

                Rect configRect = new Rect(
                    inner.x,
                    forceSupportCardRect.yMax + 12f,
                    inner.width,
                    Mathf.Max(96f, inner.yMax - forceSupportCardRect.yMax - 12f));
                if (expandedProtocol == SpecialProtocolKind.MechClusterDeployment
                    && clusterMap != null)
                {
                    clusterOrder.SanitizeConditionCauser();
                    DrawClusterConfiguration(configRect, clusterOrder, clusterMap);
                }
                else if (expandedProtocol == SpecialProtocolKind.MechForceSupport)
                {
                    DrawForceSupportConfiguration(configRect, forceSupportOrder);
                }
            }
        }

        private static void DrawEmptyState(Rect inner)
        {
            MechanoidOvermindUiStyle.DrawPanel(
                new Rect(inner.x, inner.y + 60f, inner.width, 72f),
                alt: true,
                cornerMarks: false);
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(inner.x + 12f, inner.y + 68f, inner.width - 24f, 56f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Empty"
                    .Translate(),
                GameFont.Small,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.TextSecondary,
                wordWrap: true);
        }

        private void DrawProtocolCard(
            Rect rect,
            SpecialProtocolKind kind,
            string titleKey,
            string descriptionKey,
            string meta,
            bool enabled,
            MechClusterDeploymentOrder clusterOrder,
            MechForceSupportOrder forceSupportOrder)
        {
            bool expanded = expandedProtocol == kind;
            Widgets.DrawBoxSolid(
                rect,
                expanded
                    ? MechanoidOvermindUiStyle.NavSelectedFill
                    : MechanoidOvermindUiStyle.PanelAlt);
            MechanoidOvermindUiStyle.DrawBorder(rect);
            Widgets.DrawBoxSolid(
                new Rect(rect.x, rect.y + 4f, 4f, rect.height - 8f),
                expanded
                    ? MechanoidOvermindUiStyle.AccentBright
                    : MechanoidOvermindUiStyle.Accent);

            Color titleColor = !enabled
                ? MechanoidOvermindUiStyle.TextSecondary
                : expanded
                    ? MechanoidOvermindUiStyle.AccentBright
                    : MechanoidOvermindUiStyle.TextPrimary;
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.x + 14f, rect.y + 7f, rect.width - 28f, 22f),
                titleKey.Translate(),
                GameFont.Small,
                TextAnchor.MiddleLeft,
                titleColor);
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.x + 14f, rect.y + 30f, rect.width - 28f, 32f),
                descriptionKey.Translate(),
                GameFont.Tiny,
                TextAnchor.UpperLeft,
                MechanoidOvermindUiStyle.TextSecondary,
                wordWrap: true);
            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(rect.x + 14f, rect.yMax - 23f, rect.width - 28f, 18f),
                meta);

            if (enabled && Mouse.IsOver(rect))
            {
                Widgets.DrawBoxSolid(
                    rect,
                    new Color(
                        MechanoidOvermindUiStyle.AccentBright.r,
                        MechanoidOvermindUiStyle.AccentBright.g,
                        MechanoidOvermindUiStyle.AccentBright.b,
                        0.06f));
            }

            if (enabled && Widgets.ButtonInvisible(rect))
            {
                ToggleProtocol(kind, clusterOrder, forceSupportOrder);
            }
        }

        private void ToggleProtocol(
            SpecialProtocolKind kind,
            MechClusterDeploymentOrder clusterOrder,
            MechForceSupportOrder forceSupportOrder)
        {
            if (expandedProtocol == kind)
            {
                ClearProtocolOrder(kind, clusterOrder, forceSupportOrder);
                expandedProtocol = SpecialProtocolKind.None;
                return;
            }

            ClearProtocolOrder(expandedProtocol, clusterOrder, forceSupportOrder);
            expandedProtocol = kind;
        }

        private static void ClearProtocolOrder(
            SpecialProtocolKind kind,
            MechClusterDeploymentOrder? clusterOrder,
            MechForceSupportOrder? forceSupportOrder)
        {
            if (kind == SpecialProtocolKind.MechClusterDeployment)
            {
                clusterOrder?.Clear();
            }
            else if (kind == SpecialProtocolKind.MechForceSupport)
            {
                forceSupportOrder?.Clear();
            }
        }

        private static void DrawClusterConfiguration(
            Rect rect,
            MechClusterDeploymentOrder order,
            Map map)
        {
            MechanoidOvermindUiStyle.DrawPanel(rect, alt: true, cornerMarks: false);
            Rect inner = rect.ContractedBy(12f);

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(inner.x, inner.y, inner.width, 22f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.Configuration"
                    .Translate(),
                GameFont.Small,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.AccentBright);

            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(inner.x, inner.y + 28f, inner.width, 20f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.Map"
                    .Translate(map.Parent.LabelCap));

            Rect threatSelectorRect = new Rect(
                inner.x,
                inner.y + 54f,
                Mathf.Min(430f, inner.width),
                30f);
            if (MechanoidOvermindUiStyle.DrawActionButton(
                    threatSelectorRect,
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.Threat"
                        .Translate(order.ThreatPoints)))
            {
                OpenThreatPointsMenu(order);
            }

            string conditionLabel = order.ConditionCauser?.LabelCap
                ?? "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.NoConditionCauser"
                    .Translate();
            Rect selectorRect = new Rect(
                inner.x,
                threatSelectorRect.yMax + 8f,
                Mathf.Min(430f, inner.width),
                30f);
            if (MechanoidOvermindUiStyle.DrawActionButton(
                    selectorRect,
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.ConditionCauser"
                        .Translate(conditionLabel)))
            {
                OpenConditionCauserMenu(order);
            }

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(
                    selectorRect.xMax + 12f,
                    threatSelectorRect.y,
                    inner.xMax - selectorRect.xMax - 12f,
                    threatSelectorRect.height + 8f + selectorRect.height),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.Cost"
                    .Translate(order.Cost),
                GameFont.Small,
                TextAnchor.MiddleRight,
                MechanoidOvermindUiStyle.AccentBright);

            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(
                    inner.x,
                    selectorRect.yMax + 8f,
                    inner.width,
                    Mathf.Max(20f, inner.yMax - selectorRect.yMax - 8f)),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.Footnote"
                    .Translate(),
                TextAnchor.UpperLeft);
        }

        private static void DrawForceSupportConfiguration(
            Rect rect,
            MechForceSupportOrder order)
        {
            MechanoidOvermindUiStyle.DrawPanel(rect, alt: true, cornerMarks: false);
            Rect inner = rect.ContractedBy(12f);
            MechForceSupportService.SanitizeSelectedTemplate(order, null);

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(inner.x, inner.y, inner.width, 22f),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.ForceSupport.Configuration"
                    .Translate(),
                GameFont.Small,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.AccentBright);

            Rect pointsLabelRect = new Rect(
                inner.x,
                inner.y + 34f,
                78f,
                30f);
            MechanoidOvermindUiStyle.DrawLabel(
                pointsLabelRect,
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.ForceSupport.Points"
                    .Translate(),
                GameFont.Small,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.TextPrimary);

            Rect pointsFieldRect = new Rect(
                pointsLabelRect.xMax + 8f,
                pointsLabelRect.y,
                Mathf.Min(240f, Mathf.Max(80f, inner.width * 0.34f)),
                pointsLabelRect.height);
            order.DrawThreatPointsField(pointsFieldRect);

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(
                    pointsFieldRect.xMax + 12f,
                    pointsFieldRect.y,
                    Mathf.Max(0f, inner.xMax - pointsFieldRect.xMax - 12f),
                    pointsFieldRect.height),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.ForceSupport.Cost"
                    .Translate(order.Cost),
                GameFont.Small,
                TextAnchor.MiddleRight,
                MechanoidOvermindUiStyle.AccentBright);

            Rect templateButtonRect = new Rect(
                inner.x,
                pointsFieldRect.yMax + 8f,
                Mathf.Min(430f, inner.width),
                30f);
            string templateFullLabel =
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.ForceSupport.Template"
                    .Translate(order.GetTemplateLabel());
            Text.Font = GameFont.Small;
            string templateButtonLabel = templateFullLabel.Truncate(
                Mathf.Max(40f, templateButtonRect.width - 16f));
            if (MechanoidOvermindUiStyle.DrawActionButton(
                    templateButtonRect,
                    templateButtonLabel))
            {
                OpenForceSupportTemplateMenu(order);
            }

            TooltipHandler.TipRegion(templateButtonRect, templateFullLabel);

            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(
                    inner.x,
                    templateButtonRect.yMax + 8f,
                    inner.width,
                    Mathf.Max(20f, inner.yMax - templateButtonRect.yMax - 8f)),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.ForceSupport.Footnote"
                    .Translate(),
                TextAnchor.UpperLeft);
        }

        private static void OpenForceSupportTemplateMenu(MechForceSupportOrder order)
        {
            List<(PawnGroupMaker? maker, string label, string fullLabel)> entries =
                MechForceSupportService.BuildTemplateMenuEntries(order, null);
            List<FloatMenuOption> options = new List<FloatMenuOption>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                PawnGroupMaker? maker = entries[i].maker;
                string label = entries[i].label;
                string fullLabel = entries[i].fullLabel;
                bool selected = order.SelectedGroupMaker == maker;
                FloatMenuOption option = new FloatMenuOption(
                    (selected ? "● " : string.Empty) + label,
                    () => order.SetSelectedGroupMaker(maker))
                {
                    tooltip = fullLabel
                };
                options.Add(option);
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void OpenThreatPointsMenu(MechClusterDeploymentOrder order)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int points = MechClusterDeploymentOrder.MinThreatPoints;
                points <= MechClusterDeploymentOrder.MaxThreatPoints;
                points += MechClusterDeploymentOrder.ThreatPointsStep)
            {
                int captured = points;
                string label = (order.ThreatPoints == captured ? "● " : string.Empty)
                    + "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.ThreatOption"
                        .Translate(
                            captured,
                            MechClusterDeploymentOrder.ComputeCost(captured, false));
                options.Add(new FloatMenuOption(
                    label,
                    () => order.SetThreatPoints(captured)));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void OpenConditionCauserMenu(MechClusterDeploymentOrder order)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    (order.ConditionCauser == null ? "● " : string.Empty)
                        + "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.NoConditionCauser"
                            .Translate(),
                    () => order.SetConditionCauser(null))
            };

            List<ThingDef> defs = MechClusterDeploymentService.GetConditionCausers(
                order.ThreatPoints);
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                ThingDef capturedDef = def;
                string label = (order.ConditionCauser == def ? "● " : string.Empty)
                    + def.LabelCap;
                options.Add(
                    new FloatMenuOption(
                        label,
                        () => order.SetConditionCauser(capturedDef),
                        capturedDef));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
