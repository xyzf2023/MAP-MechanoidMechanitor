using System.Linq;
using GD3;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    internal sealed class GD5StoryDialog : Window
    {
        private readonly GD5DialogueDef dialogue;
        private readonly GD5StoryContext context;
        private readonly GD5DialogueNode node;
        private GraphicWindow? portraitWindow;
        private bool completed;

        private const float WindowWidth = 520f;
        private const float WindowHeight = 570f;

        public override Vector2 InitialSize => new Vector2(WindowWidth, WindowHeight);

        internal bool IsVisitorConversation(Pawn visitor) => ReferenceEquals(context.Visitor, visitor);

        internal GD5StoryDialog(GD5DialogueDef dialogue)
            : this(dialogue, new GD5StoryContext(null, null))
        {
        }

        internal GD5StoryDialog(GD5DialogueDef dialogue, GD5StoryContext context)
            : this(dialogue, dialogue.entryNode, context)
        {
        }

        private GD5StoryDialog(GD5DialogueDef dialogue, string nodeId, GD5StoryContext context)
        {
            this.dialogue = dialogue;
            node = dialogue.nodes.First(n => n.id == nodeId);
            this.context = context;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            closeOnCancel = false;
            doCloseX = false;
            doCloseButton = false;
            soundAppear = SoundDefOf.CommsWindow_Open;
            soundClose = SoundDefOf.CommsWindow_Close;
        }

        public override void PreOpen()
        {
            base.PreOpen();
            string? graphicPath = dialogue.graphic;
            if (!node.hideGraphic && graphicPath != null && graphicPath.Length > 0)
            {
                // 通讯保留电话叠图；面对面交谈沿用同一窗口布局，只绘制角色头像。
                portraitWindow = context.Visitor == null
                    ? new GraphicWindow(graphicPath, dialogue.drawSize,
                        dialogue.drawOffset, WindowWidth, WindowHeight)
                    : new GD5VisitorPortraitWindow(graphicPath, dialogue.drawSize,
                        dialogue.drawOffset, WindowWidth, WindowHeight);
                Find.WindowStack.Add(portraitWindow);
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            // 仅关闭本次通讯的头像；异常关闭也不能留下悬空窗口。
            GraphicWindow? portrait = portraitWindow;
            portraitWindow = null;
            portrait?.Close();
        }

        public override void DoWindowContents(Rect inRect)
        {
            // 与创意工坊版 CommunicationWindow_BlackMech.DoWindowContents
            // 使用相同的尺寸、内边距、正文排版及底部文字选项绘制。
            Rect contentRect = inRect.ContractedBy(10f);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(contentRect.x, contentRect.y, contentRect.width, 40f),
                (node.titleKey ?? dialogue.titleKey).Translate());
            Text.Font = GameFont.Small;
            string textKey = node.textVariants.FirstOrDefault(v => context.Matches(v.condition))?.textKey ?? node.textKey;
            string text = context.Translate(textKey);
            Widgets.Label(new Rect(contentRect.x, contentRect.y + Text.LineHeight + 5f,
                contentRect.width, Text.CalcHeight(text, contentRect.width)), text);

            // 条件选项优先；无匹配条件时使用无条件选项（中立回答）。
            var options = node.options.Where(o => o.condition != GD5DialogueCondition.Always && context.Matches(o.condition)).ToList();
            if (options.Count == 0)
                options = node.options.Where(o => o.condition == GD5DialogueCondition.Always).ToList();
            for (int i = 0; i < options.Count; i++)
            {
                GD5DialogueOption option = options[i];
                Rect optionRect = new Rect(contentRect.x,
                    inRect.height - 25f - (options.Count + 1) * Text.LineHeight + (i + 2) * Text.LineHeight,
                    contentRect.width, Text.LineHeight);
                string? disabledReason = option.action == GD5DialogueAction.DepartWithBlackHive
                    ? GD5BlackHiveEndingService.DepartureDisabledReason(context) : null;
                if (option.action == GD5DialogueAction.CancelBlackHiveVisit
                    || option.action == GD5DialogueAction.ReturnBlackHiveSpeaker)
                    disabledReason = GD5BlackHiveEndingService.ReturnDisabledReason(context,
                        speakerOnly: option.action == GD5DialogueAction.ReturnBlackHiveSpeaker);
                if (disabledReason == null) Widgets.DrawHighlightIfMouseover(optionRect);
                Color oldColor = GUI.color;
                if (disabledReason != null) GUI.color = Color.gray;
                Widgets.Label(optionRect, option.textKey.Translate());
                GUI.color = oldColor;
                if (disabledReason != null) TooltipHandler.TipRegion(optionRect, disabledReason);
                if (disabledReason != null || !Widgets.ButtonInvisible(optionRect))
                    continue;
                if (option.action == GD5DialogueAction.Close)
                {
                    Close();
                }
                else if (option.action == GD5DialogueAction.ScheduleBlackHiveVisit)
                {
                    if (!completed)
                    {
                        completed = true;
                        if (GameComponent_GD5StoryState.Current?.TrySchedule(context.Map, context.Speaker) != true)
                            Messages.Message(GD5BlackHiveEndingService.ContactDisabledReason(context.Map)
                                ?? "MAP_GD5.Ending.Unavailable".Translate(), MessageTypeDefOf.RejectInput);
                        Close();
                    }
                }
                else if (option.action == GD5DialogueAction.DepartWithBlackHive)
                {
                    if (!completed && GameComponent_GD5StoryState.Current?.TryStartDeparture(context.Visitor) == true)
                    {
                        completed = true;
                        Close();
                    }
                }
                else if (option.action == GD5DialogueAction.CancelBlackHiveVisit)
                {
                    if (!completed && GameComponent_GD5StoryState.Current?.TryCancelVisit(context.Visitor) == true)
                    {
                        completed = true;
                        Close();
                    }
                }
                else if (option.action == GD5DialogueAction.ReturnBlackHiveSpeaker)
                {
                    if (!completed && GameComponent_GD5StoryState.Current?.TryReturnSpeaker(
                        context.Visitor, context.Speaker) == true)
                    {
                        completed = true;
                        Close();
                    }
                }
                else if (option.action == GD5DialogueAction.CompleteFirstContact)
                {
                    if (!completed && GD5StoryFlowService.CompleteFirstContact())
                    {
                        completed = true;
                        Close();
                    }
                }
                else if (option.action == GD5DialogueAction.ResumeCooperation)
                {
                    // 构造成功后再关闭当前窗；仍使用原对话树，保证 Parent 与后续跳转有效。
                    Window? nextWindow = GD5StoryFlowService.CreateCooperationContinuation(context);
                    if (nextWindow != null)
                    {
                        Close();
                        Find.WindowStack.Add(nextWindow);
                    }
                }
                else
                {
                    // 与原通讯一样，每个节点关闭旧窗口并重新打开，保留通讯音效。
                    // 沿用本次通讯的关系快照，不在翻页时重新选择关系分支。
                    var nextWindow = new GD5StoryDialog(dialogue, option.next, context);
                    Close();
                    Find.WindowStack.Add(nextWindow);
                }
                // 本帧节点已改变，不能继续处理旧节点按钮。
                return;
            }
        }
    }
}
