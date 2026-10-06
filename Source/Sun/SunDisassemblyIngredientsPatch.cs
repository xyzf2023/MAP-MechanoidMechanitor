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

    /// <summary>保留基础尸体拆解产物，玩家阵营的普通太阳额外返还一个反物质约束器。</summary>
    [HarmonyPatch(typeof(Corpse), nameof(Corpse.ButcherProducts))]
    internal static class SunCorpseDisassemblyProductsPatch
    {
        [HarmonyPostfix]
        private static IEnumerable<Thing> Postfix(IEnumerable<Thing> values, Corpse __instance)
        {
            foreach (Thing value in values)
            {
                yield return value;
            }

            Pawn? pawn = __instance.InnerPawn;
            // 能源核心使用 MAP_Mech_SunBOSS，即使属于玩家也不追加约束器。
            if (pawn == null || pawn.def.defName != "MAP_Mech_Sun" || pawn.Faction != Faction.OfPlayer)
                yield break;

            ThingDef containmentDeviceDef =
                DefDatabase<ThingDef>.GetNamed("MAP_AntimatterContainmentDevice");
            yield return ThingMaker.MakeThing(containmentDeviceDef);
        }
    }
}
