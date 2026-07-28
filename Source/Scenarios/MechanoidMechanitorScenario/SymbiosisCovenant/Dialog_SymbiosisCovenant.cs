using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public enum SymbiosisCovenantPage
    {
        Communication,
        Relations
    }

    public sealed class Dialog_SymbiosisCovenant : Window
    {
        private const float HeaderHeight = 132f;
        private const float FooterHeight = 48f;
        private const float MainGap = 14f;
        private const float MemberCardWidth = 120f;
        private const float MemberCardHeight = 150f;
        private const float MemberCardGap = 10f;
        private const float RelationCardHeight = 150f;
        private const float RelationCardGap = 10f;
        private const float PageTabWidth = 150f;
        private const float PageTabHeight = 36f;
        private const float FooterGap = 6f;

        private static readonly Color BackgroundColor =
            new Color(0.045f, 0.065f, 0.07f, 0.98f);
        private static readonly Color PanelColor =
            new Color(0.075f, 0.105f, 0.11f, 0.98f);
        private static readonly Color PanelOutlineColor =
            new Color(0.20f, 0.55f, 0.52f, 0.75f);
        private static readonly Color TealColor =
            new Color(0.22f, 0.82f, 0.72f, 1f);
        private static readonly Color MutedTextColor =
            new Color(0.63f, 0.72f, 0.71f, 1f);

        private SymbiosisCovenantPage currentPage = SymbiosisCovenantPage.Communication;
        private Vector2 memberScrollPosition = Vector2.zero;
        private Vector2 relationsScrollPosition = Vector2.zero;

        public override Vector2 InitialSize => new Vector2(1180f, 720f);

        public Dialog_SymbiosisCovenant()
        {
            doCloseX = true;
            doCloseButton = false;
            absorbInputAroundWindow = true;
            forcePause = false;
            closeOnAccept = false;
            closeOnCancel = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.DrawBoxSolid(inRect, BackgroundColor);

            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (!GameComponent_SymbiosisCovenantState.IsActive || state == null)
            {
                DrawConnectionLost(inRect);
                return;
            }

            state.SynchronizeNow();

            Rect footerRect = new Rect(
                inRect.x,
                inRect.yMax - FooterHeight,
                inRect.width,
                FooterHeight);

            Rect pageRect = new Rect(
                inRect.x,
                inRect.y,
                inRect.width,
                footerRect.y - inRect.y);

            switch (currentPage)
            {
                case SymbiosisCovenantPage.Relations:
                    DrawRelationsPage(pageRect, state);
                    break;

                default:
                    DrawCommunicationPage(pageRect, state);
                    break;
            }

            DrawFooter(footerRect);
        }

        private static int GetNextLevelThreshold(int level)
        {
            switch (level)
            {
                case 0:
                case 1:
                    return 100;
                case 2:
                    return 250;
                case 3:
                    return 450;
                case 4:
                    return 700;
                default:
                    return -1;
            }
        }

        private static void DrawHeader(
            Rect rect,
            GameComponent_SymbiosisCovenantState state)
        {
            Widgets.DrawBoxSolid(rect, PanelColor);
            DrawOutline(rect, 1, TealColor);
            Widgets.DrawBoxSolid(
                new Rect(rect.x, rect.yMax - 2f, rect.width, 2f),
                TealColor);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            try
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                Widgets.Label(
                    new Rect(rect.x + 14f, rect.y + 8f, rect.width - 28f, 28f),
                    "MAP_MechanoidMechanitor.Symbiosis.Title".Translate());

                Text.Font = GameFont.Tiny;
                GUI.color = MutedTextColor;
                Widgets.Label(
                    new Rect(rect.x + 15f, rect.y + 36f, rect.width - 30f, 18f),
                    "MAP_MechanoidMechanitor.Symbiosis.Subtitle".Translate());

                float lineY = rect.y + 60f;
                float labelWidth = (rect.width - 28f) / 2f;
                Text.Font = GameFont.Small;
                GUI.color = TealColor;
                Widgets.Label(
                    new Rect(rect.x + 15f, lineY, labelWidth, 22f),
                    "MAP_MechanoidMechanitor.Symbiosis.CovenantLevel".Translate(
                        state.CovenantLevel, 5));
                Widgets.Label(
                    new Rect(rect.x + 15f + labelWidth, lineY, labelWidth, 22f),
                    "MAP_MechanoidMechanitor.Symbiosis.MemberCount".Translate(
                        state.CovenantMemberCount));

                Text.Font = GameFont.Tiny;
                GUI.color = MutedTextColor;
                int nextThreshold = GetNextLevelThreshold(state.CovenantLevel);
                string unityText = nextThreshold > 0
                    ? "MAP_MechanoidMechanitor.Symbiosis.UnityNext".Translate(
                        Mathf.FloorToInt(state.Unity),
                        nextThreshold)
                    : "MAP_MechanoidMechanitor.Symbiosis.UnityMax".Translate(
                        Mathf.FloorToInt(state.Unity));
                Widgets.Label(
                    new Rect(rect.x + 15f, lineY + 22f, labelWidth, 20f),
                    unityText);
                Widgets.Label(
                    new Rect(
                        rect.x + 15f + labelWidth,
                        lineY + 22f,
                        labelWidth,
                        20f),
                    "MAP_MechanoidMechanitor.Symbiosis.HighestLevel".Translate(
                        state.HighestCovenantLevel));

                Text.Font = GameFont.Small;
                GUI.color = state.PublicDeclarationBroadcast
                    ? TealColor
                    : new Color(1f, 0.38f, 0.30f);
                Widgets.Label(
                    new Rect(rect.x + 15f, lineY + 44f, rect.width - 30f, 22f),
                    state.PublicDeclarationBroadcast
                        ? "MAP_MechanoidMechanitor.Symbiosis.Declaration.Done"
                            .Translate()
                        : "MAP_MechanoidMechanitor.Symbiosis.Declaration.NotDone"
                            .Translate());
            }
            finally
            {
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                GUI.color = previousColor;
            }
        }

        private void DrawMembers(
            Rect rect,
            GameComponent_SymbiosisCovenantState state)
        {
            Widgets.DrawBoxSolid(rect, PanelColor);
            DrawOutline(rect, 1, PanelOutlineColor);

            List<SymbiosisCovenantFactionRecord> members = state.GetRecordsSorted()
                .Where(record => record.CovenantMember && record.Faction != null)
                .ToList();

            if (members.Count == 0)
            {
                DrawNoData(
                    rect.ContractedBy(12f),
                    "MAP_MechanoidMechanitor.Symbiosis.NoMembers".Translate());
                return;
            }

            Rect inner = rect.ContractedBy(12f);
            int columns = Math.Max(
                1,
                Mathf.FloorToInt(
                    (inner.width + MemberCardGap)
                    / (MemberCardWidth + MemberCardGap)));
            float rowHeight = MemberCardHeight + MemberCardGap;

            Rect viewRect = new Rect(
                0f,
                0f,
                inner.width,
                Mathf.CeilToInt((float)members.Count / columns) * rowHeight);
            Widgets.BeginScrollView(inner, ref memberScrollPosition, viewRect);
            try
            {
                for (int i = 0; i < members.Count; i++)
                {
                    int column = i % columns;
                    int row = i / columns;
                    Rect cardRect = new Rect(
                        column * (MemberCardWidth + MemberCardGap),
                        row * rowHeight,
                        MemberCardWidth,
                        MemberCardHeight);
                    DrawMemberCard(cardRect, members[i]);
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawCommunicationPage(
            Rect rect,
            GameComponent_SymbiosisCovenantState state)
        {
            Rect headerRect = new Rect(
                rect.x,
                rect.y,
                rect.width,
                HeaderHeight);

            Rect membersRect = new Rect(
                rect.x,
                headerRect.yMax + MainGap,
                rect.width,
                Mathf.Max(
                    0f,
                    rect.yMax - headerRect.yMax - MainGap));

            DrawHeader(headerRect, state);
            DrawMembers(membersRect, state);
        }

        private void DrawRelationsPage(
            Rect rect,
            GameComponent_SymbiosisCovenantState state)
        {
            Widgets.DrawBoxSolid(rect, PanelColor);
            DrawOutline(rect, 1, PanelOutlineColor);

            Faction? player = Faction.OfPlayerSilentFail;
            List<SymbiosisCovenantFactionRecord> records =
                state.GetRecordsSorted().Where(r => r.Faction != null).ToList();

            Rect inner = rect.ContractedBy(12f);
            DrawLabel(
                new Rect(inner.x, inner.y, inner.width, 24f),
                "MAP_MechanoidMechanitor.Symbiosis.Relations.Title".Translate(),
                GameFont.Small,
                Color.white,
                TextAnchor.UpperLeft);

            Rect listRect = new Rect(
                inner.x,
                inner.y + 30f,
                inner.width,
                inner.yMax - inner.y - 30f);

            if (records.Count == 0)
            {
                DrawNoData(
                    listRect.ContractedBy(12f),
                    "MAP_MechanoidMechanitor.Symbiosis.Relations.NoFactions"
                        .Translate());
                return;
            }

            float viewHeight = records.Count > 0
                ? records.Count * RelationCardHeight
                    + (records.Count - 1) * RelationCardGap
                : 0f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 18f, viewHeight);
            Widgets.BeginScrollView(listRect, ref relationsScrollPosition, viewRect);
            try
            {
                for (int i = 0; i < records.Count; i++)
                {
                    Rect cardRect = new Rect(
                        0f,
                        i * (RelationCardHeight + RelationCardGap),
                        listRect.width - 18f,
                        RelationCardHeight);
                    DrawRelationCard(cardRect, records[i], player);
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawRelationCard(
            Rect rect,
            SymbiosisCovenantFactionRecord record,
            Faction? player)
        {
            Faction? faction = record.Faction;
            if (faction == null)
            {
                return;
            }

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            try
            {
                Widgets.DrawBoxSolid(rect, new Color(0.065f, 0.085f, 0.09f, 1f));
                DrawOutline(rect, 1, TealColor);

                Rect iconRect = new Rect(
                    rect.x + 22f,
                    rect.y + (rect.height - 72f) / 2f,
                    72f,
                    72f);
                Texture2D? icon = faction.def?.FactionIcon;
                if (icon != null)
                {
                    Color previousIconColor = GUI.color;
                    try
                    {
                        GUI.color = faction.Color;
                        GUI.DrawTexture(
                            iconRect,
                            icon,
                            ScaleMode.ScaleToFit,
                            true);
                    }
                    finally
                    {
                        GUI.color = previousIconColor;
                    }
                }

                float textX = iconRect.xMax + 24f;
                float textWidth = rect.xMax - 22f - textX;

                Rect nameRect = new Rect(textX, rect.y + 20f, textWidth, 38f);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white;
                Widgets.LabelFit(nameRect, faction.Name);

                int goodwill = player != null ? player.GoodwillWith(faction) : 0;
                Rect goodwillRect = new Rect(textX, rect.y + 70f, textWidth, 28f);
                Text.Font = GameFont.Small;
                GUI.color = MutedTextColor;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(
                    goodwillRect,
                    "MAP_MechanoidMechanitor.Symbiosis.Relations.Goodwill".Translate(
                        goodwill));

                Rect trustRect = new Rect(textX, rect.y + 106f, textWidth, 28f);
                Widgets.Label(
                    trustRect,
                    "MAP_MechanoidMechanitor.Symbiosis.Relations.Trust".Translate(
                        record.Trust));
            }
            finally
            {
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                GUI.color = previousColor;
            }
        }

        private void DrawPageTab(Rect rect, SymbiosisCovenantPage page, string label)
        {
            bool selected = currentPage == page;
            bool hovered = Mouse.IsOver(rect);

            Color background;
            if (selected)
            {
                background = new Color(0.10f, 0.30f, 0.27f, 1f);
            }
            else if (hovered)
            {
                background = Color.Lerp(
                    new Color(0.06f, 0.10f, 0.11f, 1f),
                    TealColor,
                    0.14f);
            }
            else
            {
                background = new Color(0.06f, 0.10f, 0.11f, 1f);
            }

            Color borderColor;
            if (selected)
            {
                borderColor = TealColor;
            }
            else if (hovered)
            {
                borderColor = Color.Lerp(PanelOutlineColor, TealColor, 0.5f);
            }
            else
            {
                borderColor = PanelOutlineColor;
            }

            Color textColor;
            if (selected)
            {
                textColor = TealColor;
            }
            else if (hovered)
            {
                textColor = Color.white;
            }
            else
            {
                textColor = MutedTextColor;
            }

            Widgets.DrawBoxSolid(rect, background);
            DrawOutline(rect, 1, borderColor);

            if (selected)
            {
                Rect accentRect = new Rect(
                    rect.x + 2f,
                    rect.yMax - 3f,
                    rect.width - 4f,
                    3f);
                Widgets.DrawBoxSolid(accentRect, TealColor);
            }

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = textColor;
                Widgets.Label(rect, label);
            }
            finally
            {
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                GUI.color = previousColor;
            }

            if (Widgets.ButtonInvisible(rect))
            {
                currentPage = page;
            }
        }

        private static void DrawMemberCard(
            Rect rect,
            SymbiosisCovenantFactionRecord record)
        {
            Faction? faction = record.Faction;
            if (faction == null)
            {
                return;
            }

            Color factionColor = faction.Color;
            Widgets.DrawBoxSolid(rect, new Color(0.065f, 0.085f, 0.09f, 1f));
            DrawOutline(rect, 1, factionColor);

            Rect headerRect = new Rect(
                rect.x + 6f,
                rect.y + 3f,
                rect.width - 12f,
                20f);
            DrawCenteredLabel(
                headerRect,
                faction.Name.Truncate(headerRect.width),
                GameFont.Tiny,
                factionColor);

            Rect portraitRect = new Rect(
                rect.x + (rect.width - 64f) / 2f,
                headerRect.yMax + 4f,
                64f,
                64f);
            Pawn? leader = faction.leader;
            if (leader != null && !leader.Destroyed)
            {
                Faction? player = Faction.OfPlayerSilentFail;
                bool grayscale = player != null && faction.HostileTo(player);
                Widgets.ThingIcon(
                    portraitRect,
                    leader,
                    alpha: grayscale ? 0.72f : 1f,
                    grayscale: grayscale);
            }
            else
            {
                DrawNoData(
                    portraitRect.ContractedBy(4f),
                    "MAP_MechanoidMechanitor.Symbiosis.NoLeader".Translate());
            }

            Rect leaderRect = new Rect(
                rect.x + 6f,
                portraitRect.yMax + 4f,
                rect.width - 12f,
                18f);
            DrawCenteredLabel(
                leaderRect,
                (leader?.LabelShortCap ?? "NO DATA").Truncate(leaderRect.width),
                GameFont.Tiny,
                MutedTextColor);

            Rect badgeRect = new Rect(rect.x + 4f, rect.y + 4f, 58f, 18f);
            Widgets.DrawBoxSolid(badgeRect, new Color(0.07f, 0.30f, 0.25f, 0.95f));
            DrawCenteredLabel(
                badgeRect,
                "MAP_MechanoidMechanitor.Symbiosis.MemberBadge".Translate(),
                GameFont.Tiny,
                TealColor);
        }

        private void DrawFooter(Rect rect)
        {
            Rect communicationTabRect = new Rect(
                rect.x,
                rect.y,
                PageTabWidth,
                PageTabHeight);
            Rect relationsTabRect = new Rect(
                communicationTabRect.xMax + FooterGap,
                rect.y,
                PageTabWidth,
                PageTabHeight);

            DrawPageTab(
                communicationTabRect,
                SymbiosisCovenantPage.Communication,
                "MAP_MechanoidMechanitor.Symbiosis.Page.Communication".Translate());
            DrawPageTab(
                relationsTabRect,
                SymbiosisCovenantPage.Relations,
                "MAP_MechanoidMechanitor.Symbiosis.Page.Relations".Translate());

            Rect closeRect = new Rect(rect.xMax - 150f, rect.y + 6f, 150f, 30f);
            if (Widgets.ButtonText(
                    closeRect,
                    "MAP_MechanoidMechanitor.Symbiosis.Disconnect".Translate()))
            {
                Close();
            }

            if (Prefs.DevMode)
            {
                Rect devRect = new Rect(rect.xMax - 228f, rect.y + 6f, 70f, 30f);
                if (Widgets.ButtonText(devRect, "DEV"))
                {
                    Find.WindowStack.Add(new Dialog_SymbiosisCovenantDev(null));
                }
            }
        }

        private static void DrawConnectionLost(Rect rect)
        {
            DrawNoData(
                rect.ContractedBy(40f),
                "MAP_MechanoidMechanitor.Symbiosis.ConnectionLost".Translate());
        }

        private static void DrawNoData(Rect rect, string text)
        {
            DrawScanLines(rect);
            DrawCenteredLabel(rect, text, GameFont.Tiny, MutedTextColor);
        }

        private static void DrawScanLines(Rect rect)
        {
            Color line = new Color(0.30f, 0.48f, 0.47f, 0.07f);
            float offset = Mathf.Repeat(Time.realtimeSinceStartup * 18f, 8f);
            for (float y = rect.y + offset; y < rect.yMax; y += 8f)
            {
                Widgets.DrawBoxSolid(new Rect(rect.x, y, rect.width, 1f), line);
            }
        }

        private static void DrawCenteredLabel(
            Rect rect,
            string text,
            GameFont font,
            Color color)
        {
            DrawLabel(rect, text, font, color, TextAnchor.MiddleCenter);
        }

        private static void DrawLabel(
            Rect rect,
            string text,
            GameFont font,
            Color color,
            TextAnchor anchor)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            Color previousColor = GUI.color;
            try
            {
                Text.Font = font;
                Text.Anchor = anchor;
                Text.WordWrap = false;
                GUI.color = color;
                Widgets.Label(rect, text);
            }
            finally
            {
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWordWrap;
                GUI.color = previousColor;
            }
        }

        private static void DrawOutline(Rect rect, int thickness, Color color)
        {
            Color previousColor = GUI.color;
            try
            {
                GUI.color = color;
                Widgets.DrawBox(rect, thickness);
            }
            finally
            {
                GUI.color = previousColor;
            }
        }
    }

    public sealed class Dialog_SymbiosisCovenantDev : Window
    {
        private Faction? selectedFaction;
        private Vector2 scrollPosition = Vector2.zero;

        public override Vector2 InitialSize => new Vector2(780f, 640f);

        public Dialog_SymbiosisCovenantDev(Faction? faction)
        {
            selectedFaction = faction;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            closeOnAccept = false;
            closeOnCancel = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (!Prefs.DevMode)
            {
                Close();
                return;
            }

            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (state == null)
            {
                Widgets.Label(
                    new Rect(inRect.x, inRect.y, inRect.width, 60f),
                    "MAP_MechanoidMechanitor.Symbiosis.Dev.NoRecord".Translate());
                return;
            }

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(
                    new Rect(inRect.x, inRect.y, inRect.width, 32f),
                    "MAP_MechanoidMechanitor.Symbiosis.Dev.Title".Translate());

                // 标题下方建立滚动区域；派系选择按钮及其状态一并放入滚动视图。
                Rect outRect = new Rect(
                    inRect.x,
                    inRect.y + 40f,
                    inRect.width,
                    inRect.yMax - (inRect.y + 40f));

                SymbiosisCovenantFactionRecord? record = state.GetRecord(selectedFaction);
                bool hasRecord = record != null && selectedFaction != null;
                float contentHeight = hasRecord ? 1320f : 420f;
                Rect viewRect = new Rect(
                    0f,
                    0f,
                    outRect.width - 18f,
                    Mathf.Max(outRect.height, contentHeight));

                Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
                try
                {
                    float y = 0f;

                    // 选择测试派系按钮（位于滚动视图内）。
                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.UpperLeft;
                    string factionLabel = selectedFaction?.Name
                        ?? "MAP_MechanoidMechanitor.Symbiosis.Dev.SelectFaction"
                            .Translate();
                    Rect selectRect = new Rect(0f, y, 320f, 28f);
                    if (Widgets.ButtonText(selectRect, factionLabel))
                    {
                        OpenFactionSelector(state);
                    }

                    y += 34f;

                    if (hasRecord)
                    {
                        DrawRecordControls(state, record!, viewRect, y);
                    }
                    else
                    {
                        Widgets.Label(
                            new Rect(0f, y, viewRect.width, 40f),
                            "MAP_MechanoidMechanitor.Symbiosis.Dev.NoRecord"
                                .Translate());
                        DrawGlobalState(state, viewRect, y + 46f);
                    }
                }
                finally
                {
                    Widgets.EndScrollView();
                }
            }
            finally
            {
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWordWrap;
            }
        }

        private void OpenFactionSelector(GameComponent_SymbiosisCovenantState state)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            IReadOnlyList<SymbiosisCovenantFactionRecord> records =
                state.GetRecordsSorted();
            for (int i = 0; i < records.Count; i++)
            {
                Faction? faction = records[i].Faction;
                if (faction == null)
                {
                    continue;
                }

                Faction localFaction = faction;
                options.Add(
                    new FloatMenuOption(
                        faction.Name,
                        () =>
                        {
                            selectedFaction = localFaction;
                            scrollPosition = Vector2.zero;
                        }));
            }

            if (options.Count == 0)
            {
                options.Add(
                    new FloatMenuOption(
                        "MAP_MechanoidMechanitor.Symbiosis.Dev.NoRecord".Translate(),
                        null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DrawRecordControls(
            GameComponent_SymbiosisCovenantState state,
            SymbiosisCovenantFactionRecord record,
            Rect inRect,
            float startY)
        {
            float y = startY;
            DrawSectionLabel(
                new Rect(inRect.x, y, inRect.width, 22f),
                "MAP_MechanoidMechanitor.Symbiosis.Dev.Adjust".Translate());
            y += 26f;
            DrawButtonRow(
                new Rect(inRect.x, y, inRect.width, 32f),
                new[] { "-25", "-1", "+1", "+25" },
                new Action[]
                {
                    () => state.DevAdjustTrust(selectedFaction, -25, DevReason()),
                    () => state.DevAdjustTrust(selectedFaction, -1, DevReason()),
                    () => state.DevAdjustTrust(selectedFaction, 1, DevReason()),
                    () => state.DevAdjustTrust(selectedFaction, 25, DevReason())
                });
            y += 40f;

            DrawSectionLabel(
                new Rect(inRect.x, y, inRect.width, 22f),
                "MAP_MechanoidMechanitor.Symbiosis.Dev.Set".Translate());
            y += 26f;
            DrawButtonRow(
                new Rect(inRect.x, y, inRect.width, 32f),
                new[] { "-100", "-50", "-25", "0" },
                new Action[]
                {
                    () => state.DevSetTrust(selectedFaction, -100, DevReason()),
                    () => state.DevSetTrust(selectedFaction, -50, DevReason()),
                    () => state.DevSetTrust(selectedFaction, -25, DevReason()),
                    () => state.DevSetTrust(selectedFaction, 0, DevReason())
                });
            y += 38f;
            DrawButtonRow(
                new Rect(inRect.x, y, inRect.width, 32f),
                new[] { "50", "100", "150", "200" },
                new Action[]
                {
                    () => state.DevSetTrust(selectedFaction, 50, DevReason()),
                    () => state.DevSetTrust(selectedFaction, 100, DevReason()),
                    () => state.DevSetTrust(selectedFaction, 150, DevReason()),
                    () => state.DevSetTrust(selectedFaction, 200, DevReason())
                });
            y += 42f;

            DrawSectionLabel(
                new Rect(inRect.x, y, inRect.width, 22f),
                "MAP_MechanoidMechanitor.Symbiosis.Dev.Utilities".Translate());
            y += 26f;
            DrawButtonRow(
                new Rect(inRect.x, y, inRect.width, 32f),
                new[]
                {
                    "MAP_MechanoidMechanitor.Symbiosis.Dev.RecreateRecord"
                        .Translate().ToString(),
                    "MAP_MechanoidMechanitor.Symbiosis.Dev.ResetLimits"
                        .Translate().ToString()
                },
                new Action[]
                {
                    () => state.DevRecreateRecord(selectedFaction),
                    () => state.DevResetSourceLimits(selectedFaction)
                });
            y += 42f;

            DrawSectionLabel(
                new Rect(inRect.x, y, inRect.width, 22f),
                "MAP_MechanoidMechanitor.Symbiosis.Dev.Declaration".Translate());
            y += 26f;
            DrawButtonRow(
                new Rect(inRect.x, y, inRect.width, 32f),
                new[]
                {
                    "MAP_MechanoidMechanitor.Symbiosis.Dev.BroadcastDeclaration"
                        .Translate().ToString()
                },
                new Action[] { () => state.DevBroadcastDeclaration() });
            y += 42f;

            DrawSectionLabel(
                new Rect(inRect.x, y, inRect.width, 22f),
                "MAP_MechanoidMechanitor.Symbiosis.Dev.Proposal".Translate());
            y += 26f;
            DrawButtonRow(
                new Rect(inRect.x, y, inRect.width, 32f),
                new[]
                {
                    "MAP_MechanoidMechanitor.Symbiosis.Dev.BeginProposal"
                        .Translate().ToString(),
                    "MAP_MechanoidMechanitor.Symbiosis.Dev.ResolveProposal"
                        .Translate().ToString()
                },
                new Action[]
                {
                    () => state.DevBeginProposal(selectedFaction),
                    () => state.DevResolveProposal(selectedFaction, null)
                });
            y += 38f;
            DrawButtonRow(
                new Rect(inRect.x, y, inRect.width, 32f),
                new[]
                {
                    "MAP_MechanoidMechanitor.Symbiosis.Dev.ForceProposalSuccess"
                        .Translate().ToString(),
                    "MAP_MechanoidMechanitor.Symbiosis.Dev.ForceProposalFailure"
                        .Translate().ToString(),
                    "MAP_MechanoidMechanitor.Symbiosis.Dev.ClearCooldown"
                        .Translate().ToString()
                },
                new Action[]
                {
                    () => state.DevResolveProposal(selectedFaction, true),
                    () => state.DevResolveProposal(selectedFaction, false),
                    () => state.DevClearInvitationCooldown(selectedFaction)
                });
            y += 42f;

            DrawSectionLabel(
                new Rect(inRect.x, y, inRect.width, 22f),
                "MAP_MechanoidMechanitor.Symbiosis.Dev.Counters".Translate());
            y += 26f;
            DrawButtonRow(
                new Rect(inRect.x, y, inRect.width, 32f),
                new[] { "invFail=0", "invFail=1", "invFail=2", "invFail=3" },
                new Action[]
                {
                    () => state.DevSetInvitationFailureCount(selectedFaction, 0),
                    () => state.DevSetInvitationFailureCount(selectedFaction, 1),
                    () => state.DevSetInvitationFailureCount(selectedFaction, 2),
                    () => state.DevSetInvitationFailureCount(selectedFaction, 3)
                });
            y += 38f;
            DrawButtonRow(
                new Rect(inRect.x, y, inRect.width, 32f),
                new[] { "exitCount=0", "exitCount=1", "exitCount=2" },
                new Action[]
                {
                    () => state.DevSetCovenantExitCount(selectedFaction, 0),
                    () => state.DevSetCovenantExitCount(selectedFaction, 1),
                    () => state.DevSetCovenantExitCount(selectedFaction, 2)
                });
            y += 42f;

            DrawSectionLabel(
                new Rect(inRect.x, y, inRect.width, 22f),
                "MAP_MechanoidMechanitor.Symbiosis.Dev.Covenant".Translate());
            y += 26f;
            DrawButtonRow(
                new Rect(inRect.x, y, inRect.width, 32f),
                new[]
                {
                    "MAP_MechanoidMechanitor.Symbiosis.Dev.ForceJoin"
                        .Translate().ToString(),
                    "MAP_MechanoidMechanitor.Symbiosis.Dev.ForceLeave"
                        .Translate().ToString()
                },
                new Action[]
                {
                    () => state.DevForceJoinCovenant(selectedFaction),
                    () => state.DevForceLeaveCovenant(selectedFaction)
                });
            y += 42f;

            DrawSectionLabel(
                new Rect(inRect.x, y, inRect.width, 22f),
                "MAP_MechanoidMechanitor.Symbiosis.Dev.Unity".Translate());
            y += 26f;
            DrawButtonRow(
                new Rect(inRect.x, y, inRect.width, 32f),
                new[]
                {
                    "MAP_MechanoidMechanitor.Symbiosis.Dev.UnityMinus100"
                        .Translate().ToString(),
                    "MAP_MechanoidMechanitor.Symbiosis.Dev.UnityPlus100"
                        .Translate().ToString()
                },
                new Action[]
                {
                    () => state.DevChangeUnity(-100f),
                    () => state.DevChangeUnity(100f)
                });
            y += 38f;
            DrawButtonRow(
                new Rect(inRect.x, y, inRect.width, 32f),
                new[] { "0", "100", "250", "450" },
                new Action[]
                {
                    () => state.DevSetUnity(0f),
                    () => state.DevSetUnity(100f),
                    () => state.DevSetUnity(250f),
                    () => state.DevSetUnity(450f)
                });
            y += 38f;
            DrawButtonRow(
                new Rect(inRect.x, y, inRect.width, 32f),
                new[]
                {
                    "700",
                    "MAP_MechanoidMechanitor.Symbiosis.Dev.UpdateUnityDaily"
                        .Translate().ToString(),
                    "MAP_MechanoidMechanitor.Symbiosis.Dev.RecalcLevel"
                        .Translate().ToString()
                },
                new Action[]
                {
                    () => state.DevSetUnity(700f),
                    () => state.DevUpdateUnityDaily(),
                    () => state.DevRecalculateCovenantLevel()
                });
            y += 42f;

            DrawSectionLabel(
                new Rect(inRect.x, y, inRect.width, 22f),
                "MAP_MechanoidMechanitor.Symbiosis.Dev.State".Translate());
            y += 26f;
            Text.Font = GameFont.Tiny;
            Text.WordWrap = true;
            float stateHeight = inRect.yMax - y - 10f;
            if (stateHeight > 20f)
            {
                Widgets.DrawBoxSolid(
                    new Rect(inRect.x, y, inRect.width, stateHeight),
                    new Color(0.06f, 0.08f, 0.085f, 0.96f));
                Widgets.Label(
                    new Rect(inRect.x + 10f, y + 8f, inRect.width - 20f, stateHeight - 16f),
                    BuildStateText(state, record));
            }
        }

        private void DrawGlobalState(
            GameComponent_SymbiosisCovenantState state,
            Rect inRect,
            float y)
        {
            Text.Font = GameFont.Tiny;
            Text.WordWrap = true;
            float stateHeight = inRect.yMax - y - 10f;
            if (stateHeight > 20f)
            {
                Widgets.DrawBoxSolid(
                    new Rect(inRect.x, y, inRect.width, stateHeight),
                    new Color(0.06f, 0.08f, 0.085f, 0.96f));
                Widgets.Label(
                    new Rect(inRect.x + 10f, y + 8f, inRect.width - 20f, stateHeight - 16f),
                    BuildStateText(state, null));
            }
        }

        private static string DevReason()
        {
            return "MAP_MechanoidMechanitor.Symbiosis.TrustReason.Dev".Translate();
        }

        private static string BuildStateText(
            GameComponent_SymbiosisCovenantState state,
            SymbiosisCovenantFactionRecord? record)
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.AppendLine(
                "PublicDeclaration = " + state.PublicDeclarationBroadcast);
            builder.AppendLine("Unity = " + state.Unity);
            builder.AppendLine("CovenantLevel = " + state.CovenantLevel);
            builder.AppendLine("HighestCovenantLevel = " + state.HighestCovenantLevel);
            builder.AppendLine("CovenantMemberCount = " + state.CovenantMemberCount);

            Faction? mechHive = MechanoidMechanitorOrdinaryFactionUtility.TryGetMechHive();
            if (mechHive != null)
            {
                FactionRelation? relation =
                    Faction.OfPlayerSilentFail?.RelationWith(mechHive, allowNull: true);
                builder.AppendLine(
                    "MechHiveRelation = "
                    + (relation?.kind.ToString() ?? "NO DATA"));
            }
            else
            {
                builder.AppendLine(
                    "MechHiveRelation = "
                    + "MAP_MechanoidMechanitor.Symbiosis.Dev.MechHiveNone"
                        .Translate());
            }

            if (record == null)
            {
                return builder.ToString();
            }

            builder.AppendLine("----");
            builder.AppendLine("Trust = " + record.Trust);
            builder.AppendLine(
                "Stage = "
                + GameComponent_SymbiosisCovenantState.GetStageLabel(record.Trust));
            builder.AppendLine("PreDeclarationLock = " + record.PreDeclarationLock);
            builder.AppendLine("CovenantMember = " + record.CovenantMember);
            builder.AppendLine("ProposalResolutionTick = " + record.ProposalResolutionTick);
            builder.AppendLine("ProposalPending = " + record.ProposalPending);
            builder.AppendLine(
                "SuccessChance = " + state.GetInvitationSuccessChance(record) + "%");
            builder.AppendLine("InvitationFailureCount = " + record.InvitationFailureCount);
            builder.AppendLine(
                "NextInvitationTick = " + record.NextInvitationTick);
            if (record.NextInvitationTick > currentTick)
            {
                builder.AppendLine(
                    "CooldownRemaining = "
                    + GenDate.ToStringTicksToPeriod(record.NextInvitationTick - currentTick));
            }

            builder.AppendLine("CovenantExitCount = " + record.CovenantExitCount);
            builder.AppendLine(
                "PermanentlyRefusesInvitation = " + record.PermanentlyRefusesInvitation);
            return builder.ToString();
        }

        private static void DrawSectionLabel(Rect rect, string label)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(rect, label);
        }

        private static void DrawButtonRow(
            Rect rect,
            string[] labels,
            Action[] actions)
        {
            const float gap = 8f;
            float width = (rect.width - gap * (labels.Length - 1)) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                Rect buttonRect = new Rect(
                    rect.x + i * (width + gap),
                    rect.y,
                    width,
                    rect.height);
                if (Widgets.ButtonText(buttonRect, labels[i]))
                {
                    actions[i]();
                }
            }
        }
    }
}
