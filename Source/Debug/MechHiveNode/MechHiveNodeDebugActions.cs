using LudeonTK;
using MAP_MechanoidMechanitor.Scenarios;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MechHiveNodeDebugActions
    {
        [DebugAction(
            "MAP-机械族机械师",
            "尝试生成机械巢节点",
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing)]
        private static void DevTryGenerateMechHiveNode()
        {
            if (Current.Game == null || Find.World == null)
            {
                return;
            }

            MechHiveNodeManager? manager = Find.World.GetComponent<MechHiveNodeManager>();
            if (manager == null)
            {
                Log.Warning("[MAP] 未找到 MechHiveNodeManager，无法执行开发者生成指令。");
                return;
            }

            manager.DevTryNaturalGenerationAttempt();
        }
    }
}
