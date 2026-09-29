using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 完整机械巢节点在玩家与机械巢敌对时产生的额外机械巢袭击。每日概率检查一次，
    /// 每座殖民地每天最多触发一次，使用原版 RaidEnemy 链路，不修改叙述者事件权重，
    /// 不影响原版机械族袭击体系。
    /// </summary>
    public static class MechHiveNodeRaidUtility
    {
        /// <summary>
        /// 每日额外袭击检查。仅在玩家与机械巢敌对（含永久敌对）时进行；
        /// 对每张已加载的殖民地图各掷一次骰，成功则触发一次机械巢 RaidEnemy。
        /// </summary>
        public static void RunDailyExtraRaidCheck()
        {
            try
            {
                if (!MechHiveNodeRelationUtility.IsHostile())
                {
                    return;
                }

                Faction? mechHive = MechHiveNodeRelationUtility.GetMechHive();
                if (mechHive == null)
                {
                    return;
                }

                List<Map> maps = Find.Maps;
                if (maps == null)
                {
                    return;
                }

                List<MAPMechHiveNode> nodes = new List<MAPMechHiveNode>();
                for (int i = 0; i < maps.Count; i++)
                {
                    Map map = maps[i];
                    if (map == null || map.Disposed || !map.IsPlayerHome)
                    {
                        continue;
                    }

                    MechHiveNodeGenerationUtility.GetCompletedUncleanedNodesNearColony(map.Tile, nodes);
                    int count = Mathf.Min(nodes.Count, MechHiveNodeGenerationUtility.MaxNodesPerColony);
                    if (count <= 0)
                    {
                        continue;
                    }

                    // 概率按独立事件合并：1 - (1 - p)^count，p 来自当前剧情配置。
                    float probability =
                        MechanoidMechanitorStrategicChanceUtility.CombineIndependentChance(
                            MechanoidMechanitorStrategicChanceUtility
                                .GetMechHiveNodeRaidChancePerNode(),
                            count);

                    if (probability <= 0f)
                    {
                        continue;
                    }

                    if (Rand.Chance(probability))
                    {
                        TryFireExtraRaid(map, mechHive);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP-机械族机械师] 机械巢节点每日额外袭击检查异常: " + ex);
            }
        }

        private static void TryFireExtraRaid(Map map, Faction mechHive)
        {
            try
            {
                IncidentParms parms = StorytellerUtility.DefaultParmsNow(
                    IncidentCategoryDefOf.ThreatBig,
                    map);
                parms.faction = mechHive;

                // 使用原版 RaidEnemy 及其合法性检查、到达模式与部队生成链路；
                // 威胁点数沿用该地图当前正常袭击威胁点数（DefaultParmsNow 已计算）。
                IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP-机械族机械师] 机械巢节点额外袭击触发异常: " + ex);
            }
        }
    }
}
