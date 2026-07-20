using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidOvermindPage_Battlefield
    {
        public void Draw(Rect inRect)
        {
            using (MechanoidOvermindUiStyle.Push())
            {
                MechanoidOvermindUiStyle.DrawPanel(inRect);
                Rect inner = inRect.ContractedBy(16f);

                MechanoidOvermindUiStyle.DrawLabel(
                    new Rect(inner.x, inner.y, inner.width, 24f),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Body".Translate(),
                    GameFont.Small,
                    TextAnchor.UpperLeft,
                    MechanoidOvermindUiStyle.TextSecondary,
                    wordWrap: true);

                MechanoidOvermindUiStyle.DrawLabel(
                    new Rect(inner.x, inner.y + 40f, inner.width, 48f),
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Todo".Translate(),
                    GameFont.Small,
                    TextAnchor.UpperLeft,
                    MechanoidOvermindUiStyle.Warning,
                    wordWrap: true);

                Rect buttonRect = new Rect(inner.x, inner.yMax - 36f, 180f, 32f);
                MechanoidOvermindUiStyle.DrawActionButton(
                    buttonRect,
                    "MAP_MechanoidMechanitor.MechHiveCommunication.Battlefield.Request".Translate(),
                    enabled: false);
            }
        }
    }
}
