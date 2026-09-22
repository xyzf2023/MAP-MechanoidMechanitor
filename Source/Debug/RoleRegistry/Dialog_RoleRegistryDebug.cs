using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 开发者模式角色注册表管理窗口：查看/删除机械族机械师、仿生伴侣、飞行授权、
    /// 合体资格与独立自律授权记录。
    /// </summary>
    public sealed class Dialog_RoleRegistryDebug : Window
    {
        private enum Tab
        {
            MechanoidMechanitor,
            SyntheticCompanion,
            MechanicalFlight,
            MechFusion,
            AutonomousMech
        }

        private const float TitleHeight = 32f;
        private const float RefreshButtonWidth = 72f;
        private const float RefreshButtonHeight = 28f;
        private const float RowHeight = 52f;
        private const float DeleteButtonWidth = 64f;

        private Tab currentTab = Tab.MechanoidMechanitor;
        private Vector2 scrollPosition;
        private readonly List<TabRecord> tabs = new List<TabRecord>();
        private readonly List<MechanoidMechanitorRegistrySnapshotEntry> mechanitorRows =
            new List<MechanoidMechanitorRegistrySnapshotEntry>();
        private readonly List<SyntheticCompanionAuthorizationRecord> companionRows =
            new List<SyntheticCompanionAuthorizationRecord>();
        private readonly List<MechanicalFlightAuthorizationRecord> flightRows =
            new List<MechanicalFlightAuthorizationRecord>();
        private readonly List<MechFusionEligibilityRecord> fusionRows =
            new List<MechFusionEligibilityRecord>();
        private readonly List<AutonomousMechAuthorizationRecord> autonomyRows =
            new List<AutonomousMechAuthorizationRecord>();

        public override Vector2 InitialSize => new Vector2(720f, 560f);

        public Dialog_RoleRegistryDebug()
        {
            forcePause = false;
            doCloseButton = true;
            doCloseX = true;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            EnsureTabs();
            RefreshSnapshots();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;

            try
            {
                Rect contentRect = inRect;
                contentRect.yMax -= CloseButSize.y + 8f;

                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(
                    new Rect(
                        contentRect.x,
                        contentRect.y,
                        contentRect.width - RefreshButtonWidth - 8f,
                        TitleHeight),
                    "角色注册表");

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Rect refreshRect = new Rect(
                    contentRect.xMax - RefreshButtonWidth,
                    contentRect.y + (TitleHeight - RefreshButtonHeight) / 2f,
                    RefreshButtonWidth,
                    RefreshButtonHeight);
                if (Widgets.ButtonText(refreshRect, "刷新"))
                {
                    scrollPosition = Vector2.zero;
                    RefreshSnapshots();
                }

                // TabDrawer 会把分页绘制在 sectionRect 上方 32px。
                float curY = contentRect.y + TitleHeight + 4f + TabDrawer.TabHeight;
                Rect sectionRect = new Rect(
                    contentRect.x,
                    curY,
                    contentRect.width,
                    Mathf.Max(0f, contentRect.yMax - curY));
                Widgets.DrawMenuSection(sectionRect);
                UpdateTabLabels();
                TabDrawer.DrawTabs(sectionRect, tabs);

                Rect listRect = sectionRect.ContractedBy(12f);
                if (currentTab == Tab.MechanoidMechanitor)
                {
                    DrawMechanitorTab(listRect);
                }
                else if (currentTab == Tab.SyntheticCompanion)
                {
                    DrawCompanionTab(listRect);
                }
                else if (currentTab == Tab.MechanicalFlight)
                {
                    DrawFlightTab(listRect);
                }
                else if (currentTab == Tab.AutonomousMech)
                {
                    DrawAutonomyTab(listRect);
                }
                else
                {
                    DrawMechFusionTab(listRect);
                }
            }
            finally
            {
                GUI.color = oldColor;
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
            }
        }

        private void EnsureTabs()
        {
            if (tabs.Count > 0)
            {
                return;
            }

            tabs.Add(new TabRecord(
                "机械族机械师 (0)",
                () =>
                {
                    currentTab = Tab.MechanoidMechanitor;
                    scrollPosition = Vector2.zero;
                },
                () => currentTab == Tab.MechanoidMechanitor));
            tabs.Add(new TabRecord(
                "仿生伴侣 (0)",
                () =>
                {
                    currentTab = Tab.SyntheticCompanion;
                    scrollPosition = Vector2.zero;
                },
                () => currentTab == Tab.SyntheticCompanion));
            tabs.Add(new TabRecord(
                "飞行授权 (0)",
                () =>
                {
                    currentTab = Tab.MechanicalFlight;
                    scrollPosition = Vector2.zero;
                },
                () => currentTab == Tab.MechanicalFlight));
            tabs.Add(new TabRecord(
                "合体资格 (0)",
                () =>
                {
                    currentTab = Tab.MechFusion;
                    scrollPosition = Vector2.zero;
                },
                () => currentTab == Tab.MechFusion));
            tabs.Add(new TabRecord(
                "自律授权 (0)",
                () =>
                {
                    currentTab = Tab.AutonomousMech;
                    scrollPosition = Vector2.zero;
                },
                () => currentTab == Tab.AutonomousMech));
        }

        private void UpdateTabLabels()
        {
            EnsureTabs();
            tabs[0].label = "机械族机械师 (" + mechanitorRows.Count + ")";
            tabs[1].label = "仿生伴侣 (" + companionRows.Count + ")";
            tabs[2].label = "飞行授权 (" + flightRows.Count + ")";
            tabs[3].label = "合体资格 (" + fusionRows.Count + ")";
            tabs[4].label = "自律授权 (" + autonomyRows.Count + ")";
        }

        private void RefreshSnapshots()
        {
            mechanitorRows.Clear();
            companionRows.Clear();
            flightRows.Clear();
            fusionRows.Clear();
            autonomyRows.Clear();

            IReadOnlyList<MechanoidMechanitorRegistrySnapshotEntry> mechanitorSnapshot =
                GameComponent_MechanoidMechanitorRegistry.GetPersistentRecordSnapshot();
            for (int i = 0; i < mechanitorSnapshot.Count; i++)
            {
                mechanitorRows.Add(mechanitorSnapshot[i]);
            }

            mechanitorRows.Sort(CompareMechanitorEntries);

            IReadOnlyList<SyntheticCompanionAuthorizationRecord> companionSnapshot =
                GameComponent_SyntheticCompanionRegistry.GetAuthorizationRecordSnapshot();
            for (int i = 0; i < companionSnapshot.Count; i++)
            {
                companionRows.Add(companionSnapshot[i]);
            }

            companionRows.Sort(CompareCompanionRecords);

            IReadOnlyList<MechanicalFlightAuthorizationRecord> flightSnapshot =
                GameComponent_MechanicalFlightRegistry.GetAuthorizationRecordSnapshot();
            for (int i = 0; i < flightSnapshot.Count; i++)
            {
                flightRows.Add(flightSnapshot[i]);
            }

            flightRows.Sort(CompareFlightRecords);

            IReadOnlyList<MechFusionEligibilityRecord> fusionSnapshot =
                GameComponent_MechFusionRegistry.GetEligibilityRecordSnapshot();
            for (int i = 0; i < fusionSnapshot.Count; i++)
            {
                fusionRows.Add(fusionSnapshot[i]);
            }

            fusionRows.Sort(CompareFusionRecords);
            autonomyRows.AddRange(GameComponent_AutonomousMechRegistry.GetAuthorizationRecordSnapshot());
            autonomyRows.Sort((a, b) => (a.Pawn?.thingIDNumber ?? int.MaxValue)
                .CompareTo(b.Pawn?.thingIDNumber ?? int.MaxValue));
            UpdateTabLabels();
        }

        private void DrawAutonomyTab(Rect listRect)
        {
            if (autonomyRows.Count == 0)
            {
                DrawCenteredMessage(listRect, "当前没有自律授权记录。可通过开发者的添加目标到角色注册表入口授予。");
                return;
            }

            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f,
                autonomyRows.Count * (RowHeight + 4f));
            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);
            for (int i = 0; i < autonomyRows.Count; i++)
            {
                AutonomousMechAuthorizationRecord record = autonomyRows[i];
                Pawn? pawn = record.Pawn;
                if (pawn == null) continue;
                Rect row = new Rect(0f, i * (RowHeight + 4f), viewRect.width, RowHeight);
                Widgets.DrawHighlightIfMouseover(row);
                float textWidth = row.width - DeleteButtonWidth - 16f;
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(new Rect(4f, row.y + 4f, textWidth, Text.LineHeight),
                    pawn.LabelShortCap + "  |  " + pawn.ThingID + "  |  " + DescribePawnStatus(pawn));
                string sources = DescribeAutonomySources(record.Sources);
                string details = "来源：" + sources + "  |  充电："
                    + record.RechargeThresholds.min.ToStringPercent() + "～"
                    + record.RechargeThresholds.max.ToStringPercent();
                GUI.color = new Color(0.75f, 0.75f, 0.75f);
                Widgets.Label(new Rect(4f, row.y + 4f + Text.LineHeight, textWidth, Text.LineHeight), details);
                GUI.color = Color.white;
                TooltipHandler.TipRegion(row, "阵营：" + DescribeFaction(pawn) + "\n" + details
                    + "\n撤销只移除独立授权；身份、先天组件或节点来源仍存在时，自律资格继续保留。");
                Rect button = new Rect(row.xMax - DeleteButtonWidth,
                    row.y + (RowHeight - 30f) / 2f, DeleteButtonWidth, 30f);
                if (record.HasIndependentAuthorization)
                {
                    if (Widgets.ButtonText(button, "撤销")) ConfirmRevokeAutonomy(pawn);
                }
                else Widgets.Label(button, record.Sources == AutonomousMechAuthorizationSource.None ? "未启用" : "来源保留");
            }
            Widgets.EndScrollView();
        }

        private static string DescribeAutonomySources(AutonomousMechAuthorizationSource sources)
        {
            if (sources == AutonomousMechAuthorizationSource.None) return "无（仅保留个人设置）";
            var labels = new List<string>();
            if ((sources & AutonomousMechAuthorizationSource.Independent) != 0) labels.Add("独立授权");
            if ((sources & AutonomousMechAuthorizationSource.MechanitorIdentity) != 0) labels.Add("机械师身份");
            if ((sources & AutonomousMechAuthorizationSource.LegacyNode) != 0) labels.Add("节点配置");
            if ((sources & AutonomousMechAuthorizationSource.InnateComp) != 0) labels.Add("先天组件");
            return string.Join("、", labels);
        }

        private void ConfirmRevokeAutonomy(Pawn pawn)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "确认撤销 " + pawn.LabelShortCap + " 的独立自律授权？\n"
                + "若没有其他来源，将恢复普通机械体规则，不会自动指定监管者。",
                () =>
                {
                    bool changed = GameComponent_AutonomousMechRegistry.TryRevokeAuthorization(pawn);
                    bool remains = GameComponent_AutonomousMechRegistry.TryGetRecord(pawn, out var record)
                        && record!.Sources != AutonomousMechAuthorizationSource.None;
                    Messages.Message(changed
                            ? (remains ? "已撤销独立授权；其他来源仍提供自律资格。" : "已撤销自律资格，恢复普通机械体规则。")
                            : "目标没有可撤销的独立授权。",
                        changed ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.RejectInput,
                        historical: false);
                    RefreshSnapshots();
                }, destructive: true));
        }

        private static int CompareMechanitorEntries(
            MechanoidMechanitorRegistrySnapshotEntry a,
            MechanoidMechanitorRegistrySnapshotEntry b)
        {
            int idCompare = a.Pawn.thingIDNumber.CompareTo(b.Pawn.thingIDNumber);
            if (idCompare != 0)
            {
                return idCompare;
            }

            return string.Compare(
                a.Pawn.LabelShortCap,
                b.Pawn.LabelShortCap,
                StringComparison.CurrentCulture);
        }

        private static int CompareCompanionRecords(
            SyntheticCompanionAuthorizationRecord a,
            SyntheticCompanionAuthorizationRecord b)
        {
            Pawn? pawnA = a.Pawn;
            Pawn? pawnB = b.Pawn;
            int idA = pawnA?.thingIDNumber ?? int.MaxValue;
            int idB = pawnB?.thingIDNumber ?? int.MaxValue;
            int idCompare = idA.CompareTo(idB);
            if (idCompare != 0)
            {
                return idCompare;
            }

            return string.Compare(
                pawnA?.LabelShortCap,
                pawnB?.LabelShortCap,
                StringComparison.CurrentCulture);
        }

        private static int CompareFlightRecords(
            MechanicalFlightAuthorizationRecord a,
            MechanicalFlightAuthorizationRecord b)
        {
            Pawn? pawnA = a.Pawn;
            Pawn? pawnB = b.Pawn;
            int idCompare = (pawnA?.thingIDNumber ?? int.MaxValue)
                .CompareTo(pawnB?.thingIDNumber ?? int.MaxValue);
            return idCompare != 0 ? idCompare : string.Compare(
                pawnA?.LabelShortCap, pawnB?.LabelShortCap,
                StringComparison.CurrentCulture);
        }

        private static int CompareFusionRecords(
            MechFusionEligibilityRecord a,
            MechFusionEligibilityRecord b)
        {
            Pawn? pawnA = a.Pawn;
            Pawn? pawnB = b.Pawn;
            int idCompare = (pawnA?.thingIDNumber ?? int.MaxValue)
                .CompareTo(pawnB?.thingIDNumber ?? int.MaxValue);
            return idCompare != 0 ? idCompare : string.Compare(
                pawnA?.LabelShortCap, pawnB?.LabelShortCap,
                StringComparison.CurrentCulture);
        }

        private void DrawMechanitorTab(Rect listRect)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            if (mechanitorRows.Count == 0)
            {
                DrawCenteredMessage(listRect, "当前没有机械族机械师注册记录。");
                return;
            }

            float viewHeight = mechanitorRows.Count * (RowHeight + 4f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);

            float y = 0f;
            for (int i = 0; i < mechanitorRows.Count; i++)
            {
                MechanoidMechanitorRegistrySnapshotEntry entry = mechanitorRows[i];
                Rect rowRect = new Rect(0f, y, viewRect.width, RowHeight);
                DrawMechanitorRow(rowRect, entry);
                y += RowHeight + 4f;
            }

            Widgets.EndScrollView();
        }

        private void DrawCompanionTab(Rect listRect)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            if (companionRows.Count == 0)
            {
                DrawCenteredMessage(listRect, "当前没有动态仿生伴侣注册记录。");
                return;
            }

            float viewHeight = companionRows.Count * (RowHeight + 4f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);

            float y = 0f;
            for (int i = 0; i < companionRows.Count; i++)
            {
                SyntheticCompanionAuthorizationRecord record = companionRows[i];
                Rect rowRect = new Rect(0f, y, viewRect.width, RowHeight);
                DrawCompanionRow(rowRect, record);
                y += RowHeight + 4f;
            }

            Widgets.EndScrollView();
        }

        private void DrawFlightTab(Rect listRect)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            if (flightRows.Count == 0)
            {
                DrawCenteredMessage(listRect, "当前没有飞行授权注册记录。");
                return;
            }

            float viewHeight = flightRows.Count * (RowHeight + 4f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);
            float y = 0f;
            for (int i = 0; i < flightRows.Count; i++)
            {
                DrawFlightRow(new Rect(0f, y, viewRect.width, RowHeight), flightRows[i]);
                y += RowHeight + 4f;
            }
            Widgets.EndScrollView();
        }

        private void DrawMechFusionTab(Rect listRect)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            if (fusionRows.Count == 0)
            {
                DrawCenteredMessage(listRect, "当前没有合体资格注册记录。");
                return;
            }

            float viewHeight = fusionRows.Count * (RowHeight + 4f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);
            float y = 0f;
            for (int i = 0; i < fusionRows.Count; i++)
            {
                DrawMechFusionRow(
                    new Rect(0f, y, viewRect.width, RowHeight),
                    fusionRows[i]);
                y += RowHeight + 4f;
            }
            Widgets.EndScrollView();
        }

        private void DrawMechFusionRow(
            Rect rowRect,
            MechFusionEligibilityRecord record)
        {
            Widgets.DrawHighlightIfMouseover(rowRect);
            Pawn? pawn = record.Pawn;
            if (pawn == null)
            {
                return;
            }

            Rect textRect = rowRect;
            textRect.xMax -= DeleteButtonWidth + 8f;

            bool hasInnate =
                MechFusionEligibilityUtility.HasInnateFusionMarker(pawn);
            bool isFusing =
                GameComponent_MechFusionSessionRegistry.TryGetSessionForSource(
                    pawn,
                    out MechFusionSession? session)
                && session != null
                && session.IsActive;

            string line1 = pawn.LabelShortCap
                + "  |  "
                + (pawn.KindLabel ?? "?")
                + "  |  "
                + pawn.ThingID;
            string line2 = "阵营："
                + DescribeFaction(pawn)
                + "  |  状态："
                + DescribePawnStatus(pawn)
                + "  |  先天标记："
                + (hasInnate ? "是" : "否")
                + "  |  合体状态："
                + (isFusing ? "正在合体" : "未合体");

            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(
                new Rect(textRect.x + 4f, textRect.y + 4f, textRect.width - 8f, Text.LineHeight),
                line1);
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            Widgets.Label(
                new Rect(
                    textRect.x + 4f,
                    textRect.y + 4f + Text.LineHeight,
                    textRect.width - 8f,
                    Text.LineHeight),
                line2);
            GUI.color = Color.white;

            Rect deleteRect = new Rect(
                rowRect.xMax - DeleteButtonWidth,
                rowRect.y + (rowRect.height - 30f) / 2f,
                DeleteButtonWidth,
                30f);
            if (Widgets.ButtonText(deleteRect, "删除"))
            {
                ConfirmDeleteMechFusion(pawn);
            }
        }

        private void DrawMechanitorRow(
            Rect rowRect,
            MechanoidMechanitorRegistrySnapshotEntry entry)
        {
            Widgets.DrawHighlightIfMouseover(rowRect);
            Pawn pawn = entry.Pawn;

            Rect textRect = rowRect;
            textRect.xMax -= DeleteButtonWidth + 8f;

            string originLabel = entry.Origin == MechanoidMechanitorOrigin.Native
                ? "原生"
                : "后天";
            string hostLabel = entry.IsMechanicalConsciousnessHost ? "是" : "否";
            string line1 = pawn.LabelShortCap
                + "  |  "
                + (pawn.KindLabel ?? "?")
                + "  |  "
                + pawn.ThingID;
            string line2 = "阵营："
                + DescribeFaction(pawn)
                + "  |  状态："
                + DescribePawnStatus(pawn)
                + "  |  Origin："
                + originLabel
                + "  |  机械意识宿主："
                + hostLabel;

            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(
                new Rect(textRect.x + 4f, textRect.y + 4f, textRect.width - 8f, Text.LineHeight),
                line1);
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            Widgets.Label(
                new Rect(
                    textRect.x + 4f,
                    textRect.y + 4f + Text.LineHeight,
                    textRect.width - 8f,
                    Text.LineHeight),
                line2);
            GUI.color = Color.white;

            Rect deleteRect = new Rect(
                rowRect.xMax - DeleteButtonWidth,
                rowRect.y + (rowRect.height - 30f) / 2f,
                DeleteButtonWidth,
                30f);
            if (Widgets.ButtonText(deleteRect, "删除"))
            {
                ConfirmDeleteMechanitor(pawn, entry.Origin);
            }
        }

        private void DrawCompanionRow(
            Rect rowRect,
            SyntheticCompanionAuthorizationRecord record)
        {
            Widgets.DrawHighlightIfMouseover(rowRect);
            Pawn? pawn = record.Pawn;
            if (pawn == null)
            {
                return;
            }

            Rect textRect = rowRect;
            textRect.xMax -= DeleteButtonWidth + 8f;

            string line1 = pawn.LabelShortCap
                + "  |  "
                + (pawn.KindLabel ?? "?")
                + "  |  "
                + pawn.ThingID;
            string line2 = "阵营："
                + DescribeFaction(pawn)
                + "  |  状态："
                + DescribePawnStatus(pawn);

            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(
                new Rect(textRect.x + 4f, textRect.y + 4f, textRect.width - 8f, Text.LineHeight),
                line1);
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            Widgets.Label(
                new Rect(
                    textRect.x + 4f,
                    textRect.y + 4f + Text.LineHeight,
                    textRect.width - 8f,
                    Text.LineHeight),
                line2);
            GUI.color = Color.white;

            Rect deleteRect = new Rect(
                rowRect.xMax - DeleteButtonWidth,
                rowRect.y + (rowRect.height - 30f) / 2f,
                DeleteButtonWidth,
                30f);
            if (Widgets.ButtonText(deleteRect, "删除"))
            {
                ConfirmDeleteCompanion(pawn);
            }
        }

        private void DrawFlightRow(
            Rect rowRect,
            MechanicalFlightAuthorizationRecord record)
        {
            Widgets.DrawHighlightIfMouseover(rowRect);
            Pawn? pawn = record.Pawn;
            if (pawn == null)
            {
                return;
            }
            Rect textRect = rowRect;
            textRect.xMax -= DeleteButtonWidth + 8f;
            string line1 = pawn.LabelShortCap + "  |  " + (pawn.KindLabel ?? "?")
                + "  |  " + pawn.ThingID;
            string line2 = "阵营：" + DescribeFaction(pawn) + "  |  状态："
                + DescribePawnStatus(pawn) + "  |  飞行阶段：" + record.Phase
                + "  |  配置：" + (record.Profile?.defName ?? "无");
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(textRect.x + 4f, textRect.y + 4f,
                textRect.width - 8f, Text.LineHeight), line1);
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            Widgets.Label(new Rect(textRect.x + 4f,
                textRect.y + 4f + Text.LineHeight, textRect.width - 8f,
                Text.LineHeight), line2);
            GUI.color = Color.white;
            Rect deleteRect = new Rect(rowRect.xMax - DeleteButtonWidth,
                rowRect.y + (rowRect.height - 30f) / 2f, DeleteButtonWidth, 30f);
            if (Widgets.ButtonText(deleteRect, "删除"))
            {
                ConfirmDeleteFlight(pawn);
            }
        }

        private void ConfirmDeleteMechFusion(Pawn pawn)
        {
            if (GameComponent_MechFusionSessionRegistry.TryGetSessionForSource(
                    pawn,
                    out MechFusionSession? session)
                && session != null
                && session.IsActive)
            {
                Messages.Message(
                    "无法删除合体资格："
                    + pawn.LabelShortCap
                    + " 正在作为合体源机械族，请先解除合体。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            string message = "确认从合体资格注册表删除 "
                + pawn.LabelShortCap
                + "（"
                + pawn.ThingID
                + "）？";
            if (MechFusionEligibilityUtility.HasInnateFusionMarker(pawn))
            {
                message +=
                    "\n\n注意：该 Pawn 的静态先天标记仍然存在，之后 Spawn、初始化或重新读档时可能重新注册。";
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                message,
                () =>
                {
                    if (GameComponent_MechFusionRegistry.TryUnregisterFromDebug(pawn))
                    {
                        Messages.Message(
                            "已从合体资格注册表删除 " + pawn.LabelShortCap + "。",
                            MessageTypeDefOf.TaskCompletion,
                            historical: false);
                        RefreshSnapshots();
                    }
                    else
                    {
                        Messages.Message(
                            "删除合体资格记录失败：" + pawn.LabelShortCap + "。",
                            MessageTypeDefOf.RejectInput,
                            historical: false);
                    }
                },
                destructive: true));
        }

        private void ConfirmDeleteMechanitor(Pawn pawn, MechanoidMechanitorOrigin origin)
        {
            string message =
                "确认从机械族机械师注册表删除 "
                + pawn.LabelShortCap
                + "（"
                + pawn.ThingID
                + "）？";
            if (origin == MechanoidMechanitorOrigin.Native)
            {
                message +=
                    "\n\n注意：其静态身份来源仍存在，后续初始化可能重新注册。";
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                message,
                () =>
                {
                    if (GameComponent_MechanoidMechanitorRegistry.TryUnregisterFromDebug(pawn))
                    {
                        Messages.Message(
                            "已从机械族机械师注册表删除 " + pawn.LabelShortCap + "。",
                            MessageTypeDefOf.TaskCompletion,
                            historical: false);
                        RefreshSnapshots();
                    }
                    else
                    {
                        Messages.Message(
                            "删除机械族机械师记录失败：" + pawn.LabelShortCap + "。",
                            MessageTypeDefOf.RejectInput,
                            historical: false);
                    }
                },
                destructive: true));
        }

        private void ConfirmDeleteCompanion(Pawn pawn)
        {
            string message =
                "确认从仿生伴侣注册表删除 "
                + pawn.LabelShortCap
                + "（"
                + pawn.ThingID
                + "）的动态授权？";

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                message,
                () =>
                {
                    if (GameComponent_SyntheticCompanionRegistry.TryRevokeAuthorization(pawn))
                    {
                        Messages.Message(
                            "已撤销仿生伴侣动态授权：" + pawn.LabelShortCap + "。",
                            MessageTypeDefOf.TaskCompletion,
                            historical: false);
                        RefreshSnapshots();
                    }
                    else
                    {
                        Messages.Message(
                            "撤销仿生伴侣动态授权失败：" + pawn.LabelShortCap + "。",
                            MessageTypeDefOf.RejectInput,
                            historical: false);
                    }
                },
                destructive: true));
        }

        private void ConfirmDeleteFlight(Pawn pawn)
        {
            string message = "确认从飞行授权注册表删除 " + pawn.LabelShortCap
                + "（" + pawn.ThingID + "）的动态授权？";
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(message, () =>
            {
                if (GameComponent_MechanicalFlightRegistry.TryRevokeAuthorization(pawn))
                {
                    Messages.Message("已撤销飞行动态授权：" + pawn.LabelShortCap + "。",
                        MessageTypeDefOf.TaskCompletion, historical: false);
                    RefreshSnapshots();
                }
                else
                {
                    Messages.Message("撤销飞行动态授权失败：" + pawn.LabelShortCap + "。",
                        MessageTypeDefOf.RejectInput, historical: false);
                }
            }, destructive: true));
        }

        private static void DrawCenteredMessage(Rect rect, string message)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, message);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static string DescribeFaction(Pawn pawn)
        {
            return pawn.Faction?.Name ?? "无";
        }

        internal static string DescribePawnStatus(Pawn pawn)
        {
            if (pawn.Corpse != null)
            {
                return "位于尸体中";
            }

            if (pawn.Dead)
            {
                return "已死亡";
            }

            if (pawn.Spawned && pawn.Map != null)
            {
                return "当前地图中";
            }

            if (pawn.IsWorldPawn() || pawn.GetCaravan() != null)
            {
                return "远行队或世界对象中";
            }

            if (!pawn.Spawned)
            {
                return "未生成或暂时离图";
            }

            return "无法确认";
        }
    }
}
