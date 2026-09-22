using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(PawnComponentsUtility), nameof(PawnComponentsUtility.AddAndRemoveDynamicComponents))]
    internal static class MAPMechanitorNodeDynamicComponentsPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Pawn pawn) => MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(pawn);
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
    internal static class MAPMechanitorNodeFactionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Pawn __instance) => MAPMechanitorNodeLifecycleUtility.NotifyLifecycle(__instance);
    }

    [HarmonyPatch(typeof(PawnRelationWorker_Overseer), nameof(PawnRelationWorker_Overseer.OnRelationCreated))]
    internal static class MAPMechanitorNodeRelationCreatedPatch
    {
        // 关系写入是初始化边界；原版随后负责分组。资格查询永远不创建 Tracker。
        [HarmonyPrefix]
        private static void Prefix(Pawn firstPawn, Pawn secondPawn)
        {
            MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(firstPawn);
            MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(secondPawn);
        }
    }
}
