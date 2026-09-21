using HarmonyLib;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族机械师注册表的派生缓存失效时，同步清除 State 热路径使用的 Pawn 节点档案缓存。
    /// 该入口覆盖新增、删除、回滚、Origin 转换以及读档重建等注册表结构变化。
    /// </summary>
    [HarmonyPatch(typeof(GameComponent_MechanoidMechanitorRegistry), "InvalidateDerivedCaches")]
    internal static class Patch_MechanoidMechanitorRegistry_InvalidateDerivedCaches_NodeProfileCache
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            MAPMechanitorNodeUtility.InvalidateVanillaControlNodeProfileCache();
            GameComponent_AutonomousMechRegistry.SynchronizeMechanitorSources();
        }
    }
}
