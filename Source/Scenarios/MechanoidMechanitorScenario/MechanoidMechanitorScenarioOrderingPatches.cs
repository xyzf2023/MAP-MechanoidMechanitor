using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(
        typeof(Page_ScenarioEditor),
        nameof(Page_ScenarioEditor.DoWindowContents))]
    public static class
        MechanoidMechanitorScenario_PageScenarioEditor_EnsureBoundGroup_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Page_ScenarioEditor __instance)
        {
            MechanoidMechanitorScenarioUtility.EnsureEditorBoundGroupInvariant(
                __instance.EditingScenario);
        }
    }

    [HarmonyPatch(typeof(Scenario), nameof(Scenario.CanReorder))]
    public static class
        MechanoidMechanitorScenario_Scenario_CanReorderGroup_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            Scenario __instance,
            ScenPart part,
            ReorderDirection dir,
            ref bool __result)
        {
            if (MechanoidMechanitorScenarioUtility.TryGetBoundGroupCanReorder(
                    __instance,
                    part,
                    dir,
                    out bool canReorder))
            {
                __result = canReorder;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Scenario), nameof(Scenario.Reorder))]
    public static class
        MechanoidMechanitorScenario_Scenario_ReorderGroup_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            Scenario __instance,
            ScenPart part,
            ReorderDirection dir)
        {
            return !MechanoidMechanitorScenarioUtility.TryReorderBoundGroup(
                __instance,
                part,
                dir);
        }

        [HarmonyPostfix]
        public static void Postfix(Scenario __instance)
        {
            MechanoidMechanitorScenarioUtility.EnsureBoundGroupAfterReorder(
                __instance);
        }
    }
}
