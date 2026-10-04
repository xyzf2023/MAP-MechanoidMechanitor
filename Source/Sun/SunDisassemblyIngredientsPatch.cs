using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>玩家型太阳的固定拆解返还；确认菜单与原版拆解任务共用此入口。</summary>
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.IngredientsFromDisassembly))]
    internal static class SunDisassemblyIngredientsPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ThingDef mech, ref List<ThingDefCountClass> __result)
        {
            if (mech == null || mech.defName != "MAP_Mech_Sun")
                return true;

            // 每次独立生成清单，避免改写原版共享列表或在反复打开菜单时累加。
            __result = new List<ThingDefCountClass>
            {
                new ThingDefCountClass(
                    DefDatabase<ThingDef>.GetNamed("MAP_AntimatterContainmentDevice"), 1),
                new ThingDefCountClass(DefDatabase<ThingDef>.GetNamed("SubcoreHigh"), 1),
                new ThingDefCountClass(ThingDefOf.Steel, 300),
                new ThingDefCountClass(ThingDefOf.Plasteel, 150)
            };
            return false;
        }
    }
}
