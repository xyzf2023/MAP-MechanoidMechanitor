using RimWorld;
using RimWorld.Planet;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 在世界界面左下角详细信息中为「联合军事行动」目标追加标识。
    /// 标识完全由当前活动 QuestPart 动态查询决定，任务结束后自动消失，不在 WorldObject 上持久保存标志。
    /// - Site 及其子类（机械族前哨 MAPFactionOutpost、文化 DLC WorkSite）由 Patch_Site_GetInspectString 处理；
    /// - Settlement 由 Patch_WorldObject_GetInspectString 处理（RimWorld 1.6 的 Settlement
    ///   未声明自己的 GetInspectString，而是继承 WorldObject.GetInspectString，因此必须 Patch
    ///   WorldObject 才能覆盖 Settlement；该 Patch 内部只处理 Settlement，Site 实例会直接跳过，
    ///   不会重复追加）。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class SymbiosisCovenantJointOperationInspectPatches
    {
        private const string TargetLabelKey =
            "MAP_MechanoidMechanitor.Symbiosis.JointOp.TargetInspectLabel";

        [HarmonyPatch(typeof(Site), "GetInspectString")]
        public static class Patch_Site_GetInspectString
        {
            [HarmonyPostfix]
            public static void Postfix(Site __instance, ref string __result)
            {
                if (__instance == null)
                {
                    return;
                }

                if (SymbiosisCovenantJointOperationUtility.IsAcceptedJointOperationTarget(__instance))
                {
                    AppendTargetLabel(ref __result);
                }
            }
        }

        [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.GetInspectString))]
        public static class Patch_WorldObject_GetInspectString
        {
            [HarmonyPostfix]
            public static void Postfix(WorldObject __instance, ref string __result)
            {
                if (__instance == null)
                {
                    return;
                }

                // 只处理 Settlement；Site 由 Patch_Site_GetInspectString 单独处理，避免重复追加。
                Settlement? settlement = __instance as Settlement;
                if (settlement == null)
                {
                    return;
                }

                if (SymbiosisCovenantJointOperationUtility.IsAcceptedJointOperationTarget(settlement))
                {
                    AppendTargetLabel(ref __result);
                }
            }
        }

        /// <summary>
        /// 在不破坏其它模组追加文本的前提下，向上附加「联合军事行动目标」标签。
        /// 原字符串为空时直接使用标签；非空时先追加换行；已包含标签时不重复。
        /// </summary>
        private static void AppendTargetLabel(ref string inspectString)
        {
            string label = TargetLabelKey.Translate();
            if (string.IsNullOrEmpty(label))
            {
                return;
            }

            if (inspectString != null && inspectString.Contains(label))
            {
                return;
            }

            if (string.IsNullOrEmpty(inspectString))
            {
                inspectString = label;
            }
            else
            {
                inspectString += "\n" + label;
            }
        }
    }
}
