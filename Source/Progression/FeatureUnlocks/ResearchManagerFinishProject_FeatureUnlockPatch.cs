using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.FinishProject))]
    public static class ResearchManagerFinishProject_FeatureUnlockPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ResearchProjectDef proj)
        {
            if (proj == null)
            {
                return;
            }

            GameComponent_MechanoidMechanitorFeatureManager.NotifyResearchProjectFinished();
        }
    }
}
