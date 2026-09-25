using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    // 新段落使用稳定节点 ID；显示文字使用本模块中文 Keyed 文本。
    public sealed class GD5DialogueDef : Def
    {
        public string entryNode = "";
        public string titleKey = "";
        public List<GD5DialogueNode> nodes = new List<GD5DialogueNode>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            foreach (string error in Validate()) yield return error;
        }

        internal IEnumerable<string> Validate()
        {
            if (nodes.Count == 0 || !nodes.Any(n => n.id == entryNode))
                yield return "缺少入口节点。";
            if (string.IsNullOrEmpty(titleKey)) yield return "缺少标题翻译键。";
            if (nodes.GroupBy(n => n.id).Any(g => g.Count() != 1))
                yield return "节点 ID 重复。";
            foreach (GD5DialogueNode node in nodes)
            {
                if (string.IsNullOrEmpty(node.id) || string.IsNullOrEmpty(node.textKey) || node.options.Count == 0)
                    yield return "节点缺少 ID、正文或选项：" + node.id;
                foreach (GD5DialogueOption option in node.options)
                {
                    if (string.IsNullOrEmpty(option.textKey)) yield return "选项缺少文本：" + node.id;
                    if (option.action == GD5DialogueAction.Continue && !nodes.Any(n => n.id == option.next))
                        yield return "选项跳转目标不存在：" + node.id + " -> " + option.next;
                }
                if (!node.options.Any(o => o.condition == GD5DialogueCondition.Always))
                    yield return "节点至少需要一个无条件选项：" + node.id;
            }
        }
    }

    public sealed class GD5DialogueNode
    {
        public string id = "";
        public string textKey = "";
        public List<GD5DialogueOption> options = new List<GD5DialogueOption>();
    }

    public enum GD5DialogueCondition { Always, HiveHostile, HiveNeutral, HiveAlly }
    public enum GD5DialogueAction { Continue, CompleteFirstContact }

    public sealed class GD5DialogueOption
    {
        public string textKey = "";
        public string next = "";
        public GD5DialogueCondition condition;
        public GD5DialogueAction action;
    }
}
