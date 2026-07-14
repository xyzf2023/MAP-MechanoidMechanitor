using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(ThingDef), "get_AllRecipes")]
    public static class MassProductionMechGestatorRecipePatches
    {
        private const string MassProductionGestatorDefName = "MAP_MassProductionMechGestator";

        [HarmonyPostfix]
        public static void Postfix(ThingDef __instance, List<RecipeDef> __result)
        {
            if (__result == null || __instance == null)
            {
                return;
            }

            if (__instance.defName != MassProductionGestatorDefName)
            {
                return;
            }

            if (!ModsConfig.BiotechActive)
            {
                return;
            }

            IReadOnlyList<RecipeDef> eligible = MassProductionMechGestatorRecipeRegistry.EligibleRecipes;
            for (int i = 0; i < eligible.Count; i++)
            {
                RecipeDef recipe = eligible[i];
                if (recipe == null)
                {
                    continue;
                }

                // 幂等追加：保留 XML 基础顺序与其他 MOD 已追加内容，仅补缺失条目。
                if (!__result.Contains(recipe))
                {
                    __result.Add(recipe);
                }
            }
        }
    }
}
