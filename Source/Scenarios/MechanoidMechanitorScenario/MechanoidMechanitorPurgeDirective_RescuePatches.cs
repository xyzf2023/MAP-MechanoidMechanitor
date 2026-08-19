using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 救援后自动加入殖民地：通过私有字段注入让原版跳过“获救后加入”分支，
    /// 并保持该 Pawn 之后离开（leftAfterRescue = true）。
    /// 不 Destroy、不先加入再踢、不创建额外 Lord。
    /// </summary>
    [HarmonyPatch(typeof(Pawn_GuestTracker), "Notify_PawnUndowned")]
    public static class
        MechanoidMechanitorPurgeDirective_PawnGuestTracker_NotifyPawnUndowned_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(
            Pawn_GuestTracker __instance,
            Pawn ___pawn)
        {
            if (!MechanoidMechanitorPurgeDirectivePopulationPolicy
                    .WouldAddForbiddenFreeColonist(
                        ___pawn,
                        Faction.OfPlayerSilentFail))
            {
                return;
            }

            // 不 return false：继续让原版执行，
            // 但原版会因 leftAfterRescue == true 跳过获救后加入分支。
            __instance.leftAfterRescue = true;
        }
    }
}
