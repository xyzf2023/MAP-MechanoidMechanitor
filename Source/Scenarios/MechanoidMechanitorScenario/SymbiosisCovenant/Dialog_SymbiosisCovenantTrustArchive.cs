using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class Dialog_SymbiosisCovenantTrustArchive : Window
    {
        private const float RowHeight = 116f;
        private const float RowGap = 8f;
        private Vector2 scrollPosition;

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

        public override Vector2 InitialSize => new Vector2(1180f, 720f);

        public Dialog_SymbiosisCovenantTrustArchive()
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
                DrawText(
                    inRect.ContractedBy(40f),
                    "MAP_MechanoidMechanitor.Symbiosis.ConnectionLost".Translate(),
                    GameFont.Medium);
                return;
            }

            state.SynchronizeNow();

            Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, 36f);
            Widgets.DrawBoxSolid(headerRect, PanelColor);
            DrawText(
                new Rect(headerRect.x + 14f, headerRect.y + 8f, headerRect.width - 28f, 22f),
                "MAP_MechanoidMechanitor.Symbiosis.Archive.Title".Translate(),
                GameFont.Medium,
                Color.white);

            Rect bodyRect = new Rect(
                inRect.x,
                headerRect.yMax + 8f,
                inRect.width,
                inRect.yMax - headerRect.yMax - 8f);

            List<SymbiosisCovenantFactionRecord> records =
                state.GetRecordsSorted().Where(r => r.Faction != null).ToList();
            if (records.Count == 0)
            {
                DrawText(
                    bodyRect.ContractedBy(20f),
                    "MAP_MechanoidMechanitor.Symbiosis.NoMembers".Translate(),
                    GameFont.Small);
                return;
            }

            Rect viewRect = new Rect(
                0f,
                0f,
                bodyRect.width - 18f,
                records.Count * (RowHeight + RowGap));
            Widgets.BeginScrollView(bodyRect, ref scrollPosition, viewRect);
            try
            {
                for (int i = 0; i < records.Count; i++)
                {
                    Rect rowRect = new Rect(
                        0f,
                        i * (RowHeight + RowGap),
                        bodyRect.width - 18f,
                        RowHeight);
                    DrawArchiveRow(rowRect, state, records[i]);
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private static void DrawArchiveRow(
            Rect rect,
            GameComponent_SymbiosisCovenantState state,
            SymbiosisCovenantFactionRecord record)
        {
            Faction? faction = record.Faction;
            Widgets.DrawBoxSolid(rect, PanelColor);
            DrawOutline(rect, 1, PanelOutlineColor);

            Rect inner = rect.ContractedBy(10f);
            float y = inner.y;

            // 标题行：派系名称 + 关系
            Faction? player = Faction.OfPlayerSilentFail;
            FactionRelation? relation =
                player?.RelationWith(faction, allowNull: true);
            FactionRelationKind kind = relation?.kind ?? FactionRelationKind.Hostile;
            int goodwill = player != null && faction != null
                ? player.GoodwillWith(faction)
                : 0;

            string title = faction?.Name ?? "NO DATA";
            DrawText(
                new Rect(inner.x, y, inner.width - 120f, 22f),
                title,
                GameFont.Small,
                faction?.Color ?? Color.white);
            DrawText(
                new Rect(inner.x + inner.width - 110f, y, 110f, 22f),
                GetRelationLabel(kind),
                GameFont.Tiny,
                MutedTextColor,
                TextAnchor.MiddleRight);
            y += 26f;

            // 数值行
            string line2 = "MAP_MechanoidMechanitor.Symbiosis.Archive.GoodwillTrust"
                .Translate(
                    goodwill,
                    GameComponent_SymbiosisCovenantState.GetStageLabel(record.Trust),
                    record.Trust);
            DrawText(
                new Rect(inner.x, y, inner.width, 18f),
                line2,
                GameFont.Tiny,
                Color.white);
            y += 20f;

            // 锁定与盟约状态
            string line3 = "MAP_MechanoidMechanitor.Symbiosis.Archive.LockStatus"
                .Translate(GetLockLabel(record.PreDeclarationLock))
                + "    "
                + "MAP_MechanoidMechanitor.Symbiosis.Archive.CovenantStatus"
                    .Translate(GetCovenantStatus(state, record));
            DrawText(
                new Rect(inner.x, y, inner.width, 18f),
                line3,
                GameFont.Tiny,
                MutedTextColor);
            y += 20f;

            // 邀请失败 / 退出 / 审议冷却
            string timing = GetTimingLabel(record);
            string line4 = "MAP_MechanoidMechanitor.Symbiosis.Archive.Counters"
                .Translate(
                    record.InvitationFailureCount,
                    record.CovenantExitCount,
                    timing);
            DrawText(
                new Rect(inner.x, y, inner.width, 18f),
                line4,
                GameFont.Tiny,
                MutedTextColor);
            y += 20f;

            // 近期变化
            IReadOnlyList<SymbiosisCovenantTrustChange> changes = record.RecentChanges;
            string recent = "MAP_MechanoidMechanitor.Symbiosis.Archive.Recent".Translate();
            if (changes.Count == 0)
            {
                recent += " " + "MAP_MechanoidMechanitor.Symbiosis.NoChanges".Translate();
            }
            else
            {
                int first = Math.Max(0, changes.Count - 3);
                for (int i = changes.Count - 1; i >= first; i--)
                {
                    SymbiosisCovenantTrustChange change = changes[i];
                    recent += "  "
                        + (change.Amount > 0 ? "+" : string.Empty)
                        + change.Amount
                        + " "
                        + change.Reason;
                }
            }

            DrawText(
                new Rect(inner.x, y, inner.width - (Prefs.DevMode ? 70f : 0f), 16f),
                recent.Truncate(inner.width - (Prefs.DevMode ? 80f : 10f)),
                GameFont.Tiny,
                TealColor);

            if (Prefs.DevMode && faction != null)
            {
                Faction localFaction = faction;
                Rect devRect = new Rect(
                    inner.x + inner.width - 60f,
                    inner.y + inner.height - 26f,
                    56f,
                    22f);
                if (Widgets.ButtonText(devRect, "DEV"))
                {
                    Find.WindowStack.Add(new Dialog_SymbiosisCovenantDev(localFaction));
                }
            }
        }

        private static string GetRelationLabel(FactionRelationKind kind)
        {
            switch (kind)
            {
                case FactionRelationKind.Ally:
                    return "MAP_MechanoidMechanitor.Symbiosis.Relation.Ally"
                        .Translate();
                case FactionRelationKind.Neutral:
                    return "MAP_MechanoidMechanitor.Symbiosis.Relation.Neutral"
                        .Translate();
                default:
                    return "MAP_MechanoidMechanitor.Symbiosis.Relation.Hostile"
                        .Translate();
            }
        }

        private static string GetLockLabel(SymbiosisPreDeclarationTrustLock lockState)
        {
            switch (lockState)
            {
                case SymbiosisPreDeclarationTrustLock.Hostile:
                    return "MAP_MechanoidMechanitor.Symbiosis.Archive.LockHostile"
                        .Translate();
                case SymbiosisPreDeclarationTrustLock.Neutral:
                    return "MAP_MechanoidMechanitor.Symbiosis.Archive.LockNeutral"
                        .Translate();
                default:
                    return "MAP_MechanoidMechanitor.Symbiosis.Archive.LockNone"
                        .Translate();
            }
        }

        private static string GetCovenantStatus(
            GameComponent_SymbiosisCovenantState state,
            SymbiosisCovenantFactionRecord record)
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (record.CovenantMember)
            {
                return "MAP_MechanoidMechanitor.Symbiosis.Archive.Status.Member"
                    .Translate();
            }

            if (record.PreDeclarationLock == SymbiosisPreDeclarationTrustLock.Hostile)
            {
                return "MAP_MechanoidMechanitor.Symbiosis.Archive.Status.LockedHostile"
                    .Translate();
            }

            if (record.PreDeclarationLock == SymbiosisPreDeclarationTrustLock.Neutral)
            {
                return "MAP_MechanoidMechanitor.Symbiosis.Archive.Status.LockedNeutral"
                    .Translate();
            }

            if (record.PermanentlyRefusesInvitation)
            {
                return "MAP_MechanoidMechanitor.Symbiosis.Archive.Status.PermanentRefuse"
                    .Translate();
            }

            if (record.ProposalPending)
            {
                return "MAP_MechanoidMechanitor.Symbiosis.Archive.Status.Pending"
                    .Translate();
            }

            if (record.NextInvitationTick > currentTick)
            {
                return "MAP_MechanoidMechanitor.Symbiosis.Archive.Status.Cooling"
                    .Translate(
                        GenDate.ToStringTicksToPeriod(
                            record.NextInvitationTick - currentTick));
            }

            if (SymbiosisCovenantDiplomacyUtility.ShouldShowSecretContact(record.Faction))
            {
                return "MAP_MechanoidMechanitor.Symbiosis.Archive.Status.CanContact"
                    .Translate();
            }

            return "MAP_MechanoidMechanitor.Symbiosis.Archive.Status.NotEligible"
                .Translate();
        }

        private static string GetTimingLabel(SymbiosisCovenantFactionRecord record)
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (record.ProposalPending)
            {
                return "MAP_MechanoidMechanitor.Symbiosis.Archive.TimingProposal"
                    .Translate(
                        GenDate.ToStringTicksToPeriod(
                            Mathf.Max(0, record.ProposalResolutionTick - currentTick)));
            }

            if (record.NextInvitationTick > currentTick)
            {
                return "MAP_MechanoidMechanitor.Symbiosis.Archive.TimingCooldown"
                    .Translate(
                        GenDate.ToStringTicksToPeriod(
                            record.NextInvitationTick - currentTick));
            }

            return "MAP_MechanoidMechanitor.Symbiosis.Archive.TimingNone".Translate();
        }

        private static void DrawText(
            Rect rect,
            string text,
            GameFont font,
            Color color = default,
            TextAnchor anchor = TextAnchor.UpperLeft)
        {
            if (color == default)
            {
                color = Color.white;
            }

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
}
