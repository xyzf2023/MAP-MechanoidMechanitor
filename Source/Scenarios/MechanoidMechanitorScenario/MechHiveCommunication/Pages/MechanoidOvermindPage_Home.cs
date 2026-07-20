using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidOvermindPage_Home
    {
        public void Draw(Rect inRect)
        {
            using (MechanoidOvermindUiStyle.Push())
            {
                MechanoidOvermindUiStyle.DrawPanel(inRect);
                Rect inner = inRect.ContractedBy(16f);

                MechanoidOvermindUiStyle.DrawLabel(
                    new Rect(inner.x, inner.y, inner.width, 28f),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Home.Title".Translate(),
                    GameFont.Medium);

                MechanoidOvermindUiStyle.DrawLabel(
                    new Rect(inner.x, inner.y + 40f, inner.width, 24f),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Home.Body".Translate(),
                    GameFont.Small,
                    TextAnchor.UpperLeft,
                    MechanoidOvermindUiStyle.TextSecondary,
                    wordWrap: true);

                MechanoidOvermindUiStyle.DrawLabel(
                    new Rect(inner.x, inner.y + 88f, inner.width, Mathf.Max(24f, inner.height - 100f)),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Home.Todo".Translate(),
                    GameFont.Small,
                    TextAnchor.UpperLeft,
                    MechanoidOvermindUiStyle.Warning,
                    wordWrap: true);
            }
        }
    }
}
