using System.Linq;
using GD3;
using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    internal static class GD5StoryCompatibilityPatch
    {
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
