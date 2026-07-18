using LudeonTK;
using MAP_MechanoidMechanitor.Scenarios;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class PurgeDirectiveDebugActions
    {
        [DebugAction(
            "MAP-机械族机械师",
            "立即执行一次肃清指令检查",
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing)]
        private static void ForcePurgeDirectiveProtocolCheck()
        {
            MechanoidMechanitorPurgeDirectiveUtility.ForceProtocolCheckNow();
        }
    }
}
