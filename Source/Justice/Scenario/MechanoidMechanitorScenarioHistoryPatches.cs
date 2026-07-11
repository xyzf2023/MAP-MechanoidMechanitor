using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(
        typeof(HistoryAutoRecorderWorker_ColonistMood),
        nameof(HistoryAutoRecorderWorker_ColonistMood.PullRecord))]
    public static class MechanoidMechanitorScenario_ColonistMoodHistory_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref float __result)
        {
            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return true;
            }

            __result = 0f;
            return false;
        }
    }
}
