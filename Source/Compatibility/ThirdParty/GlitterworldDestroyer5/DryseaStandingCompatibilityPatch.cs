using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5
{
    /// <summary>
    /// 只在原方法拒绝时，为站在枯海白花格上的玩家机械族机械师补足资格。
    /// 原MOD仍负责选择格内首个Pawn、持续站立计时、重复触发保护和任务信号。
    /// </summary>
    internal static class DryseaStandingCompatibilityPatch
    {
        internal static void Postfix(object? __instance, ref bool __result)
        {
            if (__result || __instance is not Thing trigger)
            {
                return;
            }

            Map? map = trigger.Map;
            if (map == null)
            {
                return;
            }

            Pawn? pawn = trigger.Position.GetFirstPawn(map);
            if (pawn == null
                || pawn.Faction != Faction.OfPlayer
                || !pawn.RaceProps.IsMechanoid
                || !MechanoidMechanitorRoleUtility
                    .IsMechanoidMechanitor(pawn))
            {
                return;
            }

            __result = true;
        }
    }
}
