using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 接管注册表原有的简单 workGiverDef 判断，使灭火和通过 JobDef 扩展声明的MOD工作
    /// 能进入正式动态状态评估，而无需改写预算与调度代码。
    /// </summary>
    [HarmonyPatch]
    internal static class DataProcessingDynamicWorkRecognitionPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "IsPawnDoingWork",
                new[] { typeof(Pawn) });
        }

        private static bool Prefix(Pawn target, ref bool __result)
        {
            __result = DataProcessingDynamicWorkRecognitionUtility.IsPawnDoingWork(target);
            return false;
        }
    }
}
