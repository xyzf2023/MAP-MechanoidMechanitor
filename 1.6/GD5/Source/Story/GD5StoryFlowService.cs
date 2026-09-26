using System.Collections.Generic;
using GD3;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    internal static class GD5StoryFlowService
    {
        internal static bool IsEnabled => Current.Game != null
            && MechanoidMechanitorScenarioUtility.IsScenarioActive;

        internal static GD5DialogueCondition GetHiveRelation()
        {
            Faction? hive = MechanoidMechanitorOrdinaryFactionUtility.TryGetMechHive();
            Faction? player = Faction.OfPlayerSilentFail;
            if (hive == null || player == null || hive == player)
                return GD5DialogueCondition.HiveNeutral;
            switch (hive.RelationKindWith(player))
            {
                case FactionRelationKind.Hostile: return GD5DialogueCondition.HiveHostile;
                case FactionRelationKind.Ally: return GD5DialogueCondition.HiveAlly;
                default: return GD5DialogueCondition.HiveNeutral;
            }
        }

        internal static bool CompleteFirstContact()
        {
            GameComponent_GD5StoryState? state = GameComponent_GD5StoryState.Current;
            MissionComponent? mission = Find.World?.GetComponent<MissionComponent>();
            if (!IsEnabled || state == null || mission == null) return false;
            mission.script_Finished ??= new List<int>();
            foreach (int stage in new[] { 0, 100, 200 })
                if (!mission.script_Finished.Contains(stage)) mission.script_Finished.Add(stage);
            state.firstContactCompleted = true;
            return true;
        }

        // 替换 300 开头并跳过核心前置；保持进入 300 时记录阶段完成的原行为。
        internal static void OpenAfterSubcore(TradeWindow_BlackMech source, MechanoidScriptDef tree, Map map, Pawn pawn)
        {
            MissionComponent mission = Find.World.GetComponent<MissionComponent>();
            var window = new GD5StoryDialog(GD5CompatibilityBootstrap.Cooperation,
                new GD5StoryContext(map, pawn));
            Find.WindowStack.Add(window);
            if (!mission.script_Finished.Contains(tree.ID)) mission.script_Finished.Add(tree.ID);
            source.Close();
        }

        internal static Window? CreateCooperationContinuation(GD5StoryContext context)
        {
            if (context.Map == null || context.Speaker == null) return null;
            MechanoidScriptDef tree = DefDatabase<MechanoidScriptDef>.GetNamed("Scripts_300");
            ScriptTree node = tree.scriptTree[3];
            string prefixKey = context.Relation == GD5DialogueCondition.HiveAlly
                ? "MAP_GD5.Cooperation.IntelligenceAlly"
                : "MAP_GD5.Cooperation.Intelligence";
            // 只替换原 300-03-T1；后面的科技／物资说明直接保留当前原 Def 文本。
            string description = context.Translate(prefixKey) + CooperationOriginalTail(tree);
            return new CommunicationWindow_BlackMech(node.title, description, node.graphic,
                node.drawSize, node.drawOffset, context.Map, context.Speaker, node.buttons, tree.scriptTree, 3);
        }

        internal static string CooperationOriginalTail(MechanoidScriptDef tree)
        {
            string text = tree.scriptTree[3].dialogue.Replace("\\n", "\n").Replace("\r\n", "\n");
            int separator = text.IndexOf("\n\n", System.StringComparison.Ordinal);
            if (separator < 0)
                throw new System.InvalidOperationException("Scripts_300 第 3 节点缺少后续段落分隔，无法安全保留原文。");
            return text.Substring(separator);
        }
    }
}
