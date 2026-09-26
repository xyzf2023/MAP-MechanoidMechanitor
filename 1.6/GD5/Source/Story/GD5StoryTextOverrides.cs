using System;
using System.Collections.Generic;
using System.Linq;
using GD3;
using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    public sealed class GD5StoryTextOverridesDef : Def
    {
        public List<GD5StoryNodeOverride> nodes = new List<GD5StoryNodeOverride>();
    }

    public sealed class GD5StoryNodeOverride
    {
        public List<string> scripts = new List<string>();
        public int node;
        public List<GD5StoryParagraphOverride> paragraphs = new List<GD5StoryParagraphOverride>();
        public List<GD5StoryOptionOverride> options = new List<GD5StoryOptionOverride>();
        // 将另一节点的末尾正文及明确跳转选项并入当前节点，不改变共享树的索引。
        public int mergeNode = -1;
        public int mergeSkipParagraphs;
    }

    public sealed class GD5StoryParagraphOverride
    {
        public int paragraph; // 与文档 T1/T2 一致，从 1 开始。
        public string textKey = "";
        public string? conditionalTextKey;
        public GD5DialogueCondition condition;
        public bool remove;
    }

    public sealed class GD5StoryOptionOverride
    {
        public int index; // 与文档 O0/O1 一致，从 0 开始。
        public string textKey = "";
    }

    internal static class GD5StoryTextOverrides
    {
        private static readonly Dictionary<List<ScriptTree>, Dictionary<int, GD5StoryNodeOverride>> Rules =
            new Dictionary<List<ScriptTree>, Dictionary<int, GD5StoryNodeOverride>>();

        internal static void Initialize()
        {
            Rules.Clear();
            var definition = DefDatabase<GD5StoryTextOverridesDef>.GetNamed("MAP_GD5_LaterStoryText");
            foreach (GD5StoryNodeOverride rule in definition.nodes)
            {
                if (rule.scripts.Count == 0) throw new InvalidOperationException("剧情替换缺少目标 Def。");
                foreach (string name in rule.scripts)
                {
                    MechanoidScriptDef script = DefDatabase<MechanoidScriptDef>.GetNamed(name);
                    if (rule.node < 0 || rule.node >= script.scriptTree.Count)
                        throw new InvalidOperationException("剧情替换节点不存在：" + name + "/" + rule.node);
                    ScriptTree node = script.scriptTree[rule.node];
                    int count = SplitParagraphs(node.dialogue).Length;
                    if (rule.paragraphs.Any(p => p.paragraph < 1 || p.paragraph > count
                            || (!p.remove && string.IsNullOrEmpty(p.textKey)))
                        || rule.paragraphs.GroupBy(p => p.paragraph).Any(g => g.Count() > 1)
                        || rule.options.Any(o => o.index < 0 || o.index >= node.buttons.Count || string.IsNullOrEmpty(o.textKey)))
                        throw new InvalidOperationException("剧情替换段落／选项结构变化：" + name + "/" + rule.node);
                    if (rule.mergeNode >= 0)
                    {
                        if (rule.mergeNode >= script.scriptTree.Count)
                            throw new InvalidOperationException("合并目标不存在：" + name);
                        ScriptTree merged = script.scriptTree[rule.mergeNode];
                        // 合并后所有选项必须明确跳转，不能依赖原 index + 1。
                        if (rule.mergeSkipParagraphs < 0 || rule.mergeSkipParagraphs >= SplitParagraphs(merged.dialogue).Length
                            || merged.buttons.Count == 0
                            || merged.buttons.Any(b => b.action != "Continue" || b.jumpTo < 0 || b.jumpTo >= script.scriptTree.Count || b.quest != null))
                            throw new InvalidOperationException("合并节点跳转结构变化：" + name);
                    }
                    if (!Rules.TryGetValue(script.scriptTree, out var byNode))
                        Rules.Add(script.scriptTree, byNode = new Dictionary<int, GD5StoryNodeOverride>());
                    byNode.Add(rule.node, rule);
                }
            }
        }

        internal static void Apply(List<ScriptTree> tree, int index, Pawn pawn, Map map,
            ref string description, ref List<ScriptButton> options)
        {
            if (!Rules.TryGetValue(tree, out var byNode) || !byNode.TryGetValue(index, out var rule)) return;
            var context = new GD5StoryContext(map, pawn);
            string[] original = SplitParagraphs(description);
            var paragraphs = new List<string>();
            for (int i = 0; i < original.Length; i++)
            {
                GD5StoryParagraphOverride? change = rule.paragraphs.FirstOrDefault(p => p.paragraph == i + 1);
                if (change == null) paragraphs.Add(original[i]);
                else if (!change.remove)
                {
                    string key = change.conditionalTextKey != null && context.Matches(change.condition)
                        ? change.conditionalTextKey : change.textKey;
                    // 保留 {0}，由原 CommunicationWindow 用本次通讯者名字格式化。
                    paragraphs.Add(key.Translate().ToString());
                }
            }
            List<ScriptButton> sourceOptions = options;
            if (rule.mergeNode >= 0)
            {
                ScriptTree merged = tree[rule.mergeNode];
                paragraphs.AddRange(SplitParagraphs(merged.dialogue).Skip(rule.mergeSkipParagraphs));
                sourceOptions = merged.buttons;
            }
            description = string.Join("\n\n", paragraphs);
            if (rule.options.Count == 0 && rule.mergeNode < 0) return;
            // 每个窗口独享按钮副本；quest/branch/to/jumpTo 保持原值，不污染原 Def。
            options = sourceOptions.Select(b => new ScriptButton
            {
                text = b.text, action = b.action, quest = b.quest,
                jumpTo = b.jumpTo, branch = b.branch, to = b.to
            }).ToList();
            foreach (GD5StoryOptionOverride option in rule.options)
                options[option.index].text = option.textKey.Translate();
        }

        private static string[] SplitParagraphs(string text) =>
            text.Replace("\\n", "\n").Replace("\r\n", "\n").Split(new[] { "\n\n" }, StringSplitOptions.None);
    }
}
