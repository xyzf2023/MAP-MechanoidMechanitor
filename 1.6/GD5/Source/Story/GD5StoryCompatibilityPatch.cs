using System.Linq;
using System.Collections.Generic;
using GD3;
using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    internal static class GD5StoryCompatibilityPatch
    {
        // 原窗口每次跳转都会重新构造；只覆盖当前窗口正文，保留原树及所有按钮。
        internal static void CommunicationConstructorPrefix(ref string description,
            List<ScriptTree>? scriptTree, int index, Pawn pawn, Map map, ref List<ScriptButton> options)
        {
            if (!GD5StoryFlowService.IsEnabled
                || GameComponent_GD5StoryState.Current?.firstContactCompleted != true
                || scriptTree == null)
                return;
            GD5StoryTextOverrides.Apply(scriptTree, index, pawn, map, ref description, ref options);
            MechanoidScriptDef? tree = DefDatabase<MechanoidScriptDef>.GetNamedSilentFail("Scripts_300");
            if (tree == null || !ReferenceEquals(scriptTree, tree.scriptTree)) return;
            if (index == 4)
                description = "MAP_GD5.Cooperation.Introduction".Translate();
            else if (index == 8)
                description = "MAP_GD5.Cooperation.Phone".Translate();
        }

        internal static bool Prefix(TradeWindow_BlackMech __instance, Map ___map, Pawn ___pawn)
        {
            if (!GD5StoryFlowService.IsEnabled) return true;
            MissionComponent? mission = Find.World?.GetComponent<MissionComponent>();
            GameComponent_GD5StoryState? state = GameComponent_GD5StoryState.Current;
            MechanoidScriptDef? tree = mission?.Script;
            if (tree == null || state == null) return true;
            if (tree.defName == "Scripts_Begin" && !state.firstContactCompleted)
            {
                if (!Find.WindowStack.Windows.Any(w => w is GD5StoryDialog))
                    Find.WindowStack.Add(new GD5StoryDialog(GD5CompatibilityBootstrap.FirstContact));
                __instance.Close();
                return false;
            }
            if (tree.defName == "Scripts_300" && state.firstContactCompleted)
            {
                // 缺少通讯上下文时保留入口，不回落到会重新生成核心任务的原逻辑。
                if (___map == null || ___pawn == null)
                {
                    Log.Error("[MAP-GD5] 后续通讯缺少地图或通讯者，未推进剧情。");
                    return false;
                }
                GD5StoryFlowService.OpenAfterSubcore(__instance, tree, ___map, ___pawn);
                return false;
            }
            return true;
        }
    }
}
