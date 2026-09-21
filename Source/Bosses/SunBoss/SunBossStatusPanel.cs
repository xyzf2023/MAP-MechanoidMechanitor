using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class SunBossStatusPanel
    {
        internal static void Draw(Pawn boss, Map map)
        {
            if (Find.CurrentMap != map || WorldRendererUtility.WorldSelected || !boss.Spawned
                || boss.Dead || boss.Destroyed || boss.Map != map) return;
            CompSunBossState? state = boss.GetComp<CompSunBossState>();
            if (state == null) return;
            float width = Mathf.Min(480f, UI.screenWidth - 32f);
            if (width < 100f) return;
            Rect panel = new Rect((UI.screenWidth - width) * 0.5f, 80f, width, 56f);
            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            try
            {
                GUI.color = new Color(0.06f, 0.06f, 0.07f, 0.9f);
                GUI.DrawTexture(panel, BaseContent.WhiteTex);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                int count = state.Stabilizers;
                Widgets.Label(new Rect(panel.x + 8f, panel.y, panel.width - 16f, 26f),
                    "MAP_SunBoss_StatusHeading".Translate(boss.LabelShort, state.Stage.LabelKey.Translate(), count));
                Rect bar = new Rect(panel.x + 8f, panel.y + 29f, panel.width - 16f, 21f);
                GUI.color = new Color(0.2f, 0.2f, 0.2f);
                GUI.DrawTexture(bar, BaseContent.WhiteTex);
                GUI.color = count == 0 ? new Color(0.75f, 0.18f, 0.12f)
                    : count == 1 ? new Color(0.85f, 0.4f, 0.08f) : new Color(0.65f, 0.5f, 0.12f);
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(state.Structure / state.MaxStructure), bar.height),
                    BaseContent.WhiteTex);
                GUI.color = Color.white;
                Widgets.Label(bar, state.Structure <= 0f ? "MAP_SunBoss_StructureCollapsing".Translate()
                    : "MAP_SunBoss_StructureValue".Translate(Mathf.CeilToInt(state.Structure), state.MaxStructure.ToString("0")));
            }
            finally { GUI.color = oldColor; Text.Anchor = oldAnchor; Text.Font = oldFont; }
        }
    }
}
