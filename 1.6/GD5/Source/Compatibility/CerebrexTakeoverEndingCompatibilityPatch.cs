using RimWorld;

namespace MAP_MechanoidMechanitor.GD5
{
    /// <summary>先交由 MAP 判断待接管核心，避免 GD5 将接管当作摧毁。</summary>
    internal static class CerebrexTakeoverEndingCompatibilityPatch
    {
        internal static bool Prefix(CompCerebrexCore __instance)
        {
            return CompCerebrexCore_DeactivateCore_TakeoverPatch.Prefix(__instance);
        }
    }
}
