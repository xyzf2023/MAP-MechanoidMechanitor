using System;
using System.Text;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class Dialog_JusticeBossConfirmation : Window
    {
        private const float IdealWidth = 780f;
        private const float IdealHeight = 520f;
        private const float ScreenEdgePadding = 40f;
        private const float ButtonHeight = 35f;
        private const float ButtonGapToContent = 14f;
        private const float ButtonHorizontalGap = 20f;
        private const float ContentInnerPadding = 4f;
        private const float ScrollbarReservedWidth = 16f;
        private const float TextRightSafetyPadding = 16f;
        private const float EnemyIndent = 14f;
        private const float BlockSafetyHeight = 2f;
        private const float GapAfterIntro = 8f;
        private const float GapAfterEnemy = 18f;
        private const float GapAfterTactics = 16f;
        private const float GapAfterWarning = 16f;
        private const float GapAfterArrival = 8f;
        private const float WrapMeasureSafety = 14f;
        private const float WidthCacheTolerance = 0.5f;

        private const string ForbiddenLineStartPunctuation = "，。！？；：、）》」』】％…—";
        private const string OpeningPunctuation = "（《「『【";

        private readonly Action confirmedAction;

        private Vector2 scrollPosition;
        private bool confirmed;

        private float cachedContentWidth = -1f;
        private string wrappedIntro = string.Empty;
        private string wrappedEnemy = string.Empty;
        private string wrappedTactics = string.Empty;
        private string wrappedWarning = string.Empty;
        private string wrappedArrival = string.Empty;
        private float heightIntro;
        private float heightEnemy;
        private float heightTactics;
        private float heightWarning;
        private float heightArrival;
        private float viewHeight;

        public override Vector2 InitialSize
        {
            get
            {
                float maxWidth = Mathf.Max(0f, UI.screenWidth - ScreenEdgePadding * 2f);
                float maxHeight = Mathf.Max(0f, UI.screenHeight - ScreenEdgePadding * 2f);
                return new Vector2(
                    Mathf.Min(IdealWidth, maxWidth),
                    Mathf.Min(IdealHeight, maxHeight));
            }
        }

        public Dialog_JusticeBossConfirmation(Action confirmedAction)
        {
            this.confirmedAction = confirmedAction;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            doCloseX = false;
            onlyOneOfTypeAllowed = true;
            closeOnAccept = false;
            closeOnCancel = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;

                float buttonAreaHeight = ButtonHeight + ButtonGapToContent;
                Rect outRect = new Rect(
                    inRect.x,
                    inRect.y,
                    inRect.width,
                    Mathf.Max(0f, inRect.height - buttonAreaHeight));

                float textWidth = Mathf.Max(
                    1f,
                    outRect.width - ContentInnerPadding - ScrollbarReservedWidth - TextRightSafetyPadding);
                EnsureLayout(textWidth);

                Rect viewRect = new Rect(0f, 0f, textWidth + ContentInnerPadding, Mathf.Max(viewHeight, outRect.height));
                Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

                float curY = ContentInnerPadding;
                float labelX = ContentInnerPadding;

                Widgets.Label(new Rect(labelX, curY, textWidth, heightIntro), wrappedIntro);
                curY += heightIntro + GapAfterIntro;

                float enemyWidth = Mathf.Max(1f, textWidth - EnemyIndent);
                Widgets.Label(
                    new Rect(labelX + EnemyIndent, curY, enemyWidth, heightEnemy),
                    wrappedEnemy);
                curY += heightEnemy + GapAfterEnemy;

                Widgets.Label(new Rect(labelX, curY, textWidth, heightTactics), wrappedTactics);
                curY += heightTactics + GapAfterTactics;

                Widgets.Label(new Rect(labelX, curY, textWidth, heightWarning), wrappedWarning);
                curY += heightWarning + GapAfterWarning;

                Widgets.Label(new Rect(labelX, curY, textWidth, heightArrival), wrappedArrival);

                Widgets.EndScrollView();

                float buttonY = inRect.height - ButtonHeight;
                float buttonWidth = (inRect.width - ButtonHorizontalGap) / 2f;
                Rect backRect = new Rect(0f, buttonY, buttonWidth, ButtonHeight);
                Rect confirmRect = new Rect(
                    buttonWidth + ButtonHorizontalGap,
                    buttonY,
                    buttonWidth,
                    ButtonHeight);

                if (Widgets.ButtonText(backRect, "GoBack".Translate()))
                {
                    Cancel();
                }

                if (Widgets.ButtonText(confirmRect, "Confirm".Translate()))
                {
                    Confirm();
                }
            }
            finally
            {
                GUI.color = oldColor;
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
            }
        }

        public override void OnAcceptKeyPressed()
        {
            Confirm();
            Event.current.Use();
        }

        public override void OnCancelKeyPressed()
        {
            Cancel();
            Event.current.Use();
        }

        private void Confirm()
        {
            if (confirmed)
            {
                return;
            }

            confirmed = true;
            Close();
            confirmedAction?.Invoke();
        }

        private void Cancel()
        {
            Close();
        }

        private void EnsureLayout(float contentWidth)
        {
            if (cachedContentWidth >= 0f
                && Mathf.Abs(contentWidth - cachedContentWidth) <= WidthCacheTolerance)
            {
                return;
            }

            Text.Font = GameFont.Small;

            string intro = "MAP_MechanoidMechanitor.JusticeBoss.Call.ConfirmIntro".Translate().Resolve();
            string enemy = "MAP_MechanoidMechanitor.JusticeBoss.Call.ConfirmEnemy".Translate().Resolve();
            string tactics = "MAP_MechanoidMechanitor.JusticeBoss.Call.ConfirmTactics".Translate().Resolve();
            string warning = "MAP_MechanoidMechanitor.JusticeBoss.Call.ConfirmWarning".Translate().Resolve();
            string arrival = "MAP_MechanoidMechanitor.JusticeBoss.Call.ConfirmArrival".Translate().Resolve();

            float enemyWidth = Mathf.Max(1f, contentWidth - EnemyIndent);

            wrappedIntro = WrapChineseSafely(intro, contentWidth);
            wrappedEnemy = WrapChineseSafely(enemy, enemyWidth);
            wrappedTactics = WrapChineseSafely(tactics, contentWidth);
            wrappedWarning = WrapChineseSafely(warning, contentWidth);
            wrappedArrival = WrapChineseSafely(arrival, contentWidth);

            heightIntro = Text.CalcHeight(wrappedIntro, contentWidth) + BlockSafetyHeight;
            heightEnemy = Text.CalcHeight(wrappedEnemy, enemyWidth) + BlockSafetyHeight;
            heightTactics = Text.CalcHeight(wrappedTactics, contentWidth) + BlockSafetyHeight;
            heightWarning = Text.CalcHeight(wrappedWarning, contentWidth) + BlockSafetyHeight;
            heightArrival = Text.CalcHeight(wrappedArrival, contentWidth) + BlockSafetyHeight;

            viewHeight = ContentInnerPadding
                + heightIntro + GapAfterIntro
                + heightEnemy + GapAfterEnemy
                + heightTactics + GapAfterTactics
                + heightWarning + GapAfterWarning
                + heightArrival + GapAfterArrival;

            cachedContentWidth = contentWidth;
        }

        private static string WrapChineseSafely(string text, float labelWidth)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            float measureWidth = Mathf.Max(1f, labelWidth - WrapMeasureSafety);
            StringBuilder result = new StringBuilder(text.Length + 16);
            StringBuilder currentLine = new StringBuilder();

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '\r')
                {
                    continue;
                }

                if (c == '\n')
                {
                    result.Append(currentLine);
                    result.Append('\n');
                    currentLine.Length = 0;
                    continue;
                }

                if (currentLine.Length == 0)
                {
                    currentLine.Append(c);
                    continue;
                }

                currentLine.Append(c);
                if (Text.CalcSize(currentLine.ToString()).x <= measureWidth)
                {
                    continue;
                }

                currentLine.Length--;

                if (IsForbiddenLineStart(c))
                {
                    currentLine.Append(c);
                    result.Append(currentLine);
                    result.Append('\n');
                    currentLine.Length = 0;
                    continue;
                }

                StringBuilder carriedOpeners = new StringBuilder();
                while (currentLine.Length > 0 && IsOpeningPunctuation(currentLine[currentLine.Length - 1]))
                {
                    carriedOpeners.Insert(0, currentLine[currentLine.Length - 1]);
                    currentLine.Length--;
                }

                if (currentLine.Length > 0)
                {
                    result.Append(currentLine);
                    result.Append('\n');
                    currentLine.Length = 0;
                }

                currentLine.Append(carriedOpeners);
                currentLine.Append(c);
            }

            if (currentLine.Length > 0)
            {
                result.Append(currentLine);
            }

            return result.ToString();
        }

        private static bool IsForbiddenLineStart(char c)
        {
            return ForbiddenLineStartPunctuation.IndexOf(c) >= 0;
        }

        private static bool IsOpeningPunctuation(char c)
        {
            return OpeningPunctuation.IndexOf(c) >= 0;
        }
    }
}
