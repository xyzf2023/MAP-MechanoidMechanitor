using HarmonyLib;
using RimWorld;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(typeof(GameEnder), nameof(GameEnder.CheckOrUpdateGameOver))]
    public static class MechanoidMechanitorScenario_GameEnder_CheckOrUpdateGameOver_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(GameEnder __instance)
        {
            if (OrbitalBackupGameEndUtility.IsActive)
            {
                OrbitalBackupGameEndUtility.HandleCheckOrUpdateGameOver(__instance);

                // 仍有存活机械族机械师：已取消游戏结束状态，跳过原版逻辑。
                if (OrbitalBackupGameEndUtility.AnyLivingRegisteredMechanitor())
                {
                    return false;
                }

                // 全部损毁：交还原版判断是否还有人类殖民者可以继续游戏，
                // 备用机体信件已在上方确保唯一；原版游戏结束信件由 GameEndTick 拦截清理。
                return true;
            }

            if (!MechanoidMechanitorScenarioUtility.ShouldPreventGameOver)
            {
                return true;
            }

            MechanoidMechanitorScenarioUtility.CancelGameOverState(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(GameEnder), nameof(GameEnder.GameEndTick))]
    public static class MechanoidMechanitorScenario_GameEnder_GameEndTick_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(GameEnder __instance)
        {
            if (OrbitalBackupGameEndUtility.IsActive)
            {
                // 完全接管：阻止原版“所有人都死了”信件，
                // 以及等待 20000 tick 后替换为“生成流浪者”的信件。
                OrbitalBackupGameEndUtility.HandleGameEndTick(__instance);
                return false;
            }

            if (!__instance.gameEnding && !MechanoidMechanitorScenarioUtility.HasGameEndedLetter)
            {
                return true;
            }

            if (!MechanoidMechanitorScenarioUtility.ShouldPreventGameOver)
            {
                return true;
            }

            MechanoidMechanitorScenarioUtility.CancelGameOverState(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(GameEnder), nameof(GameEnder.CanSpawnNewWanderers))]
    public static class MechanoidMechanitorScenario_GameEnder_CanSpawnNewWanderers_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result)
        {
            if (OrbitalBackupGameEndUtility.IsActive
                && !OrbitalBackupGameEndUtility.AnyLivingRegisteredMechanitor())
            {
                // 全部机械族机械师损毁期间不生成流浪者，避免绕过备用机体信件。
                __result = false;
                return false;
            }

            if (!MechanoidMechanitorScenarioUtility.ShouldPreventGameOver)
            {
                return true;
            }

            __result = false;
            return false;
        }
    }
}
