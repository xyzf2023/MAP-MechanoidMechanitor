using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 开发者模式角色注册表管理窗口：查看/删除机械族机械师与动态仿生伴侣持久记录。
    /// </summary>
    public sealed class Dialog_RoleRegistryDebug : Window
    {
        private enum Tab
        {
            MechanoidMechanitor,
            SyntheticCompanion
        }

        private const float TitleHeight = 32f;
        private const float RowHeight = 52f;
        private const float DeleteButtonWidth = 64f;

        private Tab currentTab = Tab.MechanoidMechanitor;
        private Vector2 scrollPosition;
        private readonly List<TabRecord> tabs = new List<TabRecord>();
        private readonly List<MechanoidMechanitorRegistrySnapshotEntry> mechanitorRows =
            new List<MechanoidMechanitorRegistrySnapshotEntry>();
        private readonly List<SyntheticCompanionAuthorizationRecord> companionRows =
            new List<SyntheticCompanionAuthorizationRecord>();

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
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(
                    new Rect(contentRect.x, contentRect.y, contentRect.width, TitleHeight),
                    "角色注册表");

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
                else
                {
                    DrawCompanionTab(listRect);
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
        }

        private void UpdateTabLabels()
        {
            EnsureTabs();
            tabs[0].label = "机械族机械师 (" + mechanitorRows.Count + ")";
            tabs[1].label = "仿生伴侣 (" + companionRows.Count + ")";
        }

        private void RefreshSnapshots()
        {
            mechanitorRows.Clear();
            companionRows.Clear();

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
            UpdateTabLabels();
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
