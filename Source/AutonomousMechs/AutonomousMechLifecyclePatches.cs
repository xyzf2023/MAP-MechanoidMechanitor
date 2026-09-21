using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    internal static class AutonomousMechSpawnPatch
    {
        // 生成流程可能先查询受控状态，必须先恢复纯授权数据。
        [HarmonyPrefix]
        private static void Prefix(Pawn __instance) =>
            GameComponent_AutonomousMechRegistry.SynchronizeAutomaticSources(__instance);

        [HarmonyPostfix]
        private static void Postfix(Pawn __instance) =>
            GameComponent_AutonomousMechRegistry.NotifyPawnLifecycle(__instance);
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
    internal static class AutonomousMechFactionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Pawn __instance) =>
            GameComponent_AutonomousMechRegistry.NotifyPawnLifecycle(__instance);
    }

    // 直接控制组分配也须拦截，不能仅依赖 UI 和双向关系的写入门控。
    [HarmonyPatch(typeof(MechanitorControlGroup), nameof(MechanitorControlGroup.Assign))]
    internal static class AutonomousMechControlGroupAssignmentPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Pawn pawn) => !AutonomousMechUtility.IsAutonomousMech(pawn);
    }

    [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.AssignPawnControlGroup))]
    internal static class AutonomousMechTrackerAssignmentPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Pawn pawn) => !AutonomousMechUtility.IsAutonomousMech(pawn);
    }
}
