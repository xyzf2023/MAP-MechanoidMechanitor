using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class Dialog_SymbiosisCovenant : Window
    {
        private const float HeaderHeight = 58f;
        private const float FooterHeight = 38f;
        private const float MainGap = 14f;
        private const float DetailsWidth = 350f;
        private const float CellGap = 8f;
        private const float CellHeaderHeight = 26f;
        private const float CellFooterHeight = 44f;

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
        private static readonly Color EmptyCellColor =
            new Color(0.055f, 0.075f, 0.08f, 1f);

        private Faction? selectedFaction;
        private int gridPage;
        private Vector2 optionScrollPosition;

        public override Vector2 InitialSize => new Vector2(1120f, 780f);

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
            IReadOnlyList<SymbiosisCovenantFactionRecord> records =
                state.GetRecordsSorted();
            EnsureSelection(records);

            Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight);
            Rect footerRect = new Rect(
                inRect.x,
                inRect.yMax - FooterHeight,
                inRect.width,
                FooterHeight);
            Rect bodyRect = new Rect(
                inRect.x,
                headerRect.yMax + 8f,
                inRect.width,
                footerRect.y - headerRect.yMax - 16f);
            Rect detailsRect = new Rect(
                bodyRect.xMax - DetailsWidth,
                bodyRect.y,
                DetailsWidth,
                bodyRect.height);
            Rect gridRect = new Rect(
                bodyRect.x,
                bodyRect.y,
                detailsRect.x - bodyRect.x - MainGap,
                bodyRect.height);

            DrawHeader(headerRect, state);
            DrawConferenceGrid(gridRect, records);
            DrawDetailsPanel(detailsRect, state, FindRecord(records, selectedFaction));
            DrawFooter(footerRect, records);
        }

        private static void DrawHeader(
            Rect rect,
            GameComponent_SymbiosisCovenantState state)
        {
            Widgets.DrawBoxSolid(rect, PanelColor);
            DrawOutline(rect, 1, TealColor);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), TealColor);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            try
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                Widgets.Label(
                    new Rect(rect.x + 14f, rect.y + 8f, rect.width * 0.62f, 28f),
                    "MAP_MechanoidMechanitor.Symbiosis.Title".Translate());

                Text.Font = GameFont.Tiny;
                GUI.color = MutedTextColor;
                Widgets.Label(
                    new Rect(rect.x + 15f, rect.y + 35f, rect.width * 0.62f, 18f),
                    "MAP_MechanoidMechanitor.Symbiosis.Subtitle".Translate());

                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = TealColor;
                Widgets.Label(
                    new Rect(rect.xMax - 360f, rect.y + 8f, 345f, 22f),
                    "MAP_MechanoidMechanitor.Symbiosis.Seats".Translate(
                        state.CovenantMemberCount,
                        state.TargetMemberCount));
                GUI.color = state.MechHiveRetaliationTriggered
                    ? new Color(1f, 0.38f, 0.30f)
                    : MutedTextColor;
                Widgets.Label(
                    new Rect(rect.xMax - 360f, rect.y + 31f, 345f, 18f),
                    state.MechHiveRetaliationTriggered
                        ? "MAP_MechanoidMechanitor.Symbiosis.OvermindDetected".Translate()
                        : "MAP_MechanoidMechanitor.Symbiosis.OvermindUndetected".Translate());
            }
            finally
            {
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                GUI.color = previousColor;
            }
        }

        private void DrawConferenceGrid(
            Rect rect,
            IReadOnlyList<SymbiosisCovenantFactionRecord> records)
        {
            Widgets.DrawBoxSolid(rect, PanelColor);
            DrawOutline(rect, 1, PanelOutlineColor);

            Rect inner = rect.ContractedBy(10f);
            int dimension = GetGridDimension(records.Count);
            int capacity = dimension * dimension;
            int pageCount = Math.Max(1, Mathf.CeilToInt((float)records.Count / capacity));
            gridPage = Mathf.Clamp(gridPage, 0, pageCount - 1);

            float gridSize = Mathf.Min(inner.width, inner.height);
            Rect square = new Rect(
                inner.x + (inner.width - gridSize) * 0.5f,
                inner.y + (inner.height - gridSize) * 0.5f,
                gridSize,
                gridSize);
            float cellSize =
                (gridSize - CellGap * (dimension - 1)) / dimension;
            int firstIndex = gridPage * capacity;

            DrawDataPulse(square, dimension);
            for (int row = 0; row < dimension; row++)
            {
                for (int column = 0; column < dimension; column++)
                {
                    int slot = row * dimension + column;
                    int recordIndex = firstIndex + slot;
                    Rect cellRect = new Rect(
                        square.x + column * (cellSize + CellGap),
                        square.y + row * (cellSize + CellGap),
                        cellSize,
                        cellSize);
                    if (recordIndex < records.Count)
                    {
                        DrawFactionCell(cellRect, records[recordIndex]);
                    }
                    else
                    {
                        DrawEmptyCell(cellRect);
                    }
                }
            }
        }

        private void DrawFactionCell(
            Rect rect,
            SymbiosisCovenantFactionRecord record)
        {
            Faction? faction = record.Faction;
            if (faction == null)
            {
                DrawEmptyCell(rect);
                return;
            }

            bool selected = faction == selectedFaction;
            float pulse = 0.35f + Mathf.PingPong(Time.realtimeSinceStartup * 0.55f, 0.35f);
            Color factionColor = faction.Color;
            Color background = selected
                ? Color.Lerp(PanelColor, factionColor, pulse * 0.28f)
                : new Color(0.065f, 0.085f, 0.09f, 1f);
            Widgets.DrawBoxSolid(rect, background);
            DrawOutline(rect, selected ? 2 : 1, factionColor);
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }

            Rect headerRect = new Rect(
                rect.x + 6f,
                rect.y + 3f,
                rect.width - 12f,
                CellHeaderHeight);
            DrawCenteredLabel(
                headerRect,
                faction.Name.Truncate(headerRect.width),
                GameFont.Tiny,
                factionColor);

            Rect portraitRect = new Rect(
                rect.x + 10f,
                headerRect.yMax + 3f,
                rect.width - 20f,
                Mathf.Max(
                    20f,
                    rect.height - CellHeaderHeight - CellFooterHeight - 18f));
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
                    portraitRect,
                    "MAP_MechanoidMechanitor.Symbiosis.NoLeader".Translate());
            }

            Rect stageRect = new Rect(
                rect.x + 6f,
                portraitRect.yMax + 2f,
                rect.width - 12f,
                18f);
            string leaderName = leader?.LabelShortCap ?? "NO DATA";
            DrawCenteredLabel(
                stageRect,
                leaderName.Truncate(stageRect.width),
                GameFont.Tiny,
                MutedTextColor);

            Rect barRect = new Rect(
                rect.x + 8f,
                rect.yMax - 18f,
                rect.width - 16f,
                10f);
            DrawTrustBar(barRect, record.Trust);

            if (record.CovenantMember)
            {
                Rect memberRect = new Rect(rect.x + 4f, rect.y + 4f, 58f, 18f);
                Widgets.DrawBoxSolid(memberRect, new Color(0.07f, 0.30f, 0.25f, 0.95f));
                DrawCenteredLabel(
                    memberRect,
                    "MAP_MechanoidMechanitor.Symbiosis.MemberBadge".Translate(),
                    GameFont.Tiny,
                    TealColor);
            }

            if (Widgets.ButtonInvisible(rect, doMouseoverSound: true))
            {
                if (selectedFaction != faction)
                {
                    selectedFaction = faction;
                    optionScrollPosition = Vector2.zero;
                }
            }
        }

        private static void DrawEmptyCell(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, EmptyCellColor);
            DrawOutline(rect, 1, new Color(0.18f, 0.26f, 0.27f, 0.7f));
            DrawScanLines(rect);
            DrawNoData(
                rect.ContractedBy(10f),
                "MAP_MechanoidMechanitor.Symbiosis.EmptyChannel".Translate());
        }

        private void DrawDetailsPanel(
            Rect rect,
            GameComponent_SymbiosisCovenantState state,
            SymbiosisCovenantFactionRecord? record)
        {
            Widgets.DrawBoxSolid(rect, PanelColor);
            DrawOutline(rect, 1, PanelOutlineColor);
            Rect inner = rect.ContractedBy(12f);

            if (record?.Faction == null)
            {
                DrawNoData(
                    inner,
                    "MAP_MechanoidMechanitor.Symbiosis.NoFactionSelected".Translate());
                return;
            }

            Faction faction = record.Faction;
            Faction? player = Faction.OfPlayerSilentFail;
            FactionRelation? relation = player?.RelationWith(faction, allowNull: true);

            float y = inner.y;
            DrawLeftLabel(
                new Rect(inner.x, y, inner.width, 27f),
                faction.Name,
                GameFont.Medium,
                faction.Color);
            y += 31f;
            DrawLeftLabel(
                new Rect(inner.x, y, inner.width, 20f),
                "MAP_MechanoidMechanitor.Symbiosis.Leader".Translate(
                    faction.leader?.LabelShortCap ?? "NO DATA"),
                GameFont.Tiny,
                MutedTextColor);
            y += 24f;

            Rect trustBar = new Rect(inner.x, y, inner.width, 18f);
            DrawTrustBar(trustBar, record.Trust);
            y += 23f;
            DrawLeftLabel(
                new Rect(inner.x, y, inner.width, 18f),
                "MAP_MechanoidMechanitor.Symbiosis.Trust".Translate(
                    record.Trust,
                    record.CovenantMember
                        ? "MAP_MechanoidMechanitor.Symbiosis.Stage.Member".Translate()
                        : GameComponent_SymbiosisCovenantState.GetStageLabel(record.Trust)),
                GameFont.Tiny,
                Color.white);
            y += 20f;
            DrawLeftLabel(
                new Rect(inner.x, y, inner.width, 18f),
                "MAP_MechanoidMechanitor.Symbiosis.Goodwill".Translate(
                    relation?.baseGoodwill ?? 0,
                    relation?.kind.ToString() ?? "NO DATA"),
                GameFont.Tiny,
                MutedTextColor);
            y += 28f;

            Widgets.DrawBoxSolid(new Rect(inner.x, y, inner.width, 1f), PanelOutlineColor);
            y += 8f;
            DrawLeftLabel(
                new Rect(inner.x, y, inner.width, 20f),
                "MAP_MechanoidMechanitor.Symbiosis.RecentChanges".Translate(),
                GameFont.Small,
                TealColor);
            y += 23f;

            IReadOnlyList<SymbiosisCovenantTrustChange> changes = record.RecentChanges;
            int first = Math.Max(0, changes.Count - 5);
            if (first >= changes.Count)
            {
                DrawLeftLabel(
                    new Rect(inner.x, y, inner.width, 18f),
                    "MAP_MechanoidMechanitor.Symbiosis.NoChanges".Translate(),
                    GameFont.Tiny,
                    MutedTextColor);
                y += 20f;
            }
            else
            {
                for (int i = changes.Count - 1; i >= first; i--)
                {
                    SymbiosisCovenantTrustChange change = changes[i];
                    string line =
                        (change.Amount > 0 ? "+" : string.Empty)
                        + change.Amount
                        + "  "
                        + change.Reason;
                    DrawLeftLabel(
                        new Rect(inner.x, y, inner.width, 18f),
                        line.Truncate(inner.width),
                        GameFont.Tiny,
                        change.Amount >= 0 ? TealColor : new Color(1f, 0.45f, 0.38f));
                    y += 19f;
                }
            }

            y += 5f;
            Widgets.DrawBoxSolid(new Rect(inner.x, y, inner.width, 1f), PanelOutlineColor);
            y += 8f;
            DrawLeftLabel(
                new Rect(inner.x, y, inner.width, 20f),
                "MAP_MechanoidMechanitor.Symbiosis.Options".Translate(),
                GameFont.Small,
                TealColor);
            y += 24f;

            Rect optionRect = new Rect(
                inner.x,
                y,
                inner.width,
                Mathf.Max(40f, inner.yMax - y));
            DrawOptions(optionRect, state, record);
        }

        private void DrawOptions(
            Rect rect,
            GameComponent_SymbiosisCovenantState state,
            SymbiosisCovenantFactionRecord record)
        {
            List<SymbiosisCovenantOptionDef> options =
                DefDatabase<SymbiosisCovenantOptionDef>
                    .AllDefsListForReading
                    .OrderBy(def => def.displayOrder)
                    .ThenBy(def => def.defName, StringComparer.Ordinal)
                    .Where(def => def.Worker.ShouldShow(state, record))
                    .ToList();
            float contentHeight = Math.Max(
                rect.height,
                options.Count * 62f - (options.Count > 0 ? 6f : 0f));
            Rect viewRect = new Rect(
                0f,
                0f,
                rect.width - (contentHeight > rect.height ? 18f : 0f),
                contentHeight);
            Widgets.BeginScrollView(rect, ref optionScrollPosition, viewRect);
            try
            {
                float y = 0f;
                for (int i = 0; i < options.Count; i++)
                {
                    SymbiosisCovenantOptionDef option = options[i];
                    bool enabled = option.Worker.CanExecute(
                        state,
                        record,
                        out string? disabledReason);
                    Rect rowRect = new Rect(0f, y, viewRect.width, 56f);
                    Widgets.DrawBoxSolid(
                        rowRect,
                        new Color(0.055f, 0.08f, 0.085f, 0.95f));
                    DrawOutline(
                        rowRect,
                        1,
                        new Color(0.15f, 0.32f, 0.31f, 0.8f));

                    Rect textRect = new Rect(
                        rowRect.x + 8f,
                        rowRect.y + 5f,
                        rowRect.width - 116f,
                        46f);
                    DrawLeftLabel(
                        new Rect(textRect.x, textRect.y, textRect.width, 19f),
                        option.LabelCap,
                        GameFont.Tiny,
                        enabled ? Color.white : MutedTextColor);
                    DrawLeftLabel(
                        new Rect(textRect.x, textRect.y + 20f, textRect.width, 23f),
                        (option.description ?? string.Empty).Truncate(textRect.width),
                        GameFont.Tiny,
                        MutedTextColor);

                    Rect buttonRect = new Rect(
                        rowRect.xMax - 102f,
                        rowRect.y + 13f,
                        92f,
                        30f);
                    if (Widgets.ButtonText(
                            buttonRect,
                            "MAP_MechanoidMechanitor.Symbiosis.Option.Execute".Translate(),
                            active: enabled))
                    {
                        option.Worker.Execute(state, record);
                    }

                    if (!enabled && !string.IsNullOrEmpty(disabledReason))
                    {
                        TooltipHandler.TipRegion(rowRect, disabledReason);
                    }

                    y += 62f;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawFooter(
            Rect rect,
            IReadOnlyList<SymbiosisCovenantFactionRecord> records)
        {
            int dimension = GetGridDimension(records.Count);
            int capacity = dimension * dimension;
            int pageCount = Math.Max(1, Mathf.CeilToInt((float)records.Count / capacity));

            Rect closeRect = new Rect(rect.xMax - 150f, rect.y + 4f, 150f, 30f);
            if (Widgets.ButtonText(
                    closeRect,
                    "MAP_MechanoidMechanitor.Symbiosis.Disconnect".Translate()))
            {
                Close();
            }

            if (Prefs.DevMode)
            {
                Rect devRect = new Rect(rect.xMax - 230f, rect.y + 4f, 70f, 30f);
                if (Widgets.ButtonText(devRect, "DEV"))
                {
                    Find.WindowStack.Add(
                        new Dialog_SymbiosisCovenantDev(selectedFaction));
                }
            }

            if (pageCount > 1)
            {
                Rect previousRect = new Rect(rect.x, rect.y + 4f, 88f, 30f);
                Rect nextRect = new Rect(rect.x + 184f, rect.y + 4f, 88f, 30f);
                Rect pageRect = new Rect(rect.x + 94f, rect.y + 4f, 84f, 30f);
                if (Widgets.ButtonText(previousRect, "<", active: gridPage > 0))
                {
                    gridPage--;
                }

                DrawCenteredLabel(
                    pageRect,
                    (gridPage + 1) + " / " + pageCount,
                    GameFont.Tiny,
                    MutedTextColor);
                if (Widgets.ButtonText(nextRect, ">", active: gridPage < pageCount - 1))
                {
                    gridPage++;
                }
            }
        }

        private static void DrawConnectionLost(Rect rect)
        {
            DrawNoData(
                rect.ContractedBy(40f),
                "MAP_MechanoidMechanitor.Symbiosis.ConnectionLost".Translate());
        }

        private void EnsureSelection(
            IReadOnlyList<SymbiosisCovenantFactionRecord> records)
        {
            if (FindRecord(records, selectedFaction) != null)
            {
                return;
            }

            selectedFaction = records.Count > 0 ? records[0].Faction : null;
        }

        private static SymbiosisCovenantFactionRecord? FindRecord(
            IReadOnlyList<SymbiosisCovenantFactionRecord> records,
            Faction? faction)
        {
            if (faction == null)
            {
                return null;
            }

            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].Faction == faction)
                {
                    return records[i];
                }
            }

            return null;
        }

        private static int GetGridDimension(int count)
        {
            if (count <= 4)
            {
                return 2;
            }

            return count <= 9 ? 3 : 4;
        }

        private static void DrawTrustBar(Rect rect, int trust)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.025f, 0.035f, 0.04f, 1f));
            float zeroX = rect.x
                + rect.width
                * (-GameComponent_SymbiosisCovenantState.MinimumTrust)
                / (GameComponent_SymbiosisCovenantState.MaximumTrust
                    - GameComponent_SymbiosisCovenantState.MinimumTrust);
            if (trust < 0)
            {
                float negativeFraction = Mathf.Clamp01(
                    (float)-trust
                    / -GameComponent_SymbiosisCovenantState.MinimumTrust);
                float width = (zeroX - rect.x) * negativeFraction;
                Widgets.DrawBoxSolid(
                    new Rect(zeroX - width, rect.y, width, rect.height),
                    trust >= -50
                        ? new Color(0.80f, 0.48f, 0.22f, 1f)
                        : new Color(0.65f, 0.26f, 0.23f, 1f));
            }
            else if (trust > 0)
            {
                float positiveFraction = Mathf.Clamp01(
                    (float)trust
                    / GameComponent_SymbiosisCovenantState.MaximumTrust);
                float width = (rect.xMax - zeroX) * positiveFraction;
                Color color = trust >= 150
                    ? new Color(0.20f, 0.85f, 0.52f, 1f)
                    : trust >= 100
                        ? new Color(0.24f, 0.75f, 0.68f, 1f)
                        : trust >= 50
                            ? TealColor
                            : new Color(0.87f, 0.70f, 0.25f, 1f);
                Widgets.DrawBoxSolid(
                    new Rect(zeroX, rect.y, width, rect.height),
                    color);
            }

            Widgets.DrawBoxSolid(
                new Rect(zeroX - 1f, rect.y, 2f, rect.height),
                new Color(0.74f, 0.82f, 0.80f, 0.8f));
            DrawOutline(rect, 1, new Color(0.25f, 0.42f, 0.41f, 0.9f));
        }

        private static void DrawDataPulse(Rect rect, int dimension)
        {
            float phase = Mathf.Repeat(Time.realtimeSinceStartup * 42f, rect.width);
            Color pulseColor = new Color(0.18f, 0.68f, 0.61f, 0.18f);
            for (int i = 1; i < dimension; i++)
            {
                float x = rect.x + rect.width * i / dimension;
                Widgets.DrawBoxSolid(new Rect(x - 1f, rect.y, 2f, rect.height), pulseColor);
                float y = rect.y + rect.height * i / dimension;
                Widgets.DrawBoxSolid(new Rect(rect.x, y - 1f, rect.width, 2f), pulseColor);
            }

            Widgets.DrawBoxSolid(
                new Rect(rect.x + phase, rect.y, 2f, rect.height),
                new Color(0.3f, 0.9f, 0.78f, 0.12f));
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

        private static void DrawNoData(Rect rect, string text)
        {
            DrawScanLines(rect);
            DrawCenteredLabel(rect, text, GameFont.Tiny, MutedTextColor);
        }

        private static void DrawCenteredLabel(
            Rect rect,
            string text,
            GameFont font,
            Color color)
        {
            DrawLabel(rect, text, font, color, TextAnchor.MiddleCenter);
        }

        private static void DrawLeftLabel(
            Rect rect,
            string text,
            GameFont font,
            Color color)
        {
            DrawLabel(rect, text, font, color, TextAnchor.MiddleLeft);
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
        private readonly Faction? faction;

        public override Vector2 InitialSize => new Vector2(760f, 610f);

        public Dialog_SymbiosisCovenantDev(Faction? faction)
        {
            this.faction = faction;
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
            SymbiosisCovenantFactionRecord? record = state?.GetRecord(faction);

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

                Text.Font = GameFont.Small;
                Widgets.Label(
                    new Rect(inRect.x, inRect.y + 38f, inRect.width, 26f),
                    record?.Faction?.Name
                    ?? "MAP_MechanoidMechanitor.Symbiosis.NoFactionSelected"
                        .Translate()
                        .ToString());

                if (state == null || record?.Faction == null)
                {
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(
                        new Rect(inRect.x, inRect.y + 75f, inRect.width, 50f),
                        "MAP_MechanoidMechanitor.Symbiosis.Dev.NoRecord".Translate());
                    return;
                }

                float y = inRect.y + 75f;
                DrawSectionLabel(
                    new Rect(inRect.x, y, inRect.width, 24f),
                    "MAP_MechanoidMechanitor.Symbiosis.Dev.Adjust".Translate());
                y += 28f;
                DrawButtonRow(
                    new Rect(inRect.x, y, inRect.width, 34f),
                    new[] { "-25", "-1", "+1", "+25" },
                    new Action[]
                    {
                        () => Adjust(state, -25),
                        () => Adjust(state, -1),
                        () => Adjust(state, 1),
                        () => Adjust(state, 25)
                    });

                y += 46f;
                DrawSectionLabel(
                    new Rect(inRect.x, y, inRect.width, 24f),
                    "MAP_MechanoidMechanitor.Symbiosis.Dev.Set".Translate());
                y += 28f;
                DrawButtonRow(
                    new Rect(inRect.x, y, inRect.width, 34f),
                    new[] { "-100", "-50", "0", "1" },
                    new Action[]
                    {
                        () => SetTrust(state, -100),
                        () => SetTrust(state, -50),
                        () => SetTrust(state, 0),
                        () => SetTrust(state, 1)
                    });
                y += 38f;
                DrawButtonRow(
                    new Rect(inRect.x, y, inRect.width, 34f),
                    new[] { "50", "100", "150", "200" },
                    new Action[]
                    {
                        () => SetTrust(state, 50),
                        () => SetTrust(state, 100),
                        () => SetTrust(state, 150),
                        () => SetTrust(state, 200)
                    });

                y += 48f;
                DrawSectionLabel(
                    new Rect(inRect.x, y, inRect.width, 24f),
                    "MAP_MechanoidMechanitor.Symbiosis.Dev.Utilities".Translate());
                y += 28f;
                DrawButtonRow(
                    new Rect(inRect.x, y, inRect.width, 34f),
                    new string[]
                    {
                        "MAP_MechanoidMechanitor.Symbiosis.Dev.ResetLimits"
                            .Translate()
                            .ToString(),
                        "MAP_MechanoidMechanitor.Symbiosis.Dev.ReplayMilestone"
                            .Translate()
                            .ToString(),
                        "MAP_MechanoidMechanitor.Symbiosis.Dev.RecreateRecord"
                            .Translate()
                            .ToString()
                    },
                    new Action[]
                    {
                        () => state.DevResetSourceLimits(faction),
                        () => ReplayMilestone(state),
                        () => state.DevRecreateRecord(faction)
                    });

                record = state.GetRecord(faction);
                y += 50f;
                DrawSectionLabel(
                    new Rect(inRect.x, y, inRect.width, 24f),
                    "MAP_MechanoidMechanitor.Symbiosis.Dev.State".Translate());
                y += 28f;
                Text.Font = GameFont.Tiny;
                Text.WordWrap = true;
                Widgets.DrawBoxSolid(
                    new Rect(inRect.x, y, inRect.width, inRect.yMax - y),
                    new Color(0.06f, 0.08f, 0.085f, 0.96f));
                Widgets.Label(
                    new Rect(
                        inRect.x + 10f,
                        y + 8f,
                        inRect.width - 20f,
                        inRect.yMax - y - 16f),
                    BuildStateText(state, record));
            }
            finally
            {
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWordWrap;
            }
        }

        private void Adjust(
            GameComponent_SymbiosisCovenantState state,
            int amount)
        {
            state.DevAdjustTrust(
                faction,
                amount,
                "MAP_MechanoidMechanitor.Symbiosis.TrustReason.Dev".Translate());
        }

        private void SetTrust(
            GameComponent_SymbiosisCovenantState state,
            int value)
        {
            state.DevSetTrust(
                faction,
                value,
                "MAP_MechanoidMechanitor.Symbiosis.TrustReason.Dev".Translate());
        }

        private void ReplayMilestone(GameComponent_SymbiosisCovenantState state)
        {
            if (!state.DevReplayCurrentMilestone(faction))
            {
                Messages.Message(
                    "MAP_MechanoidMechanitor.Symbiosis.Dev.NoMilestone".Translate(),
                    MessageTypeDefOf.RejectInput,
                    historical: false);
            }
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

        private static string BuildStateText(
            GameComponent_SymbiosisCovenantState state,
            SymbiosisCovenantFactionRecord? record)
        {
            if (record == null)
            {
                return "MAP_MechanoidMechanitor.Symbiosis.Dev.NoRecord".Translate();
            }

            return
                "Trust = " + record.Trust
                + "\nStage = "
                + GameComponent_SymbiosisCovenantState.GetStageLabel(record.Trust)
                + "\nHighestReachedMilestone = " + record.HighestReachedMilestone
                + "\nHighestAppliedMilestone = " + record.HighestAppliedMilestone
                + "\nCovenantMember = " + record.CovenantMember
                + "\nGoodwillWindow = " + record.GoodwillWindowStartTick
                + " / " + record.GoodwillTrustGainedInWindow
                + "\nTradeWindow = " + record.TradeWindowStartTick
                + " / " + record.TradeTrustGainedInWindow
                + "\nLastBetrayalTick = " + record.LastBetrayalTick
                + "\nPublicDeclaration = " + state.PublicDeclarationBroadcast
                + "\nMechHiveRetaliation = " + state.MechHiveRetaliationTriggered;
        }
    }
}
