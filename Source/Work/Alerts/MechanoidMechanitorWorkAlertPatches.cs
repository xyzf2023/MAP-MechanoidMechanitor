using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 原版医生警报只从 FreeColonistsSpawned 中寻找医生。
    /// 保留原版患者识别，只移除所在地图已有可用机械族机械师医生的患者。
    /// </summary>
    [HarmonyPatch(typeof(Alert_NeedDoctor), "get_Patients")]
    public static class MechanoidMechanitorNeedDoctorAlertPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref List<Pawn> __result)
        {
            if (__result == null || __result.Count == 0)
            {
                return;
            }

            Dictionary<Map, bool> mapHasMechanitorDoctor =
                new Dictionary<Map, bool>();

            for (int i = __result.Count - 1; i >= 0; i--)
            {
                Pawn patient = __result[i];
                Map? map = patient?.MapHeld;
                if (map == null)
                {
                    continue;
                }

                if (!mapHasMechanitorDoctor.TryGetValue(map, out bool hasDoctor))
                {
                    hasDoctor =
                        MechanoidMechanitorWorkAlertUtility
                            .HasAvailableMechanitorForWork(
                                map,
                                WorkTypeDefOf.Doctor);
                    mapHasMechanitorDoctor.Add(map, hasDoctor);
                }

                if (hasDoctor)
                {
                    __result.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>
    /// 原版狱卒警报只从 FreeColonistsSpawned 中寻找狱卒。
    /// 按地图追加真正获得监管授权且已启用监管工作的机械族机械师。
    /// </summary>
    [HarmonyPatch(typeof(Alert_NeedWarden), nameof(Alert_NeedWarden.GetReport))]
    public static class MechanoidMechanitorNeedWardenAlertPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref AlertReport __result)
        {
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                List<Pawn> prisoners = map.mapPawns.PrisonersOfColonySpawned;
                if (!map.IsPlayerHome || prisoners.Count == 0)
                {
                    continue;
                }

                if (HasVanillaFreeColonistWarden(map)
                    || MechanoidMechanitorWorkAlertUtility
                        .HasAvailableMechanitorForWork(
                            map,
                            WorkTypeDefOf.Warden,
                            requireWardenAuthorization: true))
                {
                    continue;
                }

                __result = AlertReport.CulpritIs(prisoners[0]);
                return;
            }

            __result = false;
        }

        private static bool HasVanillaFreeColonistWarden(Map map)
        {
            List<Pawn> freeColonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < freeColonists.Count; i++)
            {
                Pawn candidate = freeColonists[i];

                // 专属剧本会向该列表追加机械族机械师；这里只保留真正的
                // 原版自由殖民者，机械族机械师统一交由授权工具判断。
                if (!candidate.IsFreeColonist
                    || (!candidate.Spawned && !candidate.BrieflyDespawned())
                    || candidate.Downed
                    || candidate.workSettings == null
                    || candidate.workSettings.GetPriority(WorkTypeDefOf.Warden) <= 0)
                {
                    continue;
                }

                return true;
            }

            return false;
        }
    }
}
