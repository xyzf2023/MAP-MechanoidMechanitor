using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class Page_MechanoidMechanitorScenarioReady : Page
    {
        private Vector2 scrollPosition;

        public override string PageTitle =>
            "MAP_MechanoidMechanitor.Scenario.ReadyPage.Title".Translate();

        public override void DoWindowContents(Rect inRect)
        {
            DrawPageTitle(inRect);

            Rect mainRect = GetMainRect(inRect);
            Rect outRect = mainRect;
            Rect viewRect = new Rect(
                0f,
                0f,
                outRect.width - 16f,
                Mathf.Max(outRect.height, 500f));

            Widgets.BeginScrollView(
                outRect,
                ref scrollPosition,
                viewRect);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            Widgets.Label(
                viewRect,
                "MAP_MechanoidMechanitor.Scenario.ReadyPage.Text".Translate());

            Widgets.EndScrollView();

            DoBottomButtons(
                inRect,
                nextLabel: "Play".Translate());
        }
    }
}
