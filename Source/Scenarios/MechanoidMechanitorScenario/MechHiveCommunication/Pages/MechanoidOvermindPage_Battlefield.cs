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
    }

    public sealed class MechanoidOvermindPage_Battlefield
    {
        private SpecialProtocolKind expandedProtocol = SpecialProtocolKind.None;

        public SpecialProtocolKind ExpandedProtocol => expandedProtocol;

        public void ResetExpansionState()
        {
            expandedProtocol = SpecialProtocolKind.None;
        }

        public void CollapseExpandedProtocol(MechClusterDeploymentOrder order)
        {
            if (expandedProtocol == SpecialProtocolKind.None)
            {
                return;
            }

            expandedProtocol = SpecialProtocolKind.None;
            order.Clear();
        }

        public void Draw(Rect inRect, MechClusterDeploymentOrder order)
        {
            using (MechanoidOvermindUiStyle.Push())
            {
                MechanoidOvermindUiStyle.DrawPanel(inRect);
                Rect inner = inRect.ContractedBy(16f);

                MechanoidOvermindUiStyle.DrawLabel(
                    new Rect(inner.x, inner.y, inner.width, 24f),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Body".Translate(),
                    GameFont.Small,
                    TextAnchor.MiddleLeft,
                    MechanoidOvermindUiStyle.AccentBright);

                MechanoidOvermindUiStyle.DrawSecondaryLabel(
                    new Rect(inner.x, inner.y + 26f, inner.width, 20f),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Description"
                        .Translate());

                if (!MechClusterDeploymentService.TryResolveAvailableMap(out Map? map)
                    || map == null)
                {
                    MechanoidOvermindUiStyle.DrawPanel(
                        new Rect(inner.x, inner.y + 60f, inner.width, 72f),
                        alt: true,
                        cornerMarks: false);
                    MechanoidOvermindUiStyle.DrawLabel(
                        new Rect(inner.x + 12f, inner.y + 68f, inner.width - 24f, 56f),
                        "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Empty"
                            .Translate(),
                        GameFont.Small,
                        TextAnchor.MiddleLeft,
                        MechanoidOvermindUiStyle.TextSecondary,
                        wordWrap: true);
                    return;
                }

                Rect cardRect = new Rect(
                    inner.x,
                    inner.y + 58f,
                    inner.width,
                    Mathf.Min(118f, Mathf.Max(88f, inner.height * 0.22f)));
                bool clusterExpanded =
                    expandedProtocol == SpecialProtocolKind.MechClusterDeployment;
                DrawClusterCard(cardRect, order, map, clusterExpanded);

                if (!clusterExpanded)
                {
                    return;
                }

                order.SanitizeConditionCauser();

                Rect configRect = new Rect(
                    inner.x,
                    cardRect.yMax + 12f,
                    inner.width,
                    Mathf.Max(120f, inner.yMax - cardRect.yMax - 12f));
                DrawClusterConfiguration(configRect, order, map);
            }
        }

        private void DrawClusterCard(
            Rect rect,
            MechClusterDeploymentOrder order,
            Map map,
            bool expanded)
        {
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

            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.x + 14f, rect.y + 10f, rect.width - 28f, 24f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Title"
                    .Translate(),
                GameFont.Medium,
                TextAnchor.MiddleLeft,
                expanded
                    ? MechanoidOvermindUiStyle.AccentBright
                    : MechanoidOvermindUiStyle.TextPrimary);
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(rect.x + 14f, rect.y + 38f, rect.width - 28f, 38f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Description"
                    .Translate(),
                GameFont.Small,
                TextAnchor.UpperLeft,
                MechanoidOvermindUiStyle.TextSecondary,
                wordWrap: true);
            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(rect.x + 14f, rect.yMax - 27f, rect.width - 28f, 20f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.CardMeta"
                    .Translate(map.Parent.LabelCap));

            if (Mouse.IsOver(rect))
            {
                Widgets.DrawBoxSolid(
                    rect,
                    new Color(
                        MechanoidOvermindUiStyle.AccentBright.r,
                        MechanoidOvermindUiStyle.AccentBright.g,
                        MechanoidOvermindUiStyle.AccentBright.b,
                        0.06f));
            }

            if (Widgets.ButtonInvisible(rect))
            {
                ToggleProtocol(SpecialProtocolKind.MechClusterDeployment, order);
            }
        }

        private void ToggleProtocol(
            SpecialProtocolKind kind,
            MechClusterDeploymentOrder order)
        {
            if (expandedProtocol == kind)
            {
                expandedProtocol = SpecialProtocolKind.None;
                order.Clear();
                return;
            }

            if (expandedProtocol != SpecialProtocolKind.None)
            {
                // 切换协议时先清理当前协议临时状态；目前仅有集群部署。
                order.Clear();
            }

            expandedProtocol = kind;
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
                "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Configuration"
                    .Translate(),
                GameFont.Small,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.AccentBright);

            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(inner.x, inner.y + 28f, inner.width, 20f),
                "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Map"
                    .Translate(map.Parent.LabelCap));

            Rect threatSelectorRect = new Rect(
                inner.x,
                inner.y + 54f,
                Mathf.Min(430f, inner.width),
                30f);
            if (MechanoidOvermindUiStyle.DrawActionButton(
                    threatSelectorRect,
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Threat"
                        .Translate(order.ThreatPoints)))
            {
                OpenThreatPointsMenu(order);
            }

            string conditionLabel = order.ConditionCauser?.LabelCap
                ?? "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.NoConditionCauser"
                    .Translate();
            Rect selectorRect = new Rect(
                inner.x,
                threatSelectorRect.yMax + 8f,
                Mathf.Min(430f, inner.width),
                30f);
            if (MechanoidOvermindUiStyle.DrawActionButton(
                    selectorRect,
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.ConditionCauser"
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
                "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Cost"
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
                "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.Footnote"
                    .Translate(),
                TextAnchor.UpperLeft);
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
                    + "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.ThreatOption"
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
                        + "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Cluster.NoConditionCauser"
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
