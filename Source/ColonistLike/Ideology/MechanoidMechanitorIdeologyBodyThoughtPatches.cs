using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 在最终想法查询入口返回无效，兼顾警报直接查询和 def.invert 的反转处理。
    /// </summary>
    [HarmonyPatch(typeof(ThoughtWorker), nameof(ThoughtWorker.CurrentState))]
    internal static class MechanoidMechanitorIdeologyPersonalBodyThoughtPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ThoughtWorker __instance, Pawn p, ref ThoughtState __result)
        {
            if (!MechanoidMechanitorIdeologyBodyRequirementUtility
                    .ShouldSuppressPersonalThought(p, __instance.def))
            {
                return true;
            }

            __result = ThoughtState.Inactive;
            return false;
        }
    }

    [HarmonyPatch(typeof(ThoughtWorker), nameof(ThoughtWorker.CurrentSocialState))]
    internal static class MechanoidMechanitorIdeologyBodyOpinionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            ThoughtWorker __instance,
            Pawn otherPawn,
            ref ThoughtState __result)
        {
            // 豁免被评价的机械身体，不取消机械族机械师对人类的文化评价。
            if (!MechanoidMechanitorIdeologyBodyRequirementUtility
                    .ShouldSuppressBodyOpinion(otherPawn, __instance.def))
            {
                return true;
            }

            __result = ThoughtState.Inactive;
            return false;
        }
    }
}
