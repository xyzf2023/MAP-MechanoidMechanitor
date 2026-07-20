using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidOvermindPage_Home
    {
        private const float TargetRowHeight = 58f;

        private const float RowGap = 8f;

        private const int MenuCount = 4;

        public MechanoidOvermindPageKind? Draw(Rect inRect, bool inputEnabled)
        {
            using (MechanoidOvermindUiStyle.Push())
            {
                float needed = MenuCount * TargetRowHeight + (MenuCount - 1) * RowGap;
                float rowH = TargetRowHeight;
                if (inRect.height < needed && needed > 0f)
                {
                    float gaps = (MenuCount - 1) * RowGap;
                    rowH = Mathf.Max(1f, (inRect.height - gaps) / MenuCount);
                    rowH = Mathf.Min(rowH, TargetRowHeight);
                }

                MechanoidOvermindPageKind? selected = null;
                float y = inRect.y;

                if (DrawCard(
                        new Rect(inRect.x, y, inRect.width, rowH),
                        "01",
                        "MAP_MechanoidMechanitor.MechHiveCommunication.Nav.Chat".Translate(),
                        inputEnabled))
                {
                    selected = MechanoidOvermindPageKind.Chat;
                }

                y += rowH + RowGap;
                if (DrawCard(
                        new Rect(inRect.x, y, inRect.width, rowH),
                        "02",
                        "MAP_MechanoidMechanitor.MechHiveCommunication.Nav.Mechs".Translate(),
                        inputEnabled))
                {
                    selected = MechanoidOvermindPageKind.Mechs;
                }

                y += rowH + RowGap;
                if (DrawCard(
                        new Rect(inRect.x, y, inRect.width, rowH),
                        "03",
                        "MAP_MechanoidMechanitor.MechHiveCommunication.Nav.Goods".Translate(),
                        inputEnabled))
                {
                    selected = MechanoidOvermindPageKind.Goods;
                }

                y += rowH + RowGap;
                if (DrawCard(
                        new Rect(inRect.x, y, inRect.width, rowH),
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
            return MechanoidOvermindUiStyle.DrawMenuCard(
                rect,
                nodeLabel,
                title,
                inputEnabled,
                compactLayout: true);
        }
    }
}
