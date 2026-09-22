using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(PawnColumnWorker_ControlGroup), nameof(PawnColumnWorker_ControlGroup.DoCell))]
    internal static class MechanitorControlGroupColumnPatches
    {
        [HarmonyPrefix]
        private static bool Prefix(Pawn pawn)
        {
            if (pawn == null || pawn.IsGestating() || !ModsConfig.BiotechActive)
                return true;
            if (AutonomousMechUtility.IsAutonomousMech(pawn))
                return false;

            Pawn? overseer = pawn.GetOverseer();
            // 仅防御尚未完成生命周期恢复的状态；绘制不分配、不修改控制组。
            return overseer == null || !MAPMechanitorNodeUtility.HasNode(overseer)
                || pawn.GetMechControlGroup() != null;
        }
    }
}
