using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    internal sealed class GD5StoryDialog : Window
    {
        private readonly GD5DialogueDef dialogue;
        private readonly GD5DialogueCondition relation;
        private GD5DialogueNode node;
        private Vector2 scroll;
        private bool completed;

        public override Vector2 InitialSize => new Vector2(620f, 570f);

        internal GD5StoryDialog(GD5DialogueDef dialogue)
        {
            this.dialogue = dialogue;
            node = dialogue.nodes.First(n => n.id == dialogue.entryNode);
            relation = GD5StoryFlowService.GetHiveRelation();
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            closeOnCancel = false;
            doCloseX = false;
            doCloseButton = false;
            soundAppear = SoundDefOf.CommsWindow_Open;
            soundClose = SoundDefOf.CommsWindow_Close;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 40f), dialogue.titleKey.Translate());
            Text.Font = GameFont.Small;
            // 条件选项优先；无匹配条件时使用无条件选项（中立回答）。
            var options = node.options.Where(o => o.condition == relation).ToList();
            if (options.Count == 0)
                options = node.options.Where(o => o.condition == GD5DialogueCondition.Always).ToList();
            float footer = inRect.height - options.Count * 64f;
            Rect outRect = new Rect(0, 50f, inRect.width, footer - 64f);
            string text = node.textKey.Translate();
            Rect view = new Rect(0, 0, outRect.width - 20f,
                Mathf.Max(outRect.height, Text.CalcHeight(text, outRect.width - 20f)));
            Widgets.BeginScrollView(outRect, ref scroll, view);
            Widgets.Label(view, text);
            Widgets.EndScrollView();
            for (int i = 0; i < options.Count; i++)
            {
                GD5DialogueOption option = options[i];
                if (!Widgets.ButtonText(new Rect(0, footer + i * 64f, inRect.width, 58f), option.textKey.Translate()))
                    continue;
                if (option.action == GD5DialogueAction.CompleteFirstContact)
                {
                    if (!completed && GD5StoryFlowService.CompleteFirstContact())
                    {
                        completed = true;
                        Close();
                    }
                }
                else
                {
                    node = dialogue.nodes.First(n => n.id == option.next);
                    scroll = Vector2.zero;
                }
                // 本帧节点已改变，不能继续处理旧节点按钮。
                return;
            }
        }
    }
}
