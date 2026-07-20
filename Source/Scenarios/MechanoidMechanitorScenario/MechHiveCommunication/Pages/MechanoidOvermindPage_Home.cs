using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidOvermindPage_Home
    {
        public MechanoidOvermindPageKind? Draw(Rect inRect, bool inputEnabled)
        {
            using (MechanoidOvermindUiStyle.Push())
            {
                float gap = 8f;
                float cardW = (inRect.width - gap) * 0.5f;
                float cardH = (inRect.height - gap) * 0.5f;

                MechanoidOvermindPageKind? selected = null;
                if (DrawCard(
                        new Rect(inRect.x, inRect.y, cardW, cardH),
                        "01",
                        "MAP_MechanoidMechanitor.MechHiveCommunication.Nav.Chat".Translate(),
                        inputEnabled))
                {
                    selected = MechanoidOvermindPageKind.Chat;
                }

                if (DrawCard(
                        new Rect(inRect.x + cardW + gap, inRect.y, cardW, cardH),
                        "02",
                        "MAP_MechanoidMechanitor.MechHiveCommunication.Nav.Mechs".Translate(),
                        inputEnabled))
                {
                    selected = MechanoidOvermindPageKind.Mechs;
                }

                if (DrawCard(
                        new Rect(inRect.x, inRect.y + cardH + gap, cardW, cardH),
                        "03",
                        "MAP_MechanoidMechanitor.MechHiveCommunication.Nav.Goods".Translate(),
                        inputEnabled))
                {
                    selected = MechanoidOvermindPageKind.Goods;
                }

                if (DrawCard(
                        new Rect(inRect.x + cardW + gap, inRect.y + cardH + gap, cardW, cardH),
                        "04",
                        "MAP_MechanoidMechanitor.MechHiveCommunication.Nav.Battlefield".Translate(),
                        inputEnabled))
                {
                    selected = MechanoidOvermindPageKind.BattlefieldSupport;
                }

                return selected;
            }
        }

        private static bool DrawCard(
            Rect rect,
            string node,
            string title,
            bool inputEnabled)
        {
            string nodeLabel =
                "MAP_MechanoidMechanitor.MechHiveCommunication.Home.Node".Translate(node);
            return MechanoidOvermindUiStyle.DrawMenuCard(rect, nodeLabel, title, inputEnabled);
        }
    }
}
