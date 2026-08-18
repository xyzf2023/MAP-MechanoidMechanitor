using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族机械师灵感发放 Scope 补丁。
    /// 原版 TryStartInspiration 内部会再次调用 InspirationWorker.InspirationCanOccur，
    /// 而该原版方法会因 Pawn.IsColonist / Humanlike 把机械族机械师挡掉。
    /// 这里只在“本系统主动发放且 GrantScope 精确匹配”时跳过原版，
    /// 改用机械族专用资格判定。任何非匹配情况（普通殖民者、普通机械族、
    /// 第三方 MOD、DEV 原版工具、仪式、任务等）一律 return true 走原版。
    /// </summary>
    [HarmonyPatch(
        typeof(InspirationWorker),
        nameof(InspirationWorker.InspirationCanOccur))]
    public static class Patch_InspirationWorker_InspirationCanOccur_MechanoidMechanitor
    {
        [HarmonyPrefix]
        public static bool Prefix(
            InspirationWorker __instance,
            Pawn pawn,
            ref bool __result)
        {
            InspirationDef? def = __instance.def;

            if (def == null
                || !MechanoidMechanitorInspirationUtility
                    .IsGrantScopeMatch(pawn, def))
            {
                return true;
            }

            __result =
                MechanoidMechanitorInspirationUtility
                    .CanOccurForMechanoidMechanitor(pawn, def);

            return false;
        }
    }
}
