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

        // 仅替代 300 阶段的前置任务检查。使用原节点、原窗口和原按钮动作，
        // 并保留闪毁5“打开通讯即标记阶段完成”的行为，不提前解锁 300。
        internal static void OpenAfterSubcore(TradeWindow_BlackMech source, MechanoidScriptDef tree, Map map, Pawn pawn)
        {
            MissionComponent mission = Find.World.GetComponent<MissionComponent>();
            ScriptTree node = tree.scriptTree[0];
            var window = new CommunicationWindow_BlackMech(node.title, node.dialogue,
                node.graphic, node.drawSize, node.drawOffset, map, pawn,
                node.buttons, tree.scriptTree, 0);
            Find.WindowStack.Add(window);
            if (!mission.script_Finished.Contains(tree.ID)) mission.script_Finished.Add(tree.ID);
            source.Close();
        }
    }
}
