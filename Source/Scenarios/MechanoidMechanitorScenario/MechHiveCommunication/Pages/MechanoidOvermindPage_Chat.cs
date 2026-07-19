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
                    new Rect(inner.x, inner.y, inner.width, 28f),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Chat.Title".Translate(),
                    GameFont.Medium);

                MechanoidOvermindUiStyle.DrawLabel(
                    new Rect(inner.x, inner.y + 40f, inner.width, 24f),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Chat.Body".Translate(),
                    GameFont.Small,
                    TextAnchor.UpperLeft,
                    MechanoidOvermindUiStyle.TextSecondary,
                    wordWrap: true);

                Rect dialogArea = new Rect(
                    inner.x,
                    inner.y + 80f,
                    inner.width,
                    Mathf.Max(80f, inner.height - 100f));
                MechanoidOvermindUiStyle.DrawPanel(dialogArea, alt: true, cornerMarks: false);
                MechanoidOvermindUiStyle.DrawLabel(
                    dialogArea.ContractedBy(12f),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Chat.DialogTodo".Translate(),
                    GameFont.Small,
                    TextAnchor.UpperLeft,
                    MechanoidOvermindUiStyle.TextSecondary,
                    wordWrap: true);
            }
        }
    }
}
