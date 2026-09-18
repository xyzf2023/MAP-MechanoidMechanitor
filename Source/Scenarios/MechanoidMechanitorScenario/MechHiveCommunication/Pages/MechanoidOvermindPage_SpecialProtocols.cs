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
        BandwidthSupport = 3,
    }

    public sealed class MechanoidOvermindPage_SpecialProtocols
    {
        private SpecialProtocolKind expandedProtocol = SpecialProtocolKind.None;
        private MechanoidOvermindPage_BandwidthSupport? bandwidthPage;
        private Vector2 scrollPosition;

        public SpecialProtocolKind ExpandedProtocol => expandedProtocol;
        public MechanoidOvermindPage_BandwidthSupport? BandwidthPage => bandwidthPage;

        public void ResetExpansionState()
        {
            bandwidthPage?.CommitPendingEdits();
            expandedProtocol = SpecialProtocolKind.None;
            bandwidthPage = null;
            scrollPosition = Vector2.zero;
        }

        public void CollapseExpandedProtocol(
            MechClusterDeploymentOrder? clusterOrder,
            MechForceSupportOrder? forceSupportOrder)
        {
            bandwidthPage?.CommitPendingEdits();
            if (expandedProtocol == SpecialProtocolKind.None)
            {
                return;
            }

            ClearProtocolOrder(expandedProtocol, clusterOrder, forceSupportOrder);
            expandedProtocol = SpecialProtocolKind.None;
            bandwidthPage = null;
        }

        public void Draw(
            Rect inRect,
            MechClusterDeploymentOrder clusterOrder,
            MechForceSupportOrder forceSupportOrder)
        {
            MechanoidOvermindUiStyle.DrawPanel(inRect);
            Rect viewport = inRect.ContractedBy(16f);
            bool clusterAvailable = PurgeDirectiveRatingUtility.IsClusterAvailable()
                && MechClusterDeploymentService.TryResolveAvailableMap(out Map? availableClusterMap)
                && availableClusterMap != null;
            bool forceSupportAvailable = PurgeDirectiveRatingUtility.IsForceSupportAvailable()
                && MechForceSupportService.HasAvailableMap();
            bool bandwidthAvailable = GameComponent_OvermindBandwidthSupport.Available
                && GameComponent_OvermindBandwidthSupport.Current != null;
            int visibleCount = (clusterAvailable ? 1 : 0) + (forceSupportAvailable ? 1 : 0)
                + (bandwidthAvailable ? 1 : 0);
            float configHeight = expandedProtocol == SpecialProtocolKind.None ? 0f
                : expandedProtocol == SpecialProtocolKind.BandwidthSupport
                    ? (bandwidthPage?.ContentHeight ?? 150f) + 24f : 190f;
            Rect view = new Rect(0f, 0f, Mathf.Max(1f, viewport.width - 20f),
                Mathf.Max(viewport.height, 58f + visibleCount * 102f
                    + (expandedProtocol == SpecialProtocolKind.None ? 0f : configHeight + 4f)));
            Widgets.BeginScrollView(viewport, ref scrollPosition, view);
            try { DrawContent(view, configHeight, clusterOrder, forceSupportOrder); }
            finally { Widgets.EndScrollView(); }
        }

        private void DrawContent(
            Rect inner,
            float configHeight,
            MechClusterDeploymentOrder clusterOrder,
            MechForceSupportOrder forceSupportOrder)
        {
            using (MechanoidOvermindUiStyle.Push())
            {
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

                bool clusterMapAvailable =
                    MechClusterDeploymentService.TryResolveAvailableMap(out Map? clusterMap)
                    && clusterMap != null;
                bool forceSupportMapAvailable = MechForceSupportService.HasAvailableMap();

                // 只显示当前评级和地图条件均满足的协议。
                bool clusterRatingAvailable = PurgeDirectiveRatingUtility.IsClusterAvailable();
                bool forceSupportRatingAvailable = PurgeDirectiveRatingUtility.IsForceSupportAvailable();

                bool clusterAvailable = clusterMapAvailable && clusterRatingAvailable;
                bool forceSupportAvailable = forceSupportMapAvailable && forceSupportRatingAvailable;
                bool bandwidthAvailable = GameComponent_OvermindBandwidthSupport.Available
                    && GameComponent_OvermindBandwidthSupport.Current != null;

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

                if (expandedProtocol == SpecialProtocolKind.BandwidthSupport && !bandwidthAvailable)
                    CollapseExpandedProtocol(clusterOrder, forceSupportOrder);

                if (!clusterAvailable && !forceSupportAvailable && !bandwidthAvailable)
                {
                    DrawEmptyState(inner);
                    return;
                }

                const float cardHeight = 94f;
                // 本次绘制使用同一份展开状态计算位置；点击切换后，下次绘制再更新布局。
                SpecialProtocolKind layoutProtocol = expandedProtocol;
                float expandedHeight = configHeight + 12f;
                float nextY = inner.y + 58f;
                Rect NextCard(SpecialProtocolKind kind, bool available)
                {
                    if (!available) return Rect.zero;
                    Rect card = new Rect(inner.x, nextY, inner.width, cardHeight);
                    nextY += cardHeight + 8f + (layoutProtocol == kind ? expandedHeight : 0f);
                    return card;
                }
                Rect clusterCardRect = NextCard(SpecialProtocolKind.MechClusterDeployment, clusterAvailable);
                Rect forceSupportCardRect = NextCard(SpecialProtocolKind.MechForceSupport, forceSupportAvailable);
                Rect bandwidthCardRect = NextCard(SpecialProtocolKind.BandwidthSupport, bandwidthAvailable);

                string clusterMeta = clusterMap != null
                    ? "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.CardMeta"
                        .Translate(clusterMap.Parent.LabelCap)
                    : "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.ProtocolUnavailable"
                        .Translate();
                if (!clusterRatingAvailable)
                {
                    clusterMeta += "  " + "MAP_PurgeDirectiveRating.RequiredLevel".Translate(1);
                }

                if (clusterAvailable) DrawProtocolCard(
                    clusterCardRect,
                    SpecialProtocolKind.MechClusterDeployment,
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.Title",
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.Description",
                    clusterMeta,
                    clusterAvailable,
                    clusterOrder,
                    forceSupportOrder);

                string forceSupportMeta =
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.ForceSupport.CardMeta"
                        .Translate();
                if (!forceSupportRatingAvailable)
                {
                    forceSupportMeta += "  " + "MAP_PurgeDirectiveRating.RequiredLevel".Translate(1);
                }

                if (forceSupportAvailable) DrawProtocolCard(
                    forceSupportCardRect,
                    SpecialProtocolKind.MechForceSupport,
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.ForceSupport.Title",
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.ForceSupport.Description",
                    forceSupportMeta,
                    forceSupportAvailable,
                    clusterOrder,
                    forceSupportOrder);

                var bandwidth = GameComponent_OvermindBandwidthSupport.Current;
                if (bandwidthAvailable) DrawProtocolCard(
                    bandwidthCardRect,
                    SpecialProtocolKind.BandwidthSupport,
                    "MAP_BandwidthSupport.Title",
                    "MAP_BandwidthSupport.Description",
                    bandwidth != null && bandwidth.Activated
                        ? "MAP_BandwidthSupport.Allocated".Translate(bandwidth.Allocated, bandwidth.Total).ToString()
                        : "MAP_BandwidthSupport.Activate".Translate().ToString(),
                    bandwidthAvailable,
                    clusterOrder,
                    forceSupportOrder);

                if (expandedProtocol == SpecialProtocolKind.None || expandedProtocol != layoutProtocol)
                {
                    return;
                }

                Rect selectedCardRect = layoutProtocol == SpecialProtocolKind.MechClusterDeployment
                    ? clusterCardRect : layoutProtocol == SpecialProtocolKind.MechForceSupport
                        ? forceSupportCardRect : bandwidthCardRect;
                Rect configRect = new Rect(
                    inner.x,
                    selectedCardRect.yMax + 12f,
                    inner.width,
                    configHeight);
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
                else if (expandedProtocol == SpecialProtocolKind.BandwidthSupport && bandwidth != null)
                {
                    bandwidthPage ??= new MechanoidOvermindPage_BandwidthSupport(bandwidth);
                    bandwidthPage.Draw(configRect);
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
            bandwidthPage?.CommitPendingEdits();
            if (expandedProtocol == kind)
            {
                ClearProtocolOrder(kind, clusterOrder, forceSupportOrder);
                expandedProtocol = SpecialProtocolKind.None;
                bandwidthPage = null;
                return;
            }

            ClearProtocolOrder(expandedProtocol, clusterOrder, forceSupportOrder);
            bandwidthPage = null;
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
            int threatMenuMax = Mathf.Min(
                MechClusterDeploymentOrder.MaxThreatPoints,
                MechClusterDeploymentOrder.EffectiveMaxThreatPoints());
            for (int points = MechClusterDeploymentOrder.MinThreatPoints;
                points <= threatMenuMax;
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

            // 环境影响器仅在达到对应评级等级（或接管主脑）后开放。
            if (!PurgeDirectiveRatingUtility.ClusterEnvironmentAllowed())
            {
                options.Add(
                    new FloatMenuOption(
                        "MAP_PurgeDirectiveRating.ClusterEnvironmentLocked".Translate(
                            PurgeDirectiveRatingConfigDefOf.MAP_PurgeDirectiveRatingConfig
                                .mechClusterEnvironmentMinLevel),
                        () => { }));
                Find.WindowStack.Add(new FloatMenu(options));
                return;
            }

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
