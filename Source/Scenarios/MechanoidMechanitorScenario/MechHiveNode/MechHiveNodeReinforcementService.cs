using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 完整机械巢节点在玩家遭遇敌对袭击时提供的机械族盟军。复用公共 Combat 模板生成工具，
    /// 派系为机械巢、不属于玩家、不占带宽、不消耗肃清额度、不可被机械师征召，
    /// 直接以正常敌对袭击链路的到达与编队方式加入战斗。不调用会拒绝隐藏派系的原版 RaidFriendly。
    /// </summary>
    public static class MechHiveNodeReinforcementService
    {
        private const int MinReinforcementPoints = 250;

        private const int MaxReinforcementPoints = 5000;

        private const float PerNodeSupportChance = 0.10f;

        /// <summary>
        /// 敌对 RaidEnemy 成功执行后由补丁回调。检查是否满足盟军条件并至多部署一支盟军。
        /// </summary>
        public static void NotifyHostileRaidExecuted(IncidentParms parms)
        {
            try
            {
                if (parms == null || !(parms.target is Map map) || !map.IsPlayerHome)
                {
                    return;
                }

                if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
                {
                    return;
                }

                // 仅在玩家与机械巢为盟友（含永久盟友）时提供援军。
                if (!MechHiveNodeRelationUtility.IsAllyForNode())
                {
                    return;
                }

                Faction? mechHive = MechHiveNodeRelationUtility.GetMechHive();
                if (mechHive == null)
                {
                    return;
                }

                Faction? attacker = parms.faction;
                if (attacker == null || attacker == mechHive)
                {
                    // 机械巢自身的袭击不触发援军，避免递归。
                    return;
                }

                Faction? player = Faction.OfPlayerSilentFail;
                if (player == null || !attacker.HostileTo(player))
                {
                    return;
                }

                List<MAPMechHiveNode> nodes = new List<MAPMechHiveNode>();
                MechHiveNodeGenerationUtility.GetCompletedUncleanedNodesNearColony(map.Tile, nodes);
                int count = Mathf.Min(nodes.Count, MechHiveNodeGenerationUtility.MaxNodesPerColony);
                if (count <= 0)
                {
                    return;
                }

                float probability = 1f - Mathf.Pow(1f - PerNodeSupportChance, count);
                if (!Rand.Chance(probability))
                {
                    return;
                }

                int points = Mathf.Clamp(
                    Mathf.RoundToInt(parms.points * 0.5f),
                    MinReinforcementPoints,
                    MaxReinforcementPoints);
                TryDeployReinforcement(mechHive, map, points);
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP] 机械巢盟军援军处理异常: " + ex);
            }
        }

        /// <summary>
        /// 向指定地图部署一支机械巢盟军。成功返回 true。不消耗肃清额度、不发送额外事件权重。
        /// </summary>
        public static bool TryDeployReinforcement(Faction mechHive, Map map, int points)
        {
            if (mechHive == null || map == null || points <= 0)
            {
                return false;
            }

            List<Pawn> pawns = MechHiveCombatPawnUtility.GenerateCombatPawns(mechHive, map, points);
            if (pawns.Count == 0)
            {
                return false;
            }

            IncidentParms parms = new IncidentParms
            {
                target = map,
                points = points,
                faction = mechHive,
                pawnGroupKind = PawnGroupKindDefOf.Combat,
                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
                canKidnap = false,
                canSteal = false,
                canTimeoutOrFlee = true
            };

            if (!parms.raidArrivalMode.Worker.TryResolveRaidSpawnCenter(parms))
            {
                MechHiveCombatPawnUtility.DiscardPawns(pawns);
                return false;
            }

            try
            {
                parms.raidArrivalMode.Worker.Arrive(pawns, parms);
                parms.raidStrategy.Worker.MakeLords(parms, pawns);
            }
            catch (Exception ex)
            {
                Log.Error("[MAP] 机械巢盟军部署失败: " + ex);
                return false;
            }

            SendReinforcementLetter(map, mechHive);
            return true;
        }

        private static void SendReinforcementLetter(Map map, Faction mechHive)
        {
            try
            {
                // 复用“部队支援”已有翻译键，不新增设定文本。
                Find.LetterStack.ReceiveLetter(
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.ForceSupport.Letter.Label"
                        .Translate(),
                    "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.ForceSupport.Letter.Text"
                        .Translate(),
                    LetterDefOf.PositiveEvent,
                    new LookTargets(map.Center, map),
                    mechHive);
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP] 机械巢盟军援军信件发送失败: " + ex);
            }
        }
    }
}
