using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族机械师死亡时立即驱动一次备用机体信件检查，
    /// 使“最后一名机械族机械师阵亡”不必等待低频补漏检查。
    /// 只做注册表查询与信件状态刷新，不扫描全局 Pawn。
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    internal static class OrbitalDataNetworkPawnKilledPatch
    {
        private static void Postfix(Pawn __instance)
        {
            if (__instance?.Dead != true
                || !OrbitalBackupGameEndUtility.IsActive
                || !GameComponent_MechanoidMechanitorRegistry.HasPersistentRecord(__instance))
            {
                return;
            }

            if (OrbitalBackupGameEndUtility.AnyLivingRegisteredMechanitor())
            {
                return;
            }

            OrbitalBackupGameEndUtility.Refresh();
        }
    }

    /// <summary>
    /// 机械族机械师复活后重新进入安全同步流程。
    /// 不在这里直接同步整批 Pawn：复活事件只负责“取消错误的游戏结束状态 + 把该 Pawn
    /// 重新交给科研管理器的既有队列”，实际同步由 NotifyMechanitorInitialized 在安全时执行。
    /// </summary>
    [HarmonyPatch(typeof(ResurrectionUtility), nameof(ResurrectionUtility.TryResurrect))]
    internal static class OrbitalDataNetworkPawnResurrectedPatch
    {
        private static void Postfix(Pawn pawn, ref bool __result)
        {
            if (!__result
                || pawn == null
                || pawn.Dead
                || !GameComponent_MechanoidMechanitorRegistry.HasPersistentRecord(pawn))
            {
                return;
            }

            if (OrbitalBackupGameEndUtility.IsActive)
            {
                OrbitalBackupGameEndUtility.Refresh();
            }

            // 研究完成时处于死亡状态、当时没有获得“机械意识”的机械师，复活后在此补齐。
            GameComponent_MechanoidMechanitorFeatureManager.NotifyMechanitorInitialized(pawn);
        }
    }
}
