using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidOvermindPage_Communication
    {
        private const float QueryRowHeight = 58f;

        public MechanoidOvermindCommunicationQueryKind? Draw(Rect inRect)
        {
            using (MechanoidOvermindUiStyle.Push())
            {
                MechanoidOvermindUiStyle.DrawPanel(inRect);
                Rect inner = inRect.ContractedBy(16f);

                MechanoidOvermindCommunicationQueryKind? selected = null;
                Rect queryRect = new Rect(inner.x, inner.y, inner.width, QueryRowHeight);
                if (DrawQueryCard(
                        queryRect,
                        "01",
                        "MAP_MechanoidMechanitor.PurgeDirective.Communication.Communication.Query.PurgeCredits"
                            .Translate()))
                {
                    selected = MechanoidOvermindCommunicationQueryKind.PurgeCredits;
                }

                Rect ratingRect = new Rect(inner.x, inner.y + QueryRowHeight + 8f, inner.width, QueryRowHeight);
                if (DrawQueryCard(
                        ratingRect,
                        "02",
                        (GameComponent_CerebrexTakeoverState.IsActive
                            ? "MAP_PurgeDirectiveRating.Communication.Query.ControlPermission"
                            : "MAP_PurgeDirectiveRating.Communication.Query.NodeRating").Translate()))
                {
                    selected = MechanoidOvermindCommunicationQueryKind.NodeRating;
                }

                return selected;
            }
        }

        private static bool DrawQueryCard(Rect rect, string node, string title)
        {
            string nodeLabel =
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Home.Node".Translate(node);
            return MechanoidOvermindUiStyle.DrawMenuCard(
                rect,
                nodeLabel,
                title,
                enabled: true,
                compactLayout: true);
        }
    }
}
