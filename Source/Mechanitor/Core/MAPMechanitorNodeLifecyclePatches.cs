using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(PawnComponentsUtility), nameof(PawnComponentsUtility.AddAndRemoveDynamicComponents))]
    internal static class MAPMechanitorNodeDynamicComponentsPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Pawn pawn)
        {
            MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(pawn);
            MechanoidMechanitorCapabilityLifecycleUtility.EnsureInfrastructure(pawn);
        }
    }

    [HarmonyPatch(typeof(ResurrectionUtility), nameof(ResurrectionUtility.TryResurrect))]
    internal static class MAPMechanitorNodeResurrectedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Pawn pawn, bool __result)
        {
            if (!__result || pawn == null || pawn.Dead || pawn.Destroyed || pawn.Discarded)
                return;
            if (!GameComponent_MechanoidMechanitorRegistry.HasPersistentRecord(pawn)
                && !MAPMechanitorNodeUtility.IsMechanitorNodeController(pawn))
                return;

            // 原版首次创建组件时 Pawn 尚未解除死亡状态；dontSpawn / 离图复活
            // 也不会再经过 SpawnSetup。统一进入已有去重队列，在安全阶段补齐
            // 能力基础设施与机械师 Tracker，复用队列的离图处理分支。
            GameComponent_MechanoidMechanitorRegistry.QueuePostSpawnInitialization(pawn);
        }
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
