using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 所有手动/DEV 肃清合规检查入口都遵守剧情配置来源。
    /// 普通剧本可使用肃清玩法，但不会启动“清除血肉殖民者”的警告与惩罚链。
    /// </summary>
    [HarmonyPatch]
    public static class MechanoidMechanitorStory_PurgeDirectiveForceCheck_Patch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            return AccessTools
                .GetDeclaredMethods(typeof(MechanoidMechanitorPurgeDirectiveUtility))
                .Where(method => method.Name == nameof(
                    MechanoidMechanitorPurgeDirectiveUtility.ForceProtocolCheckNow));
        }

        [HarmonyPrefix]
        public static bool Prefix()
        {
            return GameComponent_MechanoidMechanitorStoryState
                .ShouldRunPurgeFleshColonistComplianceCheck;
        }
    }

    /// <summary>
    /// 共生盟约原实现把活动状态与机械族机械师专用剧本绑定。
    /// 普通剧本剧情配置只在这里补足活动判定，不改动专用剧本原有分支。
    /// </summary>
    [HarmonyPatch(
        typeof(GameComponent_SymbiosisCovenantState),
        nameof(GameComponent_SymbiosisCovenantState.IsActive),
        MethodType.Getter)]
    public static class MechanoidMechanitorStory_SymbiosisCovenantIsActive_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (__result
                || !GameComponent_MechanoidMechanitorStoryState
                    .IsGeneralScenarioStoryConfiguration)
            {
                return;
            }

            MechanoidMechanitorStoryConfiguration? configuration =
                GameComponent_MechanoidMechanitorStoryState.CurrentConfiguration;
            __result = configuration != null
                && configuration.symbiosisCovenantEnabled
                && !configuration.purgeDirectiveEnabled;
        }
    }
}
