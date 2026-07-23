using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械巢节点地图上的“有效威胁”判定。清理条件不依赖任何中心核心建筑。
    /// 有效威胁：存活机械巢机械族 Pawn（含休眠）、机械族炮塔、高低角护盾、地图状态建筑、
    /// 机械族生成器，以及集群建筑筛选认定为具有战斗/地图威胁的其他建筑。
    /// </summary>
    public static class MechHiveNodeThreatUtility
    {
        public static bool AnyMechHiveThreatOnMap(Map? map)
        {
            if (map == null || map.Disposed)
            {
                return false;
            }

            Faction? mechHive = MechHiveNodeRelationUtility.GetMechHive();
            if (mechHive == null)
            {
                // 无机械巢派系：不存在有效威胁，允许清理移除。
                return false;
            }

            // 1) 存活机械巢机械族 Pawn（包含休眠、包含被击倒但未死亡）。
            // 1.6 中 AllPawnsSpawned 返回 IReadOnlyList<Pawn>，此处只读遍历即可。
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Dead || pawn.Destroyed)
                {
                    continue;
                }

                if (pawn.Faction == mechHive && pawn.RaceProps != null && pawn.RaceProps.IsMechanoid)
                {
                    return true;
                }
            }

            // 2) 机械巢威胁建筑（炮塔、护盾、状态建筑、生成器等）。
            List<Building> buildings = map.listerBuildings.allBuildingsNonColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building building = buildings[i];
                if (building == null || building.Destroyed)
                {
                    continue;
                }

                if (building.Faction == mechHive
                    && MechClusterBuildingUtility.IsBuildingThreat(building))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
