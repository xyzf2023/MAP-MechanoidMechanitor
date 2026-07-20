using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidOvermindPage_Chat
    {
        public void Draw(Rect inRect)
        {
            using (MechanoidOvermindUiStyle.Push())
            {
                MechanoidOvermindUiStyle.DrawPanel(inRect);
                Rect inner = inRect.ContractedBy(16f);

                MechanoidOvermindUiStyle.DrawLabel(
                    new Rect(inner.x, inner.y, inner.width, 24f),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Chat.Body".Translate(),
                    GameFont.Small,
                    TextAnchor.UpperLeft,
                    MechanoidOvermindUiStyle.TextSecondary,
                    wordWrap: true);

                MechanoidOvermindUiStyle.DrawLabel(
                    new Rect(inner.x, inner.y + 40f, inner.width, Mathf.Max(24f, inner.height - 52f)),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Chat.Todo".Translate(),
                    GameFont.Small,
                    TextAnchor.UpperLeft,
                    MechanoidOvermindUiStyle.Warning,
                    wordWrap: true);
            }
        }
    }
}
