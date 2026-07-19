using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class Dialog_MechanoidOvermindCommunication : Window
    {
        private const float TitleHeight = 32f;

        private readonly Faction mechHive;

        public override Vector2 InitialSize => new Vector2(700f, 500f);

        public Dialog_MechanoidOvermindCommunication(Faction mechHive)
        {
            this.mechHive = mechHive;
            forcePause = false;
            doCloseX = true;
            doCloseButton = true;
            absorbInputAroundWindow = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;

            try
            {
                Rect contentRect = inRect;
                contentRect.yMax -= CloseButSize.y + 8f;

                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                Widgets.Label(
                    new Rect(contentRect.x, contentRect.y, contentRect.width, TitleHeight),
                    MechanoidMechanitorMechHiveCommunicationUtility.ContactOvermindLabel);
            }
            finally
            {
                GUI.color = oldColor;
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
            }
        }
    }
}
