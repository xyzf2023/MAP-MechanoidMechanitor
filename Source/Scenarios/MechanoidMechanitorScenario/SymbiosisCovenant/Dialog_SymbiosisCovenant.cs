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
        Relations,
        Effects
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
        private Vector2 currentEffectsScrollPosition = Vector2.zero;
        private Vector2 nextEffectsScrollPosition = Vector2.zero;

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

                case SymbiosisCovenantPage.Effects:
                    DrawEffectsPage(pageRect, state);
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

        private static string GetCovenantLevelLabel(int level)
        {
            switch (level)
            {
                case 1:
                    return "MAP_MechanoidMechanitor.Symbiosis.CovenantStage.MultilateralAgreement"
                        .Translate();
                case 2:
                    return "MAP_MechanoidMechanitor.Symbiosis.CovenantStage.TradeCoordination"
                        .Translate();
                case 3:
                    return "MAP_MechanoidMechanitor.Symbiosis.CovenantStage.MutualDefense"
                        .Translate();
                case 4:
                    return "MAP_MechanoidMechanitor.Symbiosis.CovenantStage.StrategicAlliance"
                        .Translate();
                case 5:
                    return "MAP_MechanoidMechanitor.Symbiosis.CovenantStage.SymbiosisAlliance"
                        .Translate();
                default:
                    return "MAP_MechanoidMechanitor.Symbiosis.CovenantStage.NotEstablished"
                        .Translate();
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
                string levelText =
                    "MAP_MechanoidMechanitor.Symbiosis.CovenantLevel"
                        .Translate(state.CovenantLevel, 5)
                        .ToString()
                    + " // "
                    + GetCovenantLevelLabel(state.CovenantLevel);
                Widgets.Label(
                    new Rect(rect.x + 15f, lineY, labelWidth, 22f),
                    levelText);
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

        private void DrawEffectsPage(Rect rect, GameComponent_SymbiosisCovenantState state)
        {
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, HeaderHeight);
            DrawHeader(headerRect, state);

            Rect bodyRect = new Rect(
                rect.x,
                headerRect.yMax + MainGap,
                rect.width,
                Mathf.Max(0f, rect.yMax - headerRect.yMax - MainGap));

            float gap = 14f;
            Rect currentRect = new Rect(
                bodyRect.x,
                bodyRect.y,
                bodyRect.width * 0.62f - gap / 2f,
                bodyRect.height);
            Rect nextRect = new Rect(
                currentRect.xMax + gap,
                bodyRect.y,
                bodyRect.xMax - currentRect.xMax - gap,
                bodyRect.height);

            DrawEffectsCurrentPanel(currentRect, state);
            DrawNextLevelPanel(nextRect, state);
        }

        private void DrawEffectsCurrentPanel(Rect rect, GameComponent_SymbiosisCovenantState state)
        {
            Widgets.DrawBoxSolid(rect, PanelColor);
            DrawOutline(rect, 1, PanelOutlineColor);

            Rect inner = rect.ContractedBy(12f);
            DrawLabel(
                new Rect(inner.x, inner.y, inner.width, 24f),
                "MAP_MechanoidMechanitor.Symbiosis.Effects.Current".Translate(),
                GameFont.Small,
                Color.white,
                TextAnchor.UpperLeft);

            Rect listRect = new Rect(
                inner.x,
                inner.y + 30f,
                inner.width,
                inner.yMax - inner.y - 30f);

            int level = state.CovenantLevel;
            List<EffectCardInfo> cards = BuildEffectsCards(state, level);

            float cardHeight = 74f;
            float cardGap = 12f;
            float viewHeight = Mathf.Max(0f, cards.Count * (cardHeight + cardGap) - cardGap);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 18f, viewHeight);

            Widgets.BeginScrollView(listRect, ref currentEffectsScrollPosition, viewRect);
            try
            {
                float y = 0f;
                for (int i = 0; i < cards.Count; i++)
                {
                    Rect cardRect = new Rect(0f, y, listRect.width - 18f, cardHeight);
                    DrawEffectCard(cardRect, cards[i].Title, cards[i].Summary, cards[i].Tooltip);
                    y += cardHeight + cardGap;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private static List<EffectCardInfo> BuildEffectsCards(
            GameComponent_SymbiosisCovenantState state,
            int level)
        {
            SymbiosisCovenantEffectSnapshot snap =
                SymbiosisCovenantEffectDisplayUtility.GetSnapshot(level);
            List<EffectCardInfo> cards = new List<EffectCardInfo>();

            if (level <= 0)
            {
                cards.Add(new EffectCardInfo
                {
                    Title = "MAP_MechanoidMechanitor.Symbiosis.Effects.NotEstablished".Translate(),
                    Summary = "MAP_MechanoidMechanitor.Symbiosis.Effects.NotEstablished.Body".Translate()
                });
                return cards;
            }

            // 成员关系保障（L1+）。
            if (snap.MemberAllianceLock)
            {
                cards.Add(new EffectCardInfo
                {
                    Title = "MAP_MechanoidMechanitor.Symbiosis.Effects.Relations.Title".Translate(),
                    Summary = "MAP_MechanoidMechanitor.Symbiosis.Effects.Relations.Allied".Translate()
                });
            }
            else
            {
                cards.Add(new EffectCardInfo
                {
                    Title = "MAP_MechanoidMechanitor.Symbiosis.Effects.Relations.Title".Translate(),
                    Summary = "MAP_MechanoidMechanitor.Symbiosis.Effects.Relations.Neutral".Translate(),
                    Tooltip = "MAP_MechanoidMechanitor.Symbiosis.Effects.Relations.Neutral.Tooltip".Translate()
                });
            }

            // 交易价格改善（L2+）。
            if (snap.TradePriceImprovement > 0f)
            {
                cards.Add(new EffectCardInfo
                {
                    Title = "MAP_MechanoidMechanitor.Symbiosis.Effects.Trade.Title".Translate(),
                    Summary = "MAP_MechanoidMechanitor.Symbiosis.Effects.Trade.Value"
                        .Translate(Percent(snap.TradePriceImprovement))
                });
            }

            // 联合贸易代表团（L2+）。
            if (snap.TradeDelegation != null)
            {
                cards.Add(new EffectCardInfo
                {
                    Title = "MAP_MechanoidMechanitor.Symbiosis.Effects.Delegation.Title".Translate(),
                    Summary = "MAP_MechanoidMechanitor.Symbiosis.Effects.Delegation.Description".Translate(),
                    Tooltip = "MAP_MechanoidMechanitor.Symbiosis.Effects.Delegation.Tooltip".Translate(
                        FormatRange(snap.TradeDelegation.intervalDays),
                        FormatRange(snap.TradeDelegation.participantFactionCount))
                });
            }

            // 共同防卫（L3+）。
            if (snap.MilitaryAid != null)
            {
                cards.Add(new EffectCardInfo
                {
                    Title = "MAP_MechanoidMechanitor.Symbiosis.Effects.MilitaryAid.Title".Translate(),
                    Summary = "MAP_MechanoidMechanitor.Symbiosis.Effects.MilitaryAid.Description".Translate(),
                    Tooltip = BuildMilitaryAidTooltip(snap)
                });
            }

            return cards;
        }

        private static string BuildMilitaryAidTooltip(SymbiosisCovenantEffectSnapshot snap)
        {
            SymbiosisCovenantMilitaryAidDef config =
                SymbiosisCovenantMilitaryAidDefOf.MAP_SymbiosisCovenant_MilitaryAidConfig;
            SymbiosisCovenantMilitaryAidLevelSettings settings = snap.MilitaryAid!;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.MilitaryAid.Tooltip.BaseChance"
                .Translate(Percent(settings.offerChance)));
            sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.MilitaryAid.Tooltip.MaxChance"
                .Translate(Percent(config.maxOfferChance)));
            sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.MilitaryAid.Tooltip.SupportScale"
                .Translate(Percent(settings.supportPointsFactor)));
            sb.AppendLine();
            sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.MilitaryAid.Tooltip.ResponderBonusHeader"
                .Translate());
            for (int i = 0; i < config.responderChanceTiers.Count; i++)
            {
                SymbiosisCovenantMilitaryAidResponderTier tier = config.responderChanceTiers[i];
                if (tier.additionalChance <= 0f)
                {
                    continue;
                }
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.MilitaryAid.Tooltip.ResponderBonusLine"
                    .Translate(FormatRange(tier.eligibleResponderCount), Percent(tier.additionalChance)));
            }
            sb.AppendLine();
            sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.MilitaryAid.Tooltip.OfferTimeout"
                .Translate(GenDate.ToStringTicksToPeriod(config.offerTimeoutTicks)));
            sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.MilitaryAid.Tooltip.AcceptCooldown"
                .Translate(GenDate.ToStringTicksToPeriod(config.acceptedCooldownTicks)));
            return sb.ToString();
        }

        private static float DrawEffectCard(
            Rect rect,
            string title,
            string summary,
            string? tooltip = null)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.065f, 0.085f, 0.09f, 1f));
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 3f, rect.height), TealColor);
            DrawOutline(rect, 1, TealColor);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            Color previousColor = GUI.color;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                Widgets.Label(
                    new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, 22f),
                    title);

                Text.Font = GameFont.Tiny;
                GUI.color = MutedTextColor;
                Text.WordWrap = true;
                Widgets.Label(
                    new Rect(rect.x + 12f, rect.y + 32f, rect.width - 24f, rect.height - 40f),
                    summary);
            }
            finally
            {
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWordWrap;
                GUI.color = previousColor;
            }

            if (!tooltip.NullOrEmpty())
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }
            return rect.height;
        }

        private static void DrawNextLevelPanel(Rect rect, GameComponent_SymbiosisCovenantState state)
        {
            Widgets.DrawBoxSolid(rect, PanelColor);
            DrawOutline(rect, 1, PanelOutlineColor);

            Rect inner = rect.ContractedBy(12f);
            DrawLabel(
                new Rect(inner.x, inner.y, inner.width, 24f),
                "MAP_MechanoidMechanitor.Symbiosis.Effects.Next".Translate(),
                GameFont.Small,
                Color.white,
                TextAnchor.UpperLeft);

            Rect contentRect = new Rect(
                inner.x,
                inner.y + 30f,
                inner.width,
                inner.yMax - inner.y - 30f);

            int level = state.CovenantLevel;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            if (level >= 5)
            {
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.MaxLevel".Translate());
                sb.AppendLine();
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.MaxLevel.Body".Translate());
                sb.AppendLine();
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.MaxLevel.Hint".Translate());
            }
            else
            {
                int nextLevel = level + 1;
                string stageLabel = GetCovenantLevelLabel(nextLevel);
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.NextLevel".Translate(stageLabel));
                sb.AppendLine();
                if (level <= 0)
                {
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.NextCondition.Join".Translate());
                }
                else
                {
                    int threshold = GetNextLevelThreshold(level);
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.RequiredUnity".Translate(threshold));
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.CurrentUnity"
                        .Translate(Mathf.FloorToInt(state.Unity)));
                    sb.AppendLine();
                    AppendChangedEffects(sb, level, nextLevel);
                }
            }

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            Color previousColor = GUI.color;
            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                GUI.color = Color.white;
                Widgets.Label(contentRect, sb.ToString());
            }
            finally
            {
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWordWrap;
                GUI.color = previousColor;
            }
        }

        private static void AppendChangedEffects(
            System.Text.StringBuilder sb,
            int current,
            int next)
        {
            SymbiosisCovenantEffectSnapshot cur =
                SymbiosisCovenantEffectDisplayUtility.GetSnapshot(current);
            SymbiosisCovenantEffectSnapshot nxt =
                SymbiosisCovenantEffectDisplayUtility.GetSnapshot(next);

            if (!Mathf.Approximately(cur.TradePriceImprovement, nxt.TradePriceImprovement))
            {
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.Changed.Trade".Translate(
                    Percent(cur.TradePriceImprovement), Percent(nxt.TradePriceImprovement)));
            }

            if (cur.TradeDelegation == null && nxt.TradeDelegation != null)
            {
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.Changed.UnlockDelegation".Translate());
            }
            else if (cur.TradeDelegation != null && nxt.TradeDelegation != null)
            {
                if (!Mathf.Approximately(cur.TradeDelegation.intervalDays.min, nxt.TradeDelegation.intervalDays.min)
                    || !Mathf.Approximately(cur.TradeDelegation.intervalDays.max, nxt.TradeDelegation.intervalDays.max))
                {
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.Changed.DelegationInterval".Translate(
                        FormatRange(cur.TradeDelegation.intervalDays),
                        FormatRange(nxt.TradeDelegation.intervalDays)));
                }
                if (cur.TradeDelegation.participantFactionCount.min != nxt.TradeDelegation.participantFactionCount.min
                    || cur.TradeDelegation.participantFactionCount.max != nxt.TradeDelegation.participantFactionCount.max)
                {
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.Changed.DelegationParticipants".Translate(
                        FormatRange(cur.TradeDelegation.participantFactionCount),
                        FormatRange(nxt.TradeDelegation.participantFactionCount)));
                }
            }

            if (cur.MilitaryAid == null && nxt.MilitaryAid != null)
            {
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.Changed.UnlockMilitaryAid".Translate());
            }
            else if (cur.MilitaryAid != null && nxt.MilitaryAid != null)
            {
                if (!Mathf.Approximately(cur.MilitaryAid.offerChance, nxt.MilitaryAid.offerChance))
                {
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.Changed.MilitaryBaseChance".Translate(
                        Percent(cur.MilitaryAid.offerChance), Percent(nxt.MilitaryAid.offerChance)));
                }
                if (!Mathf.Approximately(cur.MilitaryAid.supportPointsFactor, nxt.MilitaryAid.supportPointsFactor))
                {
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.Changed.MilitarySupportScale".Translate(
                        Percent(cur.MilitaryAid.supportPointsFactor), Percent(nxt.MilitaryAid.supportPointsFactor)));
                }
            }

            if (!cur.MemberAllianceLock && nxt.MemberAllianceLock)
            {
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Effects.Changed.UnlockAllianceLock".Translate());
            }
        }

        private static string Percent(float value)
            => Mathf.RoundToInt(value * 100f).ToString() + "%";

        private static string FormatRange(IntRange range)
            => range.min == range.max ? range.min.ToString() : range.min + "~" + range.max;

        private static string FormatRange(FloatRange range)
            => Mathf.RoundToInt(range.min) + "~" + Mathf.RoundToInt(range.max);

        private sealed class EffectCardInfo
        {
            public string Title = string.Empty;
            public string Summary = string.Empty;
            public string Tooltip = string.Empty;
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
            Rect effectsTabRect = new Rect(
                relationsTabRect.xMax + FooterGap,
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
            DrawPageTab(
                effectsTabRect,
                SymbiosisCovenantPage.Effects,
                "MAP_MechanoidMechanitor.Symbiosis.Page.Effects".Translate());

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
                // M3：新增两个系统区，L4 联合军事行动再增一区，提高内容高度。
                float contentHeight = hasRecord ? 2350f : 1450f;
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
                        y = DrawRecordControls(state, record!, viewRect, y);
                    }
                    else
                    {
                        Widgets.Label(
                            new Rect(0f, y, viewRect.width, 40f),
                            "MAP_MechanoidMechanitor.Symbiosis.Dev.NoRecord"
                                .Translate());
                        y += 46f;
                    }

                    // M3：联合贸易代表团与共同防卫 DEV 状态区（即使未选择派系也显示）。
                    y = DrawTradeDelegationDevSection(state, viewRect, y);
                    y = DrawMilitaryAidDevSection(state, viewRect, y);
                    // L4 独立机制：联合军事行动 DEV 状态区（即使未选择派系也显示）。
                    y = DrawJointOperationDevSection(state, viewRect, y);

                    DrawGlobalState(state, viewRect, y, record);
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

        private float DrawRecordControls(
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
                new[] { "25", "50", "100", "150", "200" },
                new Action[]
                {
                    () => state.DevSetTrust(selectedFaction, 25, DevReason()),
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

            // M3：内部状态面板统一由 DrawGlobalState 在末尾绘制（含当前派系记录信息）。
            return y;
        }

        private void DrawGlobalState(
            GameComponent_SymbiosisCovenantState state,
            Rect inRect,
            float y,
            SymbiosisCovenantFactionRecord? record = null)
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
                    BuildStateText(state, record));
            }
        }

        // M3：联合贸易代表团 DEV 状态区（全局/当前地图系统，不依赖所选派系）。
        private float DrawTradeDelegationDevSection(
            GameComponent_SymbiosisCovenantState state,
            Rect inRect,
            float y)
        {
            DrawSectionLabel(
                new Rect(inRect.x, y, inRect.width, 22f),
                "MAP_MechanoidMechanitor.Symbiosis.Delegation.Dev.Section".Translate());
            y += 26f;

            SymbiosisCovenantTradeDelegationDevSnapshot? snap =
                SymbiosisCovenantTradeDelegationScheduler.GetDevSnapshot(state);

            Text.Font = GameFont.Tiny;
            Text.WordWrap = true;
            Color previousColor = GUI.color;
            GUI.color = Color.white;
            try
            {
                if (snap == null)
                {
                    Widgets.Label(
                        new Rect(inRect.x, y, inRect.width, 40f),
                        "MAP_MechanoidMechanitor.Symbiosis.Delegation.Dev.Unscheduled".Translate());
                    y += 46f;
                }
                else
                {
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Delegation.Dev.Field.Level"
                        .Translate(snap.CurrentLevel));
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Delegation.Dev.Field.MemberCount"
                        .Translate(snap.MemberCount));
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Delegation.Dev.Field.BaseInterval"
                        .Translate(FormatFloatRange(snap.BaseIntervalDays)));
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Delegation.Dev.Field.MemberSpeed"
                        .Translate(snap.MemberSpeedMultiplier.ToString("F2")));
                    if (snap.NextTick < 0)
                    {
                        sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Delegation.Dev.Field.NextVisit"
                            .Translate("MAP_MechanoidMechanitor.Symbiosis.Delegation.Dev.Unscheduled".Translate()));
                    }
                    else
                    {
                        sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Delegation.Dev.Field.NextTick"
                            .Translate(snap.NextTick));
                        sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Delegation.Dev.Field.DaysUntilNext"
                            .Translate(snap.DaysUntilNext.ToString("F1")));
                    }
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Delegation.Dev.Field.RetryCount"
                        .Translate(snap.RetryCount));
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.Delegation.Dev.Field.LastLead"
                        .Translate(snap.LastLeadFaction?.Name ?? "—"));
                    Widgets.Label(
                        new Rect(inRect.x, y, inRect.width, 150f),
                        sb.ToString());
                    y += 156f;
                }
            }
            finally
            {
                GUI.color = previousColor;
            }

            // 操作按钮：立即生成 / 重新安排 / 立即到期。
            DrawButtonRow(
                new Rect(inRect.x, y, inRect.width, 32f),
                new[]
                {
                    "MAP_MechanoidMechanitor.Symbiosis.Delegation.Dev.SpawnNow".Translate().ToString(),
                    "MAP_MechanoidMechanitor.Symbiosis.Delegation.Dev.Reschedule".Translate().ToString(),
                    "MAP_MechanoidMechanitor.Symbiosis.Delegation.Dev.MakeDue".Translate().ToString()
                },
                new Action[]
                {
                    () => ShowDelegationMessage(SymbiosisCovenantTradeDelegationScheduler.DevSpawnNow()),
                    () => ShowDelegationMessage(SymbiosisCovenantTradeDelegationScheduler.DevReschedule()),
                    () => ShowDelegationMessage(SymbiosisCovenantTradeDelegationScheduler.DevMakeDueNow())
                });
            y += 40f;

            return y;
        }

        // M3：共同防卫 DEV 状态区（全局/当前地图系统，不依赖所选派系）。
        private float DrawMilitaryAidDevSection(
            GameComponent_SymbiosisCovenantState state,
            Rect inRect,
            float y)
        {
            DrawSectionLabel(
                new Rect(inRect.x, y, inRect.width, 22f),
                "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Section".Translate());
            y += 26f;

            Map? map = Find.CurrentMap;
            SymbiosisCovenantMilitaryAidUtility.SymbiosisCovenantMilitaryAidDevSnapshot snap =
                SymbiosisCovenantMilitaryAidUtility.GetDevSnapshot(state, map);

            Text.Font = GameFont.Tiny;
            Text.WordWrap = true;
            Color previousColor = GUI.color;
            GUI.color = Color.white;
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.Level"
                    .Translate(snap.CovenantLevel));
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.BaseChance"
                    .Translate(Percent(snap.BaseOfferChance)));
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.MaxChance"
                    .Translate(Percent(snap.MaxOfferChance)));
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.SupportFactor"
                    .Translate(Percent(snap.SupportPointsFactor)));

                if (snap.CurrentThreatFaction != null)
                {
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.ThreatFaction"
                        .Translate(snap.CurrentThreatFaction.Name));
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.ThreatPower"
                        .Translate(snap.CurrentThreatCombatPower.ToString("F0")));
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.EligibleResponders"
                        .Translate(snap.EligibleResponderCount));
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.EffectiveChance"
                        .Translate(Percent(snap.EffectiveOfferChance)));
                }
                else
                {
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.ThreatFaction"
                        .Translate("—"));
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.EffectiveChance"
                        .Translate("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.NA".Translate()));
                }

                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.PendingRaid"
                    .Translate(snap.PendingEvaluation.ToString()));
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.PendingRaidAttacker"
                    .Translate(snap.PendingRaidAttackerFaction?.Name ?? "-"));
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.PendingRaidPoints"
                    .Translate(snap.PendingRaidPoints.ToString("F0")));
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.PendingRaidEvaluateTick"
                    .Translate(snap.PendingRaidEvaluateAtTick));
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.PendingRaidTicksRemaining"
                    .Translate(snap.PendingRaidTicksRemaining));
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.PendingOffer"
                    .Translate(snap.PendingOffer.ToString()));
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.LastResponder"
                    .Translate(snap.LastResponderFaction?.Name ?? "—"));

                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.ActiveAidFaction"
                    .Translate(snap.ActiveAidFaction?.Name ?? "—"));
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.AidTag"
                    .Translate(snap.ActiveAidTag ?? "—"));
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.TriggerRaidPoints"
                    .Translate(snap.ActiveAidTriggerRaidPoints.ToString("F0")));
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.SupportPoints"
                    .Translate(snap.ActiveAidSupportPoints.ToString("F0")));
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.TaggedLords"
                    .Translate(snap.TaggedAssistLordCount));
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.AidStartTick"
                    .Translate(snap.ActiveAidStartTick));
                sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Field.CooldownRemaining"
                    .Translate(snap.CooldownRemainingTicks));

                Widgets.Label(
                    new Rect(inRect.x, y, inRect.width, 440f),
                    sb.ToString());
                y += 446f;
            }
            finally
            {
                GUI.color = previousColor;
            }

            // 操作按钮：强制发送援助询问 / 清除当前地图状态 / 仅清除冷却。
            DrawButtonRow(
                new Rect(inRect.x, y, inRect.width, 32f),
                new[]
                {
                    "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.ForceOffer".Translate().ToString(),
                    "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.ClearState".Translate().ToString(),
                    "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.ClearCooldown".Translate().ToString()
                },
                new Action[]
                {
                    () => ShowMessage(SymbiosisCovenantMilitaryAidUtility.DevForceOfferForCurrentThreat()),
                    () => ShowMessage(SymbiosisCovenantMilitaryAidUtility.DevClearCurrentMapState()),
                    () => ShowMessage(SymbiosisCovenantMilitaryAidUtility.DevClearCurrentMapCooldown())
                });
            y += 40f;

            return y;
        }

        // L4：联合军事行动 DEV 状态区（全局系统，不依赖所选派系）。
        private float DrawJointOperationDevSection(
            GameComponent_SymbiosisCovenantState state,
            Rect inRect,
            float y)
        {
            DrawSectionLabel(
                new Rect(inRect.x, y, inRect.width, 22f),
                "MAP_MechanoidMechanitor.Symbiosis.JointOp.Dev.Section".Translate());
            y += 26f;

            SymbiosisCovenantJointOperationScheduler
                .SymbiosisCovenantJointOperationDevSnapshot? snap =
                SymbiosisCovenantJointOperationScheduler.GetDevSnapshot(state);

            Text.Font = GameFont.Tiny;
            Text.WordWrap = true;
            Color previousColor = GUI.color;
            GUI.color = Color.white;
            try
            {
                if (snap == null)
                {
                    Widgets.Label(
                        new Rect(inRect.x, y, inRect.width, 40f),
                        "MAP_MechanoidMechanitor.Symbiosis.JointOp.Dev.Unscheduled".Translate());
                    y += 46f;
                }
                else
                {
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.JointOp.Dev.Field.Level"
                        .Translate((NamedArgument)snap.CovenantLevel));
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.JointOp.Dev.Field.Available"
                        .Translate((NamedArgument)(snap.Available ? "True" : "False")));
                    if (snap.NextTick < 0)
                    {
                        sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.JointOp.Dev.Field.NextTick"
                            .Translate("MAP_MechanoidMechanitor.Symbiosis.JointOp.Dev.Unscheduled".Translate()));
                    }
                    else
                    {
                        sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.JointOp.Dev.Field.NextTick"
                            .Translate((NamedArgument)snap.NextTick));
                        sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.JointOp.Dev.Field.DaysUntilNext"
                            .Translate((NamedArgument)snap.DaysUntilNext.ToString("F1")));
                    }
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.JointOp.Dev.Field.CooldownRemaining"
                        .Translate((NamedArgument)snap.CooldownRemainingTicks));
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.JointOp.Dev.Field.Ongoing"
                        .Translate((NamedArgument)(snap.Ongoing ? "True" : "False")));
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.JointOp.Dev.Field.Target"
                        .Translate((NamedArgument)(snap.TargetLabel ?? "—")));
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.JointOp.Dev.Field.Stage"
                        .Translate((NamedArgument)snap.Stage));
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.JointOp.Dev.Field.Participants"
                        .Translate((NamedArgument)snap.ParticipantsCount));
                    sb.AppendLine("MAP_MechanoidMechanitor.Symbiosis.JointOp.Dev.Field.RewardValue"
                        .Translate(snap.RewardValue));
                    Widgets.Label(
                        new Rect(inRect.x, y, inRect.width, 250f),
                        sb.ToString());
                    y += 256f;
                }
            }
            finally
            {
                GUI.color = previousColor;
            }

            // 操作按钮：一键准备测试 / 立即生成邀请 / 立即到期 / 清除冷却 / 清除行动。
            DrawButtonRow(
                new Rect(inRect.x, y, inRect.width, 32f),
                new[]
                {
                    "MAP_MechanoidMechanitor.Symbiosis.JointOp.Dev.PrepareTest".Translate().ToString(),
                    "MAP_MechanoidMechanitor.Symbiosis.JointOp.Dev.SpawnNow".Translate().ToString(),
                    "MAP_MechanoidMechanitor.Symbiosis.JointOp.Dev.MakeDue".Translate().ToString(),
                    "MAP_MechanoidMechanitor.Symbiosis.JointOp.Dev.ClearCooldown".Translate().ToString(),
                    "MAP_MechanoidMechanitor.Symbiosis.JointOp.Dev.ClearOperation".Translate().ToString()
                },
                new Action[]
                {
                    () =>
                    {
                        bool ok = MAP_MechanoidMechanitor.SymbiosisCovenantDebugUtility
                            .TryPrepareAndSpawnJointOperationTest(out string msg);
                        ShowJointOpMessage(ok, msg);
                    },
                    () => ShowJointOpMessage(
                        MAP_MechanoidMechanitor.SymbiosisCovenantDebugUtility.TrySpawnJointOperationNow(out string msg), msg),
                    () => ShowJointOpMessage(
                        MAP_MechanoidMechanitor.SymbiosisCovenantDebugUtility.TryMakeJointOperationDueNow(out string msg), msg),
                    () => ShowJointOpMessage(
                        MAP_MechanoidMechanitor.SymbiosisCovenantDebugUtility.TryClearJointOperationCooldown(out string msg), msg),
                    () => ShowJointOpMessage(
                        MAP_MechanoidMechanitor.SymbiosisCovenantDebugUtility.TryClearJointOperation(out string msg), msg)
                });
            y += 40f;

            return y;
        }

        private static void ShowJointOpMessage(bool success, string message = "")
        {
            string text = success
                ? "MAP_MechanoidMechanitor.Symbiosis.JointOp.Dev.Success".Translate()
                : "MAP_MechanoidMechanitor.Symbiosis.JointOp.Dev.Failed".Translate();

            if (!string.IsNullOrEmpty(message))
            {
                text += "\n" + message;
            }

            Messages.Message(
                text,
                success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput,
                historical: false);
        }

        private static string FormatFloatRange(FloatRange range)
            => Mathf.RoundToInt(range.min) + "~" + Mathf.RoundToInt(range.max);

        private static void ShowMessage(bool success)
        {
            Messages.Message(
                success
                    ? "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Success".Translate()
                    : "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Failed".Translate(),
                success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput,
                historical: false);
        }

        private static void ShowDelegationMessage(bool success)
        {
            Messages.Message(
                success
                    ? "MAP_MechanoidMechanitor.Symbiosis.Delegation.Dev.Success".Translate()
                    : "MAP_MechanoidMechanitor.Symbiosis.Delegation.Dev.Failed".Translate(),
                success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput,
                historical: false);
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
