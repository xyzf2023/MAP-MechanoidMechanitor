using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 完整普通派系前哨的敌对军事压力。每张玩家主基地地图每日最多额外触发一次袭击；
    /// 总概率由附近所有当前敌对前哨合并，触发后按各派系附近前哨数量选择攻击者。
    /// </summary>
    public static class FactionOutpostRaidUtility
    {
        public static void RunDailyExtraRaidCheck()
        {
            try
            {
                if (!GameComponent_MechanoidMechanitorStoryState.IsStoryConfigurationActive)
                {
                    return;
                }

                Faction? player = Faction.OfPlayerSilentFail;
                if (player == null)
                {
                    return;
                }

                List<Map> maps = Find.Maps;
                List<MAPFactionOutpost> nearby = new List<MAPFactionOutpost>();
                for (int i = 0; i < maps.Count; i++)
                {
                    Map map = maps[i];
                    if (map == null || map.Disposed || !map.IsPlayerHome)
                    {
                        continue;
                    }

                    FactionOutpostGenerationUtility.GetCompletedUncleanedOutpostsNearColony(
                        map.Tile,
                        nearby);

                    Dictionary<Faction, int> counts = new Dictionary<Faction, int>();
                    int total = 0;
                    for (int n = 0; n < nearby.Count; n++)
                    {
                        Faction? owner = nearby[n].Faction;
                        if (owner == null
                            || !FactionOutpostFactionUtility.IsEligibleFaction(owner)
                            || !owner.HostileTo(player))
                        {
                            continue;
                        }

                        counts.TryGetValue(owner, out int oldCount);
                        counts[owner] = oldCount + 1;
                        total++;
                    }

                    if (total <= 0)
                    {
                        continue;
                    }

                    // 概率按独立事件合并：1 - (1 - p)^total，p 来自当前剧情配置。
                    float probability =
                        MechanoidMechanitorStrategicChanceUtility.CombineIndependentChance(
                            MechanoidMechanitorStrategicChanceUtility
                                .GetFactionOutpostRaidChancePerNode(),
                            total);

                    if (probability <= 0f)
                    {
                        continue;
                    }

                    if (!Rand.Chance(probability)
                        || !TryChooseFactionByCount(counts, total, out Faction? attacker)
                        || attacker == null)
                    {
                        continue;
                    }

                    TryFireExtraRaid(map, attacker);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP-机械族机械师] 普通派系前哨每日额外袭击检查异常: " + ex);
            }
        }

        private static void TryFireExtraRaid(Map map, Faction attacker)
        {
            try
            {
                IncidentParms parms = StorytellerUtility.DefaultParmsNow(
                    IncidentCategoryDefOf.ThreatBig,
                    map);
                parms.faction = attacker;
                IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP-机械族机械师] 普通派系前哨额外袭击触发异常: " + ex);
            }
        }

        internal static bool TryChooseFactionByCount(
            Dictionary<Faction, int> counts,
            int total,
            out Faction? selected)
        {
            selected = null;
            if (counts == null || total <= 0)
            {
                return false;
            }

            int roll = Rand.Range(0, total);
            foreach (KeyValuePair<Faction, int> pair in counts)
            {
                if (pair.Key == null || pair.Value <= 0)
                {
                    continue;
                }

                if (roll < pair.Value)
                {
                    selected = pair.Key;
                    return true;
                }

                roll -= pair.Value;
            }

            return false;
        }
    }
}
