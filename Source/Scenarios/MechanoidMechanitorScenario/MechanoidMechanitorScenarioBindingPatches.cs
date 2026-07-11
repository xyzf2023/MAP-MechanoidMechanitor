using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(
        typeof(ScenPart_ConfigPage_ConfigureStartingPawnsBase),
        nameof(ScenPart_ConfigPage_ConfigureStartingPawnsBase.PostIdeoChosen))]
    public static class MechanoidMechanitorScenario_ConfigureStartingPawnsBase_PostIdeoChosen_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (!MechanoidMechanitorScenarioUtility.IsScenarioActive)
            {
                return true;
            }

            Find.GameInitData.startingPawnCount = 0;

            if (ModsConfig.BiotechActive)
            {
                Current.Game.customXenotypeDatabase.customXenotypes.Clear();
                foreach (Ideo ideo in Find.IdeoManager.IdeosListForReading)
                {
                    foreach (Precept precept in ideo.PreceptsListForReading)
                    {
                        if (precept is Precept_Xenotype { customXenotype: not null } xenotypePrecept
                            && !Current.Game.customXenotypeDatabase.customXenotypes.Contains(
                                xenotypePrecept.customXenotype))
                        {
                            Current.Game.customXenotypeDatabase.customXenotypes.Add(
                                xenotypePrecept.customXenotype);
                        }
                    }
                }
            }

            if (ModsConfig.IdeologyActive
                && Faction.OfPlayerSilentFail?.ideos?.PrimaryIdeo != null)
            {
                foreach (Precept precept in Faction.OfPlayerSilentFail.ideos.PrimaryIdeo
                    .PreceptsListForReading)
                {
                    if (precept.def.defaultDrugPolicyOverride != null)
                    {
                        Current.Game.drugPolicyDatabase.MakePolicyDefault(
                            precept.def.defaultDrugPolicyOverride);
                    }
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Page_ScenarioEditor), "AddScenPart")]
    public static class MechanoidMechanitorScenario_Page_ScenarioEditor_AddScenPart_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Page_ScenarioEditor __instance, ScenPartDef def)
        {
            if (def == null
                || def.defName != MechanoidMechanitorScenarioUtility.ScenarioPartDefName)
            {
                return;
            }

            MechanoidMechanitorScenarioUtility.EnsureMechanitorStartPartAfterScenarioAdded(
                __instance.EditingScenario);
        }
    }

    [HarmonyPatch(typeof(Scenario), nameof(Scenario.RemovePart))]
    public static class MechanoidMechanitorScenario_Scenario_RemovePart_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Scenario __instance, ScenPart part)
        {
            if (MechanoidMechanitorScenarioUtility.IsSyncingBoundParts)
            {
                return;
            }

            if (part is ScenPart_MechanoidMechanitorScenario)
            {
                MechanoidMechanitorScenarioUtility
                    .RemoveBoundMechanitorStartPartsAfterScenarioRemoved(__instance);
            }
        }
    }

    [HarmonyPatch(
        typeof(Page_SelectScenario),
        nameof(Page_SelectScenario.BeginScenarioConfiguration))]
    public static class MechanoidMechanitorScenario_BeginScenarioConfiguration_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Scenario scen)
        {
            MechanoidMechanitorScenarioUtility.NormalizeScenarioParts(scen);
        }
    }

    [HarmonyPatch(typeof(ScenPart_ConfigPage), nameof(ScenPart_ConfigPage.GetConfigPages))]
    public static class MechanoidMechanitorScenario_SkipStartingPawnConfigPages_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            ScenPart_ConfigPage __instance,
            ref IEnumerable<Page> __result)
        {
            if (__instance is not ScenPart_ConfigPage_ConfigureStartingPawnsBase)
            {
                return true;
            }

            if (!MechanoidMechanitorScenarioUtility.IsScenarioActive)
            {
                return true;
            }

            __result = Enumerable.Empty<Page>();
            return false;
        }
    }

    [HarmonyPatch(typeof(Scenario), nameof(Scenario.PostIdeoChosen))]
    public static class MechanoidMechanitorScenario_Scenario_PostIdeoChosen_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!MechanoidMechanitorScenarioUtility.IsScenarioActive)
            {
                return;
            }

            MechanoidMechanitorScenarioUtility.ClearOrdinaryStartingPawnData();
            GameComponent_MechanoidMechanitorScenarioState.EnableForCurrentGame();
        }
    }
}
