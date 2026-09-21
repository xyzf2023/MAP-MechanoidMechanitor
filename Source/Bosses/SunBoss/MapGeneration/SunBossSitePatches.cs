using System.Linq;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 按 Def 限定，兼容旧存档中已经生成的普通 Site 实例。
    [HarmonyPatch(typeof(Site), nameof(Site.ShouldRemoveMapNow))]
    internal static class SunBossSiteRetreatPatch
    {
        private static void Postfix(Site __instance, bool __result, ref bool alsoRemoveWorldObject)
        {
            if (__result && __instance.def.defName == "MAP_SunBossFacility"
                && __instance.Map?.GetComponent<MapComponent_SunBossArena>().BossDefeated != true)
                alsoRemoveWorldObject = false;
            // Site 已继承 EnterCooldownComp：移除地图后自动开始一天的进入冷却。
        }
    }

    [HarmonyPatch(typeof(Site), nameof(Site.PostMapGenerate))]
    internal static class SunBossSiteCameraPatch
    {
        private static void Postfix(Site __instance)
        {
            if (__instance.def.defName != "MAP_SunBossFacility") return;
            Map map = __instance.Map;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (map == null || !Find.Maps.Contains(map)) return;
                Pawn? pawn = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
                CameraJumper.TryJump(pawn?.Position ?? map.Center, map, CameraJumper.MovementMode.Cut);
            });
        }
    }
}
