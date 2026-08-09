using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 完整盟友前哨在玩家遭遇敌对 RaidEnemy 后提供的原版友军袭击援军。
    /// 只选择同时与玩家结盟、且与本次攻击者敌对的前哨所属派系。
    /// </summary>
    public static class FactionOutpostReinforcementService
    {
        private const int MinReinforcementPoints = 250;
        private const int MaxReinforcementPoints = 5000;
        private const float PerOutpostSupportChance = 0.10f;

        public static void NotifyHostileRaidExecuted(IncidentParms parms)
        {
            try
            {
                if (parms == null || !(parms.target is Map map) || !map.IsPlayerHome)
                {
                    return;
                }

                if (!GameComponent_MechanoidMechanitorStoryState.IsStoryConfigurationActive)
                {
                    return;
                }

                Faction? player = Faction.OfPlayerSilentFail;
                Faction? attacker = parms.faction;
                if (player == null || attacker == null || !attacker.HostileTo(player))
                {
                    return;
                }

                List<MAPFactionOutpost> nearby = new List<MAPFactionOutpost>();
                FactionOutpostGenerationUtility.GetCompletedUncleanedOutpostsNearColony(
                    map.Tile,
                    nearby);

                Dictionary<Faction, int> counts = new Dictionary<Faction, int>();
                int total = 0;
                for (int i = 0; i < nearby.Count; i++)
                {
                    Faction? owner = nearby[i].Faction;
                    if (owner == null
                        || owner == attacker
                        || !FactionOutpostFactionUtility.IsEligibleFaction(owner)
                        || owner.PlayerRelationKind != FactionRelationKind.Ally
                        || !owner.HostileTo(attacker))
                    {
                        continue;
                    }

                    counts.TryGetValue(owner, out int oldCount);
                    counts[owner] = oldCount + 1;
                    total++;
                }

                if (total <= 0)
                {
                    return;
                }

                float probability = 1f - Mathf.Pow(1f - PerOutpostSupportChance, total);
                if (!Rand.Chance(probability)
                    || !FactionOutpostRaidUtility.TryChooseFactionByCount(
                        counts,
                        total,
                        out Faction? ally)
                    || ally == null)
                {
                    return;
                }

                int points = Mathf.Clamp(
                    Mathf.RoundToInt(parms.points * 0.5f),
                    MinReinforcementPoints,
                    MaxReinforcementPoints);
                TryFireFriendlyRaid(map, ally, points);
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP] 普通派系前哨盟军援军处理异常: " + ex);
            }
        }

        private static void TryFireFriendlyRaid(Map map, Faction ally, int points)
        {
            try
            {
                IncidentParms friendlyParms = StorytellerUtility.DefaultParmsNow(
                    IncidentDefOf.RaidFriendly.category,
                    map);
                friendlyParms.faction = ally;
                friendlyParms.points = points;
                IncidentDefOf.RaidFriendly.Worker.TryExecute(friendlyParms);
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP] 普通派系前哨盟军部署异常: " + ex);
            }
        }
    }
}
